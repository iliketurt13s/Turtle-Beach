using UnityEngine;

/// <summary>
/// Building-branch upgrade card ("Root Network"): only offered once the
/// Planter Pot is unlocked (see BuildingUpgradeCard/IRequiresBuilding), and
/// turns every pot into a Fertilizer as well — trees near it, wild or planted,
/// respawn faster.
///
/// Modelled directly on AlgaeFertilizerUpgradeCard, and for the same reason no
/// component is placed by this: the Planter Pot prefab already carries a
/// ResourceRespawnBooster configured as BoosterKind.PlanterPot, which sits
/// completely inert (no boosting, no visual) until this flag flips, so
/// already-placed pots start fertilizing the moment the card is picked with
/// nothing to retrofit.
///
/// Pots boosting each other is the intended payoff, not an oversight — a
/// cluster of them regrows meaningfully faster than the same pots spread out,
/// which is what makes placement a decision.
/// </summary>
public class PlanterPotFertilizerUpgradeCard : BuildingUpgradeCard
{
    public override void Apply() => UpgradeManager.Instance?.UnlockPlanterPotFertilizer();
}
