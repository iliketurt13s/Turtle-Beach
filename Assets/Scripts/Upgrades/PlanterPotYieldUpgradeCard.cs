using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once the Planter Pot is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and makes every planted tree
/// take more hits before going dormant — which is the same thing as saying it
/// yields more resources per cycle, since a turtle collects on every hit.
///
/// This is what closes the gap the Planter Pot starts with: a fresh pot is
/// deliberately worse than a wild tree (fewer hits, slower regrowth, both
/// authored on its prefab), and this card is how the player buys that back and
/// eventually past it.
///
/// Applies to already-placed pots as well as future ones — each pot polls the
/// run-wide total and pushes it into its own ResourceNode (see PlanterPot),
/// including mid-cycle, so a tree being chopped right now gets deeper the
/// instant the card is picked.
/// </summary>
public class PlanterPotYieldUpgradeCard : BuildingUpgradeCard
{
    [Tooltip("Extra harvest hits every Planter Pot yields before going dormant. Stacks additively with repeat picks.")]
    [SerializeField, Min(1)] private int hitsAdded = 2;

    public override void Apply() => UpgradeManager.Instance?.AddPlanterPotHitsBonus(hitsAdded);
}
