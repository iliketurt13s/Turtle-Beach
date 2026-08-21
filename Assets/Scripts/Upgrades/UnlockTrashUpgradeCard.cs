using UnityEngine;

/// <summary>
/// Hazard upgrade card: unlocks one trash prefab in TrashSpawner. Generic and
/// data-driven, mirroring BuildingUnlockUpgradeCard — every new trash type
/// (Glue Bottle, Plastic Straw, Battery, ...) gets its own unlock card by
/// making a new prefab with this same script and pointing Trash Prefab at
/// that trash type's prefab, no new code required.
/// </summary>
public class UnlockTrashUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private GameObject trashPrefab;

    public override void Apply()
    {
        if (trashPrefab == null)
        {
            Debug.LogError($"UnlockTrashUpgradeCard ({DisplayName}): Trash Prefab is unassigned — nothing was unlocked.");
            return;
        }

        if (TrashSpawner.Instance == null)
        {
            Debug.LogError($"UnlockTrashUpgradeCard ({DisplayName}): no TrashSpawner in the scene — \"{trashPrefab.name}\" was NOT unlocked.");
            return;
        }

        TrashSpawner.Instance.Unlock(trashPrefab);
    }
}
