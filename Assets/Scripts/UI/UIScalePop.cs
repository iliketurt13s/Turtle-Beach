using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to a UI element to grow it in from nothing to its authored
/// localScale (captured once, whatever it already is when this first runs — no
/// separate "full scale" field duplicating the layout) and shrink it back away
/// again. PlayIn snaps to zero scale and animates up to Home Scale; PlayOut
/// reverses that from wherever it currently is. Both restart cleanly if called
/// mid-animation.
///
/// This is UISlidePanel's counterpart for elements that read better appearing
/// in place than travelling in from an edge — a speech bubble, a popup, a
/// badge. Deliberately built to the same shape: same on-demand home capture,
/// same unscaled time, same serialized ease curve, same PlayIn/PlayOut pair, so
/// the two can be swapped for each other without relearning anything. An
/// overshooting curve is fine and gives the pop its bounce.
///
/// Scale rather than position means this never contends with a UISlidePanel on
/// the SAME object (that one writes anchoredPosition, this one writes
/// localScale) — but two components both writing localScale would fight
/// exactly the way BuildMenuHudShift's doc comment describes, so don't pair
/// this with SquashAndStretch on one object.
/// </summary>
public class UIScalePop : MonoBehaviour
{
    [Tooltip("Seconds the grow/shrink takes. Runs on unscaled time, so it still plays while the game is paused at Time.timeScale 0.")]
    [SerializeField] private float duration = 0.25f;
    [Tooltip("Eases both directions. An overshooting curve gives the element a bounce as it lands — the scale has somewhere to overshoot to, unlike an alpha.")]
    [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Automatically PlayIn every time this GameObject becomes active. Turn off if an owning script drives PlayIn/PlayOut itself (see TutorialMentor).")]
    [SerializeField] private bool playInOnEnable = true;

    private Vector3 homeScale = Vector3.one;
    private bool homeCaptured;
    private Coroutine activeRoutine;

    /// <summary>This element's authored resting scale — where PlayIn lands. Safe to read before Awake (it captures on demand), which matters because another component on the SAME object can't otherwise tell the authored scale apart from the zero Awake immediately snaps to. Same reasoning as UISlidePanel.HomePosition.</summary>
    public Vector3 HomeScale
    {
        get
        {
            CaptureHome();
            return homeScale;
        }
    }

    /// <summary>True once PlayIn has been called and until PlayOut finishes — i.e. whether this element is showing or on its way to showing. Lets an owner skip a redundant PlayIn without tracking that state itself.</summary>
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        CaptureHome();

        // Starts collapsed by default so an element driven manually
        // (playInOnEnable off) isn't sitting visibly at full size until
        // something calls PlayIn — harmless for the auto-play case too, since
        // OnEnable's PlayIn re-snaps here anyway right before animating out of it.
        transform.localScale = Vector3.zero;
    }

    private void OnEnable()
    {
        CaptureHome();
        if (playInOnEnable) PlayIn();
    }

    private void CaptureHome()
    {
        // Resolved here rather than only in Awake: the accessor above is
        // reachable from another component's Awake, which may run first.
        if (homeCaptured) return;

        homeCaptured = true;
        homeScale = transform.localScale;

        // A zero authored scale would make PlayIn a no-op and leave the element
        // permanently invisible with no error — almost always someone leaving a
        // prefab's scale at 0 rather than a deliberate choice.
        if (homeScale == Vector3.zero) homeScale = Vector3.one;
    }

    /// <summary>Snaps to zero scale, then animates up to Home Scale.</summary>
    public void PlayIn()
    {
        CaptureHome();
        IsOpen = true;
        transform.localScale = Vector3.zero;
        StartPop(Vector3.zero, homeScale, null);
    }

    /// <summary>Animates from wherever this element currently is (normally Home Scale) down to nothing, then invokes onComplete — e.g. to deactivate it only once it's actually gone, rather than cutting the shrink short.</summary>
    public void PlayOut(Action onComplete = null)
    {
        CaptureHome();
        IsOpen = false;
        StartPop(transform.localScale, Vector3.zero, onComplete);
    }

    private void StartPop(Vector3 from, Vector3 to, Action onComplete)
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);

        // isActiveAndEnabled guards the animated path the way TypewriterText
        // and CountUpText do: StartCoroutine throws on an inactive GameObject,
        // and a caller waiting on onComplete would hang forever.
        if (!isActiveAndEnabled || duration <= 0f)
        {
            transform.localScale = to;
            activeRoutine = null;
            onComplete?.Invoke();
            return;
        }

        activeRoutine = StartCoroutine(PopRoutine(from, to, onComplete));
    }

    private IEnumerator PopRoutine(Vector3 from, Vector3 to, Action onComplete)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = easeCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
            transform.localScale = Vector3.LerpUnclamped(from, to, t);
            yield return null;
        }

        transform.localScale = to;
        activeRoutine = null;
        onComplete?.Invoke();
    }
}
