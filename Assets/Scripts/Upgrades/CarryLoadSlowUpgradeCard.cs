using UnityEngine;

/// <summary>
/// Run-modifier effect (Heavy Load): a turtle gets slower the fuller its arms
/// are, so the trip home is the slow half of every harvesting round trip and
/// carry-capacity upgrades stop being purely good — a bigger load is a longer
/// walk back.
///
/// The penalty ramps in from Slowdown Start Fraction rather than switching on
/// at a threshold, so the turtle visibly bogs down as it fills instead of
/// stepping between two speeds. It lands on its own TurtleLocomotion buff
/// layer, so it composes multiplicatively with every other speed source
/// (upgrades, Campfire, Algae, food, glue, Barnacles) exactly as they already
/// compose with each other.
/// </summary>
public class CarryLoadSlowUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("How much slower a completely full turtle moves. 0.4 = 40% slower, i.e. it travels at 60% speed with a full load.")]
    [SerializeField, Range(0f, 0.95f)] private float slowdownAtFullLoad = 0.4f;

    [Tooltip("Fraction of carry capacity the load has to pass before the penalty starts at all, ramping to the full amount at capacity. 0.5 = the back half of a turtle's capacity is where it gets heavy; 0 = it slows from the very first unit.")]
    [SerializeField, Range(0f, 1f)] private float slowdownStartFraction = 0.5f;

    public override void Apply()
    {
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"CarryLoadSlowUpgradeCard ({DisplayName}): no UpgradeManager in the scene — turtles will carry full loads at full speed.");
            return;
        }

        UpgradeManager.Instance.SetCarryLoadSlowdown(slowdownAtFullLoad, slowdownStartFraction);
    }
}
