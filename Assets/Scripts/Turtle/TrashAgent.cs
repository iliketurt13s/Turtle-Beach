using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ocean-washed trash. Sits inert (but visible) until DayStormCycle enters its
/// storm phase, then periodically bursts toward the nest with a fresh random
/// direction jitter each time — a gust-of-wind snap, not a steered approach.
/// Rotation is independent of travel direction: each burst also applies a
/// random torque so the trash tumbles on its own. Burst timing itself is
/// randomized per-cycle (Burst Interval Variance) for a less mechanical feel.
/// At storm's end, fades out and self-destroys.
///
/// One variant branches off this: a prefab carrying a MagnetAgent marker aims
/// for the nearest BUILDING instead of the nest, and re-aims at the next one
/// each time it destroys what it was on (see UpdateMagnetTargeting). That is
/// the only difference — bursts, tumble, round scaling and the storm-end fade
/// are shared verbatim, which is why it's a marker plus a few branches here
/// rather than a second agent script (same shape as CrabAgent/TurtleAgent).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class TrashAgent : MonoBehaviour
{
    [Header("Burst Movement")]
    [Tooltip("Average seconds between each burst of movement.")]
    [SerializeField] private float burstInterval = 1.2f;
    [Tooltip("How much each burst's interval randomly varies, in seconds. E.g. 0.5 means each interval is Burst Interval plus or minus up to 0.5s.")]
    [SerializeField] private float burstIntervalVariance = 0.5f;
    [Tooltip("Impulse force applied per burst once fully up to speed.")]
    [SerializeField] private float impulseForce = 3f;
    [Tooltip("Degrees of random deviation applied around the direct heading to the nest, re-rolled every burst.")]
    [SerializeField, Range(0f, 180f)] private float directionRandomness = 45f;
    [Tooltip("Seconds of active storming before bursts reach full impulse force.")]
    [SerializeField] private float momentumRampDuration = 4f;
    [Tooltip("Fraction of full impulse force applied on the very first burst, ramping up to 1 over Momentum Ramp Duration.")]
    [SerializeField, Range(0f, 1f)] private float startingSpeedMultiplier = 0.3f;

    [Header("Round Scaling")]
    [Tooltip("How much Burst Interval shrinks per round survived, compounding (e.g. 0.05 = 5% shorter each round past the first) — trash bursts more often night over night. Floored at Min Burst Interval so it never becomes impossibly fast however many rounds have passed.")]
    [SerializeField, Range(0f, 1f)] private float burstIntervalReductionPerRound = 0.05f;
    [Tooltip("Burst Interval, after round scaling, never drops below this.")]
    [SerializeField] private float minBurstInterval = 0.4f;

    [Header("Tumble")]
    [Tooltip("Random torque impulse applied each burst so the trash spins/tumbles independently of its travel direction.")]
    [SerializeField] private float rotationForce = 20f;

    [Header("Pathfinding")]
    [Tooltip("Distance (world units) at which the current path waypoint is considered reached, advancing to the next one.")]
    [SerializeField] private float waypointArrivalDistance = 0.5f;
    [Tooltip("Extra clearance (grid cells, on top of PathfindingManager's own Obstacle Inflation Radius) this trash's route keeps from nature obstacles — raise this on bigger trash prefabs so their path avoids gaps only wide enough for something smaller, instead of routing through one it can't physically fit and getting wedged against it mid-burst.")]
    [SerializeField, Range(0, 3)] private int extraObstacleClearance = 0;

    private float stormElapsedTime;
    private float currentBurstInterval;

    // Computed exactly once, in Initialize, and only ever walked forward by
    // index afterward — never recomputed.
    private List<Vector3> path;
    private int pathIndex;

    [Header("Fade Out")]
    [Tooltip("Renderer whose alpha is faded to 0 when the storm ends.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Transform nestTarget;
    private float burstTimer;

    /// <summary>Non-null only on the Magnet trash variant (see MagnetAgent). Everything below that mentions buildings is inert without it.</summary>
    private MagnetAgent magnet;

    /// <summary>
    /// What this piece of trash is actually pathing toward — the nest for
    /// ordinary trash, the building currently being hunted for a Magnet.
    ///
    /// Kept separate from nestTarget rather than replacing it: nestTarget
    /// stays the run's real objective, which is what a Magnet falls back to
    /// with no buildings left standing, and what TrashHealth.Die hands to
    /// TrashDefinition.SpawnDeathDrops so the pieces still head for the nest
    /// rather than inheriting whatever wall their parent happened to be on.
    /// </summary>
    private Transform currentTarget;

    private float retargetTimer;

    private bool isFadingOut;
    private float fadeTimer;
    private float fadeDuration;
    private Color fadeStartColor;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        magnet = GetComponent<MagnetAgent>();
        currentBurstInterval = RollBurstInterval();
    }

    /// <summary>This instance's nest target, e.g. so TrashHealth.Die can pass it through to TrashDefinition.SpawnDeathDrops so dropped pieces get a path too.</summary>
    public Transform NestTarget => nestTarget;

    /// <summary>Called by TrashSpawner right after instantiation with the nest to burst toward. Computes a path around nature ONCE, here — never recomputed afterward, only walked forward by index (see Update/CurrentAimPoint). Passes extraObstacleClearance so bigger trash's route keeps more distance from nature than a gap it can't actually fit through. A Magnet is the one exception to "once": it repaths whenever the building it was hunting is destroyed or another becomes clearly nearer, since standing still after flattening a wall would defeat the entire point of it.</summary>
    public void Initialize(Transform target)
    {
        nestTarget = target;
        SetTarget(magnet != null ? (FindNearestBuilding() ?? target) : target);
    }

    /// <summary>Points this trash at a new destination and computes the one path it walks toward it. The shared tail of Initialize and every Magnet retarget, so both build their route identically.</summary>
    private void SetTarget(Transform target)
    {
        currentTarget = target;
        path = target != null && PathfindingManager.Instance != null
            ? PathfindingManager.Instance.FindPath(transform.position, target.position, extraObstacleInflation: extraObstacleClearance)
            : null;
        pathIndex = 0;
    }

    /// <summary>
    /// Magnet-only. Re-aims at the nearest building whenever the one being
    /// hunted has been destroyed (or was never there — a storm that starts
    /// with a bare island), and otherwise, no more often than Retarget
    /// Interval, when a different building has become nearer by more than
    /// Retarget Hysteresis. Falls back to the nest, exactly like ordinary
    /// trash, once there is nothing left to knock down.
    ///
    /// The hysteresis and the interval both exist for the same reason: each
    /// change of mind costs a fresh FindPath, and two walls a similar distance
    /// away would otherwise have a magnet re-pathing between them every single
    /// recheck for the whole storm.
    /// </summary>
    private void UpdateMagnetTargeting()
    {
        bool targetLost = currentTarget == null;

        if (!targetLost)
        {
            retargetTimer -= Time.deltaTime;
            if (retargetTimer > 0f) return;
        }

        retargetTimer = magnet.RetargetInterval;

        Transform nearest = FindNearestBuilding();
        if (nearest == null)
        {
            // Only worth switching to the nest once there is genuinely nothing
            // left to ravage; if it's already heading there, leave the path be.
            if (targetLost && currentTarget != nestTarget) SetTarget(nestTarget);
            return;
        }

        if (nearest == currentTarget) return;

        if (!targetLost)
        {
            float currentDistance = Vector2.Distance(rb.position, currentTarget.position);
            float candidateDistance = Vector2.Distance(rb.position, nearest.position);
            if (candidateDistance > currentDistance - magnet.RetargetHysteresis) return;
        }

        SetTarget(nearest);
    }

    /// <summary>The closest live building to this trash, or null if none are standing. Reads BuildingHealth's static registry rather than walking the scene, the same way every other sweep in the project does.</summary>
    private Transform FindNearestBuilding()
    {
        BuildingHealth nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (BuildingHealth building in BuildingHealth.AllBuildings)
        {
            if (building == null) continue;

            float sqrDistance = ((Vector2)building.transform.position - rb.position).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = building;
            }
        }

        return nearest != null ? nearest.transform : null;
    }

    private void Update()
    {
        if (isFadingOut)
        {
            UpdateFadeOut();
            return;
        }

        if (!DayStormCycle.IsStorming || nestTarget == null)
        {
            stormElapsedTime = 0f;
            return;
        }

        stormElapsedTime += Time.deltaTime;

        // Ahead of the waypoint advance below, since a retarget replaces both
        // the path and the index this frame and the old one's leftovers would
        // otherwise get one more step applied to them.
        if (magnet != null) UpdateMagnetTargeting();

        if (path != null && pathIndex < path.Count &&
            Vector2.Distance(rb.position, path[pathIndex]) <= waypointArrivalDistance)
        {
            pathIndex++;
        }

        burstTimer += Time.deltaTime;
        if (burstTimer >= currentBurstInterval)
        {
            burstTimer -= currentBurstInterval;
            currentBurstInterval = RollBurstInterval();
            BurstTowardNest();
        }
    }

    /// <summary>Burst Interval scaled down by burstIntervalReductionPerRound, compounding each round past the first (round 1 = no change, round 5 = burstInterval * (1 - reduction)^4), floored at minBurstInterval.</summary>
    private float EffectiveBurstInterval
    {
        get
        {
            int round = DayStormCycle.Instance != null ? DayStormCycle.Instance.CurrentRound : 1;
            float scaled = burstInterval * Mathf.Pow(1f - burstIntervalReductionPerRound, Mathf.Max(0, round - 1));
            return Mathf.Max(minBurstInterval, scaled);
        }
    }

    private float RollBurstInterval()
    {
        return Mathf.Max(0.05f, EffectiveBurstInterval + Random.Range(-burstIntervalVariance, burstIntervalVariance));
    }

    private void BurstTowardNest()
    {
        Vector2 toTarget = CurrentAimPoint() - rb.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg
                      + Random.Range(-directionRandomness, directionRandomness);

        // Rotation is independent of travel direction — the trash tumbles from
        // its own random spin (torque), rather than always facing the way it moves.
        rb.AddTorque(Random.Range(-rotationForce, rotationForce), ForceMode2D.Impulse);

        // Ramps from startingSpeedMultiplier up to full force over
        // momentumRampDuration, so trash picks up speed slowly at first and
        // moves faster once it's built up momentum in the storm.
        float rampT = momentumRampDuration > 0f ? Mathf.Clamp01(stormElapsedTime / momentumRampDuration) : 1f;
        float speedMultiplier = Mathf.Lerp(startingSpeedMultiplier, 1f, rampT);

        float speedBonus = UpgradeManager.Instance != null ? UpgradeManager.Instance.TrashSpeedBonus : 0f;
        Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        rb.AddForce(direction * impulseForce * speedMultiplier * (1f + speedBonus), ForceMode2D.Impulse);
    }

    /// <summary>The current burst's aim point: the in-progress path's current waypoint, or the destination directly once the path is exhausted/unavailable. That destination is the nest for ordinary trash and the hunted building for a Magnet (see currentTarget).</summary>
    private Vector2 CurrentAimPoint()
    {
        if (path != null && pathIndex < path.Count) return path[pathIndex];
        return currentTarget != null ? (Vector2)currentTarget.position : rb.position;
    }

    /// <summary>Called by TrashSpawner when the storm ends. Fades out and destroys this instance.</summary>
    public void BeginFadeOut(float duration)
    {
        if (isFadingOut) return;

        isFadingOut = true;
        fadeDuration = Mathf.Max(duration, 0.01f);
        fadeTimer = 0f;
        fadeStartColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
    }

    private void UpdateFadeOut()
    {
        fadeTimer += Time.deltaTime;
        float t = Mathf.Clamp01(fadeTimer / fadeDuration);

        if (spriteRenderer != null)
        {
            Color color = fadeStartColor;
            color.a = Mathf.Lerp(fadeStartColor.a, 0f, t);
            spriteRenderer.color = color;
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
