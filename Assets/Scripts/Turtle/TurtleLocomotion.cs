using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a unit's root (with Rigidbody2D). Listens to a set of fin
/// LimbOscillators and gives the unit a forward physics impulse each time a
/// fin enters its backward (power) stroke. The Rigidbody2D's own Linear Damping
/// (and any Physics Material 2D on its collider) provides the friction that
/// slows it back down, so movement stays interactible with the environment.
///
/// A unit with Propelling Fins left EMPTY (the Crab, which scuttles rather than
/// swimming and has no oscillating limbs at all) switches automatically to
/// strokeless propulsion, which has two modes and picks between them by what
/// the unit is currently doing (TurtleAgent drives this via SetImpulseBursts):
///
/// - Travelling anywhere at all — including the whole approach to a resource
///   it intends to harvest: a steady per-step force, so it glides at a
///   constant speed rather than lurching (ApplyContinuousThrust).
/// - Within contact range of what it's harvesting, attacking or bumping:
///   discrete lunges on a fixed interval (AccumulateStrokelessImpulse),
///   because that whole contact mechanic is built on bouncing off and
///   re-approaching, and because each impulse raises ImpulseApplied — which is
///   what reloads TurtleHeadHitbox so repeated hits can land.
///
/// The two are deliberately tuned to the same average speed (see
/// ApplyContinuousThrust) so switching between them is invisible, and both feed
/// the same buff layers, so the rest of the codebase never needs to know which
/// kind of unit — or which mode — it's driving. See also SetPlaying, which is
/// the single stop control for finned and finless units alike.
///
/// Two of the speed layers below are owned here rather than pushed in from
/// outside, because this class is already sampling what it needs for them:
/// the shallow-water bonus keys off the same per-frame surface sample that
/// recolors the wake particles (see Update/SampleSurface), so asking any other
/// system to detect it would mean sampling the tilemaps twice per unit per
/// frame. Everything else still arrives through a Set* call as before.
///
/// Speed buffs and debuffs (permanent upgrades, Campfire, the temporary food
/// buff, a glue slow, standing on Algae, wearing Barnacles, a heavy load of
/// cargo, swimming in the shallows, swimming in company) never touch
/// impulseForce — a bigger lunge per stroke tends to fling a turtle off
/// its intended course. Instead they scale how often strokes happen at all:
/// the combined product of every active buff is pushed to each propelling
/// fin's oscillation frequency (LimbOscillator.SetSpeedBuffMultiplier, so the
/// animation itself visibly speeds up) and to TurtleTargetSteering's turn rate
/// (SetTurnSpeedMultiplier, so a buffed turtle also turns quicker to keep up),
/// every time any of the buff layers below changes. Different buff types
/// stack multiplicatively with each other via this combined product, exactly
/// as they did when they scaled impulseForce; speedMultiplier (the idle-amble
/// slowdown, not a player-granted buff) is deliberately excluded from that
/// product and keeps scaling impulseForce alone, since a calmer amble should
/// still look like weaker strokes, not a slower stroke rate.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class TurtleLocomotion : MonoBehaviour
{
    [Header("Propulsion")]
    [Tooltip("Fins whose backward stroke should push the turtle forward.")]
    [SerializeField] private LimbOscillator[] propellingFins;
    [Tooltip("Impulse force applied per fin stroke. Tune alongside the Rigidbody2D's Linear Damping.")]
    [SerializeField] private float impulseForce = 2f;
    [Tooltip("Seconds between propulsion impulses for a unit with NO Propelling Fins — the Crab, which scuttles rather than swimming and so has no oscillating limbs to key off. Ignored entirely whenever Propelling Fins has anything in it. A turtle's fins at their default 1.5 Hz land roughly a third of a second apart, so 0.33 is the like-for-like starting point; lower is a faster scuttle.")]
    [SerializeField, Min(0.01f)] private float strokelessImpulseInterval = 0.33f;

    private float speedMultiplier = 1f;
    private float permanentSpeedMultiplier = 1f;
    private float campfireSpeedMultiplier = 1f;
    private float temporaryBuffSpeedMultiplier = 1f;
    private float glueDebuffSpeedMultiplier = 1f;
    private float algaeSpeedMultiplier = 1f;
    private float barnacleSpeedMultiplier = 1f;
    private float carryLoadSpeedMultiplier = 1f;
    private float shallowWaterSpeedMultiplier = 1f;
    private float tailwindSpeedMultiplier = 1f;

    /// <summary>The fins driving propulsion, so other systems (e.g. idle/select logic) can pause/resume the same set without re-wiring it separately.</summary>
    public IReadOnlyList<LimbOscillator> PropellingFins => propellingFins;

    /// <summary>Raised in FixedUpdate exactly when a forward impulse is actually applied (see FixedUpdate) — lets other systems (e.g. TurtleHeadHitbox, guaranteeing every impulse gets a fresh shot at registering a hit) key off the same "stroke landed" moment instead of duplicating the fin-stroke-aggregation logic themselves.</summary>
    public event Action ImpulseApplied;

    private Rigidbody2D rb;
    private TurtleTargetSteering steering;
    private int pendingImpulses;

    /// <summary>The combined buff product last computed by RecalculateBuffSpeedMultiplier, kept so strokeless propulsion can scale its cadence by exactly what a fin's frequency would have been scaled by.</summary>
    private float buffSpeedMultiplier = 1f;

    /// <summary>Permanent baseline cadence boost for strokeless propulsion — the finless counterpart to LimbOscillator.MultiplyFrequency (Flipper).</summary>
    private float strokelessRateMultiplier = 1f;

    private float strokelessTimer;
    private bool isPlaying;

    /// <summary>Set by TurtleAgent each frame: true while this unit is attacking, harvesting, or bumping an interactable building, i.e. whenever the bounce-and-reapproach contact mechanic is actually in play. Only consulted for strokeless propulsion — see FixedUpdate.</summary>
    private bool useImpulseBursts;

    /// <summary>True for a unit with no oscillating limbs at all (the Crab), which drives propulsion off a plain timer instead of fin strokes. Auto-detected from Propelling Fins being empty rather than a separate toggle, so the two can never disagree in the Inspector.</summary>
    private bool UsesStrokelessPropulsion => propellingFins == null || propellingFins.Length == 0;

    public ParticleSystem finParticle1;
    public ParticleSystem finParticle2;
    [Tooltip("Continuous trail particle system (e.g. a wake/sand trail), recolored by surface alongside the fin splash particles above.")]
    public ParticleSystem trailParticle;

    [Header("Audio")]
    [Tooltip("Played on each propulsion stroke while this unit is on sand — the flipper push. Silent in water, since it's gated on the same surface check that colors the fin particles. Its Min Interval/Max Voices matter more than most: this fires per stroke per unit, so a beach full of turtles leans on that shared budget hard.")]
    [SerializeField] private SoundEffect sandPushSound = new SoundEffect();

    [Header("Color By Surface")]
    [Tooltip("Fin splash + trail particle color range while swimming, in shallow or deep water — each particle picks a random color between these two, instead of every particle being the exact same flat color.")]
    [SerializeField] private Color waterParticleColorMin = Color.white;
    [SerializeField] private Color waterParticleColorMax = Color.white;
    [Tooltip("Fin splash + trail particle color range while on land (sand). Same two-color randomization as the water range above.")]
    [SerializeField] private Color landParticleColorMin = Color.white;
    [SerializeField] private Color landParticleColorMax = Color.white;

    /// <summary>What a unit is standing/swimming on. Split out of the old land/water bool because shallow water now differs from deep water in more than particle color — it is where the Shallow Water Sprint upgrade applies — while the particles still only care about land vs. not.</summary>
    private enum Surface { Unknown, Land, ShallowWater, DeepWater }

    private Surface currentSurface = Surface.Unknown;

    /// <summary>The shallow-water bonus last folded into the layer, so Update can notice the upgrade being picked mid-run while a unit is already sitting in the shallows and its surface therefore never changes.</summary>
    private float appliedShallowWaterBonus = -1f;

    /// <summary>Non-null pins the surface below instead of sampling it. See SetForcedSurface.</summary>
    private bool? forcedSurfaceIsLand;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        steering = GetComponent<TurtleTargetSteering>();
    }

    private void Update()
    {
        Surface surface = SampleSurface();

        // Read live and compared rather than pushed in from UpgradeManager:
        // the value only moves when a card is picked, and comparing one float
        // per frame is cheaper than every card having to remember to fan out
        // to every live unit.
        float shallowBonus = UpgradeManager.Instance != null ? UpgradeManager.Instance.ShallowWaterSpeedBonus : 0f;

        if (surface == currentSurface && Mathf.Approximately(shallowBonus, appliedShallowWaterBonus)) return;

        bool surfaceChanged = surface != currentSurface;
        currentSurface = surface;
        appliedShallowWaterBonus = shallowBonus;

        if (surfaceChanged)
        {
            bool onLand = surface == Surface.Land;
            ApplyParticleColor(onLand ? landParticleColorMin : waterParticleColorMin, onLand ? landParticleColorMax : waterParticleColorMax);
        }

        // The whole point of the upgrade: fast through the shallow ring around
        // the island, ordinary speed on the sand and out in open water.
        shallowWaterSpeedMultiplier = surface == Surface.ShallowWater ? 1f + shallowBonus : 1f;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>
    /// Where this unit currently is. Shallow water is defined by elimination
    /// — not land, not deep water — which is exactly how the rest of the
    /// project reads the three tilemaps (see PathfindingManager.IsDeepWater's
    /// own "water AND NOT sand AND NOT shallow" test), so there is no fourth
    /// answer to worry about.
    ///
    /// No PathfindingManager (the Menu scene) reads as deep water rather than
    /// unknown: it is the neutral answer, giving the old water particle colors
    /// and no speed bonus, so ambience turtles behave exactly as they did.
    /// </summary>
    private Surface SampleSurface()
    {
        if (forcedSurfaceIsLand.HasValue) return forcedSurfaceIsLand.Value ? Surface.Land : Surface.DeepWater;
        if (PathfindingManager.Instance == null) return Surface.DeepWater;

        if (PathfindingManager.Instance.IsOnLand(transform.position)) return Surface.Land;
        return PathfindingManager.Instance.IsDeepWater(transform.position) ? Surface.DeepWater : Surface.ShallowWater;
    }

    /// <summary>
    /// Pins which surface this turtle's fin/trail particles color themselves
    /// for, rather than asking PathfindingManager where it currently is. Pass
    /// null to go back to sampling per frame (the default, and what the whole
    /// gameplay scene uses).
    ///
    /// This exists for scenes with no PathfindingManager at all — the Menu
    /// scene's ambience turtles (see MenuAmbienceTurtle), where the "am I on
    /// land" lookup can only ever answer no, so turtles pottering about on a
    /// backdrop that is nothing but sand would kick up water spray forever.
    /// </summary>
    public void SetForcedSurface(bool? onLand)
    {
        forcedSurfaceIsLand = onLand;
        // Drop the cached comparison so the next Update actually re-applies the
        // color, instead of waiting on a change that may now never come.
        currentSurface = Surface.Unknown;
    }

    private void ApplyParticleColor(Color colorMin, Color colorMax)
    {
        // The two-Color constructor puts the MinMaxGradient in "Two Colors" mode, so each
        // particle samples a random point between colorMin/colorMax rather than every
        // particle rendering the exact same flat color.
        ParticleSystem.MinMaxGradient range = new ParticleSystem.MinMaxGradient(colorMin, colorMax);
        SetParticleColor(finParticle1, range);
        SetParticleColor(finParticle2, range);
        SetParticleColor(trailParticle, range);
    }

    private static void SetParticleColor(ParticleSystem particle, ParticleSystem.MinMaxGradient range)
    {
        if (particle == null) return;

        ParticleSystem.MainModule main = particle.main;
        main.startColor = range;
    }

    private void OnEnable()
    {
        foreach (LimbOscillator fin in propellingFins)
        {
            if (fin != null) fin.StrokeChanged += HandleStrokeChanged;
        }
    }

    private void OnDisable()
    {
        foreach (LimbOscillator fin in propellingFins)
        {
            if (fin != null) fin.StrokeChanged -= HandleStrokeChanged;
        }
    }

    private void HandleStrokeChanged(LimbOscillator fin)
    {
        if (fin.CurrentStroke == LimbOscillator.Stroke.Backward)
        {
            pendingImpulses++;
        }
    }

    private void FixedUpdate()
    {
        if (UsesStrokelessPropulsion)
        {
            if (useImpulseBursts) AccumulateStrokelessImpulse();
            else ApplyContinuousThrust();
        }

        if (pendingImpulses > 0)
        {
            // Assumes the turtle's art faces along local +X (rotation 0 = facing right).
            // Only speedMultiplier (idle-amble) scales the lunge itself — see the
            // class doc comment for why the three buff layers don't.
            rb.AddForce((Vector2)transform.right * impulseForce * speedMultiplier * pendingImpulses, ForceMode2D.Impulse);

            RestartBurst(finParticle1);
            RestartBurst(finParticle2);

            // Same moment as the particle burst, and gated on the same surface
            // those particles are already colored by — this is the flipper
            // shoving off sand, so it must not play while swimming. The surface
            // is Unknown only before the first Update has sampled it, and
            // "unknown" reads as water here rather than guessing.
            if (currentSurface == Surface.Land) sandPushSound.Play(transform.position);

            pendingImpulses = 0;
            ImpulseApplied?.Invoke();
        }
    }

    /// <summary>
    /// The strokeless default: a steady push every physics step, so a crab
    /// glides at a constant speed instead of lurching. The Rigidbody2D's Linear
    /// Damping is what caps it — force in, drag out, settling at a terminal
    /// velocity — so nothing here needs to track or clamp speed itself.
    ///
    /// Force is DERIVED from impulseForce and strokelessImpulseInterval rather
    /// than being its own tunable, precisely so the two modes travel at the same
    /// speed: one impulse of J every T seconds averages the same push as a
    /// steady J/T, so a crab neither surges nor stalls at the moment it switches
    /// into bursts to attack. That leaves impulseForce as the single "how fast
    /// is this unit" knob for both, and strokelessImpulseInterval controlling
    /// only how punchy the attack lunge reads.
    /// </summary>
    private void ApplyContinuousThrust()
    {
        if (!isPlaying) return;

        float force = strokelessImpulseInterval > 0f ? impulseForce / strokelessImpulseInterval : impulseForce;
        rb.AddForce((Vector2)transform.right * force * speedMultiplier * buffSpeedMultiplier * strokelessRateMultiplier, ForceMode2D.Force);
    }

    /// <summary>
    /// Queues an impulse every strokelessImpulseInterval while moving, standing
    /// in for the StrokeChanged events a finned unit gets for free. Used only
    /// while the unit is attacking/harvesting/bumping (see useImpulseBursts) —
    /// that contact mechanic is built on bouncing off and re-approaching, which
    /// needs discrete shoves, whereas ordinary travel reads better as a glide.
    /// Scaled by
    /// the same combined buff product that scales fin frequency, so every speed
    /// buff and debuff moves a crab exactly as much as it moves a turtle.
    ///
    /// Goes through pendingImpulses rather than applying force itself, so the
    /// shared FixedUpdate path still fires ImpulseApplied identically for both
    /// kinds of unit — that event is what reloads TurtleHeadHitbox, so routing
    /// around it would leave a finless unit unable to land repeated hits.
    /// </summary>
    private void AccumulateStrokelessImpulse()
    {
        if (!isPlaying)
        {
            strokelessTimer = 0f;
            return;
        }

        float rate = buffSpeedMultiplier * strokelessRateMultiplier;
        if (rate <= 0f) return;

        strokelessTimer += Time.fixedDeltaTime * rate;
        if (strokelessTimer < strokelessImpulseInterval) return;

        strokelessTimer -= strokelessImpulseInterval;
        pendingImpulses++;
    }

    /// <summary>
    /// Starts/stops propulsion. Forwards to every propelling fin (which is what
    /// TurtleAgent.SetFinsPlaying used to do itself) AND gates strokeless
    /// propulsion, so one call halts a crab exactly as it halts a turtle.
    ///
    /// The second half is why this moved here at all: a finless unit has no
    /// oscillators to switch off, so a caller looping over fins would silently
    /// no-op and leave the crab coasting forever with nothing able to stop it.
    /// </summary>
    public void SetPlaying(bool value)
    {
        isPlaying = value;
        if (!value) strokelessTimer = 0f;

        if (propellingFins == null) return;

        foreach (LimbOscillator fin in propellingFins)
        {
            if (fin != null) fin.SetPlaying(value);
        }
    }

    /// <summary>Permanently scales strokeless propulsion (both the burst cadence and the continuous glide) — the finless counterpart to LimbOscillator.MultiplyFrequency, called alongside it by TurtleAgent.ApplyFlipperBuff so a Flipper rune speeds a crab up instead of silently doing nothing. Harmless on a finned unit, which never reads it.</summary>
    public void MultiplyStrokelessRate(float multiplier) => strokelessRateMultiplier *= multiplier;

    /// <summary>
    /// Pushed by TurtleAgent every frame: true whenever this unit is in contact
    /// range of something it's harvesting, attacking or bumping (see
    /// TurtleAgent.IsWithinLungeRange), which is the only time a strokeless unit
    /// wants discrete lunges instead of a smooth glide. Ignored entirely by a
    /// finned unit, so TurtleAgent can call it unconditionally without caring
    /// what it's driving.
    ///
    /// Deliberately does NOT reset strokelessTimer. Bouncing off after a hit
    /// routinely carries a unit just out of lunge range and straight back in,
    /// and clearing the accumulation on each of those flips would starve it of
    /// lunges exactly when it's trying to land them. Since the timer only
    /// advances while bursting, leaving it alone simply pauses it for the glide
    /// and resumes on contact — which also makes the first strike on arrival
    /// land promptly instead of a full interval late.
    /// </summary>
    public void SetImpulseBursts(bool value) => useImpulseBursts = value;

    /// <summary>Stop-then-Play, so a stroke landing while the previous burst is still emitting restarts it from the beginning instead of being swallowed. Null-tolerant like every other particle field here (see SetParticleColor): a unit whose prefab simply has no fin spray leaves these unassigned rather than needing a dummy system wired in.</summary>
    private static void RestartBurst(ParticleSystem particle)
    {
        if (particle == null) return;

        particle.Stop();
        particle.Play();
    }

    /// <summary>Scales every future stroke's impulse (not the fin animation itself), e.g. for a slower idle amble. 1 = normal speed. Overwritten constantly by TurtleAgent's task/idle state — deliberately independent of the buff layers below, which scale stroke rate instead of impulse.</summary>
    public void SetSpeedMultiplier(float multiplier) => speedMultiplier = multiplier;

    /// <summary>Scales this turtle's stroke rate (fin animation + propulsion frequency) and turn rate, independently of SetSpeedMultiplier, so an upgrade persists across whatever task/idle state TurtleAgent is in. Overwrites (not compounds) since callers always pass the already-cumulative total.</summary>
    public void SetPermanentSpeedMultiplier(float multiplier)
    {
        permanentSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for Campfire's while-inside-radius buff — overwrites, since TurtleAgent tracks the linear-stacked total across every overlapping campfire itself.</summary>
    public void SetCampfireSpeedMultiplier(float multiplier)
    {
        campfireSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for a personal, time-limited buff (e.g. breaking a Coconut). 1 = no buff active.</summary>
    public void SetTemporaryBuffSpeedMultiplier(float multiplier)
    {
        temporaryBuffSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for a trash-inflicted slow (e.g. Glue Bottle), separate from the food-buff temporary layer above since the two are unrelated (one is a debuff with its own independent timer, not tied to the day/night edge). 1 = no debuff active.</summary>
    public void SetGlueDebuffSpeedMultiplier(float multiplier)
    {
        glueDebuffSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for standing on an Algae pile — separate from the Campfire layer despite being the same "while inside a building's radius" shape, so the two stack with each other instead of overwriting one another. Overwrites, since TurtleAgent tracks the linear-stacked total across every overlapping patch (plus its own linger hold-over) itself. 1 = no buff active.</summary>
    public void SetAlgaeSpeedMultiplier(float multiplier)
    {
        algaeSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for swimming in company, under the Tailwind upgrade (see TurtleAgent.RefreshTailwindSpeed, which owns the neighbour scan and the falloff curve). 1 = alone, or the upgrade not taken.</summary>
    public void SetTailwindSpeedMultiplier(float multiplier)
    {
        tailwindSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for the weight of what this unit is carrying, under the Heavy Load run modifier (see TurtleAgent.RefreshCarryLoadSpeed, which owns the curve and pushes the result here whenever the load or the capacity changes). Its own layer rather than folded into the barnacle or glue one so it composes multiplicatively with both — a barnacled turtle hauling a full load is slowed by each independently. 1 = empty-handed, or the modifier not taken.</summary>
    public void SetCarryLoadSpeedMultiplier(float multiplier)
    {
        carryLoadSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Independent layer for the permanent Barnacles debuff — deliberately its own layer rather than folded into permanentSpeedMultiplier, so a Turtle Speed card and the barnacle penalty compose multiplicatively instead of one overwriting the other's total. Being part of the buff product (not a day/night branch) is also what makes it apply day and night alike. 1 = no barnacles.</summary>
    public void SetBarnacleSpeedMultiplier(float multiplier)
    {
        barnacleSpeedMultiplier = multiplier;
        RecalculateBuffSpeedMultiplier();
    }

    /// <summary>Recombines the buff layers (permanent x campfire x temporary x glue debuff x algae x barnacles x carry load x shallow water x tailwind — speedMultiplier is deliberately not part of this, see class doc comment) into one product and pushes it to every propelling fin's stroke rate and to this turtle's turn rate, so different buffs stack multiplicatively with each other exactly as they did back when they scaled impulseForce instead.</summary>
    private void RecalculateBuffSpeedMultiplier()
    {
        float combined = permanentSpeedMultiplier * campfireSpeedMultiplier * temporaryBuffSpeedMultiplier
            * glueDebuffSpeedMultiplier * algaeSpeedMultiplier * barnacleSpeedMultiplier * carryLoadSpeedMultiplier
            * shallowWaterSpeedMultiplier * tailwindSpeedMultiplier;

        // Stored as well as pushed: a finless unit has no oscillator to hold it,
        // and AccumulateStrokelessImpulse reads it straight off this field.
        buffSpeedMultiplier = combined;

        if (propellingFins != null)
        {
            foreach (LimbOscillator fin in propellingFins)
            {
                if (fin != null) fin.SetSpeedBuffMultiplier(combined);
            }
        }

        if (steering != null) steering.SetTurnSpeedMultiplier(combined);
    }
}
