using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A wandering shallow-water food pickup, spawned by JellyfishSpawner once the
/// Jellyfish upgrade raises its spawn chance. Hit-collection mirrors Coconut
/// exactly (RegisterHit adds one carried unit per hit via
/// TurtleAgent.CollectResourceUnit, self-destructing once Hits Required is
/// reached) — the only difference is this one moves on its own, drifting to a
/// random nearby point and rotating to face its heading (same idea as
/// TurtleAgent.WanderIdle + TurtleTargetSteering), constrained to always
/// re-roll toward a point still on the Shallow Water tilemap specifically —
/// one check that already rules out both sand and deep water, since shallow
/// water is its own tilemap layer disjoint from both. No hard collider wall:
/// staying in the shallows is enforced purely by never picking an invalid
/// drift target, matching how turtles are kept out of deep water.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class JellyfishAgent : MonoBehaviour
{
    private static readonly List<JellyfishAgent> allJellyfish = new List<JellyfishAgent>();

    /// <summary>Every currently-alive jellyfish, so JellyfishSpawner can enforce a total population cap.</summary>
    public static IReadOnlyList<JellyfishAgent> AllJellyfish => allJellyfish;

    [Header("Hit Collection")]
    [SerializeField] private int hitsRequired = 3;

    [Header("Drift")]
    [SerializeField] private float wanderRadius = 2f;
    [SerializeField] private float wanderInterval = 3f;
    [SerializeField] private float wanderIntervalVariance = 1f;
    [SerializeField] private float driftSpeed = 0.5f;
    [SerializeField] private float turnSpeed = 90f;
    [Tooltip("Distance at which the current drift target counts as reached.")]
    [SerializeField] private float arrivalDistance = 0.1f;

    private int hitsTaken;
    private IslandGenerator islandGenerator;
    private Rigidbody2D rb;

    private Vector3 anchor;
    private Vector3 driftTarget;
    private bool hasTarget;
    private float wanderTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        anchor = transform.position;
    }

    private void OnEnable()
    {
        allJellyfish.Add(this);
    }

    private void OnDisable()
    {
        allJellyfish.Remove(this);
    }

    /// <summary>Called by JellyfishSpawner right after Instantiate — a prefab asset can't hold a pre-wired scene reference (same rationale as TurtleBed.Initialize).</summary>
    public void Initialize(IslandGenerator generator)
    {
        islandGenerator = generator;
        anchor = transform.position;
    }

    private void Update()
    {
        if (hasTarget)
        {
            if (Vector2.Distance(transform.position, driftTarget) <= arrivalDistance)
            {
                hasTarget = false;
                wanderTimer = wanderInterval + Random.Range(-wanderIntervalVariance, wanderIntervalVariance);
            }

            return;
        }

        wanderTimer -= Time.deltaTime;
        if (wanderTimer > 0f) return;

        if (TryPickShallowWaterPoint(out Vector3 candidate))
        {
            driftTarget = candidate;
            hasTarget = true;
        }
        else
        {
            // Every reroll landed off the shallow water tilemap (e.g. this
            // anchor's whole wander radius runs off the ring's edge) — skip
            // this cycle and try again at the next interval.
            wanderTimer = wanderInterval;
        }
    }

    private void FixedUpdate()
    {
        if (!hasTarget) return;

        Vector2 toTarget = (Vector2)driftTarget - rb.position;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            float newAngle = Mathf.MoveTowardsAngle(rb.rotation, targetAngle, turnSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(newAngle);
        }

        Vector2 newPosition = Vector2.MoveTowards(rb.position, driftTarget, driftSpeed * Time.fixedDeltaTime);
        rb.MovePosition(newPosition);
    }

    private bool TryPickShallowWaterPoint(out Vector3 result)
    {
        Tilemap shallow = islandGenerator != null ? islandGenerator.ShallowWaterTilemap : null;
        if (shallow == null)
        {
            result = transform.position;
            return false;
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = anchor + new Vector3(offset.x, offset.y, 0f);

            if (shallow.HasTile(shallow.WorldToCell(candidate)))
            {
                result = candidate;
                return true;
            }
        }

        result = transform.position;
        return false;
    }

    /// <summary>Called by TurtleAgent.HandleHeadHit when a turtle's head touches this jellyfish. Returns true if this hit was the one that consumed/destroyed it — callers must not rely on a `this == null` check afterward, since Destroy() only takes effect at end of frame, not immediately.</summary>
    public bool RegisterHit(TurtleAgent attacker)
    {
        if (attacker == null) return false;

        hitsTaken++;

        // Mirrors Coconut.RegisterHit exactly — see its comment for why the
        // roll affects units collected but never hitsTaken.
        int amount = UpgradeManager.Instance != null
            ? UpgradeManager.Instance.RollHarvestAmount(ResourceManager.ResourceType.JellyfishGuts, attacker)
            : 1;

        for (int i = 0; i < amount; i++)
        {
            if (!attacker.CollectResourceUnit(ResourceManager.ResourceType.JellyfishGuts, transform.position)) break; // full — no loss, just stop adding
        }

        bool consumed = hitsTaken >= hitsRequired;
        if (consumed) Destroy(gameObject);
        return consumed;
    }
}
