using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Sand Pile is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises how much every
/// Sand Pile slows trapped trash down — already-placed ones and future ones
/// alike, since SandPile reads UpgradeManager.SandPileDampingBonus live
/// rather than caching it (see SandPile.EffectiveDampingIncrease).
/// </summary>
public class SandPileSlowUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(0f)] private float dampingIncreaseAdded = 3f;

    public override void Apply() => UpgradeManager.Instance?.AddSandPileDampingBonus(dampingIncreaseAdded);
}
