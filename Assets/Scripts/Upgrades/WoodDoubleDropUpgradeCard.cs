using UnityEngine;

/// <summary>Upgrade card: adds to the chance a harvested Wood node yields 2 instead of 1.</summary>
public class WoodDoubleDropUpgradeCard : UpgradeCardDefinition
{
    [SerializeField, Range(0f, 1f)] private float doubleDropChanceAdded = 0.2f;

    public override void Apply() => UpgradeManager.Instance?.AddWoodDoubleDropChance(doubleDropChanceAdded);
}
