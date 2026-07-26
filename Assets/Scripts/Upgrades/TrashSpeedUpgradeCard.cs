using UnityEngine;

/// <summary>Hazard upgrade card: permanently increases every piece of trash's burst impulse (see UpgradeManager.TrashSpeedBonus, read live by TrashAgent.BurstTowardNest). Stackable — repeatable across island transitions.</summary>
public class TrashSpeedUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private float speedBonusAdded = 0.15f;

    public override void Apply() => UpgradeManager.Instance?.AddTrashSpeedBonus(speedBonusAdded);
}
