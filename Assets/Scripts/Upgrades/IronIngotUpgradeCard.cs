using UnityEngine;

/// <summary>Upgrade card: raises the chance a harvested Rock node spawns an Iron Ingot. Stacks.</summary>
public class IronIngotUpgradeCard : UpgradeCardDefinition
{
    [SerializeField, Range(0f, 1f)] private float spawnChanceAdded = 0.15f;

    public override void Apply() => UpgradeManager.Instance?.AddIronIngotSpawnChance(spawnChanceAdded);
}
