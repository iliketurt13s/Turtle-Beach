using UnityEngine;

/// <summary>
/// Marker component that turns an ordinary piece of trash into a Magnet: it
/// aims for the nearest BUILDING rather than the nest, and moves on to the
/// next nearest one each time it flattens the one it was on, so a run with
/// magnets in the water loses its island's infrastructure long before anything
/// reaches the nest.
///
/// Deliberately a bare marker plus a few TrashAgent branches, exactly the shape
/// CrabAgent uses on the turtle side, rather than a second agent script:
/// everything else about a magnet — the burst movement, the tumble, the round
/// scaling, the storm-end fade, TrashHealth, TrashItem, the rating it costs
/// TrashSpawner to place — is unchanged, and duplicating TrashAgent to alter
/// one target lookup would leave two copies of all of it to keep in step.
///
/// The Magnet is a trash TYPE like any other (its own prefab in TrashSpawner's
/// Trash Prefabs with its own TrashDefinition.Rating), so the run modifier that
/// introduces it is just an UnlockTrashUpgradeCard pointing at that prefab —
/// no modifier-specific code anywhere.
/// </summary>
public class MagnetAgent : MonoBehaviour
{
    [Tooltip("Seconds between rechecks of which building is nearest. A magnet always retargets IMMEDIATELY when the building it was heading for is destroyed (or one is built while it had nothing to aim at) — this interval only governs how quickly it notices that some OTHER building has since become the closer prize, e.g. a wall thrown up in front of it mid-storm. Each recheck that actually changes target costs one pathfind, so keep it well above a frame; 0 rechecks every frame.")]
    [SerializeField, Min(0f)] private float retargetInterval = 2f;

    [Tooltip("How much nearer a different building has to be than the current target before the magnet bothers switching, in world units. Stops it dithering between two buildings that are almost exactly the same distance away and re-pathing every recheck. Ignored when the current target is gone, which always retargets.")]
    [SerializeField, Min(0f)] private float retargetHysteresis = 1.5f;

    public float RetargetInterval => retargetInterval;
    public float RetargetHysteresis => retargetHysteresis;
}
