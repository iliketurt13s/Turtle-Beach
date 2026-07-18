using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once the Campfire is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises the radius within
/// which every Campfire buffs turtles — already-placed ones and future ones
/// alike, since Campfire reads UpgradeManager.CampfireRangeBonus live rather
/// than caching it (see Campfire.EffectiveRange).
/// </summary>
public class CampfireRangeUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(0f)] private float rangeAdded = 0.75f;

    public override void Apply() => UpgradeManager.Instance?.AddCampfireRangeBonus(rangeAdded);
}
