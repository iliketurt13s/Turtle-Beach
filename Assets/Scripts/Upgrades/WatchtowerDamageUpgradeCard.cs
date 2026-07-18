using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Watchtower is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding), and raises the damage every
/// Watchtower's SandBall shots deal — already-placed ones and future ones
/// alike, since Watchtower reads UpgradeManager.WatchtowerDamageBonus live
/// and passes it into SandBall.Launch at fire time.
/// </summary>
public class WatchtowerDamageUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(1)] private int damageAdded = 1;

    public override void Apply() => UpgradeManager.Instance?.AddWatchtowerDamageBonus(damageAdded);
}
