using System.Collections;
using UnityEngine;

/// <summary>
/// Generic reusable "squash and stretch" scale-punch juice effect. Drop onto
/// any GameObject (world-space sprite or UI RectTransform — localScale works
/// the same way for both) and call Play() from wherever that object's own
/// trigger event already lives: a selection, a hit, a delivery, a placement,
/// a button click. No new wiring beyond that one call — scaling this up to a
/// new object later is purely an Editor step (attach this component) plus
/// one line at the call site, never new animation code.
///
/// Runs entirely off a single signed AnimationCurve: positive values stretch
/// (taller/thinner), negative values squash (shorter/wider), scaled by
/// Amount and eased back to the exact base scale by the end of Duration. If
/// the object this is attached to has a Collider2D you don't want visibly
/// resizing mid-animation, point TargetOverride at a child visuals transform
/// instead of leaving it on this object's own root.
/// </summary>
public class SquashAndStretch : MonoBehaviour
{
    [Tooltip("Transform actually scaled. Defaults to this object's own transform if left unassigned — set this to a child visuals transform instead if this component lives on a root with a Collider2D.")]
    [SerializeField] private Transform targetOverride;

    [SerializeField] private float duration = 0.25f;

    /// <summary>Exposes Duration so a caller that needs to wait out the punch (e.g. UpgradeSelectionUI delaying its card slots' deactivation so Play() isn't cut off mid-animation) can do so without hardcoding a duplicate number.</summary>
    public float Duration => duration;
    [Tooltip("How strongly the curve's value distorts the base scale, e.g. 0.25 = up to +/-25%.")]
    [SerializeField] private float amount = 0.25f;
    [Tooltip("Signed punch shape over [0,1] time: negative = squash, positive = stretch. Defaults to a quick squash dip, an overshoot stretch, then settling at 0.")]
    [SerializeField] private AnimationCurve curve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.12f, -1f),
        new Keyframe(0.5f, 0.6f),
        new Keyframe(1f, 0f));

    private Transform target;
    private Vector3 baseScale;
    private Coroutine running;

    private void Awake()
    {
        target = targetOverride != null ? targetOverride : transform;
        baseScale = target.localScale;
    }

    /// <summary>Restarts the punch from scratch if already mid-animation, rather than stacking or blending with it.</summary>
    public void Play()
    {
        if (!isActiveAndEnabled) return;

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        float t = 0f;
        while (t < duration)
        {
            // Unscaled so this juice plays at the same real-world speed
            // regardless of TimeControlUI's pause/normal/double Time.timeScale
            // setting — including still playing at all while paused (scaled
            // deltaTime would otherwise be 0).
            t += Time.unscaledDeltaTime;
            float value = curve.Evaluate(Mathf.Clamp01(t / duration));

            target.localScale = new Vector3(
                baseScale.x * (1f - value * amount),
                baseScale.y * (1f + value * amount),
                baseScale.z);

            yield return null;
        }

        target.localScale = baseScale;
        running = null;
    }
}
