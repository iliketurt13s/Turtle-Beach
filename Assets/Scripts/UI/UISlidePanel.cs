using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to a UI panel's RectTransform to slide it in from off-screen to its
/// authored anchoredPosition (captured once, whatever it already is when this
/// first runs — no separate "home position" field duplicating the layout).
/// PlayIn snaps to the off-screen start point (Direction/Off Screen Distance
/// out from home) and animates back to home; PlayOut reverses that same path.
/// Both restart cleanly if called while a slide is already in progress. Runs
/// on unscaled time, so a panel that pauses the game the same moment it shows
/// itself (see GameOverUI) still animates instead of freezing at frame one.
///
/// By default (Play In On Enable) a panel plays in every time its GameObject
/// transitions inactive → active — the common case (GameOverUI's panel,
/// MainMenuController's difficultyPanel, a freshly-instantiated
/// ResourceCounterRow, TimeControlUI at scene start) needs no other code
/// changes at all. For a panel that also needs to slide back out on cue
/// (BuildSelectionUI, tied to BuildModeController.IsActive rather than a
/// simple show/hide) turn this off and have that owning script call
/// PlayIn/PlayOut directly instead.
///
/// If this is attached to something a parent Layout Group controls (it
/// overwrites the child's anchoredPosition every layout pass, fighting this
/// component's own writes), attach it to a nested child inside that object
/// instead — a Layout Group only ever positions its direct children, never
/// grandchildren.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UISlidePanel : MonoBehaviour
{
    /// <summary>Named for the screen edge this panel enters from, not the direction it travels — FromBottom starts below Home Position and travels up into it, FromLeft starts to the left and travels right into it, etc.</summary>
    public enum SlideDirection { FromBottom, FromTop, FromLeft, FromRight }

    [Tooltip("Which off-screen edge this panel enters from (PlayIn) / exits back toward (PlayOut) — see SlideDirection's own doc comment.")]
    [SerializeField] private SlideDirection direction = SlideDirection.FromBottom;
    [Tooltip("How far off-screen (anchoredPosition units) the start/end point is from Home Position. Tune to comfortably clear the panel's own on-screen size.")]
    [SerializeField] private float offScreenDistance = 1000f;
    [Tooltip("Seconds (unscaled, so it elapses even at Time.timeScale 0) PlayIn waits before it actually starts animating — e.g. so a HUD element doesn't pop in the instant the scene loads. The panel stays off-screen for the whole delay, same as before PlayIn was ever called. Doesn't affect PlayOut.")]
    [SerializeField] private float startDelay = 0f;
    [SerializeField] private float duration = 0.4f;
    [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Automatically PlayIn every time this GameObject becomes active. Turn off if an owning script drives PlayIn/PlayOut itself (see class doc comment).")]
    [SerializeField] private bool playInOnEnable = true;

    private RectTransform rect;
    private Vector2 homePosition;
    private Vector2 offScreenPosition;
    private bool homeCaptured;
    private Coroutine activeRoutine;

    /// <summary>This panel's authored on-screen position — where PlayIn lands. Safe to read before Awake (it captures on demand), which matters because another component on the SAME object can't otherwise tell the authored position apart from the off-screen one Awake immediately snaps to. See BuildMenuHudShift, which was reading exactly that stale value.</summary>
    public Vector2 HomePosition
    {
        get
        {
            CaptureHome();
            return homePosition;
        }
    }

    /// <summary>How far this panel is currently displaced from fully closed — zero while parked off-screen, and the full travel once it's all the way in, tracking the slide frame by frame in between. Lets another element mirror this panel's movement exactly instead of running its own copy of the distance, duration and curve and hoping the two stay in sync (see BuildMenuHudShift).</summary>
    public Vector2 CurrentDisplacement
    {
        get
        {
            CaptureHome();
            return rect.anchoredPosition - offScreenPosition;
        }
    }

    private void Awake()
    {
        CaptureHome();

        // Starts off-screen by default so a panel driven manually (playInOnEnable
        // off) isn't sitting visibly at Home Position until something calls
        // PlayIn — harmless for the auto-play case too, since OnEnable's PlayIn
        // re-snaps here anyway right before animating out of it.
        rect.anchoredPosition = offScreenPosition;
    }

    private void OnEnable()
    {
        CaptureHome();
        if (playInOnEnable) PlayIn();
    }

    private void CaptureHome()
    {
        // Resolved here rather than only in Awake: the accessors above are
        // reachable from another component's Awake, which may run first.
        if (rect == null) rect = (RectTransform)transform;
        if (homeCaptured) return;

        homeCaptured = true;
        homePosition = rect.anchoredPosition;
        offScreenPosition = homePosition + DirectionOffset(direction) * offScreenDistance;
    }

    private static Vector2 DirectionOffset(SlideDirection dir)
    {
        switch (dir)
        {
            case SlideDirection.FromBottom: return Vector2.down;
            case SlideDirection.FromTop: return Vector2.up;
            case SlideDirection.FromLeft: return Vector2.left;
            case SlideDirection.FromRight: return Vector2.right;
            default: return Vector2.zero;
        }
    }

    /// <summary>Snaps to the off-screen start point, waits Start Delay, then animates to Home Position, invoking onComplete once it has actually arrived — mirroring PlayOut, so a caller sequencing a panel in and then doing something with it (see TutorialMentor, which types into a speech bubble only once its mentor has finished sliding on) doesn't have to keep its own duplicate copy of Duration and Start Delay to time that off.</summary>
    public void PlayIn(Action onComplete = null)
    {
        CaptureHome();
        rect.anchoredPosition = offScreenPosition;
        StartSlide(startDelay, offScreenPosition, homePosition, onComplete);
    }

    /// <summary>Animates from wherever this panel currently is (normally Home Position) out to the off-screen point, then invokes onComplete — e.g. to deactivate the panel only once it's actually off-screen, rather than cutting the slide short. No delay, unlike PlayIn.</summary>
    public void PlayOut(Action onComplete = null)
    {
        CaptureHome();
        StartSlide(0f, rect.anchoredPosition, offScreenPosition, onComplete);
    }

    private void StartSlide(float delay, Vector2 from, Vector2 to, Action onComplete)
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(SlideRoutine(delay, from, to, onComplete));
    }

    private IEnumerator SlideRoutine(float delay, Vector2 from, Vector2 to, Action onComplete)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = easeCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
            rect.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
            yield return null;
        }

        rect.anchoredPosition = to;
        activeRoutine = null;
        onComplete?.Invoke();
    }
}
