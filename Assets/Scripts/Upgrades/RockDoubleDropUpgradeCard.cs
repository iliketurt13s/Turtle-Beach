using UnityEngine;

/// <summary>Upgrade card: adds to the chance a harvested Rock node yields 2 instead of 1.</summary>
public class RockDoubleDropUpgradeCard : UpgradeCardDefinition
{
    [SerializeField, Range(0f, 1f)] private float doubleDropChanceAdded = 0.2f;

    public override void Apply() => UpgradeManager.Instance?.AddRockDoubleDropChance(doubleDropChanceAdded);
}
