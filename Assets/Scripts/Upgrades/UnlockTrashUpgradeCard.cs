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

    public override void Apply() => TrashSpawner.Instance?.Unlock(trashPrefab);
}
