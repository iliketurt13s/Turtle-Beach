using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Menu-scene turtle pottering about on the island backdrop: amble to a random
/// point inside the roam area, maybe stop and look around for a moment, pick
/// another point, forever. Added at runtime by MenuIslandAmbience, which strips
/// TurtleAgent off the prefab first — this stands in for it, and only for the
/// parts that read as life from across a title screen.
///
/// There is deliberately no task here. Menu turtles don't harvest, don't carry,
/// don't deliver, and never touch the props scattered around them — the trees
/// and rocks are scenery to wander among, not destinations. Nothing feeds
/// ResourceManager or ScoreManager either, and none of those systems exist in
/// this scene to feed.
///
/// Movement reuses the turtle prefab's real physics: a target marker for
/// TurtleTargetSteering to rotate toward, and TurtleLocomotion's fin strokes for
/// thrust, started and stopped by toggling the fins themselves. That's the same
/// pair of components TurtleAgent drives, and it's why a menu turtle coasts and
/// banks exactly like an in-game one instead of sliding along a lerp. It also
/// means arrival is approximate by nature, hence Arrive Radius rather than an
/// exact destination, and hence Leg Timeout Seconds as a backstop for a turtle
/// that wedges itself against a prop's collider on the way past.
///
/// All tuning lives on MenuIslandAmbience.TurtleTuning rather than as serialized
/// fields here, because this component is added by AddComponent at runtime and
/// serialized fields on it would never appear in anyone's Inspector.
/// </summary>
public class MenuAmbienceTurtle : MonoBehaviour
{
    private enum State
    {
        Wandering,
        Pausing,
    }

    private MenuIslandAmbience owner;
    private MenuIslandAmbience.TurtleTuning tuning;

    private TurtleTargetSteering steering;
    private TurtleLocomotion locomotion;
    private IReadOnlyList<LimbOscillator> fins;

    /// <summary>Not parented under the turtle: it has to hold a fixed world point while the turtle itself moves and rotates toward it. Mirrors TurtleAgent's own marker objects.</summary>
    private Transform marker;

    private State state;
    private bool running;

    private Vector3 destination;
    private float legTimer;
    private float pauseTimer;

    private void Awake()
    {
        steering = GetComponent<TurtleTargetSteering>();
        locomotion = GetComponent<TurtleLocomotion>();
        fins = locomotion != null ? locomotion.PropellingFins : null;
    }

    /// <summary>Starts the loop. Called by MenuIslandAmbience only once its spawn root is active, so every component above has Awoken and cached itself by now.</summary>
    public void Begin(MenuIslandAmbience ambience)
    {
        owner = ambience;
        tuning = ambience.Tuning;

        marker = new GameObject($"{name} Ambience Target").transform;
        marker.SetParent(ambience.transform, false);

        // The menu island is sand from edge to edge and has no
        // PathfindingManager to say so, so pin the surface rather than let every
        // turtle default to throwing up water spray. See TurtleLocomotion.
        locomotion?.SetForcedSurface(true);

        running = true;
        PickNewDestination();
    }

    private void OnDestroy()
    {
        if (marker != null) Destroy(marker.gameObject);
    }

    private void Update()
    {
        if (!running) return;

        switch (state)
        {
            case State.Wandering:
                UpdateWandering();
                break;
            case State.Pausing:
                UpdatePausing();
                break;
        }
    }

    private void UpdateWandering()
    {
        legTimer += Time.deltaTime;

        if (!HasArrived() && legTimer < tuning.legTimeoutSeconds) return;

        // Stopping every single time reads as mechanical, and never stopping
        // reads as a patrol — rolling for it gives the aimless pottering the
        // menu wants.
        if (Random.value < tuning.pauseChance) EnterPausing();
        else PickNewDestination();
    }

    private void EnterPausing()
    {
        state = State.Pausing;
        pauseTimer = RandomInRange(tuning.pauseSecondsRange);
        StopMoving();
    }

    private void UpdatePausing()
    {
        pauseTimer -= Time.deltaTime;
        if (pauseTimer <= 0f) PickNewDestination();
    }

    /// <summary>
    /// Rolls a point inside the roam area that's actually worth walking to. A
    /// point already within Arrive Radius would count as reached on the frame it
    /// was picked, leaving the turtle to twitch in place (steering chases the
    /// marker even while the fins are stopped) instead of going anywhere. A
    /// handful of tries is plenty, and the last roll is accepted regardless so
    /// this can't spin on a roam area too small to satisfy the minimum.
    /// </summary>
    private void PickNewDestination()
    {
        const int Attempts = 8;
        float minDistance = Mathf.Max(tuning.minWanderDistance, tuning.arriveRadius * 1.5f);

        for (int i = 0; i < Attempts; i++)
        {
            destination = owner.RandomRoamPoint();
            if (((Vector2)destination - (Vector2)transform.position).sqrMagnitude >= minDistance * minDistance) break;
        }

        state = State.Wandering;
        legTimer = 0f;

        marker.position = destination;
        steering?.SetTarget(marker);
        locomotion?.SetSpeedMultiplier(tuning.wanderSpeedMultiplier);
        SetFinsPlaying(true);
    }

    private bool HasArrived() => ((Vector2)transform.position - (Vector2)destination).sqrMagnitude <= tuning.arriveRadius * tuning.arriveRadius;

    /// <summary>Cuts thrust but leaves steering pointed at the marker — the Rigidbody2D's own linear damping does the braking, so the turtle glides to a stop instead of snapping to one.</summary>
    private void StopMoving() => SetFinsPlaying(false);

    private void SetFinsPlaying(bool playing)
    {
        if (fins == null) return;

        foreach (LimbOscillator fin in fins)
        {
            if (fin != null) fin.SetPlaying(playing);
        }
    }

    private static float RandomInRange(Vector2 range) => Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
}
