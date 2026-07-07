using UnityEngine;

/// <summary>Upgrade card: adds to every turtle's chance to deal double damage to trash per hit.</summary>
public class CritChanceUpgradeCard : UpgradeCardDefinition
{
    [SerializeField, Range(0f, 1f)] private float critChanceAdded = 0.1f;

    public override void Apply() => UpgradeManager.Instance?.AddCritChance(critChanceAdded);
}
