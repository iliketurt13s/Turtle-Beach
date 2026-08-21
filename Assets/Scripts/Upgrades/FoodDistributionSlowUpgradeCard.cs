using UnityEngine;

/// <summary>
/// Run-modifier effect (Picky Eaters): the nest hands food out far less often,
/// so there are stretches of every storm with no food buff running at all —
/// stockpiling more food doesn't help, since the bottleneck is the wave timer
/// rather than the supply.
///
/// Multiplies TurtleNest's per-type cooldowns via
/// UpgradeManager.FoodCooldownMultiplier rather than editing the nest's own
/// authored numbers, so the relative pacing between Seaweed, Coconut and
/// Jellyfish (each tuned separately on the nest prefab) is preserved — every
/// type gets slower by the same proportion instead of collapsing onto one
/// shared rate.
/// </summary>
public class FoodDistributionSlowUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("How much longer every food type waits between distribution waves. 3 = three times the authored cooldown. Values below 1 are ignored (floored at 1 by UpgradeManager) — this modifier only ever makes food scarcer.")]
    [SerializeField, Min(1f)] private float cooldownMultiplier = 3f;

    public override void Apply()
    {
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"FoodDistributionSlowUpgradeCard ({DisplayName}): no UpgradeManager in the scene — food will be handed out at its normal rate.");
            return;
        }

        UpgradeManager.Instance.MultiplyFoodCooldown(cooldownMultiplier);
    }
}
