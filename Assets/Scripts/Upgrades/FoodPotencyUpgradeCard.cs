using UnityEngine;

/// <summary>
/// Food-branch upgrade card: every night food buff hits harder — Seaweed's
/// speed, Coconut's knockback and Jellyfish's bonus damage alike — without
/// changing how often any of them is handed out.
///
/// Gated behind having unlocked at least one food type (see IRequiresUpgrade
/// and UpgradeManager.UnlockedFoodTypeCount), since before that there is no
/// buff for it to amplify and offering it would be a wasted pick. Answering
/// the gate from UpgradeManager's own run state rather than a serialized
/// reference to a parent card is what lets any of the three food cards open
/// this branch, instead of it being wired to one particular one.
///
/// Multiplies the BONUS part of each buff rather than the whole number, so a
/// x2 on a 1.25 speed buff is 1.5, not 2.5 — otherwise a card described as
/// "food is stronger" would quietly be worth far more on Seaweed than on the
/// other two. Stackable, compounding.
/// </summary>
public class FoodPotencyUpgradeCard : UpgradeCardDefinition, IRequiresUpgrade
{
    [Tooltip("How much stronger every food buff becomes. 1.5 = half again as strong. Compounds with repeat picks.")]
    [SerializeField, Min(1f)] private float potencyMultiplier = 1.5f;

    public bool IsPrerequisiteMet => UpgradeManager.Instance != null && UpgradeManager.Instance.UnlockedFoodTypeCount >= 1;

    public override void Apply() => UpgradeManager.Instance?.MultiplyFoodEffect(potencyMultiplier);
}
