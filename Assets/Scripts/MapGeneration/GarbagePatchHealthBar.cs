using System.Collections;
using UnityEngine;

/// <summary>
/// UNUSED as of the debris-pile overhaul: GarbagePatch no longer holds a
/// reference to this or drives it, because the pile itself now shows the
/// patch's health by visibly wearing away (see GarbagePatch's class doc
/// comment). The file is kept only so the bar object left in the prefab —
/// deactivated, not deleted — doesn't turn into a missing-script reference.
/// Delete both together if the bar is never coming back; nothing else will
/// break.
///
/// Continuous world-space health bar for GarbagePatch — a single fill sprite
/// (pivot at its left edge, so scaling local X shrinks it from the right,
/// same convention as BuildingHealthBar.SetHealth) rather than the old
/// discrete pip set. A continuous current/max ratio is exact regardless of
/// Max Segments (5/10/999999 depending on game mode — see GarbagePatch's
/// game-mode presets), so a hit is always visually perceptible; the old pip
/// version could round a hit down to "no visible change" against a large max.
/// Smoothly animates to the new ratio (unscaled time, so it still plays
/// through a paused frame) rather than snapping — except the very first call
/// (initial spawn), which snaps instantly so the patch doesn't visibly
/// fill up from empty the moment it appears.
/// </summary>
public class GarbagePatchHealthBar : MonoBehaviour
{
    [Tooltip("Fill sprite, pivot at its left edge, so scaling local X shrinks it from the right.")]
    [SerializeField] private SpriteRenderer fillRenderer;
    [SerializeField] private float animationDuration = 0.4f;
    [SerializeField] private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private bool initialized;
    private float currentRatio;
    private float fullScaleX;
    private Coroutine activeRoutine;

    /// <summary>True while the fill is still animating toward its latest target ratio — GarbagePatch waits for this to clear before destroying itself on depletion, so the bar visually finishes draining to zero rather than vanishing mid-shrink.</summary>
    public bool IsAnimating => activeRoutine != null;

    public void SetSegments(int current, int max)
    {
        if (fillRenderer == null) return;

        float targetRatio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        if (!initialized)
        {
            initialized = true;
            // Whatever local X scale the fill was authored at in the prefab
            // IS full width (e.g. GarbagePatch's Fill is scaled to 4.935 to
            // span its frame) — captured once here rather than assumed to be
            // 1, so full health renders at the artist's actual full-width
            // appearance instead of getting clobbered down to a bare ratio.
            fullScaleX = fillRenderer.transform.localScale.x;
            currentRatio = targetRatio;
            ApplyRatio(currentRatio);
            return;
        }

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(AnimateFill(currentRatio, targetRatio));
    }

    private IEnumerator AnimateFill(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = animationCurve.Evaluate(Mathf.Clamp01(elapsed / animationDuration));
            currentRatio = Mathf.LerpUnclamped(from, to, t);
            ApplyRatio(currentRatio);
            yield return null;
        }

        currentRatio = to;
        ApplyRatio(currentRatio);
        activeRoutine = null;
    }

    private void ApplyRatio(float ratio)
    {
        Vector3 scale = fillRenderer.transform.localScale;
        scale.x = fullScaleX * ratio;
        fillRenderer.transform.localScale = scale;
    }
}
