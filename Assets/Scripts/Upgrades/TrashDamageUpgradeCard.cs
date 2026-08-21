using UnityEngine;

/// <summary>Hazard upgrade card: permanently increases how much damage every piece of trash deals to buildings on collision (see UpgradeManager.TrashDamageBonus, read live by BuildingHealth.OnCollisionEnter2D). Stackable — repeatable across island transitions.</summary>
public class TrashDamageUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private int damageBonusAdded = 1;

    public override void Apply()
    {
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"TrashDamageUpgradeCard ({DisplayName}): no UpgradeManager in the scene — trash damage bonus was NOT applied.");
            return;
        }

        UpgradeManager.Instance.AddTrashDamageBonus(damageBonusAdded);
    }
}
