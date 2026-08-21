using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once the Sand Boulder Roller is
/// unlocked (see BuildingUpgradeCard/IRequiresBuilding), and makes every
/// boulder punch through more trash before breaking up.
///
/// Read live at fire time (see SandBoulderRoller.FireAt, which hands the total
/// to each boulder as it launches), so already-placed rollers get it
/// immediately — and boulders already in flight keep the pierce they were
/// launched with, which is the only sensible reading of a projectile that has
/// already left.
///
/// The natural partner to the range card: range decides how long the lane is,
/// this decides how much of what's standing in it actually gets hit.
/// </summary>
public class BoulderRollerPierceUpgradeCard : BuildingUpgradeCard
{
    [Tooltip("Extra pieces of trash each boulder carries on through, on top of whatever the boulder prefab itself authors. Stacks additively with repeat picks.")]
    [SerializeField, Min(1)] private int pierceAdded = 1;

    public override void Apply() => UpgradeManager.Instance?.AddBoulderRollerPierceBonus(pierceAdded);
}
