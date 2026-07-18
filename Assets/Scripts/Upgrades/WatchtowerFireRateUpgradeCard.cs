using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Watchtower is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises how much faster
/// every Watchtower fires — already-placed ones and future ones alike, since
/// Watchtower reads UpgradeManager.WatchtowerFireRateBonus live rather than
/// caching it (see Watchtower.EffectiveFireInterval).
/// </summary>
public class WatchtowerFireRateUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Range(0f, 1f)] private float fireRateBonusAdded = 0.2f;

    public override void Apply() => UpgradeManager.Instance?.AddWatchtowerFireRateBonus(fireRateBonusAdded);
}
