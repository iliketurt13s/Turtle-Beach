using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on the turtle's root (with Rigidbody2D). Listens to a set of fin
/// LimbOscillators and gives the turtle a forward physics impulse each time a
/// fin enters its backward (power) stroke. The Rigidbody2D's own Linear Damping
/// (and any Physics Material 2D on its collider) provides the friction that
/// slows the turtle back down, so movement stays interactible with the environment.
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

    /// <summary>The fins driving propulsion, so other systems (e.g. idle/select logic) can pause/resume the same set without re-wiring it separately.</summary>
    public IReadOnlyList<LimbOscillator> PropellingFins => propellingFins;

    private Rigidbody2D rb;
    private int pendingImpulses;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
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
            rb.AddForce((Vector2)transform.right * impulseForce * speedMultiplier * permanentSpeedMultiplier * campfireSpeedMultiplier * temporaryBuffSpeedMultiplier * pendingImpulses, ForceMode2D.Impulse);
            pendingImpulses = 0;
        }
    }

    /// <summary>Scales every future stroke's impulse (not the fin animation itself), e.g. for a slower idle amble. 1 = normal speed. Overwritten constantly by TurtleAgent's task/idle state, so it's not where a permanent upgrade should live — see SetPermanentSpeedMultiplier.</summary>
    public void SetSpeedMultiplier(float multiplier) => speedMultiplier = multiplier;

    /// <summary>Scales every future stroke's impulse independently of SetSpeedMultiplier, so an upgrade can persist across whatever task/idle state TurtleAgent is in. Overwrites (not compounds) since callers always pass the already-cumulative total.</summary>
    public void SetPermanentSpeedMultiplier(float multiplier) => permanentSpeedMultiplier = multiplier;

    /// <summary>Independent layer for Campfire's while-inside-radius buff — overwrites, since TurtleAgent tracks the linear-stacked total across every overlapping campfire itself.</summary>
    public void SetCampfireSpeedMultiplier(float multiplier) => campfireSpeedMultiplier = multiplier;

    /// <summary>Independent layer for a personal, time-limited buff (e.g. breaking a Coconut). 1 = no buff active.</summary>
    public void SetTemporaryBuffSpeedMultiplier(float multiplier) => temporaryBuffSpeedMultiplier = multiplier;
}
