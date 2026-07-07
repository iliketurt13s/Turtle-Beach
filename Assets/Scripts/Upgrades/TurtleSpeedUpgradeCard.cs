using UnityEngine;

/// <summary>Upgrade card: permanently multiplies every turtle's movement speed.</summary>
public class TurtleSpeedUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private float speedMultiplier = 1.15f;

    public override void Apply() => UpgradeManager.Instance?.AddSpeedMultiplier(speedMultiplier);
}
