using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Fertilizer is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises how much every
/// Fertilizer speeds up resource respawn — already-placed ones and future
/// ones alike, since ResourceRespawnBooster reads
/// UpgradeManager.FertilizerRespawnBonus live rather than caching it (only
/// for instances configured as Fertilizer, not Pet Rock).
/// </summary>
public class FertilizerRespawnUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Range(0f, 1f)] private float respawnBonusAdded = 0.25f;

    public override void Apply() => UpgradeManager.Instance?.AddFertilizerRespawnBonus(respawnBonusAdded);
}
