using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Algae is unlocked (see
/// BuildingUpgradeCard/IRequiresBuilding), and turns every algae pile into a
/// Fertilizer as well — plants near it respawn faster. No new component is
/// placed by this; the Algae prefab already carries a ResourceRespawnBooster
/// configured as BoosterKind.Algae, which sits completely inert (no boosting,
/// no visual) until this flag flips, so already-placed piles start
/// fertilizing the moment the card is picked.
/// </summary>
public class AlgaeFertilizerUpgradeCard : BuildingUpgradeCard
{
    public override void Apply() => UpgradeManager.Instance?.UnlockAlgaeFertilizer();
}
