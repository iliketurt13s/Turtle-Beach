using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Pet Rock is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises how much every
/// Pet Rock speeds up resource respawn — already-placed ones and future ones
/// alike, since ResourceRespawnBooster reads
/// UpgradeManager.PetRockRespawnBonus live rather than caching it (only for
/// instances configured as Pet Rock, not Fertilizer).
/// </summary>
public class PetRockRespawnUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Range(0f, 1f)] private float respawnBonusAdded = 0.25f;

    public override void Apply() => UpgradeManager.Instance?.AddPetRockRespawnBonus(respawnBonusAdded);
}
