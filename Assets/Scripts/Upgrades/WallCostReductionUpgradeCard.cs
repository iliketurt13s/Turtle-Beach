using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Wall is unlocked (see
/// BuildingUpgradeCard/IRequiresBuilding), and makes every Wall placed from
/// now on cheaper (see BuildableDefinition.MultiplyCost — only affects future
/// placements, since cost is only read at placement time). Stacks
/// multiplicatively with repeated picks.
/// </summary>
public class WallCostReductionUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Range(0.5f, 0.99f)] private float costMultiplier = 0.85f;

    public override void Apply() => RequiredBuilding?.MultiplyCost(costMultiplier);
}
