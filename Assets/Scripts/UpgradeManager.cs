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

    /// <summary>Cumulative extra units every turtle can carry, from CarryCapacityUpgradeCard. Pushed onto turtles via ApplyTo — crabs use CrabCarryCapacityBonus instead.</summary>
    public int CarryCapacityBonus { get; private set; } = 0;

    /// <summary>Cumulative extra units every crab can carry, from CrabCarryCapacityUpgradeCard. Entirely separate from CarryCapacityBonus — crabs are their own unit (see CrabAgent), so the turtle card never touches them and vice versa.</summary>
    public int CrabCarryCapacityBonus { get; private set; } = 0;

    /// <summary>Seconds a turtle keeps its Algae speed buff after stepping off the last Algae pile, from AlgaeLingerUpgradeCard. Zero (the default) means the buff ends the instant it leaves, exactly like Campfire's. Read live by TurtleAgent.RemoveAlgaeSpeedBuff.</summary>
    public float AlgaeLingerDuration { get; private set; } = 0f;

    /// <summary>True once AlgaeFertilizerUpgradeCard has been picked, at which point every Algae pile also speeds up the respawn of nearby plants. Read live by ResourceRespawnBooster, which is inert on Algae until then.</summary>
    public bool AlgaeFertilizerUnlocked { get; private set; } = false;

    /// <summary>How many crab recruits this run has earned (see CrabRecruitUpgradeCard). Gates the crab improvement branch (see IRequiresUpgrade); the crabs themselves are respawned on each new island by the card's own RegisterPerIslandRespawn action, not from this count.</summary>
    public int CrabCount { get; private set; } = 0;

    /// <summary>True once CrabCombatUpgradeCard has been picked, at which point crabs stop sitting out storms at the nest and fight trash like turtles. Read live by TurtleAgent.CanAttackTrash.</summary>
    public bool CrabsFightAtNight { get; private set; } = false;

    /// <summary>True once BarnaclesUpgradeCard has been picked. Governs whether BarnacleBonusDamage is actually handed out (the speed penalty needs no such flag — it simply stays at 1 until unlocked).</summary>
    public bool BarnaclesUnlocked { get; private set; } = false;

    /// <summary>The barnacle movement penalty as a multiplier, e.g. 0.75 = 25% slower. Starts at 1 (no penalty), drops when Barnacles is picked, and is eased back toward 1 by BarnacleSpeedReliefUpgradeCard. Pushed onto turtles via ApplyTo.</summary>
    public float BarnacleSpeedMultiplier { get; private set; } = 1f;

    /// <summary>Bonus damage per hit granted by Barnacles, folded into TurtleAgent.BonusDamageToTrash. Pushed onto turtles via ApplyTo.</summary>
    public int BarnacleBonusDamage { get; private set; } = 0;

    /// <summary>Chance for a turtle wearing barnacles to scrape two units instead of one off ANY harvest source, from BarnacleDoubleHarvestUpgradeCard. Unlike the per-type Wood/Rock double-drop chances this applies to every resource type including food, so it's added on top of them in RollHarvestAmount rather than being another switch arm.</summary>
    public float BarnacleDoubleHarvestChance { get; private set; } = 0f;

    // ---------------------------------------------------------------------
    // Run modifiers (see GameModifierDefinition). These are hardships the
    // player opted into on the menu in exchange for a score multiplier, so
    // unlike the upgrade totals above they are set ONCE at run start
    // (GameModifierManager applies its effects in Start) and never move
    // again. They live here anyway, because from every reader's point of view
    // they are the same thing as an upgrade: one run-wide number polled live
    // by whichever system it modifies. Each defaults to the value meaning
    // "modifier not taken", so no reader ever has to ask whether one is on.
    // ---------------------------------------------------------------------

    /// <summary>True once the Far Sighted modifier is active, at which point a turtle sent to harvest walks to the FURTHEST instance of that resource type from wherever it stood when the order was given, instead of the nearest one. Read live by TurtleAgent's harvest-target search.</summary>
    public bool SeekFurthestResource { get; private set; } = false;

    /// <summary>How far (world units) a turtle may stray from the nest under the Short Leash modifier. 0 (the default) means no leash at all, and every distance check against it is skipped, so an unmodified run behaves exactly as before. Read live by TurtleAgent.</summary>
    public float TurtleLeashRadius { get; private set; } = 0f;

    /// <summary>Multiplier on every food type's distribution cooldown, from the Picky Eaters modifier — 1 (the default) is the authored rate, 3 means waves come three times as slowly, so turtles spend stretches of a storm with no food buff at all. Read live by TurtleNest.GetCooldownSeconds.</summary>
    public float FoodCooldownMultiplier { get; private set; } = 1f;

    /// <summary>Fraction of its max health each building recovers when a storm ends, from the Shoddy Repairs modifier. 1 (the default) is the original heal-to-full. Read live by BuildingHealth.Heal.</summary>
    public float StormRepairFraction { get; private set; } = 1f;

    /// <summary>How much slower a turtle moves at a completely full load, from the Heavy Load modifier — 0.4 means a full carrier travels at 60% speed. 0 (the default) is no penalty at all. Ramps in from CarryLoadSlowdownStartFraction; see TurtleAgent.RefreshCarryLoadSpeed.</summary>
    public float CarryLoadSlowdownFraction { get; private set; } = 0f;

    /// <summary>The fraction of carry capacity a turtle has to pass before the Heavy Load penalty bites at all, so a turtle holding one twig isn't already trudging. 0.5 = the back half of its capacity. Only meaningful while CarryLoadSlowdownFraction is above zero.</summary>
    public float CarryLoadSlowdownStartFraction { get; private set; } = 0.5f;

    /// <summary>Cumulative fractional speed bonus a unit gets while swimming in shallow water, from ShallowWaterSpeedUpgradeCard — 0.5 = 50% faster in the shallows. Read live by TurtleLocomotion, which is already sampling the surface under each unit every frame for its wake particles.</summary>
    public float ShallowWaterSpeedBonus { get; private set; } = 0f;

    /// <summary>Cumulative bonus to every Sand Boulder Roller's target radius, from BoulderRollerRangeUpgradeCard. Its own track rather than Watchtower's, since the two are separate buildings with separate branches. Read live by SandBoulderRoller.</summary>
    public float BoulderRollerRangeBonus { get; private set; } = 0f;

    /// <summary>Cumulative extra targets a Sand Boulder Roller's boulder punches through before stopping, from BoulderRollerPierceUpgradeCard. Added on top of whatever pierce the boulder prefab itself authors. Read live by SandBoulderRoller at fire time and handed to the projectile.</summary>
    public int BoulderRollerPierceBonus { get; private set; } = 0;

    /// <summary>Cumulative extra harvest hits every Planter Pot yields before going dormant, from PlanterPotYieldUpgradeCard — i.e. how much more wood one pot is worth per cycle. Pushed into each pot's own ResourceNode by PlanterPot, which polls this the way BuildingHealth polls its health bonus.</summary>
    public int PlanterPotHitsBonus { get; private set; } = 0;

    /// <summary>True once PlanterPotFertilizerUpgradeCard has been picked, at which point every Planter Pot also speeds up the respawn of nearby trees. Read live by ResourceRespawnBooster, which is inert on a Planter Pot until then — exactly the same shape as AlgaeFertilizerUnlocked.</summary>
    public bool PlanterPotFertilizerUnlocked { get; private set; } = false;

    /// <summary>Chance that a freshly spawned piece of trash carrying a TrashResourceCargo component is actually loaded with resources this round, from TrashSalvageUpgradeCard. 0 (the default) means salvage hasn't been unlocked and no trash ever carries anything.</summary>
    public float TrashSalvageChance { get; private set; } = 0f;

    /// <summary>Multiplier on the strength of every night food buff — Seaweed's speed, Coconut's knockback, Jellyfish's bonus damage alike — from FoodPotencyUpgradeCard. 1 (the default) is the prefab-authored strength. Applied to the bonus PART of a buff, not the whole number, so 2x on a 1.25 speed buff gives 1.5, not 2.5.</summary>
    public float FoodEffectMultiplier { get; private set; } = 1f;

    /// <summary>True once CompleteDietUpgradeCard has been picked, at which point a turtle running two or more different food buffs at once gets all of them amplified by CompleteDietMultiplier. Read live per turtle by TurtleAgent, since which turtle is eating what changes constantly through a storm.</summary>
    public bool CompleteDietUnlocked { get; private set; } = false;

    /// <summary>Extra multiplier stacked on top of FoodEffectMultiplier for a turtle running two or more different food buffs at once. Only consulted while CompleteDietUnlocked.</summary>
    public float CompleteDietMultiplier { get; private set; } = 1f;

    /// <summary>Cumulative fractional speed bonus a turtle gets for swimming in company, from TailwindUpgradeCard — awarded at full strength only for a neighbour right on top of it, tapering to nothing at TailwindRadius. 0 (the default) means the upgrade isn't taken and the neighbour scan never runs.</summary>
    public float TailwindSpeedBonus { get; private set; } = 0f;

    /// <summary>How close another turtle has to be to contribute any tailwind at all. Set (not accumulated) by TailwindUpgradeCard alongside the bonus, since it describes the shape of the effect rather than its strength.</summary>
    public float TailwindRadius { get; private set; } = 3f;

    /// <summary>Ceiling on how much total proximity counts, in units of "one neighbour touching this turtle" — so a pack of eight doesn't move at eight times the bonus. Set, not accumulated, for the same reason as TailwindRadius.</summary>
    public float TailwindMaxStack { get; private set; } = 3f;

    /// <summary>True once SeaweedUpgradeCard has been picked. The other two food types can be told apart by their spawn chances being above zero, but Seaweed's card spawns a patch rather than setting a number, so it needs a flag of its own for UnlockedFoodTypeCount to see it.</summary>
    public bool SeaweedUnlocked { get; private set; } = false;

    /// <summary>How many of the three food types have been unlocked this run. Backs the food-branch cards' IRequiresUpgrade gates: FoodPotencyUpgradeCard needs one, CompleteDietUpgradeCard needs two (it can't do anything before a turtle can hold two different buffs at once).</summary>
    public int UnlockedFoodTypeCount =>
        (SeaweedUnlocked ? 1 : 0) + (CoconutSpawnChance > 0f ? 1 : 0) + (JellyfishSpawnChance > 0f ? 1 : 0);

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

    public void AddCarryCapacity(int amount)
    {
        CarryCapacityBonus += Mathf.Max(0, amount);
        Debug.Log($"UpgradeManager: turtle carry capacity bonus now +{CarryCapacityBonus}");
        ReapplyToAllTurtles();
    }

    public void AddCrabCarryCapacity(int amount)
    {
        CrabCarryCapacityBonus += Mathf.Max(0, amount);
        Debug.Log($"UpgradeManager: crab carry capacity bonus now +{CrabCarryCapacityBonus}");
        ReapplyToAllTurtles();
    }

    public void AddAlgaeLingerDuration(float seconds)
    {
        AlgaeLingerDuration += Mathf.Max(0f, seconds);
        Debug.Log($"UpgradeManager: Algae buff now lingers {AlgaeLingerDuration:F1}s after stepping off");
    }

    public void UnlockAlgaeFertilizer()
    {
        AlgaeFertilizerUnlocked = true;
        Debug.Log("UpgradeManager: Algae now speeds up nearby plant respawn.");
    }

    /// <summary>Records one more crab recruit. The spawning itself is the card's own job (see CrabRecruitUpgradeCard) — this is purely the run-state counter the crab improvement branch gates on.</summary>
    public void AddCrab()
    {
        CrabCount++;
        Debug.Log($"UpgradeManager: crab recruits now {CrabCount}");
    }

    public void UnlockCrabCombat()
    {
        CrabsFightAtNight = true;
        Debug.Log("UpgradeManager: crabs now fight trash during storms.");
    }

    /// <summary>A direct set rather than additive — Barnacles is a one-time, non-stackable pick establishing a baseline penalty and bonus, which BarnacleSpeedReliefUpgradeCard then eases from.</summary>
    public void UnlockBarnacles(float speedMultiplier, int bonusDamage)
    {
        BarnaclesUnlocked = true;
        BarnacleSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.05f, 1f);
        BarnacleBonusDamage = Mathf.Max(0, bonusDamage);
        Debug.Log($"UpgradeManager: barnacles on — speed x{BarnacleSpeedMultiplier:F2}, +{BarnacleBonusDamage} damage");
        ReapplyToAllTurtles();
    }

    /// <summary>Moves the barnacle speed penalty back toward 1 (no penalty), clamped there — repeat picks can soften the debuff but never turn it into a speed boost.</summary>
    public void EaseBarnacleSpeedPenalty(float amount)
    {
        BarnacleSpeedMultiplier = Mathf.Min(1f, BarnacleSpeedMultiplier + Mathf.Max(0f, amount));
        Debug.Log($"UpgradeManager: barnacle speed penalty eased to x{BarnacleSpeedMultiplier:F2}");
        ReapplyToAllTurtles();
    }

    public void AddBarnacleDoubleHarvestChance(float amount)
    {
        BarnacleDoubleHarvestChance = Mathf.Clamp01(BarnacleDoubleHarvestChance + amount);
        Debug.Log($"UpgradeManager: barnacle double-harvest chance now {BarnacleDoubleHarvestChance:P0}");
    }

    /// <summary>Turns the Far Sighted modifier on. A direct set rather than additive — it's a single on/off rule about how targets get chosen, and taking it twice can't make it more true.</summary>
    public void EnableFurthestResourceSeeking()
    {
        SeekFurthestResource = true;
        Debug.Log("UpgradeManager: turtles now seek the FURTHEST instance of their resource objective.");
    }

    /// <summary>Sets how far turtles may stray from the nest. Keeps the TIGHTEST (smallest non-zero) radius it is handed rather than overwriting, so two modifiers that both leash turtles can never loosen each other. A value at or below zero clears the leash entirely.</summary>
    public void SetTurtleLeashRadius(float radius)
    {
        if (radius <= 0f)
        {
            TurtleLeashRadius = 0f;
            Debug.Log("UpgradeManager: turtle leash cleared.");
            return;
        }

        TurtleLeashRadius = TurtleLeashRadius <= 0f ? radius : Mathf.Min(TurtleLeashRadius, radius);
        Debug.Log($"UpgradeManager: turtles may now stray at most {TurtleLeashRadius:F1} units from the nest.");
    }

    /// <summary>Multiplies (rather than sets) the food distribution cooldown, so it composes if ever applied more than once. Floored at 1: this exists to make food scarcer, and a value below 1 would quietly turn a hardship modifier into a buff.</summary>
    public void MultiplyFoodCooldown(float multiplier)
    {
        FoodCooldownMultiplier *= Mathf.Max(1f, multiplier);
        Debug.Log($"UpgradeManager: food distribution cooldown now x{FoodCooldownMultiplier:F2}");
    }

    /// <summary>Sets how much of its max health a building gets back when a storm ends. Keeps the HARSHEST (smallest) fraction it is handed, for the same reason SetTurtleLeashRadius keeps the tightest radius.</summary>
    public void SetStormRepairFraction(float fraction)
    {
        StormRepairFraction = Mathf.Min(StormRepairFraction, Mathf.Clamp01(fraction));
        Debug.Log($"UpgradeManager: buildings now recover {StormRepairFraction:P0} of their max health per storm.");
    }

    /// <summary>Turns on the Heavy Load movement penalty. A direct set of both halves (they describe one curve together, so an additive version of either alone would mean nothing), and it pushes the result onto every live turtle so it takes hold on units already mid-load.</summary>
    public void SetCarryLoadSlowdown(float fractionAtFullLoad, float startFraction)
    {
        CarryLoadSlowdownFraction = Mathf.Clamp(fractionAtFullLoad, 0f, 0.95f);
        CarryLoadSlowdownStartFraction = Mathf.Clamp01(startFraction);
        Debug.Log($"UpgradeManager: a fully loaded turtle is now {CarryLoadSlowdownFraction:P0} slower (penalty starts at {CarryLoadSlowdownStartFraction:P0} of capacity).");
        ReapplyToAllTurtles();
    }

    public void AddShallowWaterSpeedBonus(float amount)
    {
        ShallowWaterSpeedBonus += amount;
        Debug.Log($"UpgradeManager: shallow-water speed bonus now +{ShallowWaterSpeedBonus:P0}");
    }

    public void AddBoulderRollerRangeBonus(float amount)
    {
        BoulderRollerRangeBonus += amount;
        Debug.Log($"UpgradeManager: Sand Boulder Roller range bonus now +{BoulderRollerRangeBonus:F1}");
    }

    public void AddBoulderRollerPierceBonus(int amount)
    {
        BoulderRollerPierceBonus += Mathf.Max(0, amount);
        Debug.Log($"UpgradeManager: Sand Boulder Roller pierce bonus now +{BoulderRollerPierceBonus}");
    }

    public void AddPlanterPotHitsBonus(int amount)
    {
        PlanterPotHitsBonus += Mathf.Max(0, amount);
        Debug.Log($"UpgradeManager: Planter Pot yield bonus now +{PlanterPotHitsBonus} hits per cycle");
    }

    public void UnlockPlanterPotFertilizer()
    {
        PlanterPotFertilizerUnlocked = true;
        Debug.Log("UpgradeManager: Planter Pots now speed up nearby tree respawn.");
    }

    public void AddTrashSalvageChance(float amount)
    {
        TrashSalvageChance = Mathf.Clamp01(TrashSalvageChance + amount);
        Debug.Log($"UpgradeManager: trash salvage chance now {TrashSalvageChance:P0}");
    }

    /// <summary>Multiplies (rather than adds to) food potency, so repeat picks compound the same way SpeedMultiplier does — each one is "half again as strong" rather than a flat step that shrinks in relative terms as the total grows.</summary>
    public void MultiplyFoodEffect(float multiplier)
    {
        FoodEffectMultiplier *= Mathf.Max(0.01f, multiplier);
        Debug.Log($"UpgradeManager: food buff strength now x{FoodEffectMultiplier:F2}");
        RefreshFoodBuffsOnAllTurtles();
    }

    /// <summary>A direct set of both halves — Complete Diet is a one-time, non-stackable pick establishing a single bonus for eating a varied diet, not a stacking total.</summary>
    public void UnlockCompleteDiet(float multiplier)
    {
        CompleteDietUnlocked = true;
        CompleteDietMultiplier = Mathf.Max(1f, multiplier);
        Debug.Log($"UpgradeManager: Complete Diet on — two or more food buffs at once are now x{CompleteDietMultiplier:F2} stronger.");
        RefreshFoodBuffsOnAllTurtles();
    }

    /// <summary>
    /// Pushes a food-potency change onto turtles already running a buff.
    ///
    /// Needed because the three buffs aren't uniform in how they reach the
    /// world: Coconut's knockback and Jellyfish's damage are read live off the
    /// turtle at the moment of a hit and so pick up a change for free, but
    /// Seaweed's speed was pushed into a TurtleLocomotion layer when the buff
    /// landed and would otherwise keep its old strength until the next time
    /// that turtle was fed.
    /// </summary>
    private static void RefreshFoodBuffsOnAllTurtles()
    {
        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle != null) turtle.RefreshFoodBuffStrength();
        }
    }

    /// <summary>Sets the tailwind bonus and the shape of its falloff. The bonus accumulates across repeat picks; the radius and stack cap are set, since they describe the effect's shape rather than its strength (and a second pick shouldn't silently double the radius).</summary>
    public void AddTailwind(float speedBonus, float radius, float maxStack)
    {
        TailwindSpeedBonus += Mathf.Max(0f, speedBonus);
        TailwindRadius = Mathf.Max(0.1f, radius);
        TailwindMaxStack = Mathf.Max(0.1f, maxStack);
        Debug.Log($"UpgradeManager: tailwind now +{TailwindSpeedBonus:P0} at full stack, within {TailwindRadius:F1} units (stack cap {TailwindMaxStack:F1}).");
    }

    /// <summary>Records that Seaweed is available this run — see SeaweedUnlocked for why this one type needs a flag when the other two don't.</summary>
    public void UnlockSeaweed()
    {
        if (SeaweedUnlocked) return;

        SeaweedUnlocked = true;
        Debug.Log("UpgradeManager: Seaweed unlocked.");
    }

    /// <summary>Called once by a freshly spawned turtle (or crab) to catch up to whatever's already been picked this run.</summary>
    public void ApplyCurrentUpgradesTo(TurtleAgent turtle) => ApplyTo(turtle);

    private void ReapplyToAllTurtles()
    {
        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle == null) continue;
            ApplyTo(turtle);
        }
    }

    /// <summary>
    /// The single place every pushed (as opposed to read-live) run upgrade
    /// reaches a unit, shared by the freshly-spawned and the retroactive paths
    /// so the two can't drift apart.
    ///
    /// Crabs take the short branch: they're their own unit (see CrabAgent), so
    /// the turtle-facing cards — speed, crit, carry capacity, Barnacles —
    /// deliberately skip them, and their own carry-capacity card is the only
    /// thing here that applies. Environmental effects a crab earns out in the
    /// world (Campfire, Algae, a Rune) don't come through here at all and are
    /// unaffected by this split.
    /// </summary>
    private void ApplyTo(TurtleAgent turtle)
    {
        if (turtle.IsCrab)
        {
            turtle.SetCarryCapacityBonus(CrabCarryCapacityBonus);
            // Capacity is the denominator of the Heavy Load curve, so the
            // penalty is stale the moment a capacity card lands.
            turtle.RefreshCarryLoadSpeed();
            return;
        }

        turtle.ApplySpeedUpgrade(SpeedMultiplier);
        turtle.SetCritChance(CritChance);
        turtle.SetCarryCapacityBonus(CarryCapacityBonus);
        turtle.ApplyBarnacles(BarnaclesUnlocked, BarnacleSpeedMultiplier, BarnacleBonusDamage);
        turtle.RefreshCarryLoadSpeed();
    }

    /// <summary>
    /// Rolls whether a harvest of this resource type doubles, returning 1 or 2.
    ///
    /// Two independent sources feed the chance. The per-type Wood/Rock cards
    /// are exactly as they were — they only ever apply to their own resource.
    /// Barnacles adds on top of that for every type instead of being another
    /// switch arm, which is what extends doubling to Seaweed, Coconut and
    /// JellyfishGuts (types whose arm is still 0) as well as stacking with the
    /// Wood/Rock cards on theirs. Barnacles being turtle-only is why the
    /// harvester matters here at all — a crab rolls the per-type chance alone.
    /// </summary>
    public int RollHarvestAmount(ResourceManager.ResourceType type, TurtleAgent harvester = null)
    {
        float chance = type switch
        {
            ResourceManager.ResourceType.Wood => WoodDoubleDropChance,
            ResourceManager.ResourceType.Rock => RockDoubleDropChance,
            _ => 0f,
        };

        if (harvester == null || !harvester.IsCrab) chance += BarnacleDoubleHarvestChance;

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
