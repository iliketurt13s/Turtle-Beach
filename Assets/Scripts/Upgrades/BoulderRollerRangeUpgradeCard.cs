using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once the Sand Boulder Roller is
/// unlocked (see BuildingUpgradeCard/IRequiresBuilding), and extends every
/// roller's target radius — already-placed ones and future ones alike, since
/// SandBoulderRoller reads UpgradeManager.BoulderRollerRangeBonus live rather
/// than caching it at Instantiate.
///
/// Worth more on this building than the same card would be on a Watchtower:
/// a roller's boulder rakes an entire lane, so a longer lane is more trash per
/// shot as well as more reach. It also grows the placement ghost's preview
/// circle for free, via Watchtower.PlacementRange.
/// </summary>
public class BoulderRollerRangeUpgradeCard : BuildingUpgradeCard
{
    [Tooltip("World units added to every Sand Boulder Roller's target radius. Stacks additively with repeat picks.")]
    [SerializeField, Min(0f)] private float rangeAdded = 2f;

    public override void Apply() => UpgradeManager.Instance?.AddBoulderRollerRangeBonus(rangeAdded);
}
