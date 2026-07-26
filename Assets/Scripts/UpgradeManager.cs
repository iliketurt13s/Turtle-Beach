using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-wide singleton (same rationale as ResourceManager/TurtleNest: turtles
/// are runtime-spawned with no scene-authored reference) holding every
/// permanent upgrade total the player has picked so far this run. Speed and
/// crit chance are pushed onto every live TurtleAgent immediately whenever
/// they change (see ReapplyToAllTurtles) and onto every freshly spawned one
/// (see ApplyCurrentUpgradesTo, called from TurtleAgent.OnEnable) so upgrades
/// always affect the whole population, not just future turtles. Wood/Rock
/// double-drop chances are read live at harvest time instead, since the
/// amount doesn't depend on which turtle harvested.
///
/// Most upgrades are plain numbers, which already survive an island
/// transition untouched since this whole object is never destroyed. A card
/// that instead spawns something physical into the world once (e.g.
/// SeaweedUpgradeCard growing a patch) needs more than that, though —
/// IslandTransitionController wipes and regenerates the map, taking whatever
/// was spawned with it — so such a card registers a respawn action via
/// RegisterPerIslandRespawn, which this class re-runs every time a new island
/// generates from then on, for the rest of the run.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [Tooltip("Same scene IslandGenerator every other manager references — needed so permanent spawn-effect upgrades (see RegisterPerIslandRespawn) know when a new island has generated and their effect should replay.")]
    [SerializeField] private IslandGenerator islandGenerator;

    private readonly List<Action> perIslandRespawns = new List<Action>();

    public float SpeedMultiplier { get; private set; } = 1f;
    public float CritChance { get; private set; } = 0f;
    public float WoodDoubleDropChance { get; private set; } = 0f;
    public float RockDoubleDropChance { get; private set; } = 0f;
    public float CoconutSpawnChance { get; private set; } = 0f;
    public float IronIngotSpawnChance { get; private set; } = 0f;
    public float JellyfishSpawnChance { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Campfire's own speed bonus, from Campfire-branch upgrade cards (see CampfireSpeedBonusUpgradeCard). Read live by Campfire rather than pushed to instances, so it applies to already-placed and future Campfires alike.</summary>
    public float CampfireSpeedBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Campfire's range, from CampfireRangeUpgradeCard. Read live by Campfire.EffectiveRange.</summary>
    public float CampfireRangeBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Fertilizer's range, from FertilizerRangeUpgradeCard. Read live by ResourceRespawnBooster instances configured as Fertilizer.</summary>
    public float FertilizerRangeBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Fertilizer's respawn speed bonus, from FertilizerRespawnUpgradeCard. Read live by ResourceRespawnBooster instances configured as Fertilizer.</summary>
    public float FertilizerRespawnBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Pet Rock's range, from PetRockRangeUpgradeCard. Read live by ResourceRespawnBooster instances configured as Pet Rock.</summary>
    public float PetRockRangeBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Pet Rock's respawn speed bonus, from PetRockRespawnUpgradeCard. Read live by ResourceRespawnBooster instances configured as Pet Rock.</summary>
    public float PetRockRespawnBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus added to every Sand Pile's damping increase (slow strength), from SandPileSlowUpgradeCard. Read live by SandPile.EffectiveDampingIncrease.</summary>
    public float SandPileDampingBonus { get; private set; } = 0f;

    /// <summary>Damage every Sand Pile deals per tick to trash trapped on it, from SandPileCostAndDamageUpgradeCard. Zero (the default) means no Sand Pile deals damage-over-time yet. Read live by SandPile.</summary>
    public int SandPileDotDamagePerTick { get; private set; } = 0;

    /// <summary>Seconds between each Sand Pile damage-over-time tick, set (not additive — see SetSandPileDotTickInterval) by SandPileCostAndDamageUpgradeCard alongside SandPileDotDamagePerTick. Irrelevant until that damage is actually above zero. Read live by SandPile.</summary>
    public float SandPileDotTickInterval { get; private set; } = 1f;

    /// <summary>Cumulative fractional bonus to every Watchtower's fire rate, from WatchtowerFireRateUpgradeCard, e.g. 0.2 = 20% faster. Read live by Watchtower.EffectiveFireInterval.</summary>
    public float WatchtowerFireRateBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus damage added to every Watchtower's SandBall shots, from WatchtowerDamageUpgradeCard. Read live by Watchtower when firing.</summary>
    public int WatchtowerDamageBonus { get; private set; } = 0;

    /// <summary>Cumulative fractional bonus to every piece of trash's burst impulse, from TrashSpeedUpgradeCard (a hazard card), e.g. 0.15 = 15% harder bursts. Read live by TrashAgent.BurstTowardNest.</summary>
    public float TrashSpeedBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus damage every piece of trash deals to buildings on collision, from TrashDamageUpgradeCard (a hazard card). Read live by BuildingHealth.OnCollisionEnter2D, alongside Plastic Straw's own per-type tower multiplier.</summary>
    public int TrashDamageBonus { get; private set; } = 0;

    /// <summary>True once the Box/Pallet death-drop hazard card has been picked. Read live by TrashHealth.Die — governs whether any trash type's configured TrashDefinition.DeathDropPrefabs actually fire.</summary>
    public bool TrashDeathDropsUnlocked { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += RunPerIslandRespawns;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= RunPerIslandRespawns;
    }

    /// <summary>Registers an action to re-run every time a new island generates from now on — for upgrade cards that spawn something physical into the world once (e.g. SeaweedUpgradeCard), which would otherwise only ever exist on whichever island it was picked on, since IslandTransitionController wipes and regenerates the map on every transition. Safe to call more than once (e.g. a stackable card picked repeatedly registers one respawn per pick, so future islands get that many repeats too).</summary>
    public void RegisterPerIslandRespawn(Action respawnAction)
    {
        if (respawnAction != null) perIslandRespawns.Add(respawnAction);
    }

    private void RunPerIslandRespawns()
    {
        foreach (Action respawn in perIslandRespawns) respawn?.Invoke();
    }

    public void AddSpeedMultiplier(float multiplier)
    {
        SpeedMultiplier *= multiplier;
        Debug.Log($"UpgradeManager: speed multiplier now {SpeedMultiplier:F2}");
        ReapplyToAllTurtles();
    }

    public void AddCritChance(float amount)
    {
        CritChance = Mathf.Clamp01(CritChance + amount);
        Debug.Log($"UpgradeManager: crit chance now {CritChance:P0}");
        ReapplyToAllTurtles();
    }

    public void AddWoodDoubleDropChance(float amount)
    {
        WoodDoubleDropChance = Mathf.Clamp01(WoodDoubleDropChance + amount);
        Debug.Log($"UpgradeManager: Wood double-drop chance now {WoodDoubleDropChance:P0}");
    }

    public void AddRockDoubleDropChance(float amount)
    {
        RockDoubleDropChance = Mathf.Clamp01(RockDoubleDropChance + amount);
        Debug.Log($"UpgradeManager: Rock double-drop chance now {RockDoubleDropChance:P0}");
    }

    public void AddCoconutSpawnChance(float amount)
    {
        CoconutSpawnChance = Mathf.Clamp01(CoconutSpawnChance + amount);
        Debug.Log($"UpgradeManager: Coconut spawn chance now {CoconutSpawnChance:P0}");
    }

    public void AddIronIngotSpawnChance(float amount)
    {
        IronIngotSpawnChance = Mathf.Clamp01(IronIngotSpawnChance + amount);
        Debug.Log($"UpgradeManager: Iron Ingot spawn chance now {IronIngotSpawnChance:P0}");
    }

    public void AddJellyfishSpawnChance(float amount)
    {
        JellyfishSpawnChance = Mathf.Clamp01(JellyfishSpawnChance + amount);
        Debug.Log($"UpgradeManager: Jellyfish spawn chance now {JellyfishSpawnChance:P0}");
    }

    public void AddCampfireSpeedBonus(float amount)
    {
        CampfireSpeedBonus += amount;
        Debug.Log($"UpgradeManager: Campfire speed bonus now +{CampfireSpeedBonus:P0}");
    }

    public void AddCampfireRangeBonus(float amount)
    {
        CampfireRangeBonus += amount;
        Debug.Log($"UpgradeManager: Campfire range bonus now +{CampfireRangeBonus:F1}");
    }

    public void AddFertilizerRangeBonus(float amount)
    {
        FertilizerRangeBonus += amount;
        Debug.Log($"UpgradeManager: Fertilizer range bonus now +{FertilizerRangeBonus:F1}");
    }

    public void AddFertilizerRespawnBonus(float amount)
    {
        FertilizerRespawnBonus += amount;
        Debug.Log($"UpgradeManager: Fertilizer respawn speed bonus now +{FertilizerRespawnBonus:P0}");
    }

    public void AddPetRockRangeBonus(float amount)
    {
        PetRockRangeBonus += amount;
        Debug.Log($"UpgradeManager: Pet Rock range bonus now +{PetRockRangeBonus:F1}");
    }

    public void AddPetRockRespawnBonus(float amount)
    {
        PetRockRespawnBonus += amount;
        Debug.Log($"UpgradeManager: Pet Rock respawn speed bonus now +{PetRockRespawnBonus:P0}");
    }

    public void AddSandPileDampingBonus(float amount)
    {
        SandPileDampingBonus += amount;
        Debug.Log($"UpgradeManager: Sand Pile damping bonus now +{SandPileDampingBonus:F1}");
    }

    public void AddSandPileDotDamagePerTick(int amount)
    {
        SandPileDotDamagePerTick += amount;
        Debug.Log($"UpgradeManager: Sand Pile damage-over-time now {SandPileDotDamagePerTick}/tick");
    }

    /// <summary>A direct set rather than additive like the bonuses above — tick speed is a single rate, not a stacking bonus (SandPileCostAndDamageUpgradeCard is itself a one-time, non-stackable pick anyway).</summary>
    public void SetSandPileDotTickInterval(float seconds)
    {
        SandPileDotTickInterval = Mathf.Max(0.05f, seconds);
        Debug.Log($"UpgradeManager: Sand Pile damage-over-time tick interval now {SandPileDotTickInterval:F2}s");
    }

    public void AddWatchtowerFireRateBonus(float amount)
    {
        WatchtowerFireRateBonus += amount;
        Debug.Log($"UpgradeManager: Watchtower fire rate bonus now +{WatchtowerFireRateBonus:P0}");
    }

    public void AddWatchtowerDamageBonus(int amount)
    {
        WatchtowerDamageBonus += amount;
        Debug.Log($"UpgradeManager: Watchtower damage bonus now +{WatchtowerDamageBonus}");
    }

    public void AddTrashSpeedBonus(float amount)
    {
        TrashSpeedBonus += amount;
        Debug.Log($"UpgradeManager: trash speed bonus now +{TrashSpeedBonus:P0}");
    }

    public void AddTrashDamageBonus(int amount)
    {
        TrashDamageBonus += amount;
        Debug.Log($"UpgradeManager: trash damage bonus now +{TrashDamageBonus}");
    }

    public void UnlockTrashDeathDrops()
    {
        TrashDeathDropsUnlocked = true;
        Debug.Log("UpgradeManager: trash now drops loot on death.");
    }

    /// <summary>Called once by a freshly spawned turtle to catch up to whatever's already been picked this run.</summary>
    public void ApplyCurrentUpgradesTo(TurtleAgent turtle)
    {
        turtle.ApplySpeedUpgrade(SpeedMultiplier);
        turtle.SetCritChance(CritChance);
    }

    private void ReapplyToAllTurtles()
    {
        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle == null) continue;
            turtle.ApplySpeedUpgrade(SpeedMultiplier);
            turtle.SetCritChance(CritChance);
        }
    }

    /// <summary>Rolls whether a harvest of this resource type doubles, returning 1 or 2.</summary>
    public int RollHarvestAmount(ResourceManager.ResourceType type)
    {
        float chance = type switch
        {
            ResourceManager.ResourceType.Wood => WoodDoubleDropChance,
            ResourceManager.ResourceType.Rock => RockDoubleDropChance,
            _ => 0f, // Seaweed has no double-drop card yet
        };

        bool doubled = UnityEngine.Random.value < chance;
        if (doubled) Debug.Log($"UpgradeManager: double drop! {type} x2");
        return doubled ? 2 : 1;
    }

    /// <summary>Rolls whether this harvest hit also spawns the node's configured drop (Coconut for Wood, Iron Ingot for Rock).</summary>
    public void TryRollNodeDrop(ResourceNode node)
    {
        float chance = node.ResourceType switch
        {
            ResourceManager.ResourceType.Wood => CoconutSpawnChance,
            ResourceManager.ResourceType.Rock => IronIngotSpawnChance,
            _ => 0f,
        };

        if (chance <= 0f || UnityEngine.Random.value >= chance) return;
        node.SpawnDrop();
    }
}
