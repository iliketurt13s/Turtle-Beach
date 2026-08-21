using UnityEngine;

/// <summary>
/// Run-modifier effect (Far Sighted): turtles sent to harvest walk to the
/// FURTHEST instance of that resource type from wherever they were standing
/// when the order landed, instead of the nearest one — so every trip is a long
/// one and the island's convenient near-side trees are the last things touched
/// rather than the first.
///
/// Written as an UpgradeCardDefinition like every other modifier effect (see
/// GameModifierDefinition) rather than as a bespoke modifier type: the effect
/// is one flag pushed into UpgradeManager, which is exactly what an upgrade
/// card is. Not offered in the normal card pool — it only ever reaches the
/// player by sitting on a modifier prefab.
/// </summary>
public class FurthestResourceUpgradeCard : UpgradeCardDefinition
{
    public override void Apply()
    {
        // Explicitly checked rather than null-conditional, matching
        // TrashSpeedUpgradeCard: applied at run start from a modifier, a
        // missing manager would otherwise vanish without trace and read to the
        // player as a modifier that simply does nothing.
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"FurthestResourceUpgradeCard ({DisplayName}): no UpgradeManager in the scene — turtles will keep seeking the NEAREST resource.");
            return;
        }

        UpgradeManager.Instance.EnableFurthestResourceSeeking();
    }
}
