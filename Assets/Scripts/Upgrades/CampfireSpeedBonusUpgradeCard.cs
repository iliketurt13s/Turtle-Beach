using UnityEngine;

/// <summary>
/// Example building-branch upgrade card: only offered once the Campfire is
/// unlocked (see BuildingUpgradeCard/IRequiresBuilding), and raises the speed
/// bonus every Campfire grants — already-placed ones and future ones alike,
/// since Campfire reads UpgradeManager.CampfireSpeedBonus live rather than
/// caching it (see Campfire.EffectiveSpeedBonus).
/// </summary>
public class CampfireSpeedBonusUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Range(0f, 1f)] private float speedBonusAdded = 0.2f;

    public override void Apply() => UpgradeManager.Instance?.AddCampfireSpeedBonus(speedBonusAdded);
}
