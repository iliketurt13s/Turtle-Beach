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
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    public float SpeedMultiplier { get; private set; } = 1f;
    public float CritChance { get; private set; } = 0f;
    public float WoodDoubleDropChance { get; private set; } = 0f;
    public float RockDoubleDropChance { get; private set; } = 0f;
    public float CoconutSpawnChance { get; private set; } = 0f;
    public float IronIngotSpawnChance { get; private set; } = 0f;
    public float JellyfishSpawnChance { get; private set; } = 0f;

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

        bool doubled = Random.value < chance;
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

        if (chance <= 0f || Random.value >= chance) return;
        node.SpawnDrop();
    }
}
