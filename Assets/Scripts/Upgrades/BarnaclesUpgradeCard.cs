using UnityEngine;

/// <summary>
/// Upgrade card: every turtle grows barnacles — a permanent trade of movement
/// speed for damage against trash, in force day and night alike. Applies to
/// already-hatched turtles and future ones (see UpgradeManager.ApplyTo);
/// crabs never grow them.
///
/// The slowdown lives on its own TurtleLocomotion layer rather than folding
/// into the permanent-upgrade one, so a Turtle Speed card and this multiply
/// together instead of one overwriting the other. Non-stackable by design:
/// this establishes the baseline that BarnacleSpeedReliefUpgradeCard and
/// BarnacleDoubleHarvestUpgradeCard then improve on.
/// </summary>
public class BarnaclesUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Movement speed multiplier while wearing barnacles, e.g. 0.75 = 25% slower. Applies during the day and during storms.")]
    [SerializeField, Range(0.05f, 1f)] private float speedMultiplier = 0.75f;
    [Tooltip("Bonus damage every turtle deals to trash per hit, on top of Hard Hat and the Jellyfish night buff.")]
    [SerializeField, Min(0)] private int bonusDamage = 1;

    public override void Apply() => UpgradeManager.Instance?.UnlockBarnacles(speedMultiplier, bonusDamage);
}
