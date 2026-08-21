using UnityEngine;

/// <summary>
/// Upgrade card: crated trash — Pallets and Boxes — sometimes washes ashore
/// with resources lashed to it, which pop off and fly to the nest when it's
/// destroyed. Turns fighting into a second income stream, and gives the player
/// a reason to prioritize one piece of trash in a clump over another.
///
/// Which trash can carry what is authored on the prefabs (see
/// TrashResourceCargo, which every eligible trash type carries and which stays
/// completely inert while this chance is zero) — exactly how death drops are
/// split between UpgradeManager's unlock flag and each TrashDefinition's own
/// Death Drop Prefabs. So extending this to a new trash type is a component on
/// that prefab, not an edit here. Stackable: repeat picks raise the odds.
/// </summary>
public class TrashSalvageUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Chance each piece of eligible trash is carrying salvage when it spawns. Rolled once per piece, at spawn, so the cargo is visible on it for the whole storm rather than being a hidden lottery settled on death.")]
    [SerializeField, Range(0f, 1f)] private float salvageChanceAdded = 0.25f;

    public override void Apply()
    {
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"TrashSalvageUpgradeCard ({DisplayName}): no UpgradeManager in the scene — trash will keep washing ashore empty.");
            return;
        }

        UpgradeManager.Instance.AddTrashSalvageChance(salvageChanceAdded);
    }
}
