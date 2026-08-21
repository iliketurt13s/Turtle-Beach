using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
/// One rule sits between those defaults and actually roaming: a turtle never
/// ambles around still holding cargo. Whenever it would start roaming and it's
/// carrying anything, it takes the load to the nest first — by day as a
/// dedicated trip home (TryBeginCarriedDelivery), by night by walking in past
/// the guard ring to delivery range instead (UpdateIdle), which is the same
/// thing without the aggro blindness a return trip would bring to a storm.
/// This ranks strictly below every real duty, so a player order or a standing
/// resource objective still takes the turtle away mid-load exactly as before;
/// it only ever fills the gap where the turtle had nothing else to do.
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
/// in progress too (see CancelReturnTrip; the turtle keeps whatever it was
/// carrying, just isn't forced to keep walking it home through the fight). A
/// fresh ground/building movement command (MoveToPoint/MoveToBuilding) cancels
/// an in-progress beeline-home trip the same way — the turtle can be
/// redirected to help elsewhere even while ferrying a full load, still
/// carrying everything it collected, delivering it later either via a
/// subsequent order back near the nest or passively (CheckPassiveNestDelivery)
/// if ordinary movement happens to bring it back into range; an already-in-
/// progress delivery (the pop-off-and-fly animation once a trip has actually
/// arrived) is left alone rather than interrupted mid-flight. MoveToResource is
/// the one exception: picking a new resource while beelining home only swaps
/// the standing objective for later, leaving the current trip and its path
/// untouched. Separately, a turtle that notices trash within its aggro
/// distance while storming temporarily abandons whatever it was doing to go
/// attack the nearest one (same bounce-and-collide mechanic as harvesting,
/// damaging the trash's TrashHealth on each hit — trash itself is never a
/// target objective), then resumes its previous task once that trash is
/// destroyed or the storm ends, whichever comes first (see EndAggro's call
/// in Update()) — aggro is strictly storm-only and self-terminates at dawn
/// rather than chasing into daylight. A saved ground-move order (a bare
/// MoveToPoint click) is the one exception: EndAggro treats it as abandoned
/// rather than resuming it, falling through to the normal nest-guard/wander
/// default instead — a turtle shouldn't finish a fight only to walk back out
/// to some stale pre-storm click instead of helping defend. A saved
/// building-visit order still resumes normally.
///
/// Three run modifiers bend the rules above without changing their shape.
/// Far Sighted (UpgradeManager.SeekFurthestResource) inverts every harvest
/// search: the turtle walks to the FURTHEST instance of its objective FROM
/// THE NEST (see HarvestSearchOrigin) rather than the nearest one to itself.
/// Anchoring on the nest rather than on the turtle is what makes the answer
/// stable — it doesn't depend on where the turtle happened to be standing, so
/// a re-seek after a delivery picks the next-furthest node outward instead of
/// re-measuring from wherever the last trip left it and doubling back. Short Leash (UpgradeManager.TurtleLeashRadius) caps how
/// far from the nest a turtle may be sent at all — order destinations are
/// clamped into the disc, harvest and aggro searches skip anything outside it,
/// and an idle turtle that ends up beyond it walks itself back in. Heavy Load
/// (UpgradeManager.CarryLoadSlowdownFraction) makes a turtle slower the fuller
/// it is, via a dedicated TurtleLocomotion buff layer refreshed by
/// RefreshCarryLoadSpeed. All three are inert — every check short-circuits on
/// a default value — in a run that didn't take them.
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
    [Tooltip("Base radius (world units) around a MoveToPoint destination within which this turtle becomes eligible to re-acquire aggro again — checked live against its current distance to the destination every frame, not a timer, so it's never an estimate that can run out early or linger too long. Scales up with the order's total travel distance (see Aggro Unlock Radius Per Distance/Max below): a short click needs this turtle to arrive almost exactly before it'll fight, so directing it at one specific piece of trash close by still feels precise, while a long relocation across the island only needs it to get generally clear of/close to the area — it doesn't have to thread the exact clicked pixel through whatever's in the way. Doesn't affect an aggro chase already in progress, and doesn't apply to resource/building orders.")]
    [SerializeField] private float aggroUnlockRadiusBase = 0.4f;
    [Tooltip("Additional aggro-unlock radius per world unit of a MoveToPoint order's total travel distance, added to Aggro Unlock Radius Base and capped at Aggro Unlock Radius Max.")]
    [SerializeField] private float aggroUnlockRadiusPerDistance = 0.15f;
    [Tooltip("Hard cap on the aggro-unlock radius regardless of how far a MoveToPoint order sends this turtle.")]
    [SerializeField] private float aggroUnlockRadiusMax = 4f;
    [Tooltip("Extra obstacle clearance (grid cells) applied to the aggro line-of-sight shortcut (see UpdateAggroSteering), matching how wide this turtle physically is. Now a coarse supplement to the exact body-width clearance below rather than the only defense: PathfindingManager.HasLineOfSight measures this unit's real radius against each obstacle, so whole extra cells of inflation are mostly only needed for a unit too big to fit the cell-center waypoints FindPath returns. Note nothing adds this to this turtle's FindPath calls, so leaving it above 0 makes the shortcut stricter than the path it falls back to — which is the safe direction (it just paths instead of steering straight), but 0 is now a reasonable setting.")]
    [SerializeField, Range(0, 3)] private int aggroLineOfSightWidth = 1;
    [Tooltip("Seconds between line-of-sight rechecks while chasing. The check walks every cell along the line to the target and is easily the most expensive thing an aggroed turtle does, so it ran per-frame per turtle for an answer that changes only when something has moved a meaningful distance. The chase itself still steers every frame off the last answer, and a target that moves further than Aggro Line Of Sight Recheck Distance forces a fresh one regardless — so this is the ceiling on how stale the answer can get while nothing much is happening, not a delay on reacting. 0 restores the per-frame check.")]
    [SerializeField, Min(0f)] private float aggroLineOfSightInterval = 0.15f;
    [Tooltip("How far this turtle or its target must move since the last line-of-sight check to force a new one before the interval is up. Around half a cell keeps the answer honest across the only movement that can actually change it — a whole cell is what it takes to newly clear or newly block a route.")]
    [SerializeField, Min(0f)] private float aggroLineOfSightRecheckDistance = 0.5f;
    [Tooltip("Safety margin (world units) added to this unit's measured collider half-width when asking PathfindingManager.HasLineOfSight whether it physically fits past an obstacle. Small on purpose — it just keeps a turtle from grazing a palm tree it technically clears by a hair. Raise it if units still scrape obstacle edges while chasing; lower it if they refuse shortcuts through gaps they visibly fit.")]
    [SerializeField] private float lineOfSightSkin = 0.05f;
    /// <summary>Measured once in Awake (see MeasureLineOfSightRadius) rather than per frame — the aggro shortcut asks for it every frame, and a unit's collider doesn't change size mid-run.</summary>
    private float moverLineOfSightRadius;

    [Header("Separation")]
    [Tooltip("Turtles within this distance of each other get a slight push apart every physics step, so they don't fully overlap now that they no longer physically collide (see TurtleBuildingCollisionSetup).")]
    [SerializeField] private float separationRadius = 1f;
    [Tooltip("Strength of the separation push at zero distance, fading to 0 at Separation Radius. Tune to taste — too high and turtles visibly jitter apart, too low and they keep overlapping.")]
    [SerializeField] private float separationForce = 2f;

    [Header("Nest Defense")]
    [Tooltip("While storming, an idle turtle (no order, not aggroed) heads toward the nest to help guard it, stopping once within this distance rather than stacking on top of it.")]
    [SerializeField] private float nestGuardDistance = 2f;
    [Tooltip("Seconds to wait before retrying a path to the nest after one failed to find a route (e.g. the nest is currently hemmed in by obstacles). Covers both idle trips there: the storm-time nest-guard approach (see UpdateIdle) and an idle turtle walking a part-load home (see TryBeginCarriedDelivery). Without this, a genuinely unreachable nest would otherwise re-run a full pathfind every single frame for as long as it stays blocked.")]
    [SerializeField] private float nestGuardRetryInterval = 2f;
    private float nestGuardRetryTimer;
    /// <summary>Separate from nestGuardRetryTimer despite sharing an interval — the two throttle different callers (storm-time guard approach vs. daytime carried-load delivery) and shouldn't consume each other's cooldown across a day/night boundary.</summary>
    private float nestDeliveryRetryTimer;
    [Tooltip("Once at the nest, a guard ambles around the ring between this fraction of Nest Guard Distance and the full distance — the same band it stops in on approach — instead of the small disc it idle-wanders in by day. So widening Nest Guard Distance widens where guards spread out to, not just how far out they stop. 0 lets them roam right across the nest itself.")]
    [SerializeField, Range(0f, 1f)] private float nestGuardRoamInnerFraction = 0.5f;
    [Tooltip("How many candidate points a guard samples and scores each time it picks somewhere new to amble to — it goes to whichever is least crowded by other turtles, so guards fan out around the nest instead of bunching up wherever they happened to arrive. 1 disables the scoring entirely (a plain random point).")]
    [SerializeField, Min(1)] private int nestGuardRoamCandidates = 6;
    [Tooltip("Personal space a roaming guard tries to keep from other turtles. Candidates are scored by their distance to the nearest other turtle, capped here — beyond it they all count as equally clear, so a guard that already has room settles down instead of chasing the emptiest corner of the ring.")]
    [SerializeField] private float nestGuardRoamSpacing = 2f;

    [Header("Resource Carrying")]
    [Tooltip("Maximum combined units (Wood/Rock plus Seaweed/Coconut/... food) this turtle can carry at once before it stops picking up more of whichever type it just harvested and returns to deliver it. Raised at runtime by carry-capacity upgrade cards — see SetCarryCapacityBonus.")]
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

    [Header("Contact Lunge")]
    [Tooltip("How close this unit must get to whatever it's harvesting, attacking or bumping before propulsion switches from a smooth glide to discrete lunges. Only has any effect on a unit with no propelling fins (the Crab) — a finned turtle always moves in fin strokes regardless. Larger values start the lunging earlier on the approach; it should comfortably exceed the distance the unit bounces back to after a hit, or it'll drop out of lunging between strikes.")]
    [SerializeField] private float lungeDistance = 1.5f;

    [Header("Buffs")]
    [Tooltip("Swapped to the hard-hat sprite when this turtle earns the Hard Hat buff.")]
    [SerializeField] private SpriteRenderer headRenderer;
    [SerializeField] private Sprite hardHatHeadSprite;
    [Tooltip("The two front-leg renderers, swapped to their flipper sprite when this turtle earns the Flipper buff.")]
    [SerializeField] private SpriteRenderer frontLeftFinRenderer;
    [SerializeField] private Sprite flipperFinSpriteLeft;
    [SerializeField] private SpriteRenderer frontRightFinRenderer;
    [SerializeField] private Sprite flipperFinSpriteRight;
    [Tooltip("Barnacle overlay objects (shell crust, head barnacles, ...) shown for as long as this turtle is wearing barnacles. Unlike Hard Hat/Flipper these are whole objects toggled rather than sprite swaps, so barnacles can be layered ON TOP of whatever the turtle already looks like instead of replacing a part of it. Best SAVED INACTIVE in the prefab — Awake force-hides them anyway (and HideUpgradeVisuals covers the menu, which strips this component), but starting them off means nothing has to run at all for an un-upgraded turtle to look right. Leave empty for no visual.")]
    [SerializeField] private GameObject[] barnacleVisuals;

    /// <summary>All currently-live turtles, so UpgradeManager can retroactively apply a newly picked upgrade to the whole population, not just future spawns (mirrors TrashHealth.allTrash).</summary>
    private static readonly List<TurtleAgent> allTurtles = new List<TurtleAgent>();
    public static IReadOnlyList<TurtleAgent> AllTurtles => allTurtles;

    public bool IsSelected { get; private set; }

    public bool HasHardHatBuff { get; private set; }
    public bool HasFlipperBuff { get; private set; }

    /// <summary>Extra damage this turtle deals to trash per hit: permanent contributions from the Hard Hat buff and the Barnacles upgrade plus a temporary one from the Jellyfish night buff, added together — see ApplyHardHatBuff/ApplyBarnacles/ApplyJellyfishBuff. The Jellyfish share is computed rather than stored so a food-potency upgrade picked mid-storm applies to a buff already running.</summary>
    public int BonusDamageToTrash => hardHatBonusDamage + EffectiveJellyfishBonusDamage + barnacleBonusDamage;

    /// <summary>True if this unit is a crab recruit rather than a turtle (see CrabAgent for the full list of what differs). Set by the prefab having a CrabAgent component on it, nothing else.</summary>
    public bool IsCrab => crab != null;

    /// <summary>False for a crab that hasn't been recruited to fight yet, true for everything else — read by TrashHealth before applying any damage, and by this class before ever acquiring an aggro target.</summary>
    public bool CanAttackTrash => !IsCrab || (UpgradeManager.Instance != null && UpgradeManager.Instance.CrabsFightAtNight);

    /// <summary>This turtle's chance to deal double damage per hit, from upgrade cards. Set via UpgradeManager, not directly.</summary>
    public float CritChance { get; private set; }

    private TurtleTargetSteering steering;
    private TurtleLocomotion locomotion;
    private CrabAgent crab;
    private SquashAndStretch squashAndStretch;
    private Camera cam;
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

    /// <summary>True while this turtle is actively chasing a piece of trash (see TryAcquireAggroTarget/EndAggro) — exposed for TutorialManager to detect that the player has driven a turtle into range of incoming trash rather than polling private aggro state directly.</summary>
    public bool IsAggroed => isAggroed;
    private bool hadSavedTask;
    private Transform savedTaskTarget;
    private bool savedTaskIsGroundMove;
    private bool savedTaskIsResourceTask;

    // Set by MoveToPoint (see aggroUnlockRadiusBase's own tooltip) and checked
    // live in TryAcquireAggroTarget against this turtle's current distance to
    // moveTargetMarker every frame — not a countdown, so there's nothing here
    // that can go stale between being set and actually mattering (e.g. a
    // command given well before a storm starts).
    private float aggroUnlockRadius;

    // True only if MoveToPoint's BeginPathTo call actually found a route —
    // false means this ground-move order is stuck unreachable (frozen, not
    // actually going anywhere), in which case TryAcquireAggroTarget should
    // never withhold aggro on the strength of a destination this turtle isn't
    // making any progress toward anyway.
    private bool groundMoveReachable;

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
    private bool wasGuardRoaming;

    /// <summary>Open-space scores within this of each other count as equal when a guard picks where to roam — see TryPickGuardRoamPoint's tie-break, and OpenSpaceAt for why exact ties are the common case rather than a rarity.</summary>
    private const float RoamScoreTieTolerance = 0.05f;

    [Header("Pathfinding")]
    [Tooltip("Distance (world units) at which an in-progress path's current waypoint is considered reached, advancing to the next one.")]
    [SerializeField] private float waypointArrivalDistance = 0.4f;
    [Tooltip("How often (seconds) an in-progress path checks whether its live destination Transform (e.g. chased trash, a harvested Jellyfish — anything that moves on its own) has drifted too far from where the path was originally aimed, repathing early if so. Prevents overshooting to a stale position and having to double back once the old path finishes — see UpdatePathFollowing. Irrelevant for a static destination (a resource node, a ground-move point, the nest), which never drifts.")]
    [SerializeField] private float pathRetargetCheckInterval = 0.4f;
    [Tooltip("How far (world units) a path's live destination Transform must drift from where the path was originally computed before it triggers an early repath.")]
    [SerializeField] private float pathRetargetDistance = 1f;
    [Tooltip("Waypoints trimmed off the tail end of a computed aggro-chase path, so the turtle switches from following the (already slightly stale) route to steering live at the target's actual current position a bit before it would otherwise reach the end of it. Aggroed trash is usually being driven back toward the turtle by the player, so aiming the last few steps short of its recorded position — rather than all the way to it — avoids overshooting to where it used to be. 0 = walk the full computed path, matching old behavior.")]
    [SerializeField] private int aggroChasePathShortenSteps = 3;

    private float pathRetargetTimer;
    private Vector3 pathTargetSnapshotPosition;

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

    [Header("Audio")]
    [Tooltip("Played whenever this unit's head lands a hit on something it actually interacts with — a tree, a rock, a coconut, a jellyfish, a rune, a watchtower, a piece of trash. Deliberately NOT played for every collider the head touches: turtles jostle against each other constantly and that isn't a hit. Contact is head-only and reloads once per fin stroke (see TurtleHeadHitbox), so this fires at swimming cadence; the throttle below it is shared scene-wide across every turtle, so a crowd of them harvesting can't stack copies of it. The turtle's OTHER sound, the sand push it makes while moving, lives on TurtleLocomotion instead, since that's what owns fin strokes.")]
    [SerializeField] private SoundEffect headHitSound = new SoundEffect();

    [Header("Aggro Debug")]
    [Tooltip("Draws a wire circle of radius Aggro Distance around this turtle in the Scene view (Gizmos) at all times — the range within which it'll notice and go attack trash. Purely a debug aid, has no gameplay effect.")]
    [SerializeField] private bool showAggroRangeGizmo = true;
    [SerializeField] private Color aggroRangeGizmoColor = new Color(1f, 0.3f, 0.3f);
    [Tooltip("While a ground-move order (MoveToPoint) is in progress, draws a wire circle of the live Aggro Unlock Radius around its destination — the zone this turtle must get inside before it's eligible to re-acquire aggro again (see aggroUnlockRadiusBase's own tooltip). Lets you eyeball how the radius scales with a short click versus a long one.")]
    [SerializeField] private bool showAggroUnlockRadiusGizmo = true;
    [SerializeField] private Color aggroUnlockRadiusGizmoColor = new Color(0.3f, 1f, 0.3f);
    [Tooltip("While aggroed, draws a line from this turtle to its current aggro target, tinted Aggro Range Gizmo Color.")]
    [SerializeField] private bool showAggroTargetGizmo = true;

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
        crab = GetComponent<CrabAgent>();
        squashAndStretch = GetComponent<SquashAndStretch>();
        cam = Camera.main;
        rb = GetComponent<Rigidbody2D>();
        carriedVisuals = GetComponentInChildren<CarriedResourceVisuals>(true);
        fins = locomotion.PropellingFins;

        // The authored value is the baseline every carry-capacity upgrade is
        // added on top of — capture it before any card can overwrite
        // carryCapacity, or repeat SetCarryCapacityBonus calls would compound.
        baseCarryCapacity = carryCapacity;

        normalLayer = gameObject.layer;
        interactingLayer = LayerMask.NameToLayer("TurtleInteracting");

        moverLineOfSightRadius = MeasureLineOfSightRadius();

        // Every harvest, attack, rune bump and Watchtower stationing arrives
        // through TurtleHeadHitbox → HandleHeadHit, and nothing else. A unit
        // prefab missing one still moves, paths and takes orders perfectly, so
        // the only symptom is that it silently never collects or damages
        // anything — worth a warning rather than leaving that to be puzzled out.
        if (GetComponentInChildren<TurtleHeadHitbox>(true) == null)
        {
            Debug.LogWarning($"{name}: no TurtleHeadHitbox found in children, so this unit can't harvest, attack, or interact with runes/watchtowers. It needs a child object carrying a TRIGGER Collider2D and a TurtleHeadHitbox component.", this);
        }

        // Hidden up front so the prefab can be authored with the barnacles
        // visible (far easier to position and sort them that way) without an
        // un-upgraded turtle wearing them. Runs before OnEnable's
        // ApplyCurrentUpgradesTo, which turns them straight back on if the card
        // has already been picked this run.
        SetActiveAll(barnacleVisuals, false);

        // Collected with includeInactive so the barnacle overlay above is in
        // the set too, and hover tinting covers it once it's shown.
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

    /// <summary>How wide this unit physically is (world units), for PathfindingManager.HasLineOfSight's clearance test — read off whatever body collider the prefab actually carries, scaled by the transform, plus Line Of Sight Skin. Measured per unit rather than assumed by the pathfinder, because a crab is a Turtle prefab VARIANT and can carry a differently-sized collider than a turtle; hardcoding one number in the manager would silently be wrong for one of them. Uses the larger extent of a box/capsule (the circumscribed half-width) so the answer is conservative for a body that isn't round, and falls back to Line Of Sight Skin alone if a unit somehow has no body collider — a zero radius just reproduces the old center-line behavior rather than breaking the chase.</summary>
    private float MeasureLineOfSightRadius()
    {
        Collider2D body = GetComponent<Collider2D>();
        if (body == null) return lineOfSightSkin;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float halfWidth;

        switch (body)
        {
            case CircleCollider2D circle:
                halfWidth = circle.radius * scale;
                break;
            case CapsuleCollider2D capsule:
                halfWidth = Mathf.Max(capsule.size.x, capsule.size.y) * 0.5f * scale;
                break;
            case BoxCollider2D box:
                halfWidth = Mathf.Max(box.size.x, box.size.y) * 0.5f * scale;
                break;
            default:
                // Only for a collider shape none of the above cover (polygon,
                // composite...). bounds is world-space and already scaled, but
                // it's also the one option here that depends on physics having
                // run, hence the last resort rather than the general case.
                halfWidth = Mathf.Max(body.bounds.extents.x, body.bounds.extents.y);
                break;
        }

        return halfWidth + lineOfSightSkin;
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

    private void FixedUpdate()
    {
        UpdateSeparation();
        UpdateTailwind();
    }

    [Header("Tailwind")]
    [Tooltip("Seconds between neighbour scans for the Tailwind upgrade. The scan is O(turtles) per turtle, so running it every physics step would be quadratic on a big population for a value that changes only as fast as turtles swim; a quarter-second is well inside how long it takes anyone to cross the radius. Ignored entirely, and the scan never runs at all, in a run without the upgrade.")]
    [SerializeField, Min(0f)] private float tailwindScanInterval = 0.25f;

    private float tailwindScanTimer;

    /// <summary>The multiplier last pushed to locomotion, so a scan whose answer hasn't moved doesn't churn the whole buff product for nothing.</summary>
    private float appliedTailwindMultiplier = 1f;

    /// <summary>
    /// The Tailwind upgrade: a turtle swims faster the more company it has
    /// nearby, so moving the population as a group beats scattering it.
    ///
    /// Each neighbour contributes on a linear falloff — full weight right on
    /// top of this turtle, nothing at all at Tailwind Radius — and the total
    /// is capped at Tailwind Max Stack so a swarm doesn't multiply into
    /// something absurd. Crabs count as company like anything else; they are
    /// out there swimming alongside, and excluding them would be a rule the
    /// player has no way to see.
    ///
    /// Deliberately separate from UpdateSeparation despite both walking the
    /// same list: separation runs every physics step over a much tighter
    /// radius and pushes forces, while this runs on a timer and sets a speed
    /// layer. Folding them together would drag the cheap one onto the
    /// expensive one's radius, or the expensive one onto the cheap one's
    /// cadence.
    /// </summary>
    private void UpdateTailwind()
    {
        float bonus = UpgradeManager.Instance != null ? UpgradeManager.Instance.TailwindSpeedBonus : 0f;
        if (bonus <= 0f)
        {
            // Covers the upgrade never being taken and, harmlessly, a turtle
            // that still had a stale multiplier from before one was cleared.
            if (!Mathf.Approximately(appliedTailwindMultiplier, 1f))
            {
                appliedTailwindMultiplier = 1f;
                locomotion.SetTailwindSpeedMultiplier(1f);
            }
            return;
        }

        tailwindScanTimer -= Time.fixedDeltaTime;
        if (tailwindScanTimer > 0f) return;
        tailwindScanTimer = tailwindScanInterval;

        float radius = UpgradeManager.Instance.TailwindRadius;
        float radiusSqr = radius * radius;
        float company = 0f;

        foreach (TurtleAgent other in allTurtles)
        {
            if (other == this || other == null) continue;

            float sqrDistance = ((Vector2)other.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqrDistance >= radiusSqr) continue;

            company += 1f - Mathf.Sqrt(sqrDistance) / radius;
        }

        company = Mathf.Min(company, UpgradeManager.Instance.TailwindMaxStack);

        float multiplier = 1f + bonus * company;
        if (Mathf.Approximately(multiplier, appliedTailwindMultiplier)) return;

        appliedTailwindMultiplier = multiplier;
        locomotion.SetTailwindSpeedMultiplier(multiplier);
    }

    /// <summary>
    /// Turtles no longer physically collide with each other (see
    /// TurtleBuildingCollisionSetup), so without this they can fully overlap
    /// when several converge on the same resource/building. Pushes away from
    /// every other turtle within separationRadius, but only along the axis
    /// perpendicular to this turtle's own forward heading — never the
    /// forward/backward component — so a turtle following close behind
    /// another gets nudged to the side rather than slowed, stopped, or shoved
    /// backward. A no-op while parked/kinematic (AddForce does nothing to a
    /// kinematic Rigidbody2D).
    /// </summary>
    private void UpdateSeparation()
    {
        if (allTurtles.Count <= 1) return;

        Vector2 forward = transform.right;
        Vector2 lateral = new Vector2(-forward.y, forward.x);
        Vector2 push = Vector2.zero;
        int selfIndex = allTurtles.IndexOf(this);

        for (int i = 0; i < allTurtles.Count; i++)
        {
            TurtleAgent other = allTurtles[i];
            if (other == this || other == null) continue;

            Vector2 offset = (Vector2)transform.position - (Vector2)other.transform.position;
            float distance = offset.magnitude;
            if (distance >= separationRadius || distance <= 0.0001f) continue;

            float lateralAmount = Vector2.Dot(offset, lateral);
            // Directly ahead/behind (no measurable sideways offset) — break
            // the tie deterministically via each turtle's stable position in
            // allTurtles, so two turtles dead in line still separate
            // sideways instead of never moving.
            if (Mathf.Abs(lateralAmount) < 0.01f)
            {
                lateralAmount = selfIndex < i ? 0.01f : -0.01f;
            }

            float strength = 1f - (distance / separationRadius);
            push += lateral * Mathf.Sign(lateralAmount) * strength;
        }

        if (push.sqrMagnitude > 0.0001f)
        {
            rb.AddForce(push * separationForce, ForceMode2D.Force);
        }
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

        CheckResourceTaskStillHarvestable();
        UpdatePathFollowing();
        UpdateStuckDetection();
        UpdateAlgaeLinger();

        // Tells locomotion whether the bounce-and-reapproach contact mechanic
        // is live right now, which is the only time a strokeless (finless) unit
        // wants discrete lunges rather than a smooth glide — see
        // TurtleLocomotion.SetImpulseBursts. Pushed unconditionally from one
        // place per frame rather than from each of the many call sites that
        // move a unit, so it can't go stale; a finned turtle ignores it
        // entirely, so there's no need to branch on IsCrab here.
        locomotion.SetImpulseBursts(IsWithinLungeRange());

        // Checked before the isReturningToNest bypass below so a return trip
        // in progress the instant night falls can be cancelled (see
        // CancelReturnTrip) on this same frame, before that bypass gets a
        // chance to claim the rest of Update() for it.
        bool storming = DayStormCycle.IsStorming;
        if (storming && !wasStorming)
        {
            CancelResourceTaskForNight();
            CancelReturnTrip();
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

        // Tutorial-only, and gated on IsHarvestRestricted first so it reads as
        // such: outside a tutorial run that flag is always false and nothing
        // below it is ever evaluated. Checked here for the same reason as the
        // night edge above — a turtle whose objective has just been locked out
        // must stop working it before the return-trip bypass claims the frame.
        // Polled rather than pushed by whoever changed the lock, matching how
        // this class already reads DayStormCycle.IsStorming directly.
        if (TutorialManager.IsHarvestRestricted
            && hasTargetResource
            && !TutorialManager.IsHarvestAllowed(targetResourceType))
        {
            SuspendBarredObjective();
        }

        // A turtle ferrying a full load ignores aggro/idle entirely and just
        // beelines for the nest — bypassing everything below exactly like
        // isParked already does above. Only true during the day (or the tail
        // end of a trip already past cancellation above once night falls,
        // see CancelReturnTrip) — a storm starting mid-trip cancels it instead
        // of forcing the turtle to keep ignoring the fight to walk resources
        // home, and a fresh ground/building movement command redirects it the
        // same way (see MoveToPoint/MoveToBuilding; MoveToResource is the
        // exception — it only swaps the objective, see MoveToResource's own
        // doc comment) — either way it resumes seeking its objective fresh
        // the next time it goes idle during the
        // day, same as any other cancelled task.
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

        // While selected, the player is actively steering this turtle by hand
        // and it should ignore every standing duty (objective-seeking, aggro,
        // idle) until given an explicit order or deselected — both of which
        // happen for free the instant TurtleSelectionController.HandleClick
        // issues a MoveTo*/deselects, since IsSelected simply goes false and
        // Update() resumes evaluating everything below fresh next frame.
        if (IsSelected)
        {
            FollowMouse();
            return;
        }

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

        // Last thing before roaming: cargo aboard and nothing left to do with
        // it, so walk it home rather than ambling around still holding it.
        if (!storming && carriedResources.Count > 0 && TryBeginCarriedDelivery()) return;

        UpdateIdle(storming);
    }

    /// <summary>Debug-only Scene view visualization — no gameplay effect, only runs while the Editor is drawing gizmos. Draws this turtle's current path (see currentPath/currentPathIndex/pathFinalDestination, all set by BeginPathTo and advanced by UpdatePathFollowing): the exact remaining route from here to its destination, so a pathfinding bug (an unexpectedly long detour, a route that hugs an obstacle too closely, one that never advances, etc.) is visible directly in the Scene view instead of having to reason about it from code. Also draws the aggro system's two live radii (see the Aggro Debug fields' own tooltips) so both can be eyeballed and tuned directly rather than inferred from behavior alone.</summary>
    private void OnDrawGizmos()
    {
        DrawPathGizmo();
        DrawAggroGizmos();
    }

    private void DrawPathGizmo()
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

    private void DrawAggroGizmos()
    {
        if (showAggroRangeGizmo)
        {
            Gizmos.color = aggroRangeGizmoColor;
            Gizmos.DrawWireSphere(transform.position, aggroDistance);
        }

        // Only meaningful while a ground-move order is actually in progress —
        // moveTargetMarker/aggroUnlockRadius are stale leftovers from the last
        // MoveToPoint call otherwise, so drawing them unconditionally would be
        // misleading (a circle sitting on wherever the last order happened to
        // send this turtle, long after it arrived and moved on to something else).
        if (showAggroUnlockRadiusGizmo && isGroundMove && moveTargetMarker != null)
        {
            Gizmos.color = aggroUnlockRadiusGizmoColor;
            Gizmos.DrawWireSphere(moveTargetMarker.position, aggroUnlockRadius);
        }

        if (showAggroTargetGizmo && isAggroed && aggroTarget != null)
        {
            Gizmos.color = aggroRangeGizmoColor;
            Gizmos.DrawLine(transform.position, aggroTarget.transform.position);
        }
    }

    public void Select()
    {
        if (IsSelected) return;

        IsSelected = true;
        ApplyTint(selectedTint);
        squashAndStretch?.Play();
    }

    public void Deselect()
    {
        if (!IsSelected) return;

        IsSelected = false;
        RevertTint();
    }

    /// <summary>Steers directly at the mouse cursor's world position — no pathfinding, just the same moveTargetMarker/steering hookup a ground-move order uses. Only called while IsSelected (see Update's early-return above); fins stop once within arrivalDistance so the turtle doesn't paddle in place under a stationary cursor.</summary>
    private void FollowMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null) return;

        Vector3 worldPoint = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        worldPoint.z = 0f;

        moveTargetMarker.position = worldPoint;
        steering.SetTarget(moveTargetMarker);
        SetFinsPlaying(Vector2.Distance(transform.position, worldPoint) > arrivalDistance);
    }

    /// <summary>A pure transient detour — works at any time, day or night, and never touches the target resource objective. Once arrived, the turtle simply falls back to its normal day/night default (seek the objective if it's day and has one, nest-guard/wander otherwise) via Update()'s own idle handling, exactly like a finished Rune visit or Watchtower release does. Also suppresses new aggro acquisition until this turtle gets within a radius of worldPoint scaled to how far away it is (see aggroUnlockRadiusBase/TryAcquireAggroTarget), so relocating a turtle to help elsewhere on the island doesn't just have it immediately lock back onto whatever trash happens to be nearby the moment it sets off — and a longer trip's wider radius means it doesn't have to thread the exact clicked point through whatever's in the way first. Dismounts a stationed turtle first (see Unpark) rather than silently doing nothing — a Watchtower's turtle would otherwise be stuck there, unreachable by any order, until the storm ends releases it automatically; Watchtower notices on its own next Update and forgets this turtle so a new one can be stationed.</summary>
    public void MoveToPoint(Vector3 worldPoint)
    {
        if (isParked) Unpark();

        // Clamped rather than refused, for the same reason the deep-water
        // redirect below clamps: the player gets a turtle that goes as far
        // that way as it is allowed to, instead of one that ignores the click.
        // A no-op unless the Short Leash modifier is active.
        worldPoint = ClampToLeash(worldPoint);

        // Redirects an in-progress beeline-home trip instead of ignoring the
        // order (see CancelReturnTrip) — the turtle keeps whatever it's
        // carrying and delivers it later, either via a subsequent order back
        // near the nest or passively if this new destination happens to bring
        // it into range anyway.
        CancelReturnTrip();

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

        float orderDistance = Vector2.Distance(transform.position, worldPoint);
        aggroUnlockRadius = Mathf.Min(aggroUnlockRadiusBase + orderDistance * aggroUnlockRadiusPerDistance, aggroUnlockRadiusMax);
        CancelAggro();
        moveTargetMarker.position = worldPoint;
        ApplyTask(moveTargetMarker, isGroundMove: true);
    }

    /// <summary>Assigns the target resource objective (the player selecting a turtle then clicking a resource/Coconut/Jellyfish) and, if conditions allow, immediately starts moving to harvest this exact instance. The objective itself is always recorded regardless of time of day or an in-progress delivery trip — "can still be changed, but the turtle doesn't act on it until morning." Unlike MoveToPoint/MoveToBuilding, this does NOT redirect an in-progress beeline-home trip — picking a new resource while beelining home just updates what the turtle pursues once it's delivered and free again; the current trip (and its path) is left alone rather than being cancelled out from under it. The immediate move is otherwise still gated while storming (queued for morning — harvesting is a daytime-only activity, see HandleHeadHit's storm guards).</summary>
    public void MoveToResource(Transform resourceTransform) => MoveToResource(resourceTransform, isPlayerOrder: true);

    /// <summary>
    /// The full form. isPlayerOrder separates the two callers that look
    /// identical from here but shouldn't both re-resolve: a genuine player
    /// click, which may be redirected to a different instance by the Far
    /// Sighted/Short Leash modifiers, and SeekTargetResourceOrIdle's internal
    /// "go find another one of these", which has already run that same
    /// resolution itself. Both would now reach the same answer — the search is
    /// nest-anchored and so gives the same result whoever asks — so this is
    /// purely about not running a second full scan over every node per re-seek.
    /// </summary>
    private void MoveToResource(Transform resourceTransform, bool isPlayerOrder)
    {
        if (isParked) Unpark();

        if (TryGetHarvestType(resourceTransform, out ResourceManager.ResourceType type))
        {
            targetResourceType = type;
            hasTargetResource = true;

            if (isPlayerOrder) resourceTransform = ResolveOrderedHarvestTarget(type, resourceTransform);
        }

        if (isReturningToNest) return;
        if (DayStormCycle.IsStorming) return;

        // Null only when a modifier rejected every instance of the type (the
        // clicked one is outside the leash and no other is inside it). The
        // objective is still recorded above, so TrySeekTargetResource keeps
        // rechecking and picks the job up the moment one becomes reachable —
        // exactly what happens when nothing of a type is harvestable yet.
        if (resourceTransform == null)
        {
            harvestRetryTimer = harvestRetryInterval;
            return;
        }

        CancelAggro();
        ApplyTask(resourceTransform, isGroundMove: false, isResourceTask: true);
    }

    /// <summary>
    /// Which instance a player's resource click actually sends this turtle to.
    /// Normally the one they clicked, unchanged — both branches below
    /// short-circuit to it when neither modifier is active, so ordinary runs
    /// keep the exact behavior of clicking a specific tree and getting that
    /// tree.
    ///
    /// Far Sighted overrides the click outright (that IS the modifier: you no
    /// longer get to say which one), sending the turtle to whatever sits
    /// furthest from the nest instead — the same answer every turtle gets, and
    /// the same answer a later re-seek will get.
    /// Short Leash only intervenes when the clicked instance is out of bounds,
    /// substituting the nearest one that isn't, so the order still does
    /// something sensible instead of being swallowed.
    /// </summary>
    private Transform ResolveOrderedHarvestTarget(ResourceManager.ResourceType type, Transform clicked)
    {
        if (SeeksFurthestResource) return FindHarvestTarget(type, HarvestSearchOrigin, furthest: true);

        if (!IsWithinLeash(clicked.position)) return FindHarvestTarget(type, transform.position, furthest: false);

        return clicked;
    }

    /// <summary>Sends the turtle to an interactable building, switching it onto the TurtleInteracting layer so it can physically reach and bump into it. Never touches the target resource objective — a Rune visit (see ClearTask) or a Watchtower stationing/release (see Park/Unpark) is just a transient detour, exactly like MoveToPoint. Redirects an in-progress beeline-home trip rather than blocking the order (see CancelReturnTrip). Dismounts a stationed turtle first (see MoveToPoint's own doc comment) so it can be sent straight to a different building, including another Watchtower.</summary>
    public void MoveToBuilding(Transform buildingTransform)
    {
        if (isParked) Unpark();

        // The one order the Short Leash modifier refuses rather than clamps:
        // a building visit is only meaningful at the building itself (a Rune
        // has to be bumped, a Watchtower stood on), so walking to the edge of
        // the leash and stopping short would look like the order worked when
        // it can never complete. Logged rather than silent, since from the
        // player's side an ignored click is otherwise indistinguishable from
        // a bug. No-op unless the modifier is active.
        if (!IsWithinLeash(buildingTransform.position))
        {
            Debug.Log($"TurtleAgent: \"{buildingTransform.name}\" is beyond this run's turtle leash ({UpgradeManager.Instance.TurtleLeashRadius:F1} units from the nest), so the order was refused.");
            return;
        }

        CancelReturnTrip();

        CancelAggro();
        ApplyTask(buildingTransform, isGroundMove: false);
    }

    /// <summary>Plays this unit's hit sound at its own position. Public because a hit is registered on BOTH sides of the contact depending on what was struck: HandleHeadHit below covers everything the turtle reaches out and uses, while a hit on trash is registered by TrashHealth instead (that is where damage, crits and the CanAttackTrash gate all live), and both should sound the same.</summary>
    public void PlayHeadHitSound() => headHitSound.Play(transform.position);

    /// <summary>Called by TurtleHeadHitbox — only the head's contact counts as a harvest/rune hit, not the shell. Resource/Coconut/Jellyfish hits are all no-ops while storming or while selected (see each branch's guard below) — harvesting is strictly a daytime activity, and a turtle the player is actively mouse-steering shouldn't collect anything it's dragged into.</summary>
    public void HandleHeadHit(Collider2D other)
    {
        ResourceNode node = other.GetComponentInParent<ResourceNode>();
        if (node != null)
        {
            PlayHeadHitSound();

            // Bouncing off a resource is still fine during a storm (physical
            // collision is untouched) — it just stops yielding anything. A
            // depleted (dormant) node also yields nothing until it respawns.
            // TutorialManager.IsHarvestAllowed also blocks the type not being
            // asked for during its rock/wood collection steps. !IsSelected
            // stops a turtle the player is actively mouse-steering from
            // harvesting anything it happens to be dragged into.
            if (!DayStormCycle.IsStorming && !IsSelected && node.IsHarvestable && TutorialManager.IsHarvestAllowed(node.ResourceType))
            {
                int amount = UpgradeManager.Instance != null ? UpgradeManager.Instance.RollHarvestAmount(node.ResourceType, this) : 1;

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
                // order (should be rare now that CheckResourceTaskStillHarvestable
                // catches this proactively every frame en route — this stays as
                // a fallback for the same-frame edge case where a node depletes
                // right as this turtle's own head hit lands).
                HandleDepletedResourceTask(node.ResourceType);
            }
            return;
        }

        RuneEffect rune = other.GetComponentInParent<RuneEffect>();
        if (rune != null)
        {
            PlayHeadHitSound();
            rune.RegisterHit(this);
            return;
        }

        Coconut coconut = other.GetComponentInParent<Coconut>();
        if (coconut != null)
        {
            PlayHeadHitSound();
            if (!DayStormCycle.IsStorming && !IsSelected)
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
            PlayHeadHitSound();
            if (!DayStormCycle.IsStorming && !IsSelected)
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
        if (watchtower != null)
        {
            PlayHeadHitSound();
            watchtower.TryStationTurtle(this);
        }
    }

    /// <summary>Shared by HandleHeadHit's on-contact depletion branch and CheckResourceTaskStillHarvestable's proactive per-frame one: either deliver what's already carried (no reason to make the trip back later) or go find another instance of the same resource type right away.</summary>
    private void HandleDepletedResourceTask(ResourceManager.ResourceType type)
    {
        if (carriedResources.Count >= carryCapacity) BeginReturnToNest();
        else SeekTargetResourceOrIdle(type);
    }

    /// <summary>
    /// Proactively re-checks, every frame this turtle has an active resource
    /// task, whether its target ResourceNode is still harvestable — not just
    /// on physical contact (see HandleHeadHit's own branch, now a same-frame
    /// fallback) — so a node another turtle depletes while this one is still
    /// mid-journey toward it gets caught immediately instead of only once this
    /// turtle finally arrives and bumps it, potentially after crossing the
    /// whole island for nothing. Coconut/JellyfishAgent targets don't need
    /// this: consuming one destroys it outright rather than leaving it
    /// dormant, and UpdatePathFollowing/currentTaskTarget already handle a
    /// destroyed target going null mid-path.
    /// </summary>
    /// <summary>The transform currentTaskNode was resolved from, so the lookup below repeats only when the task actually changes rather than every frame.</summary>
    private Transform currentTaskNodeSource;
    private ResourceNode currentTaskNode;

    /// <summary>
    /// The current task target's ResourceNode, looked up once per task instead
    /// of once per frame.
    ///
    /// This is checked every frame by CheckResourceTaskStillHarvestable, and a
    /// GetComponent is not free — with a task in hand every turtle was paying
    /// for one every frame, for an answer that can only change when the task
    /// does. Keyed off the transform rather than set at the assignment sites so
    /// it stays correct however the task is set, cleared or replaced.
    /// </summary>
    private ResourceNode ResolveCurrentTaskNode()
    {
        if (currentTaskTarget == null)
        {
            currentTaskNodeSource = null;
            currentTaskNode = null;
            return null;
        }

        if (!ReferenceEquals(currentTaskNodeSource, currentTaskTarget))
        {
            currentTaskNodeSource = currentTaskTarget;
            currentTaskNode = currentTaskTarget.GetComponent<ResourceNode>();
        }

        return currentTaskNode;
    }

    private void CheckResourceTaskStillHarvestable()
    {
        if (!isResourceTask || currentTaskTarget == null) return;

        ResourceNode node = ResolveCurrentTaskNode();
        if (node == null || node.IsHarvestable) return;

        HandleDepletedResourceTask(node.ResourceType);
    }

    /// <summary>Always does a fresh "closest instance of type" search — called right after a node depletes/a Coconut or Jellyfish is consumed, after a full-capacity delivery trip completes, and by TrySeekTargetResource's idle recheck. Only goes idle once genuinely nothing of type is harvestable anywhere — but leaves hasTargetResource/targetResourceType completely untouched (they aren't even parameters here anymore), so TrySeekTargetResource's periodic recheck picks the objective back up the instant something reactivates, rather than losing it. Also idles without searching while storming, since harvesting is a daytime-only activity.</summary>
    private void SeekTargetResourceOrIdle(ResourceManager.ResourceType type)
    {
        if (DayStormCycle.IsStorming)
        {
            StopAndIdle();
            return;
        }

        // Tutorial only, and false in all normal play (see
        // SuspendBarredObjective): don't walk back to a type the tutorial has
        // locked out. The objective stays alive and the retry timer keeps
        // ticking, so the job resumes on its own once the lock lifts. This is
        // the single choke point every "go find another one of my type" route
        // passes through — a node depleting, the tail of a delivery,
        // TrySeekTargetResource's recheck — so guarding it here covers all
        // three rather than each of them separately.
        if (TutorialManager.IsHarvestRestricted && !TutorialManager.IsHarvestAllowed(type))
        {
            harvestRetryTimer = harvestRetryInterval;

            // Only if there's a live task to stop: this runs every retry
            // interval while the lock stands, and StopAndIdle resets the idle
            // wander state, which would leave a waiting turtle twitching.
            if (isResourceTask) StopAndIdle();
            return;
        }

        Transform next = FindHarvestTarget(type, HarvestSearchOrigin, SeeksFurthestResource);
        if (next != null)
        {
            // Not a player order: the target above is already the right one
            // for whatever modifiers are in force, so re-resolving it there
            // would just scan every node a second time for the same answer.
            MoveToResource(next, isPlayerOrder: false);
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

    /// <summary>True while the Far Sighted run modifier is in force. Read live off UpgradeManager rather than cached, exactly like this class already reads DayStormCycle.IsStorming.</summary>
    private static bool SeeksFurthestResource => UpgradeManager.Instance != null && UpgradeManager.Instance.SeekFurthestResource;

    /// <summary>
    /// The point a harvest search measures from: wherever this turtle is now
    /// normally, but the NEST while Far Sighted is on — that modifier is "go
    /// to the furthest node from home", not "from here".
    ///
    /// A fixed, shared anchor is the whole reason it reads as a coherent rule
    /// rather than as noise. Every turtle agrees on which node is furthest, an
    /// order and every later re-seek agree with each other, and a turtle that
    /// just delivered picks the next node outward instead of re-measuring from
    /// the nest it is standing on and being sent back to the one it emptied.
    ///
    /// Falls back to this turtle's own position if the nest is gone — the run
    /// is already over at that point, so the only thing that matters is that
    /// the search still returns something rather than throwing.
    /// </summary>
    private Vector3 HarvestSearchOrigin
    {
        get
        {
            if (!SeeksFurthestResource) return transform.position;

            TurtleNest nest = TurtleNest.Instance;
            return nest != null ? nest.transform.position : transform.position;
        }
    }

    /// <summary>Finds the best still-valid harvest target of harvestType relative to fromPosition — a still-harvestable ResourceNode of that type, or a live Coconut/JellyfishAgent — or null if none qualify. furthest inverts the comparison for the Far Sighted run modifier; everything out of bounds of the Short Leash modifier is skipped either way (see IsWithinLeash, which passes everything when that modifier is off). Backs both SeekTargetResourceOrIdle's re-seek and a player order's redirect.</summary>
    private static Transform FindHarvestTarget(ResourceManager.ResourceType harvestType, Vector3 fromPosition, bool furthest)
    {
        switch (harvestType)
        {
            case ResourceManager.ResourceType.Coconut:
                return FindBestCoconut(fromPosition, furthest);
            case ResourceManager.ResourceType.JellyfishGuts:
                return FindBestJellyfish(fromPosition, furthest);
            default:
                return FindBestHarvestableNode(harvestType, fromPosition, furthest);
        }
    }

    /// <summary>The seed for the running best in each search below — deliberately the extreme the comparison can only improve on, so the first accepted candidate always wins outright whichever direction is being searched.</summary>
    private static float SeedBestSqrDistance(bool furthest) => furthest ? float.MinValue : float.MaxValue;

    /// <summary>Whether a candidate at sqrDistance beats the running best. The one place the nearest/furthest split actually lives — every search below is otherwise identical in both directions.</summary>
    private static bool IsBetterCandidate(float sqrDistance, float bestSqrDistance, bool furthest)
        => furthest ? sqrDistance > bestSqrDistance : sqrDistance < bestSqrDistance;

    private static Transform FindBestHarvestableNode(ResourceManager.ResourceType type, Vector3 fromPosition, bool furthest)
    {
        ResourceNode best = null;
        float bestSqrDistance = SeedBestSqrDistance(furthest);

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null || node.ResourceType != type || !node.IsHarvestable) continue;
            if (!IsWithinLeash(node.transform.position)) continue;

            float sqrDistance = ((Vector2)node.transform.position - (Vector2)fromPosition).sqrMagnitude;
            if (IsBetterCandidate(sqrDistance, bestSqrDistance, furthest))
            {
                bestSqrDistance = sqrDistance;
                best = node;
            }
        }

        return best != null ? best.transform : null;
    }

    private static Transform FindBestCoconut(Vector3 fromPosition, bool furthest)
    {
        Coconut best = null;
        float bestSqrDistance = SeedBestSqrDistance(furthest);

        foreach (Coconut coconut in Coconut.AllCoconuts)
        {
            if (coconut == null) continue;
            if (!IsWithinLeash(coconut.transform.position)) continue;

            float sqrDistance = ((Vector2)coconut.transform.position - (Vector2)fromPosition).sqrMagnitude;
            if (IsBetterCandidate(sqrDistance, bestSqrDistance, furthest))
            {
                bestSqrDistance = sqrDistance;
                best = coconut;
            }
        }

        return best != null ? best.transform : null;
    }

    private static Transform FindBestJellyfish(Vector3 fromPosition, bool furthest)
    {
        JellyfishAgent best = null;
        float bestSqrDistance = SeedBestSqrDistance(furthest);

        foreach (JellyfishAgent jellyfish in JellyfishAgent.AllJellyfish)
        {
            if (jellyfish == null) continue;
            if (!IsWithinLeash(jellyfish.transform.position)) continue;

            float sqrDistance = ((Vector2)jellyfish.transform.position - (Vector2)fromPosition).sqrMagnitude;
            if (IsBetterCandidate(sqrDistance, bestSqrDistance, furthest))
            {
                bestSqrDistance = sqrDistance;
                best = jellyfish;
            }
        }

        return best != null ? best.transform : null;
    }

    // ---------------------------------------------------------------------
    // Short Leash run modifier (UpgradeManager.TurtleLeashRadius).
    //
    // Both helpers below are no-ops — the radius is 0, or there is no nest to
    // measure from — in any run that didn't take the modifier, which is why
    // every call site can invoke them unconditionally instead of branching.
    // ---------------------------------------------------------------------

    /// <summary>The live leash radius, or 0 when turtles may roam freely. 0 is the "off" value everywhere below, so a nest-less scene (the Menu's ambience turtles) reads as unleashed rather than as leashed to the origin.</summary>
    private static float LeashRadius
    {
        get
        {
            float radius = UpgradeManager.Instance != null ? UpgradeManager.Instance.TurtleLeashRadius : 0f;
            return radius > 0f && TurtleNest.Instance != null ? radius : 0f;
        }
    }

    /// <summary>Whether point is somewhere a leashed turtle is allowed to be. Always true when the modifier is off.</summary>
    private static bool IsWithinLeash(Vector3 point)
    {
        float radius = LeashRadius;
        if (radius <= 0f) return true;

        return ((Vector2)point - (Vector2)TurtleNest.Instance.transform.position).sqrMagnitude <= radius * radius;
    }

    /// <summary>point pulled back onto the leash circle if it falls outside it, unchanged otherwise (and always unchanged when the modifier is off). Preserves z so a clamped destination still sits in the same plane the caller was working in.</summary>
    private static Vector3 ClampToLeash(Vector3 point)
    {
        float radius = LeashRadius;
        if (radius <= 0f) return point;

        Vector2 nestPosition = TurtleNest.Instance.transform.position;
        Vector2 offset = (Vector2)point - nestPosition;
        if (offset.sqrMagnitude <= radius * radius) return point;

        Vector2 clamped = nestPosition + offset.normalized * radius;
        return new Vector3(clamped.x, clamped.y, point.z);
    }

    /// <summary>Cached so the per-frame aggro scan doesn't allocate a fresh closure every frame per turtle. Stateless — it reads only the two statics IsWithinLeash already reads — so one shared instance serves every turtle.</summary>
    private static readonly System.Func<TrashHealth, bool> TrashWithinLeash = trash => IsWithinLeash(trash.transform.position);

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

    /// <summary>The prefab-authored carryCapacity, captured in Awake before any upgrade can raise it.</summary>
    private int baseCarryCapacity;

    /// <summary>Sets carry capacity to the authored baseline plus an upgrade-cumulative bonus. Called by UpgradeManager (safe to call repeatedly — it overwrites rather than compounds, since the caller always passes the running total). Every existing capacity check reads carryCapacity directly, so nothing else needs to know this happened.</summary>
    public void SetCarryCapacityBonus(int bonus) => carryCapacity = baseCarryCapacity + Mathf.Max(0, bonus);

    private int barnacleBonusDamage;

    /// <summary>
    /// Applies the run-wide Barnacles upgrade: a permanent movement slowdown
    /// paid for with permanent bonus damage against trash, plus the barnacle
    /// overlay art. Called by UpgradeManager, safe to call repeatedly — all
    /// three halves overwrite rather than accumulate.
    ///
    /// Takes an explicit active flag rather than inferring "off" from a
    /// multiplier of 1 or zero damage: those are legitimate authored values, so
    /// inferring would leave a turtle visually crusted by a barnacle card tuned
    /// to no damage. With the flag, one call site covers both turning barnacles
    /// on and confirming they're off.
    ///
    /// The slowdown gets its own TurtleLocomotion layer rather than sharing the
    /// permanent-upgrade one, so a Turtle Speed card and this compose instead of
    /// clobbering each other; being a buff layer rather than a day/night branch
    /// is also what makes it apply around the clock.
    /// </summary>
    public void ApplyBarnacles(bool active, float speedMultiplier, int bonusDamage)
    {
        barnacleBonusDamage = active ? bonusDamage : 0;
        locomotion.SetBarnacleSpeedMultiplier(active ? speedMultiplier : 1f);
        SetActiveAll(barnacleVisuals, active);
    }

    /// <summary>
    /// Forces every upgrade-driven overlay back to its un-upgraded look. Awake
    /// already does this for a turtle that lives a normal life, so this exists
    /// for the one case that doesn't: MenuIslandAmbience instantiates the real
    /// turtle prefab and then strips TurtleAgent straight back off it, leaving
    /// nothing behind that would ever hide gameplay art the menu has no concept
    /// of. Awake happens to run during that Instantiate and so covers it today,
    /// but only as an accident of ordering — calling this explicitly before the
    /// strip is what actually guarantees it.
    /// </summary>
    public void HideUpgradeVisuals() => SetActiveAll(barnacleVisuals, false);

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

    // Algae works like Campfire — linear stacking across overlapping patches,
    // applied/removed on entering/leaving — with one addition: once the Algae
    // Residue card is picked, stepping off doesn't drop the buff immediately,
    // it holds the bonus that was just lost for AlgaeLingerDuration seconds.
    // The lingering amount is tracked separately from the live in-range total
    // (rather than by delaying the subtraction) so re-entering a patch mid-
    // linger can't double-count: the effective bonus is simply whichever of
    // the two is larger. With the duration at its default 0 this behaves
    // exactly like Campfire does — instant on, instant off.
    private float algaeBonusTotal;
    private float lingeringAlgaeBonus;
    private float algaeLingerTimer;

    /// <summary>Called by an AlgaePatch the instant this turtle enters its radius. Overlapping patches stack linearly, not last-wins, and any leftover linger from a patch just stepped off is dropped in favor of the live bonus.</summary>
    public void ApplyAlgaeSpeedBuff(float bonusAmount)
    {
        algaeBonusTotal += bonusAmount;
        lingeringAlgaeBonus = 0f;
        algaeLingerTimer = 0f;
        RecalculateAlgaeSpeed();
    }

    /// <summary>Called by an AlgaePatch the instant this turtle leaves its radius (with the same bonusAmount it applied on entry). If that was the last patch and the Algae Residue card has been picked, the bonus is carried for a few more seconds instead of ending here.</summary>
    public void RemoveAlgaeSpeedBuff(float bonusAmount)
    {
        algaeBonusTotal = Mathf.Max(0f, algaeBonusTotal - bonusAmount);

        float lingerDuration = UpgradeManager.Instance != null ? UpgradeManager.Instance.AlgaeLingerDuration : 0f;
        if (algaeBonusTotal <= 0f && lingerDuration > 0f && bonusAmount > 0f)
        {
            lingeringAlgaeBonus = bonusAmount;
            algaeLingerTimer = lingerDuration;
        }

        RecalculateAlgaeSpeed();
    }

    /// <summary>Counts the linger down once this turtle is off every patch. Called every frame from Update; a no-op whenever nothing is lingering.</summary>
    private void UpdateAlgaeLinger()
    {
        if (algaeLingerTimer <= 0f) return;

        algaeLingerTimer -= Time.deltaTime;
        if (algaeLingerTimer > 0f) return;

        algaeLingerTimer = 0f;
        lingeringAlgaeBonus = 0f;
        RecalculateAlgaeSpeed();
    }

    private void RecalculateAlgaeSpeed()
    {
        locomotion.SetAlgaeSpeedMultiplier(1f + Mathf.Max(algaeBonusTotal, lingeringAlgaeBonus));
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
    private bool hasJellyfishBuff;

    /// <summary>True while this turtle's Coconut knockback buff is active for the night — checked by TrashHealth on every hit.</summary>
    public bool HasCoconutKnockbackBuff => hasCoconutBuff;

    /// <summary>How hard this turtle's hits shove trash while its Coconut buff is up, scaled by whatever food-potency upgrades are in force. Read live by TrashHealth at the moment of a hit, so it needs no refresh of its own.</summary>
    public float CoconutKnockbackForce => coconutKnockbackForce * FoodEffectScale;

    /// <summary>This turtle's live Jellyfish contribution to BonusDamageToTrash. Floored at the authored value once the buff is up rather than allowed to round down, since a potency upgrade must never make a buff weaker.</summary>
    private int EffectiveJellyfishBonusDamage =>
        hasJellyfishBuff ? Mathf.Max(jellyfishBonusDamage, Mathf.RoundToInt(jellyfishBonusDamage * FoodEffectScale)) : 0;

    /// <summary>How many of the three night food buffs are running on this turtle right now — what Complete Diet keys off (see FoodEffectScale).</summary>
    private int ActiveFoodBuffCount => (hasSeaweedBuff ? 1 : 0) + (hasCoconutBuff ? 1 : 0) + (hasJellyfishBuff ? 1 : 0);

    /// <summary>
    /// The factor every food buff's strength is scaled by right now: the
    /// run-wide potency upgrade, times Complete Diet's bonus when this
    /// particular turtle is running two or more different buffs at once.
    ///
    /// Per-turtle rather than run-wide precisely because of that second half —
    /// which turtle is eating what changes constantly through a storm as the
    /// nest's waves land, so there is no single answer for the whole
    /// population.
    /// </summary>
    private float FoodEffectScale
    {
        get
        {
            if (UpgradeManager.Instance == null) return 1f;

            float scale = UpgradeManager.Instance.FoodEffectMultiplier;
            if (UpgradeManager.Instance.CompleteDietUnlocked && ActiveFoodBuffCount >= 2)
            {
                scale *= UpgradeManager.Instance.CompleteDietMultiplier;
            }

            return scale;
        }
    }

    /// <summary>
    /// Re-pushes the Seaweed speed buff at its current strength, or clears the
    /// layer if that buff isn't running.
    ///
    /// Only Seaweed needs this. Coconut's knockback and Jellyfish's damage are
    /// read off this turtle at the instant of a hit and so track a strength
    /// change for free; Seaweed's was written into a TurtleLocomotion layer
    /// when the buff landed, and nothing would otherwise rewrite it. Called
    /// whenever either input can have moved: a buff starting or ending
    /// (Complete Diet's threshold), or an upgrade being picked
    /// (UpgradeManager.RefreshFoodBuffsOnAllTurtles).
    /// </summary>
    public void RefreshFoodBuffStrength()
    {
        locomotion.SetTemporaryBuffSpeedMultiplier(
            hasSeaweedBuff ? 1f + (seaweedSpeedMultiplier - 1f) * FoodEffectScale : 1f);
    }

    /// <summary>Grants this turtle's Seaweed night buff (speed). Called once per unit received during TurtleNest's night-start distribution — flat on/off regardless of how many Seaweed this turtle received tonight, so repeat calls are harmless no-ops.</summary>
    public void ApplySeaweedBuff()
    {
        if (hasSeaweedBuff) return;

        hasSeaweedBuff = true;
        // Every apply/clear below re-pushes Seaweed's strength rather than
        // setting it once, because Complete Diet's bonus depends on HOW MANY
        // buffs are up — so a Coconut landing has to restrengthen a Seaweed
        // buff that started alone.
        RefreshFoodBuffStrength();
        PlayAll(seaweedBuffEffects);
    }

    /// <summary>Grants this turtle's Coconut night buff (knockback on hit). Flat on/off, same rationale as ApplySeaweedBuff.</summary>
    public void ApplyCoconutBuff()
    {
        if (hasCoconutBuff) return;

        hasCoconutBuff = true;
        RefreshFoodBuffStrength();
        PlayAll(coconutBuffEffects);
    }

    /// <summary>Grants this turtle's Jellyfish night buff (bonus damage on hit, folded into BonusDamageToTrash). Flat on/off, same rationale as ApplySeaweedBuff.</summary>
    public void ApplyJellyfishBuff()
    {
        if (hasJellyfishBuff) return;

        hasJellyfishBuff = true;
        RefreshFoodBuffStrength();
        PlayAll(jellyfishBuffEffects);
    }

    /// <summary>Toggles a whole set of buff-visual objects at once, tolerating both an unassigned array and empty elements — the GameObject counterpart to PlayAll/StopAll below, for a visual that's an added overlay rather than a particle burst or a sprite swap.</summary>
    private static void SetActiveAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;

        foreach (GameObject obj in objects)
        {
            if (obj != null) obj.SetActive(active);
        }
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
        RefreshFoodBuffStrength();
        StopAll(seaweedBuffEffects);
    }

    /// <summary>Turns off the Coconut knockback buff early — same rationale/callers as ClearSeaweedBuff.</summary>
    public void ClearCoconutBuff()
    {
        if (!hasCoconutBuff) return;

        hasCoconutBuff = false;
        RefreshFoodBuffStrength();
        StopAll(coconutBuffEffects);
    }

    /// <summary>Turns off the Jellyfish damage buff early — same rationale/callers as ClearSeaweedBuff.</summary>
    public void ClearJellyfishBuff()
    {
        if (!hasJellyfishBuff) return;

        hasJellyfishBuff = false;
        RefreshFoodBuffStrength();
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

        // The finless equivalent of the per-fin frequency boost below, so a
        // Crab that earns this rune actually speeds up instead of the whole
        // buff quietly amounting to nothing for it.
        locomotion.MultiplyStrokelessRate(frequencyMultiplier);

        if (fins == null) return;
        foreach (LimbOscillator fin in fins)
        {
            if (fin == null) continue;

            fin.MultiplyFrequency(frequencyMultiplier);
            fin.SyncPhase();
        }
    }

    private Coroutine glueSlowCoroutine;

    /// <summary>Applies a temporary slow (see GlueSlowOnHit) for duration seconds, independent of the day/night edge every other buff/debuff is gated on — the first duration-based effect on a turtle. Re-calling before the previous one expires restarts the timer rather than stacking.</summary>
    public void ApplyGlueSlow(float multiplier, float duration)
    {
        if (glueSlowCoroutine != null) StopCoroutine(glueSlowCoroutine);
        glueSlowCoroutine = StartCoroutine(GlueSlowRoutine(multiplier, duration));
    }

    private IEnumerator GlueSlowRoutine(float multiplier, float duration)
    {
        locomotion.SetGlueDebuffSpeedMultiplier(multiplier);
        yield return new WaitForSeconds(duration);
        locomotion.SetGlueDebuffSpeedMultiplier(1f);
        glueSlowCoroutine = null;
    }

    /// <summary>Cancels an in-progress resource-harvest task the instant night falls — the target objective (targetResourceType) isn't touched, only the transient in-flight task, so it resumes on its own the next time this turtle goes idle during the day (see TrySeekTargetResource). Ground-move/building tasks and aggro are untouched.</summary>
    private void CancelResourceTaskForNight()
    {
        if (isResourceTask) StopAndIdle();
    }

    /// <summary>
    /// Tutorial only (the caller gates on TutorialManager.IsHarvestRestricted,
    /// which is false in all normal play). Stands a turtle down from a standing
    /// resource objective the tutorial has just locked out, and walks it home.
    ///
    /// Without this, a turtle sent at — or already walking to — a barred
    /// resource keeps working it, arrives, and bumps a node that can never pay
    /// out, since HandleHeadHit silently ignores a hit on a barred type. It
    /// looks like it's working and earns nothing, which is the worst of both
    /// readings. Walking home instead makes the lock legible: the turtle
    /// visibly gives up on that job.
    ///
    /// The objective is SUSPENDED, not cleared. It used to be cleared, and that
    /// was a real bug rather than a nuance: every one of the tutorial's three
    /// lock changes (wood-only, then stone-only, then stone-barred) stripped
    /// the objective off every turtle assigned to the type it had just barred,
    /// permanently — so by the "keep collecting until the storm rolls in" step,
    /// the turtles the player had already put to work were standing around
    /// doing nothing, with no indication why and no way back short of
    /// re-ordering each one. A lock the tutorial itself lifts a step later must
    /// not retire a standing order the player gave. Suspending also restores
    /// the invariant this whole class rests on (see the class doc comment):
    /// nothing but a fresh MoveToResource ever clears an objective, which is
    /// why no detour anywhere in here needs save/restore logic.
    ///
    /// Nothing has to put the job back afterwards. SeekTargetResourceOrIdle
    /// declines to walk to a barred type while the lock stands, and
    /// TrySeekTargetResource's periodic recheck picks the objective straight
    /// back up once it lifts — the same mechanism that already covers "nothing
    /// of my type is harvestable right now".
    ///
    /// Only ever interrupts work actually in progress. A turtle already idle,
    /// already walking home, or off on another errand has nothing to give up —
    /// and since this is polled every frame for as long as the lock stands, an
    /// unguarded version would restart the trip home on every one of them.
    /// </summary>
    private void SuspendBarredObjective()
    {
        if (!isResourceTask) return;

        // Clears the in-flight harvest task (steering, path, fins) before the
        // trip home is started over the top of it.
        StopAndIdle();

        BeginReturnToNest();
    }

    /// <summary>Cancels an in-progress beeline-home trip — called the instant night falls (so a turtle that happened to fill up right before dusk responds to the storm, aggro/nest-guard idle, instead of ignoring it to walk its load home) and by a fresh ground/building movement command (MoveToPoint/MoveToBuilding — MoveToResource deliberately does NOT call this, see its own doc comment), so the turtle can be redirected mid-trip. Either way it's still carrying everything it collected, just no longer forced to beeline; CheckPassiveNestDelivery still drops it off in passing whenever normal movement (e.g. nest-guard idle already heads toward the nest while storming, or wherever the new order sends it) brings it back into range. Leaves an already-in-progress delivery (deliverCoroutine running, i.e. the turtle already arrived and is popping units off) alone rather than cutting it off mid-flight.</summary>
    private void CancelReturnTrip()
    {
        if (!isReturningToNest || deliverCoroutine != null) return;

        isReturningToNest = false;
        isFollowingPath = false;
        steering.SetTarget(null);
        SetFinsPlaying(false);
    }

    /// <summary>Only called while not already aggroed (see Update()). A live geometric check, not a timer — re-evaluated fresh every frame against this turtle's current distance to moveTargetMarker, so it's never an estimate that can expire early (a dense clump's physics jostling slowing the trip down doesn't matter) or linger after the turtle's already well clear. groundMoveReachable guards against the one edge case a pure distance check can't self-correct: a MoveToPoint destination that turned out unreachable, where this turtle isn't making any progress toward closing that distance at all — without it, such a turtle would be stuck refusing to defend itself forever instead of just sitting there uselessly frozen. isCollidingWithBuilding (true iff currentTaskTarget is an interactable building — a Watchtower or Rune, see UpdateBuildingCollision) blocks aggro entirely rather than just delaying it like the ground-move check below: a turtle deliberately sent to station at a Watchtower or earn a Rune's buff shouldn't get pulled off that order by nearby trash, even mid-walk before it's arrived — this deliberately generalizes past "Watchtower" specifically, since a Rune visit has the same repeated-bump mechanic and would be equally disrupted.</summary>
    private void TryAcquireAggroTarget()
    {
        if (!DayStormCycle.IsStorming) return;
        // A crab sits the storm out at the nest unless the Crab Warriors card
        // has been picked (see CrabAgent) — same condition that stops its head
        // hits from doing any damage, so it never chases what it can't hurt.
        if (!CanAttackTrash) return;
        if (isCollidingWithBuilding) return;

        if (isGroundMove && groundMoveReachable
            && Vector2.Distance(transform.position, moveTargetMarker.position) > aggroUnlockRadius)
        {
            return;
        }

        // Filtered inside the scan rather than after it, so trash sitting just
        // outside the leash can't mask a piece inside it that this turtle is
        // both allowed and willing to fight. Passes everything when the Short
        // Leash modifier is off.
        TrashHealth nearest = LeashRadius > 0f
            ? TrashHealth.FindNearest(transform.position, aggroDistance, TrashWithinLeash)
            : TrashHealth.FindNearest(transform.position, aggroDistance);
        if (nearest == null) return;

        hadSavedTask = currentTaskTarget != null;
        savedTaskTarget = currentTaskTarget;
        savedTaskIsGroundMove = isGroundMove;
        savedTaskIsResourceTask = isResourceTask;

        isAggroed = true;
        aggroTarget = nearest;
        // A fresh chase measures line of sight rather than inheriting the last
        // one's conclusion about a different target in a different direction.
        hasCachedAggroLineOfSight = false;
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
    /// <summary>Last answer from the chase's line-of-sight check, reused between rechecks — see Aggro Line Of Sight Interval.</summary>
    private bool cachedAggroLineOfSight;
    /// <summary>Whether cachedAggroLineOfSight holds an answer at all. Cleared whenever aggro starts or ends, so a new chase always measures rather than inheriting the last one's conclusion.</summary>
    private bool hasCachedAggroLineOfSight;
    private float aggroLineOfSightTimer;
    private Vector3 aggroLineOfSightFrom;
    private Vector3 aggroLineOfSightTo;

    /// <summary>
    /// The chase's line-of-sight answer, recomputed only when it can have
    /// changed: the interval has elapsed, either end has moved far enough to
    /// matter, or there's no answer yet. Everything else in the chase still runs
    /// every frame — this throttles the measurement, not the steering.
    ///
    /// The first check of a chase is deliberately immediate (hasCached is false
    /// on aggro), so nothing about acquiring a target is delayed.
    /// </summary>
    private bool HasAggroLineOfSight(Transform target)
    {
        if (PathfindingManager.Instance == null) return true;

        aggroLineOfSightTimer -= Time.deltaTime;

        bool moved = Vector3.Distance(transform.position, aggroLineOfSightFrom) > aggroLineOfSightRecheckDistance
            || Vector3.Distance(target.position, aggroLineOfSightTo) > aggroLineOfSightRecheckDistance;

        if (hasCachedAggroLineOfSight && !moved && aggroLineOfSightTimer > 0f) return cachedAggroLineOfSight;

        // Staggered rather than every turtle landing on the same frame: they
        // all acquire aggro within a frame or two of a storm starting, so a
        // fixed interval would keep the whole group in lockstep and spike one
        // frame in every interval instead of spreading the cost out.
        aggroLineOfSightTimer = aggroLineOfSightInterval * Random.Range(0.75f, 1.25f);
        aggroLineOfSightFrom = transform.position;
        aggroLineOfSightTo = target.position;
        hasCachedAggroLineOfSight = true;
        cachedAggroLineOfSight = PathfindingManager.Instance.HasLineOfSight(
            transform.position, target.position, null, aggroLineOfSightWidth,
            allowDiagonalSqueeze: true, avoidCoral: false, moverRadius: moverLineOfSightRadius);

        return cachedAggroLineOfSight;
    }

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

        bool hasLineOfSight = HasAggroLineOfSight(target);

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
            SetFinsPlaying(BeginPathTo(target, aggroChasePathShortenSteps));
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
        hasCachedAggroLineOfSight = false;
        isWaitingAtShore = false;
        shoreUnreachable = false;

        // A saved ground-move order (a bare MoveToPoint click) is treated as
        // abandoned rather than resumed — without this, a turtle interrupted
        // mid-walk to some old clicked point would, after finishing the fight,
        // head back out to finish that stale walk instead of falling through
        // to the normal storm-time nest-guard/wander default (see StopAndIdle
        // below), which is what actually happens for a turtle with no saved
        // task at all. A saved building-visit order still resumes normally.
        if (hadSavedTask && !savedTaskIsGroundMove)
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
            bool pathStarted = BeginPathTo(target);
            SetFinsPlaying(pathStarted);
            // Only meaningful for a MoveToPoint order (see aggroUnlockRadius's
            // own field comment) — harmless to set otherwise since nothing
            // reads it unless isGroundMove is also true.
            if (isGroundMove) groundMoveReachable = pathStarted;
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
        RefreshCarryLoadSpeed();
        return true;
    }

    /// <summary>
    /// Recomputes the Heavy Load run modifier's speed penalty for this turtle's
    /// current load and pushes it to its own locomotion layer. Called whenever
    /// either side of the fraction can have moved: a unit picked up, a load
    /// delivered, or carry capacity raised by an upgrade (see
    /// UpgradeManager.ApplyTo).
    ///
    /// Pushed on change rather than polled per frame because it is a
    /// TurtleLocomotion buff layer like every other, and those are all
    /// overwrite-on-change by design (see that class's doc comment) — the
    /// layers are multiplied together once per change, not once per FixedUpdate.
    ///
    /// The penalty ramps in rather than switching on at a threshold, from
    /// CarryLoadSlowdownStartFraction of capacity up to full, so a turtle gets
    /// visibly heavier as it fills instead of stepping between two speeds; a
    /// run without the modifier lands on exactly 1 at every load and never
    /// touches the layer at all.
    /// </summary>
    public void RefreshCarryLoadSpeed()
    {
        float penaltyAtFull = UpgradeManager.Instance != null ? UpgradeManager.Instance.CarryLoadSlowdownFraction : 0f;
        if (penaltyAtFull <= 0f || carryCapacity <= 0)
        {
            locomotion.SetCarryLoadSpeedMultiplier(1f);
            return;
        }

        float startFraction = UpgradeManager.Instance.CarryLoadSlowdownStartFraction;
        float loadFraction = (float)carriedResources.Count / carryCapacity;

        // Mathf.InverseLerp already clamps to 0..1 and, importantly, returns 0
        // rather than dividing by zero when startFraction is 1 (a penalty that
        // only bites at a completely full load).
        float ramp = Mathf.InverseLerp(startFraction, 1f, loadFraction);
        locomotion.SetCarryLoadSpeedMultiplier(1f - penaltyAtFull * ramp);
    }

    /// <summary>Called once carried resources reach capacity, and by TryBeginCarriedDelivery for a part-load with nothing better to do: clears the just-finished resource task and begins the beeline trip back to the nest, bypassing normal task/aggro/idle logic entirely while active (see isReturningToNest at the top of Update()). Falls back to normal idle/task-seeking next frame if the nest is currently unreachable (e.g. hemmed in by obstacles/deep water) rather than leaving the turtle stuck "returning" forever; CheckPassiveNestDelivery's drive-by check still delivers it in passing whenever ordinary movement brings it back into range.</summary>
    private void BeginReturnToNest()
    {
        if (isReturningToNest) return;

        // Clear the just-finished resource task's flags directly (not via
        // StopAndIdle, which would also stomp the steering/path state we're
        // about to re-set below).
        isGroundMove = false;
        isResourceTask = false;
        currentTaskTarget = null;

        // Mirrors ApplyTask's own reset of these: a trip home takes over from
        // whatever idle sub-state was running, and none of it survives the
        // handover. Matters now that idle itself can start a trip (see
        // TryBeginCarriedDelivery) — without it, a turtle that set off
        // mid-amble both walks home at Idle Speed Multiplier and keeps
        // isWanderMoving set, so once it has delivered, WanderIdle sits waiting
        // to "arrive" at a marker nothing is steering it toward any more.
        hasIdleAnchor = false;
        wasHeadingToNest = false;
        isWanderMoving = false;
        locomotion.SetSpeedMultiplier(1f);

        isReturningToNest = true;
        CancelAggro();

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        bool pathStarted = BeginPathTo(nest);
        SetFinsPlaying(pathStarted);

        if (!pathStarted) isReturningToNest = false;
    }

    /// <summary>
    /// Sends an idle turtle home with whatever it's still carrying instead of
    /// letting it roam with a part-load. Returns true if a trip actually
    /// started, i.e. the caller should stand down for this frame.
    ///
    /// Called from the single point in Update() where roaming would otherwise
    /// begin, which is what gives it exactly the priority asked for: it ranks
    /// below every real duty — the player's own orders, a standing resource
    /// objective (TrySeekTargetResource, which keeps a part-loaded turtle
    /// topping up at another node rather than walking half a load home) — and
    /// above nothing but idling. Because it reuses the same dedicated return
    /// trip a full load triggers, the player redirects it with exactly the same
    /// commands and it's cancelled by exactly the same things (see
    /// CancelReturnTrip).
    ///
    /// Storm-time delivery deliberately does NOT come through here, even though
    /// a guard roams too. A return trip bypasses aggro wholesale (see the
    /// isReturningToNest branch in Update), which is the last thing wanted with
    /// trash inbound — UpdateIdle handles the night case instead, by walking a
    /// carrying guard all the way in to delivery range rather than stopping at
    /// the guard ring.
    ///
    /// Throttled on failure, sharing Nest Guard Retry Interval with the
    /// nest-guard path it sits beside: this runs from idle, so an unreachable
    /// nest would otherwise re-run a full pathfind every single frame for as
    /// long as the turtle stays idle holding something.
    /// </summary>
    private bool TryBeginCarriedDelivery()
    {
        if (nestDeliveryRetryTimer > 0f)
        {
            nestDeliveryRetryTimer -= Time.deltaTime;
            return false;
        }

        BeginReturnToNest();

        // BeginReturnToNest clears the flag again if the nest turned out to be
        // unreachable, which is the signal to back off and let this turtle roam
        // for now rather than freezing it here waiting on a route that doesn't
        // exist. It keeps its cargo either way.
        if (isReturningToNest) return true;

        nestDeliveryRetryTimer = nestGuardRetryInterval;
        return false;
    }

    /// <summary>Runs every frame in place of the normal storm/aggro/idle logic while returning — a returning-with-cargo turtle beelines for the nest, except a storm starting or a fresh ground/building movement command mid-trip cancels it instead (see CancelReturnTrip), so this only ever runs during the day, for however briefly it takes night's edge-check to catch up, or until the player redirects the turtle elsewhere. A fresh resource selection does not cancel it (see MoveToResource) — it only updates the objective for afterward.</summary>
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
                        ScoreManager.Instance?.AddResourceScore(1);
                        TurtleNest.Instance?.PlaySquash();
                    });
            }
            else
            {
                ResourceManager.Instance?.Add(unit.Type, 1); // no prefab wired yet — still deliver correctly, just instantly
                ScoreManager.Instance?.AddResourceScore(1);
                TurtleNest.Instance?.PlaySquash();
            }

            yield return new WaitForSeconds(deliveryStaggerDelay);
        }

        carriedResources.Clear();
        RefreshCarryLoadSpeed();
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

        // avoidCoral: false throughout — a Coral Reef is a wall for trash only
        // (see CoralReef); turtles pass through it physically, so routing them
        // around it would be a detour around nothing.
        if (!pathfinding.IsDeepWater(transform.position))
        {
            return pathfinding.FindPath(transform.position, destinationPosition, avoidDeepWater: true, allowDiagonalSqueeze: true, avoidCoral: false);
        }

        Vector3 escapePoint = pathfinding.NearestNonDeepWaterPoint(transform.position);
        List<Vector3> escapeLeg = pathfinding.FindPath(transform.position, escapePoint, avoidDeepWater: false, allowDiagonalSqueeze: true, avoidCoral: false);
        if (escapeLeg == null) return null; // even ignoring deep water, genuinely boxed in by obstacles

        // escapeLeg being an empty (non-null) list means start and escapePoint
        // already share/neighbor a cell, per FindPath's own convention — but
        // escapePoint is still the one real step needed to actually clear the
        // water, so it's always added as an explicit waypoint here.
        List<Vector3> combined = new List<Vector3>(escapeLeg) { escapePoint };

        List<Vector3> onwardLeg = pathfinding.FindPath(escapePoint, destinationPosition, avoidDeepWater: true, allowDiagonalSqueeze: true, avoidCoral: false);
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
    private bool BeginPathTo(Transform destination, int shortenSteps = 0)
    {
        pathFinalDestination = destination;
        pathRetargetTimer = 0f;
        if (destination != null) pathTargetSnapshotPosition = destination.position;

        bool hasManager = destination != null && PathfindingManager.Instance != null;
        currentPath = hasManager
            ? FindPathOutOfDeepWaterIfNeeded(destination.position)
            : null;

        // Trims the tail off a chase path (see aggroChasePathShortenSteps) —
        // if that eats the whole thing, currentPath falls through as a
        // non-null empty list, which the branches below already treat the
        // same as "start and goal share a cell": steer straight at the live
        // destination for this final short hop.
        if (shortenSteps > 0 && currentPath != null && currentPath.Count > 0)
        {
            int trimmedCount = Mathf.Max(0, currentPath.Count - shortenSteps);
            currentPath = trimmedCount > 0 ? currentPath.GetRange(0, trimmedCount) : new List<Vector3>();
        }

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

    /// <summary>Advances through an in-progress path's waypoints as they're reached, converging steering onto the real destination once the path is exhausted. A path's intermediate waypoints are otherwise only ever computed once, by BeginPathTo — fine for a static destination, but a moving one (chased trash, a harvested Jellyfish) can drift far enough from where the path was aimed that blindly finishing a stale route means overshooting to where it used to be before ever re-steering toward its live position — this periodically checks for that drift and repaths early instead (see pathRetargetCheckInterval/pathRetargetDistance).</summary>
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

        pathRetargetTimer += Time.deltaTime;
        if (pathRetargetTimer >= pathRetargetCheckInterval)
        {
            pathRetargetTimer = 0f;
            if (Vector2.Distance(pathTargetSnapshotPosition, pathFinalDestination.position) > pathRetargetDistance)
            {
                SetFinsPlaying(BeginPathTo(pathFinalDestination, isAggroed ? aggroChasePathShortenSteps : 0));
                return;
            }
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
    ///
    /// Note the two sub-states cover DIFFERENT ground once a guard arrives:
    /// approaching is a straight run at one point, but roaming spreads out over
    /// the whole ring around the nest and deliberately steers away from where
    /// other turtles already are (see TryPickGuardRoamPoint), so a big guard
    /// force fans out into a picket rather than piling up on the near side.
    /// </summary>
    private void UpdateIdle(bool storming)
    {
        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;

        // A guard still holding cargo walks all the way in to delivery range
        // rather than stopping out on the guard ring — the storm-time half of
        // "deliver before you roam" (see TryBeginCarriedDelivery for the
        // daytime half, and for why night can't use the same dedicated return
        // trip). Nothing else is needed to make the drop-off happen:
        // CheckPassiveNestDelivery already ran this frame, up in Update, and
        // fires the moment this brings the turtle into range. With the cargo
        // gone this reverts to the guard distance on the very next frame — by
        // which point the turtle is well inside it, so it falls straight
        // through to roaming, exactly as if it had arrived empty-handed.
        float stopDistance = carriedResources.Count > 0 ? nestDeliveryRadius : nestGuardDistance;

        // The Short Leash modifier's backstop. Every ORDER is already clamped
        // or filtered, so this only catches the ways a turtle drifts out
        // without being sent — physics jostling near the boundary, an idle
        // wander step, a leash that tightened while it was out — and it is the
        // one condition that pulls a turtle home during the DAY as well, which
        // is why it sits outside the storming test rather than inside it.
        // Reverts to plain guard behavior the instant it is back inside.
        bool beyondLeash = !IsWithinLeash(transform.position);

        bool headingToNest = nest != null
            && (beyondLeash || (storming && Vector2.Distance(transform.position, nest.position) > stopDistance));

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

        // Guard roaming ranges over the whole ring around the nest rather than
        // a disc around one anchor, so where a turtle is standing when the
        // storm ends bears no relation to the anchor it had going in — drop it
        // on the way in and back out, or daytime wander would march the turtle
        // back to a stale point that could now be most of the ring away.
        bool guarding = storming && nest != null;
        if (guarding != wasGuardRoaming)
        {
            wasGuardRoaming = guarding;
            hasIdleAnchor = false;
        }

        // A crab that hasn't been recruited to fight (see CrabAgent) sits the
        // storm out rather than joining the picket: the approach leg above
        // already walked it home, so from here it simply holds still until
        // dawn instead of roaming the guard ring. Deliberately only replaces
        // the roam — everything above still applies, so it does walk in a
        // fresh load of cargo and still gets dragged off by a player order.
        if (guarding && !CanAttackTrash)
        {
            HoldStill();
            return;
        }

        WanderIdle(guarding, nest);
    }

    /// <summary>Stops moving and stands where it is, without touching any task state (unlike StopAndIdle, which also clears the current order). For an idle sub-state that wants stillness rather than the usual amble.</summary>
    private void HoldStill()
    {
        isWanderMoving = false;
        isFollowingPath = false;
        steering.SetTarget(null);
        SetFinsPlaying(false);
        locomotion.SetSpeedMultiplier(1f);
    }

    /// <summary>Randomly turns and ambles to nearby points (slowly, see idleSpeedMultiplier), pausing between each — replaces standing completely motionless while idle. Where those points come from depends on whether this turtle is guarding the nest through a storm; see TryPickWanderPoint.</summary>
    private void WanderIdle(bool guarding, Transform nest)
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

        if (!TryPickWanderPoint(guarding, nest, out Vector3 candidate))
        {
            // Nothing valid came up this cycle (e.g. every sample landed in
            // deep water, where the area being sampled is mostly ocean) — skip
            // it and try again at the next interval rather than forcing a bad
            // candidate through.
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

    /// <summary>Picks where to amble next: somewhere around the nest that other turtles have left clear while guarding it through a storm (TryPickGuardRoamPoint), or a plain random point near this turtle's own idle anchor any other time (TryPickAnchorWanderPoint). False if nothing valid turned up this cycle.</summary>
    private bool TryPickWanderPoint(bool guarding, Transform nest, out Vector3 point)
    {
        if (guarding && nest != null) return TryPickGuardRoamPoint(nest.position, out point);

        return TryPickAnchorWanderPoint(out point);
    }

    /// <summary>The ordinary wander point: a random spot within Idle Wander Radius of this turtle's idle anchor. Re-rolls a few times past deep water rather than only discovering that once BeginPathTo refuses the point, which would just leave the turtle standing still for the cycle.</summary>
    private bool TryPickAnchorWanderPoint(out Vector3 point)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * idleWanderRadius;
            point = idleAnchor + new Vector3(offset.x, offset.y, 0f);

            if (!IsDeepWater(point)) return true;
        }

        point = idleAnchor;
        return false;
    }

    /// <summary>
    /// Picks where a nest guard ambles next: samples Nest Guard Roam Candidates
    /// points spread around the nest — anywhere in the ring from Nest Guard
    /// Roam Inner Fraction of Nest Guard Distance out to the full distance,
    /// which is the same band a guard comes to rest in on approach — and takes
    /// whichever one other turtles have left the most room around.
    ///
    /// Sampling around the NEST rather than around this turtle's own idle
    /// anchor is what actually spreads guards out, and matters more than the
    /// scoring does: turtles converge on the nest from whatever direction they
    /// happened to be working in, so anchoring each one where it landed
    /// preserves that clump no matter how carefully the point inside it is
    /// chosen. It also means widening Nest Guard Distance widens the area they
    /// cover, instead of just pushing the same huddle further out.
    ///
    /// Ties on open space are broken by travel distance — see OpenSpaceAt for
    /// why exact ties are the normal case here — so a guard that already has
    /// room ambles somewhere close by rather than marching across the ring
    /// every few seconds for no gain.
    /// </summary>
    private bool TryPickGuardRoamPoint(Vector3 nestPosition, out Vector3 point)
    {
        point = transform.position;

        float outerRadius = Mathf.Max(nestGuardDistance, 0f);
        float innerRadius = outerRadius * nestGuardRoamInnerFraction;

        bool found = false;
        float bestOpenSpace = 0f;
        float bestTravel = 0f;

        for (int i = 0; i < Mathf.Max(nestGuardRoamCandidates, 1); i++)
        {
            // Uniform across the ring's AREA, not across its radius: sampling
            // the radius directly would pile candidates toward the inner edge,
            // where there's the least circumference to spread them along.
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Mathf.Lerp(innerRadius * innerRadius, outerRadius * outerRadius, Random.value));
            Vector3 candidate = nestPosition + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);

            if (IsDeepWater(candidate)) continue;

            float openSpace = OpenSpaceAt(candidate);
            float travel = Vector2.Distance(transform.position, candidate);

            bool better = !found
                || openSpace > bestOpenSpace + RoamScoreTieTolerance
                || (openSpace > bestOpenSpace - RoamScoreTieTolerance && travel < bestTravel);
            if (!better) continue;

            found = true;
            bestOpenSpace = openSpace;
            bestTravel = travel;
            point = candidate;
        }

        return found;
    }

    /// <summary>
    /// How free of other turtles a point is: the distance to the nearest OTHER
    /// turtle, capped at Nest Guard Roam Spacing.
    ///
    /// That cap is what keeps this a spacing rule rather than a race for the
    /// emptiest spot on the island — past it, every clear point scores
    /// identically, which is also why the caller needs a tie-break at all.
    ///
    /// Counts every live turtle, not just fellow guards: a turtle parked at a
    /// Watchtower or walking past on an errand takes up just as much room as
    /// one standing guard.
    /// </summary>
    private float OpenSpaceAt(Vector3 point)
    {
        float nearest = Mathf.Max(nestGuardRoamSpacing, 0f);

        foreach (TurtleAgent other in allTurtles)
        {
            if (other == null || other == this) continue;

            float distance = Vector2.Distance(point, other.transform.position);
            if (distance < nearest) nearest = distance;
        }

        return nearest;
    }

    private static bool IsDeepWater(Vector3 point) => PathfindingManager.Instance != null && PathfindingManager.Instance.IsDeepWater(point);

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

    /// <summary>Starts/stops propulsion — the single "move / hold still" control every behavior in this class routes through. Delegates to TurtleLocomotion rather than driving the fins directly: a finless unit (the Crab) has no oscillators to switch off, so looping fins here would silently no-op and leave it coasting with nothing able to stop it.</summary>
    private void SetFinsPlaying(bool playing) => locomotion.SetPlaying(playing);

    /// <summary>
    /// True once this unit is actually close enough to whatever it's
    /// harvesting, attacking or bumping for the contact mechanic to be in play.
    ///
    /// Deliberately proximity-based rather than simply "has a task": a task
    /// lasts the whole journey, so keying off that alone had a crab lurching
    /// its way across the entire island to reach a rock. Now it glides the
    /// approach and only breaks into lunges on arrival, which is both what the
    /// movement should read as and when the bouncing actually matters.
    ///
    /// currentTaskTarget covers all three cases on its own — TryAcquireAggroTarget
    /// routes its trash target through the same ApplyTask everything else uses —
    /// so the flags below are only there to exclude a plain ground-move order,
    /// which happens to have a target but nothing to strike at the end of it.
    /// </summary>
    private bool IsWithinLungeRange()
    {
        if (currentTaskTarget == null) return false;
        if (!isAggroed && !isResourceTask && !isCollidingWithBuilding) return false;

        return Vector2.Distance(transform.position, currentTaskTarget.position) <= lungeDistance;
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
