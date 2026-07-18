using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Fertilizer is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises the radius within
/// which every Fertilizer boosts resource respawn — already-placed ones and
/// future ones alike, since ResourceRespawnBooster reads
/// UpgradeManager.FertilizerRangeBonus live rather than caching it (only for
/// instances configured as Fertilizer, not Pet Rock).
/// </summary>
public class FertilizerRangeUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(0f)] private float rangeAdded = 0.5f;

    public override void Apply() => UpgradeManager.Instance?.AddFertilizerRangeBonus(rangeAdded);
}
