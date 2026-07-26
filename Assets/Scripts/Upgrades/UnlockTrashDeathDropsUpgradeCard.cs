using UnityEngine;

/// <summary>
/// Hazard upgrade card: unlocks trash death-drops (see TrashDefinition.SpawnDeathDrops,
/// gated by UpgradeManager.TrashDeathDropsUnlocked). Any trash type with its
/// Death Drop Prefabs populated (e.g. Box/Pallet) starts releasing loot on
/// death once this is picked.
/// </summary>
public class UnlockTrashDeathDropsUpgradeCard : UpgradeCardDefinition
{
    public override void Apply() => UpgradeManager.Instance?.UnlockTrashDeathDrops();
}
