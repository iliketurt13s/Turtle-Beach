using UnityEngine;

/// <summary>
/// Run-modifier effect (Shoddy Repairs): buildings only get part of their
/// health back when a storm ends instead of being restored to full, so damage
/// accumulates night over night. A wall line that held twice is not the wall
/// line it was, and the player has to spend a day rebuilding rather than
/// treating every dawn as a clean slate.
///
/// Read live by BuildingHealth.Heal (via UpgradeManager.StormRepairFraction),
/// which repairs a ceil'd fraction of MAX health — ceil so that a small
/// building doesn't round its repair down to zero and become permanently
/// unrepairable, which would be a much harsher modifier than this one.
/// </summary>
public class PartialStormRepairUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Fraction of its max health each building recovers when a storm ends. 0.35 = a third of a bar back per night. 1 would be the normal heal-to-full, i.e. no modifier at all.")]
    [SerializeField, Range(0f, 1f)] private float repairFraction = 0.35f;

    public override void Apply()
    {
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"PartialStormRepairUpgradeCard ({DisplayName}): no UpgradeManager in the scene — buildings will keep healing to full.");
            return;
        }

        UpgradeManager.Instance.SetStormRepairFraction(repairFraction);
    }
}
