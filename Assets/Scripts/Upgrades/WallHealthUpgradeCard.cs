using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Wall is unlocked (see
/// BuildingUpgradeCard/IRequiresBuilding), and raises every Wall's max
/// health — already-placed ones and future ones alike, since BuildingHealth
/// live-diffs BuildableDefinition.HealthBonus every frame (see
/// BuildingHealth.Update) rather than only reading it once at Instantiate.
/// </summary>
public class WallHealthUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(1)] private int healthBonusAdded = 10;

    public override void Apply() => RequiredBuilding?.AddHealthBonus(healthBonusAdded);
}
