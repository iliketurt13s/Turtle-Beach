using UnityEngine;

/// <summary>Upgrade card: raises the chance a harvested Wood node spawns a Coconut. Stacks.</summary>
public class CoconutUpgradeCard : UpgradeCardDefinition
{
    [SerializeField, Range(0f, 1f)] private float spawnChanceAdded = 0.15f;

    public override void Apply() => UpgradeManager.Instance?.AddCoconutSpawnChance(spawnChanceAdded);
}
