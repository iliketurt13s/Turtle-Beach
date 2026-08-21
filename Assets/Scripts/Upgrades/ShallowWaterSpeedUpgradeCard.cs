using UnityEngine;

/// <summary>
/// Upgrade card: turtles move significantly faster while swimming in the
/// shallow ring around the island — not on the sand, and not out in open
/// water, so it rewards routing along the coast and makes the shoreline a
/// genuinely fast lane between one side of the island and the other.
///
/// The effect lives on its own TurtleLocomotion speed layer fed by that
/// class's existing per-frame surface sample (see its Update/SampleSurface),
/// which was already running for the wake particles — so this costs one extra
/// tilemap lookup per unit only while it's in water, and nothing at all on
/// land. Stackable: repeat picks add to the bonus.
/// </summary>
public class ShallowWaterSpeedUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Fractional speed bonus while in shallow water. 0.5 = 50% faster there. Stacks additively with repeat picks, and multiplicatively with every other speed source (upgrades, Campfire, Algae, food).")]
    [SerializeField, Min(0f)] private float shallowWaterSpeedBonus = 0.5f;

    public override void Apply() => UpgradeManager.Instance?.AddShallowWaterSpeedBonus(shallowWaterSpeedBonus);
}
