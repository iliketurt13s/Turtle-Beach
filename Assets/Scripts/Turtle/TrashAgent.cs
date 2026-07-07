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

    [Header("Tumble")]
    [Tooltip("Random torque impulse applied each burst so the trash spins/tumbles independently of its travel direction.")]
    [SerializeField] private float rotationForce = 20f;

    [Header("Pathfinding")]
    [Tooltip("Distance (world units) at which the current path waypoint is considered reached, advancing to the next one.")]
    [SerializeField] private float waypointArrivalDistance = 0.5f;

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

    /// <summary>Called by TrashSpawner right after instantiation with the nest to burst toward. Computes a path around nature ONCE, here — never recomputed afterward, only walked forward by index (see Update/CurrentAimPoint).</summary>
    public void Initialize(Transform target)
    {
        nestTarget = target;
        path = target != null && PathfindingManager.Instance != null
            ? PathfindingManager.Instance.FindPath(transform.position, target.position)
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

    private float RollBurstInterval()
    {
        return Mathf.Max(0.05f, burstInterval + Random.Range(-burstIntervalVariance, burstIntervalVariance));
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

        Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        rb.AddForce(direction * impulseForce * speedMultiplier, ForceMode2D.Impulse);
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
