using UnityEngine;

/// <summary>
/// Run-modifier effect (Short Leash): turtles refuse to travel further than a
/// fixed radius from the nest, whatever the player orders. A click beyond it is
/// clamped back to the boundary, a resource outside it is substituted for the
/// nearest one inside, trash outside it is never chased, and a turtle that ends
/// up out there walks itself back in — see TurtleAgent, which owns all of that
/// and reads UpgradeManager.TurtleLeashRadius live.
///
/// Tune Leash Radius against the island, not in the abstract: it has to comfortably
/// contain the nest's surroundings or the run is unplayable rather than merely
/// hard, and it has to be tight enough to cut off the coast's far side or the
/// modifier does nothing on a small island.
/// </summary>
public class TurtleLeashUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("How far (world units) a turtle may get from the nest before every order stops taking it any further. Measured from the nest itself, so this is a radius, not a diameter.")]
    [SerializeField, Min(1f)] private float leashRadius = 12f;

    public override void Apply()
    {
        if (UpgradeManager.Instance == null)
        {
            Debug.LogError($"TurtleLeashUpgradeCard ({DisplayName}): no UpgradeManager in the scene — turtles will roam freely.");
            return;
        }

        UpgradeManager.Instance.SetTurtleLeashRadius(leashRadius);
    }
}
