using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on the turtle's root (with Rigidbody2D). Listens to a set of fin
/// LimbOscillators and gives the turtle a forward physics impulse each time a
/// fin enters its backward (power) stroke. The Rigidbody2D's own Linear Damping
/// (and any Physics Material 2D on its collider) provides the friction that
/// slows the turtle back down, so movement stays interactible with the environment.
///
/// Speed buffs (permanent upgrades, Campfire, the temporary food buff) never
/// touch impulseForce — a bigger lunge per stroke tends to fling a turtle off
/// its intended course. Instead they scale how often strokes happen at all:
/// the combined product of every active buff is pushed to each propelling
/// fin's oscillation frequency (LimbOscillator.SetSpeedBuffMultiplier, so the
/// animation itself visibly speeds up) and to TurtleTargetSteering's turn rate
/// (SetTurnSpeedMultiplier, so a buffed turtle also turns quicker to keep up),
/// every time any of the three layers below changes. Different buff types
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

    private float speedMultiplier = 1f;
    private float permanentSpeedMultiplier = 1f;
    private float campfireSpeedMultiplier = 1f;
    private float temporaryBuffSpeedMultiplier = 1f;
    private float glueDebuffSpeedMultiplier = 1f;

    /// <summary>The fins driving propulsion, so other systems (e.g. idle/select logic) can pause/resume the same set without re-wiring it separately.</summary>
    public IReadOnlyList<LimbOscillator> PropellingFins => propellingFins;

    /// <summary>Raised in FixedUpdate exactly when a forward impulse is actually applied (see FixedUpdate) — lets other systems (e.g. TurtleHeadHitbox, guaranteeing every impulse gets a fresh shot at registering a hit) key off the same "stroke landed" moment instead of duplicating the fin-stroke-aggregation logic themselves.</summary>
    public event Action ImpulseApplied;

    private Rigidbody2D rb;
    private TurtleTargetSteering steering;
    private int pendingImpulses;

    public ParticleSystem finParticle1;
    public ParticleSystem finParticle2;
    [Tooltip("Continuous trail particle system (e.g. a wake/sand trail), recolored by surface alongside the fin splash particles above.")]
    public ParticleSystem trailParticle;

    [Header("Color By Surface")]
    [Tooltip("Fin splash + trail particle color range while swimming, in shallow or deep water — each particle picks a random color between these two, instead of every particle being the exact same flat color.")]
    [SerializeField] private Color waterParticleColorMin = Color.white;
    [SerializeField] private Color waterParticleColorMax = Color.white;
    [Tooltip("Fin splash + trail particle color range while on land (sand). Same two-color randomization as the water range above.")]
    [SerializeField] private Color landParticleColorMin = Color.white;
    [SerializeField] private Color landParticleColorMax = Color.white;

    private bool? wasOnLand;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        steering = GetComponent<TurtleTargetSteering>();
    }

    private void Update()
    {
        bool onLand = PathfindingManager.Instance != null && PathfindingManager.Instance.IsOnLand(transform.position);
        if (wasOnLand.HasValue && wasOnLand.Value == onLand) return;

        wasOnLand = onLand;
        ApplyParticleColor(onLand ? landParticleColorMin : waterParticleColorMin, onLand ? landParticleColorMax : waterParticleColorMax);
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
        if (pendingImpulses > 0)
        {
            // Assumes the turtle's art faces along local +X (rotation 0 = facing right).
            // Only speedMultiplier (idle-amble) scales the lunge itself — see the
            // class doc comment for why the three buff layers don't.
            rb.AddForce((Vector2)transform.right * impulseForce * speedMultiplier * pendingImpulses, ForceMode2D.Impulse);

            finParticle1.Stop();
            finParticle2.Stop();
            finParticle1.Play();
            finParticle2.Play();

            pendingImpulses = 0;
            ImpulseApplied?.Invoke();
        }
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

    /// <summary>Recombines the buff layers (permanent x campfire x temporary x glue debuff — speedMultiplier is deliberately not part of this, see class doc comment) into one product and pushes it to every propelling fin's stroke rate and to this turtle's turn rate, so different buffs stack multiplicatively with each other exactly as they did back when they scaled impulseForce instead.</summary>
    private void RecalculateBuffSpeedMultiplier()
    {
        float combined = permanentSpeedMultiplier * campfireSpeedMultiplier * temporaryBuffSpeedMultiplier * glueDebuffSpeedMultiplier;

        foreach (LimbOscillator fin in propellingFins)
        {
            if (fin != null) fin.SetSpeedBuffMultiplier(combined);
        }

        if (steering != null) steering.SetTurnSpeedMultiplier(combined);
    }
}
