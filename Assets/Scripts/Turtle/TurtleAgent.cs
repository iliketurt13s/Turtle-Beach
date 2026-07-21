using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-turtle brain, built around one central idea: the target resource
/// objective (hasTargetResource/targetResourceType — a resource *type*, e.g.
/// Wood, assigned only by the player selecting this turtle then clicking a
/// resource/Coconut/Jellyfish via MoveToResource) is a standing order that
/// nothing else is ever allowed to touch. Everything else — a movement
/// command, a building/rune visit, a storm, an aggro chase — is a transient
/// detour layered on top, and once each one ends the turtle simply falls
/// back through Update()'s own idle handling to the same fixed day/night
/// default, evaluated fresh rather than remembered: by day, if it has an
/// objective, seek the closest current instance of it and harvest+deliver,
/// repeating forever (TrySeekTargetResource/SeekTargetResourceOrIdle); by
/// night, ignore the objective entirely (harvesting is a daytime-only
/// activity — see the storm guards throughout HandleHeadHit) and just
/// nest-guard/wander (UpdateIdle). Because the objective can never be
/// cleared by anything but a fresh MoveToResource call, none of these
/// detours need a "snapshot before / restore after" mechanism to protect it.
///
/// A resource order actively en route (isResourceTask + currentTaskTarget)
/// doesn't self-stop — combined with a bouncy Physics Material 2D on the
/// resource's collider, the turtle keeps bouncing off and re-approaching,
/// harvesting whenever its head (see TurtleHeadHitbox, not the shell)
/// touches it, until redirected elsewhere. Once the current instance is
/// exhausted (a ResourceNode depletes) or consumed (a Coconut/JellyfishAgent
/// is destroyed), SeekTargetResourceOrIdle looks for the nearest other
/// current instance of the same type and keeps going. If genuinely none are
/// harvestable anywhere right now (e.g. every node of that type is
/// momentarily dormant), it idles without forgetting the objective —
/// TrySeekTargetResource rechecks periodically while idle and picks it back
/// up the instant one reactivates, rather than requiring a fresh player
/// order — but delivers any partial load it's already carrying first rather
/// than sitting on it while waiting.
///
/// A ground-point order (MoveToPoint) — "give the turtle a place to move to"
/// — works at any time, day or night, never touches the objective, and once
/// arrived just hands off to the same day/night default above; this is what
/// lets it double as rearranging turtles or giving one a patrol spot. A
/// storm cancels an in-flight resource task the instant it starts (see
/// CancelResourceTaskForNight) — and a beeline-home trip if one happens to be
/// in progress too (see CancelReturnTripForNight; the turtle keeps whatever
/// it was carrying, just isn't forced to keep walking it home through the
/// fight) — but leaves a ground-move order completely untouched. Separately, a turtle that notices trash within its aggro
/// distance while storming temporarily abandons whatever it was doing to go
/// attack the nearest one (same bounce-and-collide mechanic as harvesting,
/// damaging the trash's TrashHealth on each hit — trash itself is never a
/// target objective), then resumes its previous task once that trash is
/// destroyed or the storm ends, whichever comes first (see EndAggro's call
/// in Update()) — aggro is strictly storm-only and self-terminates at dawn
/// rather than chasing into daylight.
///
/// Turtles otherwise pass through buildings (see the Turtle/Building layer
/// collision exclusion) so they don't get stuck on walls. When targeting an
/// interactable building (see BuildingHealth.IsInteractable) specifically,
/// this turtle moves onto a separate "TurtleInteracting" layer (which must
/// collide with Building) for as long as that's its target, so it can
/// physically reach and touch it — this is how a turtle bumps into a rune
/// (see RuneEffect) repeatedly until it earns that rune's buff (indefinite:
/// Hard Hat adds BonusDamageToTrash, Flipper speeds up every fin's
/// oscillation frequency) or stations at a Watchtower (see Park/Unpark).
/// Both hand back to the same day/night default once done — a finished Rune
/// visit (ClearTask) and a Watchtower release (Unpark) are both just
/// StopAndIdle(), with nothing to restore since nothing was ever cleared.
/// </summary>
[RequireComponent(typeof(TurtleTargetSteering))]
[RequireComponent(typeof(TurtleLocomotion))]
public class TurtleAgent : MonoBehaviour
{
    [Header("Selection")]
    [Tooltip("Sprite tint applied while this turtle is selected.")]
    [SerializeField] private Color selectedTint = new Color(1f, 0.85f, 0.35f);

    [Header("Movement")]
    [Tooltip("Distance (world units) at which a ground-point order is considered arrived.")]
    [SerializeField] private float arrivalDistance = 0.15f;

    [Header("Aggro")]
    [Tooltip("Distance (world units) within which this turtle will notice and go attack trash.")]
    [SerializeField] private float aggroDistance = 3f;
    [Tooltip("Seconds a fresh movement command (MoveToPoint) suppresses new aggro acquisition — lets the player actually relocate a turtle to a different part of the island instead of it immediately locking back onto whatever trash happens to be nearby the moment it starts moving. Doesn't affect an aggro chase already in progress, and doesn't apply to resource/building orders.")]
    [SerializeField] private float aggroSuppressionDuration = 3f;

    [Header("Nest Defense")]
    [Tooltip("While storming, an idle turtle (no order, not aggroed) heads toward the nest to help guard it, stopping once within this distance rather than stacking on top of it.")]
    [SerializeField] private float nestGuardDistance = 2f;
    [Tooltip("Seconds to wait before retrying a nest-guard path after one failed to find a route (e.g. the nest is currently hemmed in by obstacles) — see UpdateIdle. Without this, a genuinely unreachable nest would otherwise re-run a full pathfind every single frame for as long as it stays blocked.")]
    [SerializeField] private float nestGuardRetryInterval = 2f;
    private float nestGuardRetryTimer;

    [Header("Resource Carrying")]
    [Tooltip("Maximum combined units (Wood/Rock plus Seaweed/Coconut/... food) this turtle can carry at once before it stops picking up more of whichever type it just harvested and returns to deliver it.")]
    [SerializeField] private int carryCapacity = 5;
    [Tooltip("Distance (world units) from the nest at which carried resources (materials and food alike) are delivered.")]
    [SerializeField] private float nestDeliveryRadius = 1.5f;
    [SerializeField] private GameObject harvestPopEffectPrefab;
    [SerializeField] private GameObject deliveryPopEffectPrefab;
    [Tooltip("Delay between each carried unit's delivery pop-effect launch, for a sequential 'ka-ching' feel.")]
    [SerializeField] private float deliveryStaggerDelay = 0.12f;

    [Header("Idle Wander")]
    [Tooltip("Radius an idle turtle randomly wanders within, centered on wherever it started idling (or arrived at the nest).")]
    [SerializeField] private float idleWanderRadius = 1.5f;
    [Tooltip("Roughly how many seconds an idle turtle pauses between wander movements.")]
    [SerializeField] private float idleWanderInterval = 3f;
    [Tooltip("How much the pause can randomly vary, e.g. 1 = anywhere from (interval - 1) to (interval + 1).")]
    [SerializeField] private float idleWanderIntervalVariance = 1f;
    [Tooltip("Movement speed multiplier while idle-wandering (see TurtleLocomotion.SetSpeedMultiplier) — lower than 1 so ambling reads as slower than normal cruising.")]
    [SerializeField, Range(0f, 1f)] private float idleSpeedMultiplier = 0.35f;

    [Header("Buffs")]
    [Tooltip("Swapped to the hard-hat sprite when this turtle earns the Hard Hat buff.")]
    [SerializeField] private SpriteRenderer headRenderer;
    [SerializeField] private Sprite hardHatHeadSprite;
    [Tooltip("The two front-leg renderers, swapped to their flipper sprite when this turtle earns the Flipper buff.")]
    [SerializeField] private SpriteRenderer frontLeftFinRenderer;
    [SerializeField] private Sprite flipperFinSpriteLeft;
    [SerializeField] private SpriteRenderer frontRightFinRenderer;
    [SerializeField] private Sprite flipperFinSpriteRight;

    /// <summary>All currently-live turtles, so UpgradeManager can retroactively apply a newly picked upgrade to the whole population, not just future spawns (mirrors TrashHealth.allTrash).</summary>
    private static readonly List<TurtleAgent> allTurtles = new List<TurtleAgent>();
    public static IReadOnlyList<TurtleAgent> AllTurtles => allTurtles;

    public bool IsSelected { get; private set; }

    public bool HasHardHatBuff { get; private set; }
    public bool HasFlipperBuff { get; private set; }

    /// <summary>Extra damage this turtle deals to trash per hit: a permanent contribution from the Hard Hat buff plus a temporary one from the Jellyfish night buff, added together — see ApplyHardHatBuff/ApplyJellyfishBuff.</summary>
    public int BonusDamageToTrash => hardHatBonusDamage + jellyfishBonusDamageActive;

    /// <summary>This turtle's chance to deal double damage per hit, from upgrade cards. Set via UpgradeManager, not directly.</summary>
    public float CritChance { get; private set; }

    private TurtleTargetSteering steering;
    private TurtleLocomotion locomotion;
    private Rigidbody2D rb;
    private IReadOnlyList<LimbOscillator> fins;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private Transform moveTargetMarker;
    private bool isGroundMove;
    private bool isResourceTask;
    private Transform currentTaskTarget;

    private int normalLayer;
    private int interactingLayer;
    private bool isCollidingWithBuilding;

    private bool isAggroed;
    private TrashHealth aggroTarget;
    private bool hadSavedTask;
    private Transform savedTaskTarget;
    private bool savedTaskIsGroundMove;
    private bool savedTaskIsResourceTask;

    // Counts down in TryAcquireAggroTarget regardless of day/night, but only
    // actually blocks acquisition while storming (the only time it'd matter) —
    // set to aggroSuppressionDuration by MoveToPoint, so a movement command
    // given well before a storm starts doesn't leave a stale window active by
    // the time it would count.
    private float aggroSuppressionTimer;

    // Set while aggro-chasing a target that's currently in deep water (see
    // UpdateWaitAtShore) — shoreWaitMarker holds the nearest shoreline point to
    // that target, which the turtle swims to and holds at instead of freezing
    // wherever it happened to be when the target went out of reach.
    private Transform shoreWaitMarker;
    private bool isWaitingAtShore;
    private bool shoreUnreachable;

    // Tracks the day/night edge so Update() can react to it exactly once per
    // transition: cancel an in-flight resource task the instant night falls
    // (CancelResourceTaskForNight), and force-end an in-progress aggro chase
    // the instant night ends (see EndAggro's call in Update()) — nothing else
    // needs remembering across the transition, since the target objective
    // (hasTargetResource/targetResourceType) and any ground-move/building
    // order (isGroundMove/currentTaskTarget) already survive it untouched by
    // construction; there's nothing to snapshot or restore.
    private bool wasStorming;

    // Idle sub-state: whenever there's no real task and no aggro, a turtle
    // either heads toward the nest to guard it (storming, and still far from
    // it) or ambles randomly nearby (otherwise) — see UpdateIdle. Entirely
    // separate from currentTaskTarget/isGroundMove/isResourceTask, so any
    // player order overrides it immediately.
    private Transform idleWanderMarker;
    private bool hasIdleAnchor;
    private Vector3 idleAnchor;
    private bool isWanderMoving;
    private float idleWanderTimer;
    private bool wasHeadingToNest;

    [Header("Pathfinding")]
    [Tooltip("Distance (world units) at which an in-progress path's current waypoint is considered reached, advancing to the next one.")]
    [SerializeField] private float waypointArrivalDistance = 0.4f;
    [Tooltip("While resource-seeking (isResourceTask) and following a path, once within this many tiles of the target with a clear line of sight, skip the remaining waypoints and steer straight at it instead — avoids a needlessly indirect final leg this close up (e.g. an isolated resource surrounded by open water).")]
    [SerializeField] private float resourceLineOfSightShortcutRange = 2f;

    [Header("Stuck Detection")]
    [Tooltip("While resource-seeking (isResourceTask), how often this turtle checks whether it's actually made progress — if it hasn't moved at least Stuck Movement Threshold since the last check, it gets a small sideways nudge to break out of a dead-on bounce loop (e.g. against a bouncy resource with nothing nearby to naturally curve the approach, like an isolated seaweed patch out in open water).")]
    [SerializeField] private float stuckCheckInterval = 1.5f;
    [Tooltip("Minimum distance a resource-seeking turtle must cover between stuck checks to NOT be considered stuck.")]
    [SerializeField] private float stuckMovementThreshold = 0.3f;
    [Tooltip("Sideways impulse applied (left or right, picked at random) to break a detected stuck loop.")]
    [SerializeField] private float stuckNudgeForce = 2f;

    private float stuckCheckTimer;
    private Vector3 stuckCheckPosition;

    [Header("Pathfinding Debug")]
    [Tooltip("Draws this turtle's current pathfinding route in the Scene view (Gizmos) while it's actively following one — every remaining waypoint ahead of it, in order, plus its final destination. Purely a debug aid, has no gameplay effect; toggle off per-turtle to declutter the view.")]
    [SerializeField] private bool showPathGizmo = true;
    [SerializeField] private Color pathGizmoColor = Color.cyan;
    [SerializeField] private Color pathGizmoCurrentWaypointColor = Color.yellow;
    [SerializeField] private Color pathGizmoDestinationColor = Color.magenta;

    // Path-following state, shared by every destination-seeking behavior
    // (real orders via ApplyTask, idle wander, storm nest-guard). Deliberately
    // separate from currentTaskTarget, since idle/nest-guard movement must
    // never touch that field (see the idle sub-state comment above).
    private Transform pathWaypointMarker;
    private List<Vector3> currentPath;
    private int currentPathIndex;
    private bool isFollowingPath;
    private Transform pathFinalDestination;

    private bool isParked;

    /// <summary>True while this turtle is stationed at a Watchtower — fully immobile (kinematic rigidbody, no steering/fins), and Update() does nothing else until Unpark() is called.</summary>
    public bool IsParked => isParked;

    /// <summary>Latched true the instant Update() first notices TurtleNest.Instance.IsDestroyed — settles the turtle into a clean idle (see StopAndIdle) exactly once, then every later Update() just returns immediately, same bypass shape as isParked above. Game over is permanent for the rest of this scene's lifetime, so this never needs to reset.</summary>
    private bool isFrozenForGameOver;

    /// <summary>Whatever this turtle is currently ordered toward (a resource node, a building, a ground point, an aggro target...), or null if idle. Lets a building (e.g. Watchtower) confirm a physical bump was an actual deliberate order to interact with it, not an incidental collision while passing by on some other task.</summary>
    public Transform CurrentTaskTarget => currentTaskTarget;

    /// <summary>One carried unit: its resource type, the specific sprite variant rolled for it at harvest time (reused for its shell slot and both pop effects so it looks consistent for its whole trip), and the exact shell slot it occupies.</summary>
    private struct CarriedResource
    {
        public ResourceManager.ResourceType Type;
        public Sprite Icon;
        public int SlotIndex;
    }

    private CarriedResourceVisuals carriedVisuals;
    private readonly List<CarriedResource> carriedResources = new List<CarriedResource>();
    private bool isReturningToNest;
    private Coroutine deliverCoroutine;

    // The player's standing target objective (Wood/Rock/Seaweed/Coconut/
    // JellyfishGuts) — a resource *type*, not any one specific node/Coconut/
    // JellyfishAgent instance, so TrySeekTargetResource/SeekTargetResourceOrIdle
    // can always retarget "the nearest one of these" once the current instance
    // is exhausted/consumed. Set ONLY by MoveToResource. Nothing else is ever
    // allowed to clear or touch it — not MoveToPoint, not MoveToBuilding, not a
    // storm starting/ending, not an aggro chase, not Watchtower parking — so it
    // needs no snapshot/restore mechanism around any of those interruptions;
    // it simply survives them all by construction. Only ever replaced by a
    // fresh MoveToResource call.
    private ResourceManager.ResourceType targetResourceType;
    private bool hasTargetResource;

    [Header("Target Resource Retry")]
    [Tooltip("Seconds an idle turtle with a target resource objective waits before rechecking for a valid instance, after a check finds nothing of that type currently harvestable (e.g. every node is momentarily dormant) — see TrySeekTargetResource.")]
    [SerializeField] private float harvestRetryInterval = 2f;

    private float harvestRetryTimer;

    private void Awake()
    {
        steering = GetComponent<TurtleTargetSteering>();
        locomotion = GetComponent<TurtleLocomotion>();
        rb = GetComponent<Rigidbody2D>();
        carriedVisuals = GetComponentInChildren<CarriedResourceVisuals>(true);
        fins = locomotion.PropellingFins;

        normalLayer = gameObject.layer;
        interactingLayer = LayerMask.NameToLayer("TurtleInteracting");

        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            originalColors[i] = spriteRenderers[i].color;
        }

        // Not parented under the turtle: it must hold a fixed world point,
        // independent of the turtle's own moving/rotating transform.
        moveTargetMarker = new GameObject($"{name} MoveTarget").transform;
        // Separate marker for idle wandering, since moveTargetMarker gets
        // overwritten by any ground-move order.
        idleWanderMarker = new GameObject($"{name} IdleWanderTarget").transform;
        // Repositioned to each successive path waypoint while following one.
        pathWaypointMarker = new GameObject($"{name} PathWaypoint").transform;
        // Holds the shoreline point picked by UpdateWaitAtShore while aggro-chasing a deep-water target.
        shoreWaitMarker = new GameObject($"{name} ShoreWaitTarget").transform;

        SetFinsPlaying(false);
    }

    private void OnEnable()
    {
        allTurtles.Add(this);
        if (UpgradeManager.Instance != null) UpgradeManager.Instance.ApplyCurrentUpgradesTo(this);
    }

    private void OnDisable()
    {
        allTurtles.Remove(this);
    }

    private void OnDestroy()
    {
        if (moveTargetMarker != null) Destroy(moveTargetMarker.gameObject);
        if (idleWanderMarker != null) Destroy(idleWanderMarker.gameObject);
        if (pathWaypointMarker != null) Destroy(pathWaypointMarker.gameObject);
        if (shoreWaitMarker != null) Destroy(shoreWaitMarker.gameObject);
    }

    private void Update()
    {
        if (isParked) return;

        if (TurtleNest.Instance != null && TurtleNest.Instance.IsDestroyed)
        {
            if (!isFrozenForGameOver)
            {
                isFrozenForGameOver = true;
                StopAndIdle();
            }

            return;
        }

        UpdatePathFollowing();
        UpdateStuckDetection();

        // Checked before the isReturningToNest bypass below so a return trip
        // in progress the instant night falls can be cancelled (see
        // CancelReturnTripForNight) on this same frame, before that bypass
        // gets a chance to claim the rest of Update() for it.
        bool storming = DayStormCycle.IsStorming;
        if (storming && !wasStorming)
        {
            CancelResourceTaskForNight();
            CancelReturnTripForNight();
        }
        else if (!storming && wasStorming)
        {
            ResetNightFoodBuffs();

            if (isAggroed)
            {
                // Trash isn't a target objective and aggro is storm-only — force
                // the chase to end at dawn (via EndAggro, not a raw field-clear,
                // so whatever it interrupted — a ground-move/building order — is
                // properly restored) rather than letting it continue into daylight.
                EndAggro();
            }
        }
        wasStorming = storming;

        // A turtle ferrying a full load ignores aggro/idle entirely and just
        // beelines for the nest — bypassing everything below exactly like
        // isParked already does above. Only true during the day (or the tail
        // end of a trip already past cancellation above once night falls,
        // see CancelReturnTripForNight) — a storm starting mid-trip cancels
        // it instead of forcing the turtle to keep ignoring the fight to walk
        // resources home; it resumes seeking its objective fresh the next
        // time it goes idle during the day, same as any other cancelled task.
        if (isReturningToNest)
        {
            UpdateReturnToNest();
            return;
        }

        // Opportunistic drive-by delivery: even a turtle that isn't on a
        // dedicated return trip drops off whatever partial load it's holding
        // the moment normal activity (harvesting, idling, wandering) brings
        // it within range of the nest, without interrupting that activity.
        CheckPassiveNestDelivery();

        // CheckPassiveNestDelivery can itself start a dedicated return trip
        // mid-frame — after the isReturningToNest bypass above already found
        // it false for this frame. Without re-checking here, the rest of this
        // same Update() call would run anyway (in particular UpdateIdle's
        // nest-guard BeginPathTo while storming), silently overwriting the
        // trip's just-set path/steering with a redundant one toward the same
        // destination.
        if (isReturningToNest) return;

        if (isAggroed)
        {
            if (aggroTarget == null) EndAggro();
            else UpdateAggroSteering();
        }
        else
        {
            TryAcquireAggroTarget();
        }

        if (isAggroed) return;

        if (isGroundMove)
        {
            if (Vector2.Distance(transform.position, moveTargetMarker.position) <= arrivalDistance)
            {
                StopAndIdle();
            }
            return;
        }

        if (currentTaskTarget != null) return;

        if (!storming && TrySeekTargetResource()) return;

        UpdateIdle(storming);
    }

    /// <summary>Debug-only Scene view visualization of this turtle's current path (see currentPath/currentPathIndex/pathFinalDestination, all set by BeginPathTo and advanced by UpdatePathFollowing) — draws the exact remaining route from here to its destination, so a pathfinding bug (an unexpectedly long detour, a route that hugs an obstacle too closely, one that never advances, etc.) is visible directly in the Scene view instead of having to reason about it from code. No gameplay effect; only runs while the Editor is drawing gizmos.</summary>
    private void OnDrawGizmos()
    {
        if (!showPathGizmo || !isFollowingPath || currentPath == null || currentPathIndex >= currentPath.Count) return;

        Gizmos.color = pathGizmoColor;
        Vector3 previous = transform.position;
        for (int i = currentPathIndex; i < currentPath.Count; i++)
        {
            Vector3 waypoint = currentPath[i];
            Gizmos.DrawLine(previous, waypoint);
            Gizmos.DrawSphere(waypoint, 0.08f);
            previous = waypoint;
        }

        // The immediate waypoint TurtleTargetSteering is actually homing
        // toward right now (pathWaypointMarker) — highlighted separately from
        // the rest of the route so it's obvious at a glance which segment is
        // "live" versus still queued up.
        Gizmos.color = pathGizmoCurrentWaypointColor;
        Gizmos.DrawWireSphere(currentPath[currentPathIndex], waypointArrivalDistance);

        if (pathFinalDestination != null)
        {
            Gizmos.color = pathGizmoDestinationColor;
            Gizmos.DrawLine(previous, pathFinalDestination.position);
            Gizmos.DrawWireSphere(pathFinalDestination.position, 0.25f);
        }
    }

    public void Select()
    {
        if (IsSelected) return;

        IsSelected = true;
        ApplyTint(selectedTint);
    }

    public void Deselect()
    {
        if (!IsSelected) return;

        IsSelected = false;
        RevertTint();
    }

    /// <summary>A pure transient detour — works at any time, day or night, and never touches the target resource objective. Once arrived, the turtle simply falls back to its normal day/night default (seek the objective if it's day and has one, nest-guard/wander otherwise) via Update()'s own idle handling, exactly like a finished Rune visit or Watchtower release does. Also suppresses new aggro acquisition for aggroSuppressionDuration (see TryAcquireAggroTarget), so relocating a turtle to help elsewhere on the island doesn't just have it immediately lock back onto whatever trash happens to be nearby the moment it sets off. Dismounts a stationed turtle first (see Unpark) rather than silently doing nothing — a Watchtower's turtle would otherwise be stuck there, unreachable by any order, until the storm ends releases it automatically; Watchtower notices on its own next Update and forgets this turtle so a new one can be stationed.</summary>
    public void MoveToPoint(Vector3 worldPoint)
    {
        if (isParked) Unpark();

        // Let an in-progress delivery trip finish on its own terms rather than
        // having this order redirect the turtle's physical steering away from
        // the nest/Food Building while isReturningToNest/Food stays stuck true.
        if (isReturningToNest) return;

        // Redirect a click out in the ocean to the nearest shallow-water/shore
        // point instead of just disregarding the order outright — BeginPathTo
        // could never fulfill a raw deep-water destination anyway (see
        // PathfindingManager.FindPath's avoidDeepWater), so this effectively
        // caps an ocean click at the shore closest to where the player
        // actually clicked, rather than leaving the turtle sitting there
        // ignoring the command.
        if (PathfindingManager.Instance != null && PathfindingManager.Instance.IsDeepWater(worldPoint))
        {
            worldPoint = PathfindingManager.Instance.NearestNonDeepWaterPoint(worldPoint);
        }

        aggroSuppressionTimer = aggroSuppressionDuration;
        CancelAggro();
        moveTargetMarker.position = worldPoint;
        ApplyTask(moveTargetMarker, isGroundMove: true);
    }

    /// <summary>Assigns the target resource objective (the player selecting a turtle then clicking a resource/Coconut/Jellyfish) and, if conditions allow, immediately starts moving to harvest this exact instance. The objective itself is always recorded regardless of time of day or an in-progress delivery trip — "can still be changed, but the turtle doesn't act on it until morning" — only the immediate move is gated: skipped while mid-delivery-trip (lets that trip finish on its own terms; the objective is picked up fresh afterward via SeekTargetResourceOrIdle) or while storming (queued for morning — harvesting is a daytime-only activity, see HandleHeadHit's storm guards).</summary>
    public void MoveToResource(Transform resourceTransform)
    {
        if (isParked) Unpark();

        if (TryGetHarvestType(resourceTransform, out ResourceManager.ResourceType type))
        {
            targetResourceType = type;
            hasTargetResource = true;
        }

        if (isReturningToNest) return;
        if (DayStormCycle.IsStorming) return;

        CancelAggro();
        ApplyTask(resourceTransform, isGroundMove: false, isResourceTask: true);
    }

    /// <summary>Sends the turtle to an interactable building, switching it onto the TurtleInteracting layer so it can physically reach and bump into it. Never touches the target resource objective — a Rune visit (see ClearTask) or a Watchtower stationing/release (see Park/Unpark) is just a transient detour, exactly like MoveToPoint. Dismounts a stationed turtle first (see MoveToPoint's own doc comment) so it can be sent straight to a different building, including another Watchtower.</summary>
    public void MoveToBuilding(Transform buildingTransform)
    {
        if (isParked) Unpark();

        if (isReturningToNest) return;

        CancelAggro();
        ApplyTask(buildingTransform, isGroundMove: false);
    }

    /// <summary>Called by TurtleHeadHitbox — only the head's contact counts as a harvest/rune hit, not the shell. Resource/Coconut/Jellyfish hits are all no-ops while storming (see each branch's guard below) — harvesting is strictly a daytime activity.</summary>
    public void HandleHeadHit(Collider2D other)
    {
        ResourceNode node = other.GetComponentInParent<ResourceNode>();
        if (node != null)
        {
            // Bouncing off a resource is still fine during a storm (physical
            // collision is untouched) — it just stops yielding anything. A
            // depleted (dormant) node also yields nothing until it respawns.
            if (!DayStormCycle.IsStorming && node.IsHarvestable)
            {
                int amount = UpgradeManager.Instance != null ? UpgradeManager.Instance.RollHarvestAmount(node.ResourceType) : 1;

                for (int i = 0; i < amount; i++)
                {
                    if (!CollectResourceUnit(node.ResourceType, node.transform.position)) break; // full — this unit doesn't fit, no loss, just stop adding
                }

                node.RegisterHarvestHit();
                UpgradeManager.Instance?.TryRollNodeDrop(node);

                if (carriedResources.Count >= carryCapacity) BeginReturnToNest();
            }
            else if (!node.IsHarvestable && isResourceTask && currentTaskTarget == node.transform)
            {
                // This node depleted while it was this turtle's active harvest
                // order — rather than keep bumping a dormant node until it
                // respawns, look for another node of the same resource type
                // right away (below capacity, so no reason to make the trip
                // back first) and only go idle if genuinely none are left.
                if (carriedResources.Count >= carryCapacity)
                {
                    BeginReturnToNest();
                }
                else
                {
                    SeekTargetResourceOrIdle(node.ResourceType);
                }
            }
            return;
        }

        RuneEffect rune = other.GetComponentInParent<RuneEffect>();
        if (rune != null)
        {
            rune.RegisterHit(this);
            return;
        }

        Coconut coconut = other.GetComponentInParent<Coconut>();
        if (coconut != null)
        {
            if (!DayStormCycle.IsStorming)
            {
                bool wasThisTurtlesTask = isResourceTask && currentTaskTarget == coconut.transform;
                bool consumed = coconut.RegisterHit(this);

                if (carriedResources.Count >= carryCapacity)
                {
                    BeginReturnToNest();
                }
                else if (wasThisTurtlesTask && consumed)
                {
                    // Fully consumed and destroyed by this hit — look for another.
                    SeekTargetResourceOrIdle(ResourceManager.ResourceType.Coconut);
                }
            }
            return;
        }

        JellyfishAgent jellyfish = other.GetComponentInParent<JellyfishAgent>();
        if (jellyfish != null)
        {
            if (!DayStormCycle.IsStorming)
            {
                bool wasThisTurtlesTask = isResourceTask && currentTaskTarget == jellyfish.transform;
                bool consumed = jellyfish.RegisterHit(this);

                if (carriedResources.Count >= carryCapacity)
                {
                    BeginReturnToNest();
                }
                else if (wasThisTurtlesTask && consumed)
                {
                    SeekTargetResourceOrIdle(ResourceManager.ResourceType.JellyfishGuts);
                }
            }
            return;
        }

        Watchtower watchtower = other.GetComponentInParent<Watchtower>();
        if (watchtower != null) watchtower.TryStationTurtle(this);
    }

    /// <summary>Always does a fresh "closest instance of type" search — called right after a node depletes/a Coconut or Jellyfish is consumed, after a full-capacity delivery trip completes, and by TrySeekTargetResource's idle recheck. Only goes idle once genuinely nothing of type is harvestable anywhere — but leaves hasTargetResource/targetResourceType completely untouched (they aren't even parameters here anymore), so TrySeekTargetResource's periodic recheck picks the objective back up the instant something reactivates, rather than losing it. Also idles without searching while storming, since harvesting is a daytime-only activity.</summary>
    private void SeekTargetResourceOrIdle(ResourceManager.ResourceType type)
    {
        if (DayStormCycle.IsStorming)
        {
            StopAndIdle();
            return;
        }

        Transform next = FindNearestHarvestTarget(type, transform.position);
        if (next != null)
        {
            MoveToResource(next);
            return;
        }

        // Nothing of this type is harvestable anywhere right now (e.g. every
        // node of it is momentarily dormant, or none exist at all) — the
        // objective stays alive either way, so TrySeekTargetResource's
        // periodic recheck picks it back up the instant one reactivates,
        // instead of forgetting it and waiting for a fresh player order. If
        // this turtle is already holding a partial load, though, don't just
        // sit on it indefinitely waiting for more of a resource that might
        // not come back for a while — deliver what it's got instead.
        harvestRetryTimer = harvestRetryInterval;
        if (carriedResources.Count > 0) BeginReturnToNest();
        else StopAndIdle();
    }

    /// <summary>While idle with a target resource objective whose last check found nothing harvestable (see SeekTargetResourceOrIdle), rechecks every harvestRetryInterval seconds rather than sitting idle forever until a fresh player order. Returns true if this recheck just sent the turtle off to harvest, so Update() skips idle behavior for this frame.</summary>
    private bool TrySeekTargetResource()
    {
        if (!hasTargetResource) return false;

        harvestRetryTimer -= Time.deltaTime;
        if (harvestRetryTimer > 0f) return false;

        SeekTargetResourceOrIdle(targetResourceType);
        return currentTaskTarget != null;
    }

    /// <summary>Resolves the ResourceManager.ResourceType a harvest target represents: a ResourceNode's own type, or the fixed type Coconut/JellyfishAgent always represent. False if target is none of these (not a valid harvest target at all).</summary>
    private static bool TryGetHarvestType(Transform target, out ResourceManager.ResourceType type)
    {
        if (target != null)
        {
            ResourceNode node = target.GetComponent<ResourceNode>();
            if (node != null)
            {
                type = node.ResourceType;
                return true;
            }

            if (target.GetComponent<Coconut>() != null)
            {
                type = ResourceManager.ResourceType.Coconut;
                return true;
            }

            if (target.GetComponent<JellyfishAgent>() != null)
            {
                type = ResourceManager.ResourceType.JellyfishGuts;
                return true;
            }
        }

        type = default;
        return false;
    }

    /// <summary>Finds the nearest still-valid harvest target of harvestType to fromPosition — the nearest still-harvestable ResourceNode of that type, or the nearest live Coconut/JellyfishAgent — or null if none exist. Backs SeekTargetResourceOrIdle's search.</summary>
    private static Transform FindNearestHarvestTarget(ResourceManager.ResourceType harvestType, Vector3 fromPosition)
    {
        switch (harvestType)
        {
            case ResourceManager.ResourceType.Coconut:
                return FindNearestCoconut(fromPosition);
            case ResourceManager.ResourceType.JellyfishGuts:
                return FindNearestJellyfish(fromPosition);
            default:
                return FindNearestHarvestableNode(harvestType, fromPosition);
        }
    }

    private static Transform FindNearestHarvestableNode(ResourceManager.ResourceType type, Vector3 fromPosition)
    {
        ResourceNode nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null || node.ResourceType != type || !node.IsHarvestable) continue;

            float sqrDistance = ((Vector2)node.transform.position - (Vector2)fromPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = node;
            }
        }

        return nearest != null ? nearest.transform : null;
    }

    private static Transform FindNearestCoconut(Vector3 fromPosition)
    {
        Coconut nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (Coconut coconut in Coconut.AllCoconuts)
        {
            if (coconut == null) continue;

            float sqrDistance = ((Vector2)coconut.transform.position - (Vector2)fromPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = coconut;
            }
        }

        return nearest != null ? nearest.transform : null;
    }

    private static Transform FindNearestJellyfish(Vector3 fromPosition)
    {
        JellyfishAgent nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (JellyfishAgent jellyfish in JellyfishAgent.AllJellyfish)
        {
            if (jellyfish == null) continue;

            float sqrDistance = ((Vector2)jellyfish.transform.position - (Vector2)fromPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = jellyfish;
            }
        }

        return nearest != null ? nearest.transform : null;
    }

    /// <summary>Clears the current order and goes idle. Called by a RuneEffect once it's done with this turtle.</summary>
    public void ClearTask()
    {
        StopAndIdle();
    }

    /// <summary>Snaps this turtle to position and freezes it there (kinematic rigidbody, fins stopped) — used by Watchtower while it's stationed. Steering is left alone so SetLookTarget can still rotate it to aim. Safe to call more than once.</summary>
    public void Park(Vector3 position)
    {
        if (isParked) return;

        isParked = true;
        CancelAggro();
        StopAndIdle();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            // Switch to Kinematic *before* teleporting: changing bodyType
            // recreates the underlying physics body, and doing that after
            // writing .position discards the write, snapping back to
            // wherever the still-Dynamic body's collision with the
            // Watchtower had already resolved it that same physics step
            // (i.e. the exact contact point, not the dock center) — this was
            // the "freezes wherever it first touched" bug.
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.position = position;
            transform.position = position;
        }
    }

    /// <summary>While parked, rotates this turtle to face target (e.g. whatever a Watchtower is currently aiming at) via the existing steering component — safe on a kinematic rigidbody, and doesn't move it since fins/locomotion are stopped. Pass null to hold the current facing.</summary>
    public void SetLookTarget(Transform target) => steering.SetTarget(target);

    /// <summary>Releases this turtle from a parked state. Just goes idle (like ClearTask) — there's nothing to restore: the target resource objective was never touched by being stationed, so TrySeekTargetResource picks it back up on its own the next time this turtle is idle during the day. Safe to call more than once.</summary>
    public void Unpark()
    {
        if (!isParked) return;

        isParked = false;
        if (rb != null) rb.bodyType = RigidbodyType2D.Dynamic;

        // Update() was fully gated off while parked, so wasStorming went stale
        // for however long that lasted — resync directly, or the very next
        // Update() could misfire CancelResourceTaskForNight/EndAggro off a
        // stale transition instead of reading the real current state.
        wasStorming = DayStormCycle.IsStorming;

        StopAndIdle();
    }

    /// <summary>Sets this turtle's crit chance to an upgrade-cumulative total. Called by UpgradeManager, safe to call repeatedly.</summary>
    public void SetCritChance(float value) => CritChance = value;

    /// <summary>Applies an upgrade-cumulative permanent speed multiplier to this turtle's locomotion. Called by UpgradeManager, safe to call repeatedly.</summary>
    public void ApplySpeedUpgrade(float multiplier) => locomotion.SetPermanentSpeedMultiplier(multiplier);

    private float campfireBonusTotal;

    /// <summary>Called by a Campfire the instant this turtle enters its radius. Overlapping campfires stack linearly, not last-wins.</summary>
    public void ApplyCampfireSpeedBuff(float bonusAmount)
    {
        campfireBonusTotal += bonusAmount;
        locomotion.SetCampfireSpeedMultiplier(1f + campfireBonusTotal);
    }

    /// <summary>Called by a Campfire the instant this turtle leaves its radius (with the same bonusAmount it applied on entry).</summary>
    public void RemoveCampfireSpeedBuff(float bonusAmount)
    {
        campfireBonusTotal = Mathf.Max(0f, campfireBonusTotal - bonusAmount);
        locomotion.SetCampfireSpeedMultiplier(1f + campfireBonusTotal);
    }

    [Header("Night Food Buffs")]
    [Tooltip("Particle effects played together while this turtle's Seaweed speed buff is active, stopped at dawn. Split across several systems on different Order in Layer values (e.g. 1/2/3), each emitting a share of the total, so individual particles land on different layers instead of every particle sharing one fixed draw order.")]
    [SerializeField] private ParticleSystem[] seaweedBuffEffects;
    [Tooltip("Speed multiplier granted for the night by receiving at least one Seaweed from the nest's periodic food distribution — see TurtleNest.SendWave.")]
    [SerializeField] private float seaweedSpeedMultiplier = 1.25f;
    [Tooltip("Particle effects played together while this turtle's Coconut knockback buff is active, stopped at dawn. Same multi-layer split as Seaweed Buff Effects.")]
    [SerializeField] private ParticleSystem[] coconutBuffEffects;
    [Tooltip("Impulse force applied to trash (pushing it away) on every hit while the Coconut buff is active — see TrashHealth.OnTriggerEnter2D.")]
    [SerializeField] private float coconutKnockbackForce = 5f;
    [Tooltip("Particle effects played together while this turtle's Jellyfish damage buff is active, stopped at dawn. Same multi-layer split as Seaweed Buff Effects.")]
    [SerializeField] private ParticleSystem[] jellyfishBuffEffects;
    [Tooltip("Bonus damage per hit granted for the night by receiving at least one JellyfishGuts in the night-start food distribution.")]
    [SerializeField] private int jellyfishBonusDamage = 1;

    private bool hasSeaweedBuff;
    private bool hasCoconutBuff;
    private int jellyfishBonusDamageActive;

    /// <summary>True while this turtle's Coconut knockback buff is active for the night — checked by TrashHealth on every hit.</summary>
    public bool HasCoconutKnockbackBuff => hasCoconutBuff;
    public float CoconutKnockbackForce => coconutKnockbackForce;

    /// <summary>Grants this turtle's Seaweed night buff (speed). Called once per unit received during TurtleNest's night-start distribution — flat on/off regardless of how many Seaweed this turtle received tonight, so repeat calls are harmless no-ops.</summary>
    public void ApplySeaweedBuff()
    {
        if (hasSeaweedBuff) return;

        hasSeaweedBuff = true;
        locomotion.SetTemporaryBuffSpeedMultiplier(seaweedSpeedMultiplier);
        PlayAll(seaweedBuffEffects);
    }

    /// <summary>Grants this turtle's Coconut night buff (knockback on hit). Flat on/off, same rationale as ApplySeaweedBuff.</summary>
    public void ApplyCoconutBuff()
    {
        if (hasCoconutBuff) return;

        hasCoconutBuff = true;
        PlayAll(coconutBuffEffects);
    }

    /// <summary>Grants this turtle's Jellyfish night buff (bonus damage on hit, folded into BonusDamageToTrash). Flat on/off, same rationale as ApplySeaweedBuff.</summary>
    public void ApplyJellyfishBuff()
    {
        if (jellyfishBonusDamageActive > 0) return;

        jellyfishBonusDamageActive = jellyfishBonusDamage;
        PlayAll(jellyfishBuffEffects);
    }

    private static void PlayAll(ParticleSystem[] effects)
    {
        if (effects == null) return;

        foreach (ParticleSystem effect in effects)
        {
            if (effect != null) effect.Play();
        }
    }

    private static void StopAll(ParticleSystem[] effects)
    {
        if (effects == null) return;

        foreach (ParticleSystem effect in effects)
        {
            if (effect != null) effect.Stop();
        }
    }

    /// <summary>Called once per day/night transition, from Update()'s falling edge: clears all three night food buffs — they're only ever meant to help through a single night's storm, so none of them linger into the day even if a turtle never got fed the night before. Replaces distributing/eating-driven refill with TurtleNest's lump-sum night-start distribution instead.</summary>
    private void ResetNightFoodBuffs()
    {
        ClearSeaweedBuff();
        ClearCoconutBuff();
        ClearJellyfishBuff();
    }

    /// <summary>Turns off the Seaweed speed buff early — called either at dawn (see ResetNightFoodBuffs) or by TurtleNest the instant Seaweed's dispense cooldown runs out with none left in storage to renew it, so the buff doesn't linger for the rest of the night on nothing. Safe to call when already off.</summary>
    public void ClearSeaweedBuff()
    {
        if (!hasSeaweedBuff) return;

        hasSeaweedBuff = false;
        locomotion.SetTemporaryBuffSpeedMultiplier(1f);
        StopAll(seaweedBuffEffects);
    }

    /// <summary>Turns off the Coconut knockback buff early — same rationale/callers as ClearSeaweedBuff.</summary>
    public void ClearCoconutBuff()
    {
        if (!hasCoconutBuff) return;

        hasCoconutBuff = false;
        StopAll(coconutBuffEffects);
    }

    /// <summary>Turns off the Jellyfish damage buff early — same rationale/callers as ClearSeaweedBuff.</summary>
    public void ClearJellyfishBuff()
    {
        if (jellyfishBonusDamageActive <= 0) return;

        jellyfishBonusDamageActive = 0;
        StopAll(jellyfishBuffEffects);
    }

    /// <summary>Permanent contribution to BonusDamageToTrash from the Hard Hat buff — kept separate from the temporary Jellyfish night buff (jellyfishBonusDamageActive) so the two add together without either overwriting the other.</summary>
    private int hardHatBonusDamage;

    /// <summary>Grants a permanent bonus to damage dealt to trash. Safe to call more than once — only takes effect the first time.</summary>
    public void ApplyHardHatBuff(int bonusDamage)
    {
        if (HasHardHatBuff) return;

        HasHardHatBuff = true;
        hardHatBonusDamage += bonusDamage;
        if (headRenderer != null && hardHatHeadSprite != null) headRenderer.sprite = hardHatHeadSprite;
    }

    /// <summary>Grants a permanent frequency boost to every propelling fin (faster, more frequent bursts), syncs all fins into lockstep (instead of their normal staggered stroke), and swaps the front fins to the flipper sprite. Safe to call more than once — only takes effect the first time.</summary>
    public void ApplyFlipperBuff(float frequencyMultiplier)
    {
        if (HasFlipperBuff) return;

        HasFlipperBuff = true;

        if (frontLeftFinRenderer != null && flipperFinSpriteLeft != null) frontLeftFinRenderer.sprite = flipperFinSpriteLeft;
        if (frontRightFinRenderer != null && flipperFinSpriteRight != null) frontRightFinRenderer.sprite = flipperFinSpriteRight;

        if (fins == null) return;
        foreach (LimbOscillator fin in fins)
        {
            if (fin == null) continue;

            fin.MultiplyFrequency(frequencyMultiplier);
            fin.SyncPhase();
        }
    }

    /// <summary>Cancels an in-progress resource-harvest task the instant night falls — the target objective (targetResourceType) isn't touched, only the transient in-flight task, so it resumes on its own the next time this turtle goes idle during the day (see TrySeekTargetResource). Ground-move/building tasks and aggro are untouched.</summary>
    private void CancelResourceTaskForNight()
    {
        if (isResourceTask) StopAndIdle();
    }

    /// <summary>Cancels an in-progress beeline-home trip the instant night falls, so a turtle that happened to fill up right before dusk responds to the storm (aggro, nest-guard idle) instead of ignoring it to walk its load home — it's still carrying everything it collected, just no longer forced to beeline; CheckPassiveNestDelivery still drops it off in passing whenever normal movement (e.g. nest-guard idle already heads toward the nest while storming) brings it back into range. Leaves an already-in-progress delivery (deliverCoroutine running, i.e. the turtle already arrived and is popping units off) alone rather than cutting it off mid-flight.</summary>
    private void CancelReturnTripForNight()
    {
        if (!isReturningToNest || deliverCoroutine != null) return;

        isReturningToNest = false;
        isFollowingPath = false;
        steering.SetTarget(null);
        SetFinsPlaying(false);
    }

    /// <summary>Only called while not already aggroed (see Update()), so this doubles as the one place aggroSuppressionTimer ever ticks down — it counts down regardless of day/night (a movement command given well before a storm starts shouldn't leave a stale window active by the time it would matter), but only actually blocks acquisition while it's both storming and still active.</summary>
    private void TryAcquireAggroTarget()
    {
        if (aggroSuppressionTimer > 0f) aggroSuppressionTimer -= Time.deltaTime;

        if (!DayStormCycle.IsStorming) return;
        if (aggroSuppressionTimer > 0f) return;

        TrashHealth nearest = TrashHealth.FindNearest(transform.position, aggroDistance);
        if (nearest == null) return;

        hadSavedTask = currentTaskTarget != null;
        savedTaskTarget = currentTaskTarget;
        savedTaskIsGroundMove = isGroundMove;
        savedTaskIsResourceTask = isResourceTask;

        isAggroed = true;
        aggroTarget = nearest;
        ApplyTask(nearest.transform, isGroundMove: false, useLineOfSightShortcut: true);
    }

    /// <summary>
    /// While aggro-chasing a piece of trash, first checks whether the target
    /// is currently in deep water — if so, swims to and holds at the nearest
    /// shoreline point (see UpdateWaitAtShore) without dropping aggro, until
    /// it drifts back out. Otherwise re-evaluates every frame whether there's
    /// a clear line of sight to it: if so, ignores pathfinding entirely and
    /// steers straight at it (a visible target doesn't need a route around
    /// anything, and trash keeps moving too fast for a stored path to stay
    /// accurate anyway); if blocked and not already navigating a path, kicks
    /// off one around whatever's in the way. Never repaths while already
    /// following one just because line of sight is still blocked.
    /// </summary>
    private void UpdateAggroSteering()
    {
        if (aggroTarget == null) return;

        Transform target = aggroTarget.transform;

        // Trash can venture into deep water on its way to the nest, but a
        // turtle can never follow it out there (see BeginPathTo's
        // avoidDeepWater) — swim to shore and wait rather than dropping aggro,
        // resuming the instant it drifts back into the shallows/onto land.
        if (PathfindingManager.Instance != null && PathfindingManager.Instance.IsDeepWater(target.position))
        {
            UpdateWaitAtShore(target.position);
            return;
        }

        isWaitingAtShore = false;
        shoreUnreachable = false;

        bool hasLineOfSight = PathfindingManager.Instance == null
            || PathfindingManager.Instance.HasLineOfSight(transform.position, target.position);

        if (hasLineOfSight)
        {
            isFollowingPath = false;
            pathFinalDestination = target;
            steering.SetTarget(target);
            // Resuming from a shore wait leaves fins stopped, and nothing else
            // in an ongoing aggro chase turns them back on — ApplyTask only
            // does that once, at the moment aggro is first acquired.
            SetFinsPlaying(true);
        }
        else if (!isFollowingPath)
        {
            SetFinsPlaying(BeginPathTo(target));
        }
    }

    /// <summary>Swims toward the nearest shoreline point to a deep-water aggro target and holds there once arrived, rather than freezing wherever the turtle happened to be when the target went out of reach. Only re-picks/restarts the path when the target has drifted far enough for the shore point to meaningfully change (beyond waypointArrivalDistance) — not every frame — so an in-progress swim to shore isn't constantly restarted by tiny target jitter. If the shore point turns out unreachable (fully hemmed in by obstacles), holds still with fins off — persisting that state via shoreUnreachable rather than just this one frame — instead of leaving fins on with nothing steering the turtle.</summary>
    private void UpdateWaitAtShore(Vector3 targetPosition)
    {
        Vector3 shorePoint = PathfindingManager.Instance.NearestNonDeepWaterPoint(targetPosition);
        bool needsNewPath = !isWaitingAtShore || Vector2.Distance(shoreWaitMarker.position, shorePoint) > waypointArrivalDistance;

        if (needsNewPath)
        {
            shoreWaitMarker.position = shorePoint;
            isWaitingAtShore = true;
            shoreUnreachable = !BeginPathTo(shoreWaitMarker);
        }

        if (shoreUnreachable)
        {
            SetFinsPlaying(false);
            return;
        }

        bool arrived = !isFollowingPath && Vector2.Distance(transform.position, shoreWaitMarker.position) <= arrivalDistance;
        SetFinsPlaying(!arrived);
    }

    private void EndAggro()
    {
        isAggroed = false;
        aggroTarget = null;
        isWaitingAtShore = false;
        shoreUnreachable = false;

        if (hadSavedTask)
        {
            ApplyTask(savedTaskTarget, savedTaskIsGroundMove, savedTaskIsResourceTask);
        }
        else
        {
            StopAndIdle();
        }

        hadSavedTask = false;
    }

    private void CancelAggro()
    {
        isAggroed = false;
        aggroTarget = null;
        hadSavedTask = false;
        isWaitingAtShore = false;
        shoreUnreachable = false;
    }

    private void ApplyTask(Transform target, bool isGroundMove, bool isResourceTask = false, bool useLineOfSightShortcut = false)
    {
        currentTaskTarget = target;
        this.isGroundMove = isGroundMove;
        this.isResourceTask = isResourceTask;

        // A real task always takes over from whatever idle sub-state was active.
        hasIdleAnchor = false;
        wasHeadingToNest = false;
        isWanderMoving = false;
        locomotion.SetSpeedMultiplier(1f);

        if (useLineOfSightShortcut)
        {
            // Clear any stale path from whatever this turtle was doing before
            // (e.g. idle-wandering mid-path) so UpdateAggroSteering evaluates
            // line of sight fresh rather than assuming an old path still applies.
            isFollowingPath = false;
            pathFinalDestination = target;
            UpdateAggroSteering();
        }
        else
        {
            SetFinsPlaying(BeginPathTo(target));
        }

        UpdateBuildingCollision(target);
    }

    private void StopAndIdle()
    {
        isGroundMove = false;
        isResourceTask = false;
        currentTaskTarget = null;
        isFollowingPath = false;
        steering.SetTarget(null);
        SetFinsPlaying(false);
        UpdateBuildingCollision(null);

        // Mirrors ApplyTask's own reset of these three fields: any transition
        // INTO idle must also start idle fresh, not assume whatever
        // nest-guard/wander navigation was already in progress is still
        // valid. Without this, a stray StopAndIdle() call (e.g. from
        // SeekTargetResourceOrIdle's storming branch, fired at the tail of a
        // delivery coroutine after night has already fallen) could wipe out
        // an in-progress UpdateIdle nest-guard path/fins while leaving
        // wasHeadingToNest stuck true — the next UpdateIdle call would then
        // see "already heading to nest" and never reissue BeginPathTo,
        // freezing the turtle in place (no steering, no fins) until the
        // storm ends and resets the flag naturally.
        hasIdleAnchor = false;
        wasHeadingToNest = false;
        isWanderMoving = false;
    }

    private void SpawnHarvestPopEffect(Vector3 position, Sprite icon)
    {
        if (harvestPopEffectPrefab == null) return;

        GameObject instance = Instantiate(harvestPopEffectPrefab, position, Quaternion.identity);
        instance.GetComponent<ResourcePopEffect>()?.Initialize(icon, position, null);
    }

    /// <summary>Adds one unit of type to the carry list if it hasn't already hit carryCapacity, showing a shell-slot icon and a harvest-pop effect. Public so Coconut can call it directly (it isn't a ResourceNode). Returns false if capacity is already full — the unit doesn't fit, no loss, caller just stops adding.</summary>
    public bool CollectResourceUnit(ResourceManager.ResourceType type, Vector3 sourcePosition)
    {
        if (carriedResources.Count >= carryCapacity) return false;

        Sprite icon = carriedVisuals != null ? carriedVisuals.GetRandomIcon(type) : null;
        int slot = carriedVisuals != null ? carriedVisuals.ShowNext(icon) : -1;
        carriedResources.Add(new CarriedResource { Type = type, Icon = icon, SlotIndex = slot });
        SpawnHarvestPopEffect(sourcePosition, icon);
        return true;
    }

    /// <summary>Called once carried resources reach capacity: clears the just-finished resource task and begins the beeline trip back to the nest, bypassing normal task/aggro/idle logic entirely while active (see isReturningToNest at the top of Update()). Falls back to normal idle/task-seeking next frame if the nest is currently unreachable (e.g. hemmed in by obstacles/deep water) rather than leaving the turtle stuck "returning" forever; CheckPassiveNestDelivery's drive-by check still delivers it in passing whenever ordinary movement brings it back into range.</summary>
    private void BeginReturnToNest()
    {
        if (isReturningToNest) return;

        // Clear the just-finished resource task's flags directly (not via
        // StopAndIdle, which would also stomp the steering/path state we're
        // about to re-set below).
        isGroundMove = false;
        isResourceTask = false;
        currentTaskTarget = null;

        isReturningToNest = true;
        CancelAggro();

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        bool pathStarted = BeginPathTo(nest);
        SetFinsPlaying(pathStarted);

        if (!pathStarted) isReturningToNest = false;
    }

    /// <summary>Runs every frame in place of the normal storm/aggro/idle logic while returning — a returning-with-cargo turtle beelines for the nest, except a storm starting mid-trip cancels it instead (see CancelReturnTripForNight), so this only ever runs during the day or for however briefly it takes night's edge-check to catch up.</summary>
    private void UpdateReturnToNest()
    {
        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        if (nest == null) return; // nest destroyed (game over) mid-trip — just stop evaluating, no crash

        if (Vector2.Distance(transform.position, nest.position) <= nestDeliveryRadius && deliverCoroutine == null)
        {
            deliverCoroutine = StartCoroutine(DeliverCarriedResources(resumeAfterDelivery: true));
        }
    }

    /// <summary>Drive-by delivery for a turtle not on a dedicated return trip: if it's carrying anything and normal movement has brought it within nest range, deliver in passing without touching its current task/steering/path state.</summary>
    private void CheckPassiveNestDelivery()
    {
        if (carriedResources.Count == 0 || deliverCoroutine != null) return;

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        if (nest == null) return;

        if (Vector2.Distance(transform.position, nest.position) <= nestDeliveryRadius)
        {
            deliverCoroutine = StartCoroutine(DeliverCarriedResources(resumeAfterDelivery: false));
        }
    }

    /// <summary>Pops each carried unit off the shell in delivery order and flies it into the nest, adding to ResourceManager only as each individual pop-effect's flight completes — not at nest-arrival. Handles both materials (Wood/Rock) and food (Seaweed/Coconut/JellyfishGuts) identically; food's per-type counts are read back out and distributed to turtles as night buffs by TurtleNest.SendWave, not spent here. When resumeAfterDelivery is true (a dedicated full-load return trip), also clears the return-trip movement state and resumes the target resource objective afterward (nearest current instance, not necessarily the same node — unless a movement command was given mid-trip, which takes priority instead); when false (a drive-by delivery mid-task), leaves whatever the turtle is currently doing untouched.</summary>
    private IEnumerator DeliverCarriedResources(bool resumeAfterDelivery)
    {
        if (resumeAfterDelivery)
        {
            isReturningToNest = false;
            isFollowingPath = false;
            steering.SetTarget(null);
            SetFinsPlaying(false);
        }

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        Vector3 nestPosition = nest != null ? nest.position : transform.position;

        for (int i = 0; i < carriedResources.Count; i++)
        {
            CarriedResource unit = carriedResources[i];
            Vector3 fromPosition = carriedVisuals != null ? carriedVisuals.GetSlotWorldPosition(unit.SlotIndex) : transform.position;
            carriedVisuals?.ClearSlot(unit.SlotIndex);

            if (deliveryPopEffectPrefab != null)
            {
                GameObject instance = Instantiate(deliveryPopEffectPrefab, fromPosition, Quaternion.identity);
                ResourceManager.ResourceType capturedType = unit.Type;
                instance.GetComponent<ResourcePopEffect>()?.Initialize(
                    unit.Icon, fromPosition, nestPosition,
                    () =>
                    {
                        ResourceManager.Instance?.Add(capturedType, 1);
                        ScoreManager.Instance?.AddScore(1);
                    });
            }
            else
            {
                ResourceManager.Instance?.Add(unit.Type, 1); // no prefab wired yet — still deliver correctly, just instantly
                ScoreManager.Instance?.AddScore(1);
            }

            yield return new WaitForSeconds(deliveryStaggerDelay);
        }

        carriedResources.Clear();
        deliverCoroutine = null;

        // The !isGroundMove guard stops a movement command issued mid-trip
        // from being clobbered the instant this trip finishes.
        if (resumeAfterDelivery && !isGroundMove && hasTargetResource) SeekTargetResourceOrIdle(targetResourceType);
    }

    /// <summary>
    /// A normal avoid-deep-water search from a start point that's itself deep
    /// water has no solution at all — every cell reachable from there is
    /// itself deep water and therefore blocked, so A* can't move anywhere
    /// (only the exact start cell is ever exempted). That's exactly what
    /// physics shoving a turtle into the ocean produces, and it used to leave
    /// the turtle frozen there forever with no route out. Detects that case
    /// and, only then, splits the request into two legs stitched into one
    /// waypoint list: first swim to the nearest non-deep-water point (a
    /// deep-water-permissive search, since crossing some deep water to get
    /// out is unavoidable while already standing in it), then a normal
    /// avoid-deep-water search from there to the real destination. If that
    /// second leg genuinely can't reach the destination, still returns the
    /// escape-only waypoints — getting out of the water is strictly better
    /// than staying frozen in it, and the very next path request (this turtle
    /// no longer starting from deep water) resumes normally. Falls straight
    /// through to a single ordinary search when the turtle isn't in deep
    /// water to begin with — the overwhelming majority of calls.
    /// </summary>
    private List<Vector3> FindPathOutOfDeepWaterIfNeeded(Vector3 destinationPosition)
    {
        PathfindingManager pathfinding = PathfindingManager.Instance;

        if (!pathfinding.IsDeepWater(transform.position))
        {
            return pathfinding.FindPath(transform.position, destinationPosition, avoidDeepWater: true, allowDiagonalSqueeze: true);
        }

        Vector3 escapePoint = pathfinding.NearestNonDeepWaterPoint(transform.position);
        List<Vector3> escapeLeg = pathfinding.FindPath(transform.position, escapePoint, avoidDeepWater: false, allowDiagonalSqueeze: true);
        if (escapeLeg == null) return null; // even ignoring deep water, genuinely boxed in by obstacles

        // escapeLeg being an empty (non-null) list means start and escapePoint
        // already share/neighbor a cell, per FindPath's own convention — but
        // escapePoint is still the one real step needed to actually clear the
        // water, so it's always added as an explicit waypoint here.
        List<Vector3> combined = new List<Vector3>(escapeLeg) { escapePoint };

        List<Vector3> onwardLeg = pathfinding.FindPath(escapePoint, destinationPosition, avoidDeepWater: true, allowDiagonalSqueeze: true);
        if (onwardLeg != null) combined.AddRange(onwardLeg);

        return combined;
    }

    /// <summary>
    /// Starts moving toward destination, requesting a path around nature
    /// obstacles (and deep water — turtles can never path further out than the
    /// shallows) from PathfindingManager. Falls back to steering straight at
    /// destination (today's exact pre-pathfinding behavior) only if there's no
    /// manager to consult at all; if a manager is present but genuinely can't
    /// find a deep-water-safe path, refuses to move and returns false rather
    /// than ever falling back to a raw direct steer that could cut straight
    /// across the ocean. Returns true whenever steering actually got a real
    /// target (a waypoint, or the destination directly) — callers must gate
    /// SetFinsPlaying on this return value, not call it unconditionally:
    /// TurtleLocomotion's fins fire forward impulses regardless of whether
    /// TurtleTargetSteering's target is null, so leaving fins on after a
    /// failed path here would just cruise the turtle in a straight line
    /// forever with nothing left to steer or stop it — this was the actual
    /// "swims off into the distance" bug. Used by every destination-seeking
    /// behavior — real orders (via ApplyTask), idle wander, and the storm
    /// nest-guard — so pathFinalDestination is tracked independently of
    /// currentTaskTarget, which idle/nest-guard movement must never touch.
    /// See FindPathOutOfDeepWaterIfNeeded for what happens when this turtle
    /// itself is currently sitting in deep water (e.g. shoved there by a
    /// physics collision) rather than just its destination.
    /// </summary>
    private bool BeginPathTo(Transform destination)
    {
        pathFinalDestination = destination;

        bool hasManager = destination != null && PathfindingManager.Instance != null;
        currentPath = hasManager
            ? FindPathOutOfDeepWaterIfNeeded(destination.position)
            : null;

        if (currentPath != null && currentPath.Count > 0)
        {
            currentPathIndex = 0;
            isFollowingPath = true;
            pathWaypointMarker.position = currentPath[0];
            steering.SetTarget(pathWaypointMarker);
            return true;
        }

        isFollowingPath = false;

        if (!hasManager)
        {
            // No manager to consult at all — fall back to steering straight
            // at the raw destination (today's exact pre-pathfinding behavior).
            steering.SetTarget(destination);
            return true;
        }

        if (currentPath != null)
        {
            // Empty (non-null) list: start and goal already share a cell (or
            // are adjacent) — no waypoints needed, just steer straight at the
            // destination for this final short hop.
            steering.SetTarget(destination);
            return true;
        }

        // Genuinely unreachable (deep water/obstacles fully block every
        // route) — refuse to move.
        steering.SetTarget(null);
        return false;
    }

    /// <summary>Advances through an in-progress path's waypoints as they're reached, converging steering onto the real destination once the path is exhausted. Computes nothing — a path is only ever produced once, by BeginPathTo.</summary>
    private void UpdatePathFollowing()
    {
        if (!isFollowingPath) return;

        if (pathFinalDestination == null)
        {
            // Destination was destroyed mid-path (e.g. chased trash killed by
            // another turtle) — whatever redirected this turtle already called
            // StopAndIdle/ApplyTask this frame or will next frame.
            isFollowingPath = false;
            return;
        }

        if (isResourceTask && PathfindingManager.Instance != null
            && Vector2.Distance(transform.position, pathFinalDestination.position) <= resourceLineOfSightShortcutRange
            && PathfindingManager.Instance.HasLineOfSight(transform.position, pathFinalDestination.position, pathFinalDestination))
        {
            isFollowingPath = false;
            steering.SetTarget(pathFinalDestination);
            return;
        }

        if (Vector2.Distance(transform.position, pathWaypointMarker.position) > waypointArrivalDistance) return;

        currentPathIndex++;
        if (currentPathIndex >= currentPath.Count)
        {
            isFollowingPath = false;
            steering.SetTarget(pathFinalDestination);
        }
        else
        {
            pathWaypointMarker.position = currentPath[currentPathIndex];
        }
    }

    /// <summary>Only meaningful while resource-seeking (isResourceTask) — outside that, just keeps the tracking position/timer fresh so a stuck check doesn't fire off stale data the next time a resource task starts. Every stuckCheckInterval seconds, checks whether this turtle has actually covered stuckMovementThreshold of ground since the last check; if not, it's presumed stuck (e.g. oscillating dead-on against a bouncy resource with nothing nearby to naturally curve the approach — an isolated seaweed patch out in open water is the textbook case) and gets a one-off sideways impulse, picked left or right at random, to break the symmetry. Purely a physical nudge — doesn't touch the task/path state at all, so the turtle keeps pursuing the same target afterward and simply approaches at a slightly different angle next time.</summary>
    private void UpdateStuckDetection()
    {
        if (!isResourceTask || currentTaskTarget == null)
        {
            stuckCheckTimer = 0f;
            stuckCheckPosition = transform.position;
            return;
        }

        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer < stuckCheckInterval) return;

        float moved = Vector2.Distance(transform.position, stuckCheckPosition);
        stuckCheckTimer = 0f;
        stuckCheckPosition = transform.position;

        if (moved >= stuckMovementThreshold) return;

        Vector2 facing = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity.normalized : (Vector2)transform.right;
        Vector2 lateral = Vector2.Perpendicular(facing);
        if (Random.value < 0.5f) lateral = -lateral;

        rb.AddForce(lateral * stuckNudgeForce, ForceMode2D.Impulse);
    }

    /// <summary>
    /// Runs whenever this turtle has no real task and isn't aggroed. While
    /// storming and still further than nestGuardDistance from the nest, it
    /// heads straight there at full speed to help guard it; otherwise it
    /// ambles randomly nearby (see WanderIdle). This is just idle's default
    /// during a storm, not a protected state — ApplyTask (a real order)
    /// overrides it immediately regardless of which sub-state this is in.
    /// </summary>
    private void UpdateIdle(bool storming)
    {
        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        bool headingToNest = storming && nest != null
            && Vector2.Distance(transform.position, nest.position) > nestGuardDistance;

        if (headingToNest)
        {
            if (!wasHeadingToNest)
            {
                // Throttled by nestGuardRetryTimer (armed below on failure) so
                // a genuinely unreachable nest doesn't re-run a full pathfind
                // every single frame for as long as it stays blocked.
                if (nestGuardRetryTimer > 0f)
                {
                    nestGuardRetryTimer -= Time.deltaTime;
                    return;
                }

                isWanderMoving = false;

                // Only latch wasHeadingToNest once a path actually starts —
                // if the nest is genuinely unreachable this frame (e.g.
                // hemmed in by obstacles), BeginPathTo already left this
                // turtle with no path and no fins (see its own fallback), and
                // latching true regardless would block every future retry:
                // next frame's !wasHeadingToNest guard above would never be
                // true again, freezing the turtle here with nothing steering
                // it until something unrelated (a player order, the storm
                // ending) happens to reset the flag.
                bool pathStarted = BeginPathTo(nest);
                SetFinsPlaying(pathStarted);
                wasHeadingToNest = pathStarted;

                if (!pathStarted)
                {
                    nestGuardRetryTimer = nestGuardRetryInterval;
                    return;
                }
            }

            locomotion.SetSpeedMultiplier(1f);
            wasHeadingToNest = true;
            return;
        }

        if (wasHeadingToNest)
        {
            // Just arrived near the nest (or the storm just ended) — wander fresh from here.
            hasIdleAnchor = false;
        }
        wasHeadingToNest = false;

        WanderIdle();
    }

    /// <summary>Randomly turns and ambles to nearby points (slowly, see idleSpeedMultiplier), pausing between each — replaces standing completely motionless while idle.</summary>
    private void WanderIdle()
    {
        if (!hasIdleAnchor)
        {
            idleAnchor = transform.position;
            hasIdleAnchor = true;
            isWanderMoving = false;
            idleWanderTimer = 0f;
        }

        if (isWanderMoving)
        {
            if (Vector2.Distance(transform.position, idleWanderMarker.position) <= arrivalDistance)
            {
                isWanderMoving = false;
                SetFinsPlaying(false);
                locomotion.SetSpeedMultiplier(1f);
                idleWanderTimer = idleWanderInterval + Random.Range(-idleWanderIntervalVariance, idleWanderIntervalVariance);
            }

            return;
        }

        idleWanderTimer -= Time.deltaTime;
        if (idleWanderTimer > 0f) return;

        Vector3 candidate = idleAnchor;
        bool foundValidCandidate = false;

        // Re-roll a few times if a candidate point lands in deep water, rather
        // than only discovering that after BeginPathTo already refuses to path
        // there (which would just leave the turtle standing still that cycle).
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * idleWanderRadius;
            candidate = idleAnchor + new Vector3(offset.x, offset.y, 0f);

            if (PathfindingManager.Instance == null || !PathfindingManager.Instance.IsDeepWater(candidate))
            {
                foundValidCandidate = true;
                break;
            }
        }

        if (!foundValidCandidate)
        {
            // Every reroll still landed in deep water (e.g. this anchor's whole
            // wander radius is mostly ocean) — skip this cycle and try again
            // at the next interval rather than forcing a bad candidate through.
            idleWanderTimer = idleWanderInterval;
            return;
        }

        idleWanderMarker.position = candidate;

        if (!BeginPathTo(idleWanderMarker))
        {
            // Not deep water (already ruled out above) but still unreachable
            // — e.g. hemmed in by resource obstacles — skip this cycle and
            // try again at the next interval rather than leaving fins on with
            // nothing steering the turtle toward a point it can never reach.
            idleWanderTimer = idleWanderInterval;
            return;
        }

        locomotion.SetSpeedMultiplier(idleSpeedMultiplier);
        SetFinsPlaying(true);
        isWanderMoving = true;
    }

    /// <summary>
    /// Turtles and buildings normally never collide (see the Turtle/Building
    /// layer collision exclusion, which is what lets turtles pass through
    /// walls). Physics2D.IgnoreCollision cannot override that — a layer-level
    /// exclusion always wins over a per-collider-pair one — so instead this
    /// moves the whole turtle onto a separate "TurtleInteracting" layer for
    /// as long as its current task target is an interactable building, then
    /// moves it back the moment the target changes to anything else.
    /// Interactable buildings (runes) live on their own "InteractableBuilding"
    /// layer, separate from plain "Building" (walls, beds), so an interacting
    /// turtle only ever collides with runes — never with a wall it happens
    /// to pass near on the way.
    /// </summary>
    private void UpdateBuildingCollision(Transform target)
    {
        BuildingHealth building = target != null ? target.GetComponent<BuildingHealth>() : null;
        bool shouldCollide = building != null && building.IsInteractable;

        if (shouldCollide == isCollidingWithBuilding) return;

        isCollidingWithBuilding = shouldCollide;

        if (interactingLayer < 0) return;
        gameObject.layer = shouldCollide ? interactingLayer : normalLayer;
    }

    private void SetFinsPlaying(bool playing)
    {
        if (fins == null) return;

        foreach (LimbOscillator fin in fins)
        {
            if (fin != null) fin.SetPlaying(playing);
        }
    }

    private void ApplyTint(Color tint)
    {
        foreach (SpriteRenderer sr in spriteRenderers)
        {
            if (sr != null) sr.color = tint;
        }
    }

    private void RevertTint()
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null) spriteRenderers[i].color = originalColors[i];
        }
    }
}
