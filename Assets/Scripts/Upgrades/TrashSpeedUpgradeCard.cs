using UnityEngine;

/// <summary>Hazard upgrade card: permanently increases every piece of trash's burst impulse (see UpgradeManager.TrashSpeedBonus, read live by TrashAgent.BurstTowardNest). Stackable — repeatable across island transitions.</summary>
public class TrashSpeedUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private float speedBonusAdded = 0.15f;

    public override void Apply()
    {
        // Explicitly checked rather than null-conditional: this now runs at
        // run start from a modifier, where a missing manager would otherwise
        // vanish without trace and look like a broken modifier.
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"TrashSpeedUpgradeCard ({DisplayName}): no UpgradeManager in the scene — trash speed bonus was NOT applied.");
            return;
        }

        UpgradeManager.Instance.AddTrashSpeedBonus(speedBonusAdded);
    }
}
