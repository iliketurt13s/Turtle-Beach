using System;
using UnityEngine;

/// <summary>
/// Drives a limb's local Z rotation with a sine wave. Generic and reusable for
/// any back-and-forth appendage motion (fins, tail, claws, etc.), not just swimming.
/// Fires <see cref="StrokeChanged"/> whenever the limb switches between its two
/// swing directions so other systems (like propulsion) can react to a specific half of the cycle.
/// </summary>
public class LimbOscillator : MonoBehaviour
{
    public enum Stroke { Forward, Backward }

    [Header("Oscillation")]
    [Tooltip("Local Z rotation (degrees) the limb swings around.")]
    [SerializeField] private float restAngle = 0f;
    [Tooltip("Maximum degrees the limb swings away from the rest angle in either direction.")]
    [SerializeField] private float amplitude = 35f;
    [Tooltip("Full swing cycles per second.")]
    [SerializeField] private float frequency = 1.5f;
    [Tooltip("0-1 offset into the cycle, lets multiple limbs move out of sync with each other.")]
    [SerializeField, Range(0f, 1f)] private float phaseOffset = 0f;
    [SerializeField] private bool playing = true;

    /// <summary>Which half of the swing the limb is currently moving through.</summary>
    public Stroke CurrentStroke { get; private set; } = Stroke.Forward;

    /// <summary>Raised once whenever CurrentStroke flips.</summary>
    public event Action<LimbOscillator> StrokeChanged;

    private float phase;

    private void Update()
    {
        if (!playing) return;

        phase += frequency * Time.deltaTime;

        float cycle = (phase + phaseOffset) * Mathf.PI * 2f;
        float angle = Mathf.Sin(cycle) * amplitude;
        float velocitySign = Mathf.Cos(cycle);

        Stroke stroke = velocitySign < 0f ? Stroke.Backward : Stroke.Forward;
        if (stroke != CurrentStroke)
        {
            CurrentStroke = stroke;
            StrokeChanged?.Invoke(this);
        }

        transform.localRotation = Quaternion.Euler(0f, 0f, restAngle + angle);
    }

    public void SetPlaying(bool value) => playing = value;

    /// <summary>Permanently scales this limb's oscillation frequency, e.g. for an indefinite speed buff.</summary>
    public void MultiplyFrequency(float multiplier) => frequency *= multiplier;

    /// <summary>Resets both the phase offset and the live cycle position to 0, so calling this on every limb the same frame brings them all into lockstep from that moment on (assuming they share the same frequency).</summary>
    public void SyncPhase()
    {
        phaseOffset = 0f;
        phase = 0f;
    }
}
