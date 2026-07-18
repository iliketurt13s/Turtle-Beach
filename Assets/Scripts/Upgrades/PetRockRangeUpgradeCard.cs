using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Pet Rock is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises the radius within
/// which every Pet Rock boosts resource respawn — already-placed ones and
/// future ones alike, since ResourceRespawnBooster reads
/// UpgradeManager.PetRockRangeBonus live rather than caching it (only for
/// instances configured as Pet Rock, not Fertilizer).
/// </summary>
public class PetRockRangeUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(0f)] private float rangeAdded = 0.5f;

    public override void Apply() => UpgradeManager.Instance?.AddPetRockRangeBonus(rangeAdded);
}
