using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-turtle brain: tracks selection state and the current move/harvest
/// order. A ground-point order stops once the turtle arrives; a resource
/// order never self-stops — combined with a bouncy Physics Material 2D on the
/// resource's collider, the turtle keeps bouncing off and re-approaching,
/// harvesting whenever its head (see TurtleHeadHitbox, not the shell) touches
/// it, until redirected elsewhere. With no order and not aggroed, a turtle
/// isn't fully passive — see UpdateIdle/WanderIdle: it ambles to random
/// nearby points at reduced speed instead of standing still.
///
/// Independently of player orders, while DayStormCycle.IsStorming: resource
/// harvesting stops yielding anything (see HandleHeadHit) though the physical
/// bounce still works; a turtle mid-harvest abandons that task the instant the
/// storm starts (see CancelResourceTaskForStorm) and, once idle, heads toward
/// the nest to help guard it instead of merely wandering (see UpdateIdle) —
/// this is just idle's default during a storm, not a protected state, so a new
/// player order overrides it immediately like anything else. Separately, a
/// turtle that notices trash within its aggro distance temporarily abandons
/// whatever it was doing to go attack the nearest one (same bounce-and-collide
/// mechanic as harvesting, damaging the trash's TrashHealth on each hit), then
/// resumes its previous task once that trash is destroyed. A new player order
/// always overrides an in-progress aggro chase — but only for the rest of the
/// storm: the instant a storm begins, this turtle snapshots whatever it was
/// doing at that moment (see CapturePreStormTask), and once the storm fully
/// ends it's returned to exactly that (see RestorePreStormTask), discarding
/// whatever aggro chases or player orders happened in between.
///
/// Turtles otherwise pass through buildings (see the Turtle/Building layer
/// collision exclusion) so they don't get stuck on walls. When targeting an
/// interactable building (see BuildingHealth.IsInteractable) specifically,
/// this turtle moves onto a separate "TurtleInteracting" layer (which must
/// collide with Building) for as long as that's its target, so it can
/// physically reach and touch it — this is how a turtle bumps into a rune
/// (see RuneEffect) repeatedly until it earns that rune's buff (indefinite:
/// Hard Hat adds BonusDamageToTrash, Flipper speeds up every fin's
/// oscillation frequency).
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

    [Header("Nest Defense")]
    [Tooltip("While storming, an idle turtle (no order, not aggroed) heads toward the nest to help guard it, stopping once within this distance rather than stacking on top of it.")]
    [SerializeField] private float nestGuardDistance = 2f;

    [Header("Resource Carrying")]
    [Tooltip("Maximum resource units this turtle can carry before automatically returning to the nest.")]
    [SerializeField] private int carryCapacity = 5;
    [Tooltip("Distance (world units) from the nest at which carried resources are delivered.")]
    [SerializeField] private float nestDeliveryRadius = 1.5f;
    [Tooltip("Maximum food units (Seaweed, Coconut, ...) this turtle can carry before automatically returning to the Food Building instead of the nest.")]
    [SerializeField] private int foodCarryCapacity = 5;
    [Tooltip("Distance (world units) from the Food Building at which carried food is delivered.")]
    [SerializeField] private float foodDeliveryRadius = 1.5f;
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

    /// <summary>Extra damage this turtle deals to trash per hit, from the Hard Hat buff.</summary>
    public int BonusDamageToTrash { get; private set; }

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

    // Independent of the aggro-resume fields above, which only span a single
    // aggro chase: this remembers whatever the turtle was doing at the exact
    // moment the storm began, surviving any number of player orders or aggro
    // chases given during the storm, so it can be restored once the storm
    // fully ends regardless of what the player did in the meantime.
    private bool wasStorming;
    private bool hasPreStormTask;
    private Transform preStormMoveMarker;
    private Transform preStormTaskTarget;
    private bool preStormTaskIsGroundMove;
    private bool preStormTaskIsResourceTask;

    // Idle sub-state: whenever there's no real task and no aggro, a turtle
    // either heads toward the nest to guard it (storming, and still far from
    // it) or ambles randomly nearby (otherwise) — see UpdateIdle. Entirely
    // separate from currentTaskTarget/isGroundMove/isResourceTask, so it never
    // gets mistaken for a real task by the pre-storm snapshot above, and any
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

    /// <summary>Whatever this turtle is currently ordered toward (a resource node, a building, a ground point, an aggro target...), or null if idle. Lets a building (e.g. Watchtower) confirm a physical bump was an actual deliberate order to interact with it, not an incidental collision while passing by on some other task.</summary>
    public Transform CurrentTaskTarget => currentTaskTarget;

    /// <summary>One carried unit: its resource type, the specific sprite variant rolled for it at harvest time (reused for its shell slot and both pop effects so it looks consistent for its whole trip), and the exact shell slot it occupies (both carry lists below share one slot pool, so delivery must clear the slot each unit actually holds, not just its index within its own list).</summary>
    private struct CarriedResource
    {
        public ResourceManager.ResourceType Type;
        public Sprite Icon;
        public int SlotIndex;
    }

    private CarriedResourceVisuals carriedVisuals;
    private readonly List<CarriedResource> carriedResources = new List<CarriedResource>();
    private bool isReturningToNest;
    private Transform resumeResourceNodeTarget;
    private Coroutine deliverCoroutine;

    // Food (Seaweed, Coconut, ...) is carried and delivered exactly like Wood/Rock
    // above, just to the Food Building instead of the Nest — a fully parallel,
    // independent capacity/state/coroutine so a turtle can carry a mix of both
    // simultaneously (e.g. Wood plus a bonus Coconut) without either capacity
    // blocking the other.
    private readonly List<CarriedResource> carriedFoodResources = new List<CarriedResource>();
    private bool isReturningToFood;
    private Coroutine deliverFoodCoroutine;

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
        // Separate marker for the pre-storm snapshot below, since moveTargetMarker
        // gets overwritten by any ground-move order given during the storm.
        preStormMoveMarker = new GameObject($"{name} PreStormMoveTarget").transform;
        // Separate marker for idle wandering, for the same reason.
        idleWanderMarker = new GameObject($"{name} IdleWanderTarget").transform;
        // Repositioned to each successive path waypoint while following one.
        pathWaypointMarker = new GameObject($"{name} PathWaypoint").transform;

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
        if (preStormMoveMarker != null) Destroy(preStormMoveMarker.gameObject);
        if (idleWanderMarker != null) Destroy(idleWanderMarker.gameObject);
        if (pathWaypointMarker != null) Destroy(pathWaypointMarker.gameObject);
    }

    private void Update()
    {
        if (isParked) return;

        UpdatePathFollowing();

        // A turtle ferrying a full load ignores aggro/storms entirely and
        // just beelines for the nest — bypassing everything below exactly
        // like isParked already does above. A storm starting or ending
        // entirely during a return trip is deliberately not captured/restored
        // (nothing storm-relevant was happening for this turtle during that
        // window); it re-enters normal Update() flow fresh the moment it
        // resumes harvesting after delivering.
        if (isReturningToNest)
        {
            UpdateReturnToNest();
            return;
        }

        // Same bypass as isReturningToNest above, for a dedicated food-delivery
        // trip — the two are mutually exclusive (see BeginReturnToFood's guard),
        // so only one of these two branches is ever active at a time.
        if (isReturningToFood)
        {
            UpdateReturnToFood();
            return;
        }

        // Opportunistic drive-by delivery: even a turtle that isn't on a
        // dedicated return trip drops off whatever partial load it's holding
        // the moment normal activity (harvesting, idling, wandering) brings
        // it within range of the nest/Food Building, without interrupting
        // that activity.
        CheckPassiveNestDelivery();
        CheckPassiveFoodDelivery();

        bool storming = DayStormCycle.IsStorming;
        if (storming && !wasStorming)
        {
            CapturePreStormTask();
            CancelResourceTaskForStorm();
        }
        else if (!storming && wasStorming)
        {
            isAggroed = false;
            aggroTarget = null;
            hadSavedTask = false;
            RestorePreStormTask();
        }
        wasStorming = storming;

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

        UpdateIdle(storming);
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

    public void MoveToPoint(Vector3 worldPoint)
    {
        CancelAggro();
        moveTargetMarker.position = worldPoint;
        ApplyTask(moveTargetMarker, isGroundMove: true);
    }

    public void MoveToResource(Transform resourceTransform)
    {
        if (isReturningToNest || isReturningToFood) return;

        CancelAggro();
        ApplyTask(resourceTransform, isGroundMove: false, isResourceTask: true);
    }

    /// <summary>Sends the turtle to an interactable building, switching it onto the TurtleInteracting layer so it can physically reach and bump into it.</summary>
    public void MoveToBuilding(Transform buildingTransform)
    {
        CancelAggro();
        ApplyTask(buildingTransform, isGroundMove: false);
    }

    /// <summary>Called by TurtleHeadHitbox — only the head's contact counts as a harvest/rune hit, not the shell.</summary>
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

                if (ResourceManager.IsFoodType(node.ResourceType))
                {
                    if (carriedFoodResources.Count >= foodCarryCapacity)
                    {
                        resumeResourceNodeTarget = node.transform;
                        BeginReturnToFood();
                    }
                }
                else if (carriedResources.Count >= carryCapacity)
                {
                    resumeResourceNodeTarget = node.transform;
                    BeginReturnToNest();
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
            coconut.RegisterHit(this);

            if (carriedFoodResources.Count >= foodCarryCapacity)
            {
                resumeResourceNodeTarget = currentTaskTarget;
                BeginReturnToFood();
            }
            return;
        }

        JellyfishAgent jellyfish = other.GetComponentInParent<JellyfishAgent>();
        if (jellyfish != null)
        {
            jellyfish.RegisterHit(this);

            if (carriedFoodResources.Count >= foodCarryCapacity)
            {
                resumeResourceNodeTarget = currentTaskTarget;
                BeginReturnToFood();
            }
            return;
        }

        FoodBuilding foodBuilding = other.GetComponentInParent<FoodBuilding>();
        if (foodBuilding != null)
        {
            foodBuilding.RegisterEatHit(this);
            return;
        }

        Watchtower watchtower = other.GetComponentInParent<Watchtower>();
        if (watchtower != null) watchtower.TryStationTurtle(this);
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
            rb.position = position;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    /// <summary>While parked, rotates this turtle to face target (e.g. whatever a Watchtower is currently aiming at) via the existing steering component — safe on a kinematic rigidbody, and doesn't move it since fins/locomotion are stopped. Pass null to hold the current facing.</summary>
    public void SetLookTarget(Transform target) => steering.SetTarget(target);

    /// <summary>Releases this turtle from a parked state, returning it to normal idle behavior. Safe to call more than once.</summary>
    public void Unpark()
    {
        if (!isParked) return;

        isParked = false;
        if (rb != null) rb.bodyType = RigidbodyType2D.Dynamic;

        // Update() was fully gated off while parked, so wasStorming/hasPreStormTask
        // went stale for however long that lasted — resync directly, or the very
        // next Update() could misfire RestorePreStormTask() with an arbitrary old
        // task instead of simply going idle as intended.
        wasStorming = DayStormCycle.IsStorming;
        hasPreStormTask = false;

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

    private float foodBuffRemainingTime;
    private Coroutine foodBuffRoutine;

    /// <summary>Grants a personal speed buff from eating at the Food Building. Unlike a one-shot buff, repeated bites are additive — each call extends the remaining duration rather than restarting it, matching continuous grazing (see FoodBuilding.RegisterEatHit).</summary>
    public void ApplyFoodBuff(float additionalDuration, float speedMultiplier)
    {
        foodBuffRemainingTime += additionalDuration;
        locomotion.SetTemporaryBuffSpeedMultiplier(speedMultiplier);

        if (foodBuffRoutine == null) foodBuffRoutine = StartCoroutine(FoodBuffCountdown());
    }

    private IEnumerator FoodBuffCountdown()
    {
        while (foodBuffRemainingTime > 0f)
        {
            foodBuffRemainingTime -= Time.deltaTime;
            yield return null;
        }

        locomotion.SetTemporaryBuffSpeedMultiplier(1f);
        foodBuffRoutine = null;
    }

    /// <summary>Grants a permanent bonus to damage dealt to trash. Safe to call more than once — only takes effect the first time.</summary>
    public void ApplyHardHatBuff(int bonusDamage)
    {
        if (HasHardHatBuff) return;

        HasHardHatBuff = true;
        BonusDamageToTrash += bonusDamage;
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

    /// <summary>Snapshots whatever this turtle is currently doing, the instant a storm begins, so RestorePreStormTask can return to it later regardless of any orders given during the storm.</summary>
    private void CapturePreStormTask()
    {
        hasPreStormTask = currentTaskTarget != null;
        if (!hasPreStormTask) return;

        if (isGroundMove)
        {
            // currentTaskTarget IS moveTargetMarker here, which a mid-storm
            // MoveToPoint order would overwrite in place — copy its position to
            // our own dedicated marker instead of referencing it directly.
            preStormMoveMarker.position = currentTaskTarget.position;
            preStormTaskTarget = preStormMoveMarker;
        }
        else
        {
            preStormTaskTarget = currentTaskTarget;
        }

        preStormTaskIsGroundMove = isGroundMove;
        preStormTaskIsResourceTask = isResourceTask;
    }

    /// <summary>Called once a storm fully ends: returns the turtle to whatever it was doing right before the storm started, overriding any aggro chase or player order given during the storm.</summary>
    private void RestorePreStormTask()
    {
        if (hasPreStormTask && preStormTaskTarget != null)
        {
            ApplyTask(preStormTaskTarget, preStormTaskIsGroundMove, preStormTaskIsResourceTask);
        }
        else
        {
            StopAndIdle();
        }

        hasPreStormTask = false;
    }

    /// <summary>Cancels an in-progress resource-harvest task the instant a storm begins — the pre-storm snapshot above already captured it, so it resumes once the storm ends. Building/rune tasks and aggro are untouched.</summary>
    private void CancelResourceTaskForStorm()
    {
        if (isResourceTask) StopAndIdle();
    }

    private void TryAcquireAggroTarget()
    {
        if (!DayStormCycle.IsStorming) return;

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
    /// is currently in deep water — if so, holds position without dropping
    /// aggro (see the deep-water branch below) until it drifts back out.
    /// Otherwise re-evaluates every frame whether there's a clear line of
    /// sight to it: if so, ignores pathfinding entirely and steers straight at
    /// it (a visible target doesn't need a route around anything, and trash
    /// keeps moving too fast for a stored path to stay accurate anyway); if
    /// blocked and not already navigating a path, kicks off one around
    /// whatever's in the way. Never repaths while already following one just
    /// because line of sight is still blocked.
    /// </summary>
    private void UpdateAggroSteering()
    {
        if (aggroTarget == null) return;

        Transform target = aggroTarget.transform;

        // Trash can venture into deep water on its way to the nest, but a
        // turtle can never follow it out there (see BeginPathTo's
        // avoidDeepWater) — hold position and keep watching rather than
        // dropping aggro, resuming the instant it drifts back into the
        // shallows/onto land.
        if (PathfindingManager.Instance != null && PathfindingManager.Instance.IsDeepWater(target.position))
        {
            isFollowingPath = false;
            steering.SetTarget(null);
            SetFinsPlaying(false);
            return;
        }

        // Resuming from a deep-water wait leaves fins stopped, and nothing
        // else in an ongoing aggro chase turns them back on — ApplyTask only
        // does that once, at the moment aggro is first acquired.
        SetFinsPlaying(true);

        bool hasLineOfSight = PathfindingManager.Instance == null
            || PathfindingManager.Instance.HasLineOfSight(transform.position, target.position);

        if (hasLineOfSight)
        {
            isFollowingPath = false;
            pathFinalDestination = target;
            steering.SetTarget(target);
        }
        else if (!isFollowingPath)
        {
            BeginPathTo(target);
        }
    }

    private void EndAggro()
    {
        isAggroed = false;
        aggroTarget = null;

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
            BeginPathTo(target);
        }

        SetFinsPlaying(true);
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
    }

    private void SpawnHarvestPopEffect(Vector3 position, Sprite icon)
    {
        if (harvestPopEffectPrefab == null) return;

        GameObject instance = Instantiate(harvestPopEffectPrefab, position, Quaternion.identity);
        instance.GetComponent<ResourcePopEffect>()?.Initialize(icon, position, null);
    }

    /// <summary>Adds one unit of type to whichever carry list it belongs to (nest-bound or food-bound, see ResourceManager.IsFoodType) if that list isn't already full, showing a shell-slot icon and a harvest-pop effect. Public so Coconut can call it directly (it isn't a ResourceNode). Returns false if that list's capacity is already full — the unit doesn't fit, no loss, caller just stops adding.</summary>
    public bool CollectResourceUnit(ResourceManager.ResourceType type, Vector3 sourcePosition)
    {
        bool isFood = ResourceManager.IsFoodType(type);
        List<CarriedResource> list = isFood ? carriedFoodResources : carriedResources;
        int capacity = isFood ? foodCarryCapacity : carryCapacity;

        if (list.Count >= capacity) return false;

        Sprite icon = carriedVisuals != null ? carriedVisuals.GetRandomIcon(type) : null;
        int slot = carriedVisuals != null ? carriedVisuals.ShowNext(icon) : -1;
        list.Add(new CarriedResource { Type = type, Icon = icon, SlotIndex = slot });
        SpawnHarvestPopEffect(sourcePosition, icon);
        return true;
    }

    /// <summary>Called once carried resources reach capacity: clears the just-finished resource task and begins the beeline trip back to the nest, bypassing normal task/aggro/idle logic entirely while active (see isReturningToNest at the top of Update()).</summary>
    private void BeginReturnToNest()
    {
        // Clear the just-finished resource task's flags directly (not via
        // StopAndIdle, which would also stomp the steering/path state we're
        // about to re-set below).
        isGroundMove = false;
        isResourceTask = false;
        currentTaskTarget = null;

        isReturningToNest = true;
        CancelAggro();

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        BeginPathTo(nest);
        SetFinsPlaying(true);
    }

    /// <summary>Runs every frame in place of the normal storm/aggro/idle logic while returning — a returning-with-cargo turtle beelines for the nest regardless of anything else.</summary>
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

    /// <summary>Pops each carried unit off the shell in delivery order and flies it into the nest, adding to ResourceManager only as each individual pop-effect's flight completes — not at nest-arrival. When resumeAfterDelivery is true (a dedicated full-load return trip), also clears the return-trip movement state and resumes the same resource node afterward; when false (a drive-by delivery mid-task), leaves whatever the turtle is currently doing untouched.</summary>
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
                    () => ResourceManager.Instance?.Add(capturedType, 1));
            }
            else
            {
                ResourceManager.Instance?.Add(unit.Type, 1); // no prefab wired yet — still deliver correctly, just instantly
            }

            yield return new WaitForSeconds(deliveryStaggerDelay);
        }

        // Only this list's own slots (already cleared above) — not ClearAll,
        // since carriedFoodResources may still have units sitting in theirs.
        carriedResources.Clear();
        deliverCoroutine = null;

        if (resumeAfterDelivery)
        {
            if (resumeResourceNodeTarget != null) MoveToResource(resumeResourceNodeTarget);
            else StopAndIdle();
        }
    }

    /// <summary>Same bypass shape as BeginReturnToNest, for a dedicated food-delivery trip. Guarded against overlapping with an in-progress nest trip — see CheckPassiveFoodDelivery for how a food-trip that was suppressed here gets picked back up.</summary>
    private void BeginReturnToFood()
    {
        if (isReturningToNest || isReturningToFood) return;

        isGroundMove = false;
        isResourceTask = false;
        currentTaskTarget = null;

        isReturningToFood = true;
        CancelAggro();

        Transform food = FoodBuilding.Instance != null ? FoodBuilding.Instance.transform : null;
        BeginPathTo(food);
        SetFinsPlaying(true);
    }

    /// <summary>Runs every frame in place of the normal storm/aggro/idle logic while returning food — mirrors UpdateReturnToNest exactly, targeting the Food Building instead.</summary>
    private void UpdateReturnToFood()
    {
        Transform food = FoodBuilding.Instance != null ? FoodBuilding.Instance.transform : null;
        if (food == null) return; // no Food Building placed (yet) — just stop evaluating, no crash

        if (Vector2.Distance(transform.position, food.position) <= foodDeliveryRadius && deliverFoodCoroutine == null)
        {
            deliverFoodCoroutine = StartCoroutine(DeliverCarriedFoodResources(resumeAfterDelivery: true));
        }
    }

    /// <summary>Mirrors CheckPassiveNestDelivery, plus a capacity safety net: since a nest-trip and a food-trip can't run simultaneously (see BeginReturnToFood's guard), a food-capacity fill that was suppressed because a nest-trip already claimed the bypass state would otherwise never trigger — this runs every frame once free of both, so it's picked up the instant the nest-trip ends, even before the turtle is anywhere near the Food Building.</summary>
    private void CheckPassiveFoodDelivery()
    {
        if (carriedFoodResources.Count == 0 || deliverFoodCoroutine != null) return;

        if (carriedFoodResources.Count >= foodCarryCapacity)
        {
            resumeResourceNodeTarget = currentTaskTarget;
            BeginReturnToFood();
            return;
        }

        FoodBuilding food = FoodBuilding.Instance;
        if (food == null) return;

        if (Vector2.Distance(transform.position, food.transform.position) <= foodDeliveryRadius)
        {
            deliverFoodCoroutine = StartCoroutine(DeliverCarriedFoodResources(resumeAfterDelivery: false));
        }
    }

    /// <summary>Mirrors DeliverCarriedResources exactly, delivering to the Food Building instead of the Nest via ResourceManager.</summary>
    private IEnumerator DeliverCarriedFoodResources(bool resumeAfterDelivery)
    {
        if (resumeAfterDelivery)
        {
            isReturningToFood = false;
            isFollowingPath = false;
            steering.SetTarget(null);
            SetFinsPlaying(false);
        }

        FoodBuilding food = FoodBuilding.Instance;
        Vector3 foodPosition = food != null ? food.transform.position : transform.position;

        for (int i = 0; i < carriedFoodResources.Count; i++)
        {
            CarriedResource unit = carriedFoodResources[i];
            Vector3 fromPosition = carriedVisuals != null ? carriedVisuals.GetSlotWorldPosition(unit.SlotIndex) : transform.position;
            carriedVisuals?.ClearSlot(unit.SlotIndex);

            if (deliveryPopEffectPrefab != null)
            {
                GameObject instance = Instantiate(deliveryPopEffectPrefab, fromPosition, Quaternion.identity);
                instance.GetComponent<ResourcePopEffect>()?.Initialize(
                    unit.Icon, fromPosition, foodPosition,
                    () => FoodBuilding.Instance?.Deposit(1));
            }
            else
            {
                FoodBuilding.Instance?.Deposit(1); // no prefab wired yet — still deliver correctly, just instantly
            }

            yield return new WaitForSeconds(deliveryStaggerDelay);
        }

        carriedFoodResources.Clear();
        deliverFoodCoroutine = null;

        if (resumeAfterDelivery)
        {
            if (resumeResourceNodeTarget != null) MoveToResource(resumeResourceNodeTarget);
            else StopAndIdle();
        }
    }

    /// <summary>
    /// Starts moving toward destination, requesting a path around nature
    /// obstacles (and deep water — turtles can never path further out than the
    /// shallows) from PathfindingManager. Falls back to steering straight at
    /// destination (today's exact pre-pathfinding behavior) only if there's no
    /// manager to consult at all; if a manager is present but genuinely can't
    /// find a deep-water-safe path, refuses to move rather than ever falling
    /// back to a raw direct steer that could cut straight across the ocean.
    /// Used by every destination-seeking behavior — real orders (via
    /// ApplyTask), idle wander, and the storm nest-guard — so
    /// pathFinalDestination is tracked independently of currentTaskTarget,
    /// which idle/nest-guard movement must never touch.
    /// </summary>
    private void BeginPathTo(Transform destination)
    {
        pathFinalDestination = destination;

        bool hasManager = destination != null && PathfindingManager.Instance != null;
        currentPath = hasManager
            ? PathfindingManager.Instance.FindPath(transform.position, destination.position, avoidDeepWater: true)
            : null;

        if (currentPath == null || currentPath.Count == 0)
        {
            isFollowingPath = false;
            steering.SetTarget(hasManager ? null : destination);
            return;
        }

        currentPathIndex = 0;
        isFollowingPath = true;
        pathWaypointMarker.position = currentPath[0];
        steering.SetTarget(pathWaypointMarker);
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
                isWanderMoving = false;
                BeginPathTo(nest);
            }

            locomotion.SetSpeedMultiplier(1f);
            SetFinsPlaying(true);
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

        BeginPathTo(idleWanderMarker);
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
