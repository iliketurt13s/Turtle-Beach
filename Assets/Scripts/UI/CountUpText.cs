using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Rapidly counts a TMP_Text up from zero to a target number. The companion to
/// TypewriterText in the end-of-run score breakdown (see ScoreBreakdownUI):
/// each line's label types itself out, then its number spins up beside it.
/// Standalone so anything else can reuse it.
///
/// Unscaled time for the same reason TypewriterText uses it — the panel this
/// runs on can be up while Time.timeScale is 0 or 2x.
/// </summary>
public class CountUpText : MonoBehaviour
{
    [Tooltip("The text to count up. Left unassigned = whatever TMP_Text is on this same GameObject.")]
    [SerializeField] private TMP_Text target;
    [Tooltip("How long the count-up takes, in seconds. Kept short — this is meant to read as a rapid spin-up, not a slow tally.")]
    [SerializeField] private float duration = 0.45f;
    [Tooltip("Format applied to the number — {0} is the value. e.g. \"{0}\" for a plain total, \"+{0}\" for a bonus line.")]
    [SerializeField] private string format = "{0}";
    [Tooltip("Eases the count so it starts fast and settles into the final number, instead of ticking at a flat rate the whole way.")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine countCoroutine;
    private Action onComplete;
    private int targetValue;

    /// <summary>True while the number is still climbing.</summary>
    public bool IsCounting => countCoroutine != null;

    private void Awake()
    {
        ResolveTarget();
    }

    /// <summary>Awake normally covers this, but resolving again on demand keeps Play/SetValue working even if they're reached before this component's GameObject was ever activated (Awake only runs on activation).</summary>
    private void ResolveTarget()
    {
        if (target == null) target = GetComponent<TMP_Text>();
    }

    /// <summary>Counts from 0 up to value, calling onComplete once it lands. Interrupts any count already running.</summary>
    public void Play(int value, Action onComplete = null)
    {
        ResolveTarget();

        if (target == null)
        {
            onComplete?.Invoke();
            return;
        }

        Stop();

        targetValue = value;
        this.onComplete = onComplete;

        // isActiveAndEnabled guards the animated path: StartCoroutine throws on
        // an inactive GameObject, which would leave onComplete un-fired and
        // strand any caller waiting on it (ScoreBreakdownUI waits per line, so
        // the whole breakdown would hang and never reveal its buttons). Landing
        // on the final value instantly is always better than not landing.
        if (duration <= 0f || value == 0 || !isActiveAndEnabled)
        {
            // A zero line (no resources delivered, no modifiers taken) still
            // needs its "0" written rather than being left blank.
            SetValue(value);
            FireComplete();
            return;
        }

        SetValue(0);
        countCoroutine = StartCoroutine(CountRoutine());
    }

    private IEnumerator CountRoutine()
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            // Both the eased t and the lerp are CLAMPED. An Ease curve authored
            // with overshooting tangents would otherwise sail past the target
            // mid-count and walk back down to it, so the number visibly changes
            // after it has already shown the player its answer — the one thing
            // the breakdown promises never happens (see ScoreBreakdownUI).
            float t = Mathf.Clamp01(ease.Evaluate(Mathf.Clamp01(elapsed / duration)));
            SetValue(Mathf.RoundToInt(Mathf.Lerp(0f, targetValue, t)));
            yield return null;
        }

        SetValue(targetValue);
        countCoroutine = null;
        FireComplete();
    }

    /// <summary>Snaps to the final number and fires the pending callback, as if the count had finished normally.</summary>
    public void Skip()
    {
        if (countCoroutine == null) return;

        Stop();
        SetValue(targetValue);
        FireComplete();
    }

    /// <summary>Overrides Format for this instance — lets one shared row prefab serve both plain totals and "+N" bonus lines without a separate prefab per format.</summary>
    public void SetFormat(string value)
    {
        format = value;
    }

    /// <summary>Writes a value immediately with no animation, e.g. to blank a row back out before the sequence replays it.</summary>
    public void SetValue(int value)
    {
        ResolveTarget();
        if (target != null) target.text = string.Format(format, value);
    }

    /// <summary>Clears the text entirely — used to hide a row's number until its turn in the sequence comes up.</summary>
    public void Clear()
    {
        Stop();
        ResolveTarget();
        if (target != null) target.text = string.Empty;
    }

    private void Stop()
    {
        if (countCoroutine != null)
        {
            StopCoroutine(countCoroutine);
            countCoroutine = null;
        }
    }

    private void FireComplete()
    {
        Action callback = onComplete;
        onComplete = null;
        callback?.Invoke();
    }
}
