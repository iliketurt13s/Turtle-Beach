using UnityEngine;

/// <summary>
/// Food-branch upgrade card ("Complete Diet"): a turtle running two or more
/// DIFFERENT food buffs at once has all of them amplified. Rewards keeping a
/// varied larder stocked rather than pouring everything into one food type,
/// and makes the third food unlock worth more than the second was.
///
/// Evaluated per turtle, live, rather than as a run-wide number: which turtle
/// is running which buffs changes constantly through a storm as the nest's
/// waves land and as stock runs dry (see TurtleAgent.FoodEffectScale, which
/// counts that turtle's own active buffs at the moment anything reads a buff's
/// strength). So the bonus switches itself on and off through a night as
/// naturally as the buffs themselves do.
///
/// Gated on TWO food types being unlocked (see IRequiresUpgrade) rather than
/// one: with a single type in the run nothing can ever satisfy its condition,
/// so offering it earlier would be offering a card that does nothing. Not
/// stackable — it establishes one bonus for eating well, and a second pick
/// would just be a bigger number with no new decision behind it.
/// </summary>
public class CompleteDietUpgradeCard : UpgradeCardDefinition, IRequiresUpgrade
{
    [Tooltip("How much stronger every one of a turtle's food buffs becomes while it is running two or more different ones. 1.5 = half again as strong, on top of any Food Potency multiplier already in force.")]
    [SerializeField, Min(1f)] private float variedDietMultiplier = 1.5f;

    public bool IsPrerequisiteMet => UpgradeManager.Instance != null && UpgradeManager.Instance.UnlockedFoodTypeCount >= 2;

    public override void Apply() => UpgradeManager.Instance?.UnlockCompleteDiet(variedDietMultiplier);
}
