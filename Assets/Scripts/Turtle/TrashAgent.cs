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

    private bool isFadingOut;
    private float fadeTimer;
    private float fadeDuration;
    private Color fadeStartColor;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        currentBurstInterval = RollBurstInterval();
    }

    /// <summary>This instance's nest target, e.g. so TrashHealth.Die can pass it through to TrashDefinition.SpawnDeathDrops so dropped pieces get a path too.</summary>
    public Transform NestTarget => nestTarget;

    /// <summary>Called by TrashSpawner right after instantiation with the nest to burst toward. Computes a path around nature ONCE, here — never recomputed afterward, only walked forward by index (see Update/CurrentAimPoint). Passes extraObstacleClearance so bigger trash's route keeps more distance from nature than a gap it can't actually fit through.</summary>
    public void Initialize(Transform target)
    {
        nestTarget = target;
        path = target != null && PathfindingManager.Instance != null
            ? PathfindingManager.Instance.FindPath(transform.position, target.position, extraObstacleInflation: extraObstacleClearance)
            : null;
        pathIndex = 0;
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
        Vector2 toNest = CurrentAimPoint() - rb.position;
        if (toNest.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(toNest.y, toNest.x) * Mathf.Rad2Deg
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

    /// <summary>The current burst's aim point: the in-progress path's current waypoint, or the nest directly once the path is exhausted/unavailable.</summary>
    private Vector2 CurrentAimPoint()
    {
        if (path != null && pathIndex < path.Count) return path[pathIndex];
        return nestTarget != null ? (Vector2)nestTarget.position : rb.position;
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
