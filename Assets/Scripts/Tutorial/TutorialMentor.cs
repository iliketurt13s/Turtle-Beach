using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial's mentor turtle: a portrait that slides in from the bottom-left
/// corner and a speech bubble that grows out of nothing beside it, types a line
/// character by character, and shrinks away again. TutorialManager drives this
/// with two coroutines and never touches the portrait, the bubble, the slide or
/// the typing directly — so re-siting the mentor, changing how it enters, or
/// swapping the bubble's pop for something else is entirely an Editor job.
///
/// Every piece of motion here is an existing component doing its normal job:
/// UISlidePanel for the entrance, UIScalePop for the bubble, TypewriterText for
/// the line, TutorialJitter for the wobble. This script only sequences them.
/// The wobble in particular is driven straight off the type finishing rather
/// than a duration of its own — "speaking" and "typing" are the same state, so
/// there is no second timeline to keep in step.
///
/// Two things are forced from code rather than left to the Inspector, both
/// because the failure mode is silent:
///
/// 1. Raycast Target is cleared on every Graphic underneath this object. Both
///    TurtleSelectionController and BuildModeController ignore a click that the
///    EventSystem says landed on UI, and the EventSystem answers for ANY
///    graphic flagged Raycast Target — including a purely decorative one. A
///    mentor left on that flag would sit in the corner silently eating every
///    click that touched it, with no error and no visible cause. See
///    UpgradeSelectionUI.SetBackdropBlocking, which is the same hazard.
/// 2. The whole thing fades out while the pause menu or the game-over panel is
///    up. The Tutorial object has to sit late in the Canvas's child order so it
///    draws over UpgradeChoiceUI's full-screen backdrop during the upgrade-pick
///    step, and that same ordering would otherwise leave it floating over
///    GameOverUI too. Polled rather than event-driven, matching how everything
///    else in this project follows the phase flags (see BuildMenuToggleButton).
/// </summary>
public class TutorialMentor : MonoBehaviour
{
    [Header("Portrait")]
    [Tooltip("Slides the mentor on and off screen. Set its Play In On Enable to OFF — this script drives PlayIn/PlayOut directly. Its Direction is what decides which edge the mentor enters from.")]
    [SerializeField] private UISlidePanel portraitSlide;
    [Tooltip("The talking wobble, on a CHILD of the sliding object (it writes anchoredPosition, and so does UISlidePanel — see TutorialJitter's doc comment). Optional; leave unassigned for a mentor that holds still.")]
    [SerializeField] private TutorialJitter portraitJitter;

    [Header("Speech Bubble")]
    [Tooltip("Grows the bubble in when a line starts and shrinks it away when the mentor leaves. Set its Play In On Enable to OFF.")]
    [SerializeField] private UIScalePop bubblePop;
    [Tooltip("Types the line into the bubble's text. Its Characters Per Second sets the speaking pace, and the wobble runs for exactly as long as it is typing.")]
    [SerializeField] private TypewriterText bubbleText;

    [Header("Timing")]
    [Tooltip("Seconds (unscaled) between the mentor finishing its slide on and the bubble popping open to start the line — a beat so it visibly ARRIVES before it talks, rather than typing while it's still settling. Set to 0 for a mentor that speaks the instant it lands.")]
    [SerializeField] private float delayBeforeSpeaking = 0.25f;
    [Tooltip("Seconds (unscaled) the finished line sits there before Speak returns, so a short line isn't over the instant its last character lands. The bubble stays up past this — the mentor only leaves when TutorialManager dismisses it.")]
    [SerializeField] private float holdAfterLine = 0.4f;

    [Header("Phase Hiding")]
    [Tooltip("Seconds the fade in/out takes when the pause menu or game-over panel comes up. Runs on unscaled time, so it still plays while the game is paused at Time.timeScale 0.")]
    [SerializeField] private float fadeDuration = 0.15f;

    private CanvasGroup group;
    private bool isSliding;
    private bool wasBlocked;
    private Coroutine fadeRoutine;

    /// <summary>True once the mentor has finished sliding on and until it has finished sliding back off — i.e. whether it is actually standing in the corner. Lets Speak skip a redundant entrance when it's already there.</summary>
    public bool IsPresent { get; private set; }

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();

        // See the class doc comment — this is the one that fails silently.
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    private void Start()
    {
        // Snap (no fade) to whatever state the scene actually starts in, so the
        // first frame doesn't animate from a default that was never on screen.
        wasBlocked = IsBlockedByPhase;
        ApplyBlocked(wasBlocked, animate: false);
    }

    private static bool IsBlockedByPhase => GameOverUI.IsPaused || GameOverUI.IsRunOver || SettingsMenuUI.IsOpen;

    private void Update()
    {
        bool blocked = IsBlockedByPhase;
        if (blocked == wasBlocked) return;

        wasBlocked = blocked;
        ApplyBlocked(blocked, animate: true);
    }

    /// <summary>
    /// Brings the mentor on if it isn't already there, then grows the bubble
    /// and types line into it, wobbling for exactly as long as the typing runs.
    /// Returns once the line has finished and sat for Hold After Line — the
    /// mentor and its bubble are still on screen at that point and stay there
    /// until Dismiss is called, so the player can re-read the instruction while
    /// carrying it out.
    /// </summary>
    public IEnumerator Speak(string line)
    {
        gameObject.SetActive(true);

        if (!IsPresent && portraitSlide != null)
        {
            isSliding = true;
            portraitSlide.PlayIn(() => isSliding = false);
            yield return new WaitUntil(() => !isSliding);
        }

        IsPresent = true;

        // The beat between arriving and talking. Realtime for the same reason
        // as the hold below — a paused game must not stall the sequence here.
        if (delayBeforeSpeaking > 0f) yield return new WaitForSecondsRealtime(delayBeforeSpeaking);

        // Normally already closed (Dismiss shrinks it at the end of every
        // step), so this is what makes a second Speak without an intervening
        // Dismiss re-pop the bubble rather than leave it sitting open.
        if (bubblePop != null && !bubblePop.IsOpen) bubblePop.PlayIn();

        if (bubbleText != null)
        {
            bool typed = false;
            bubbleText.Play(line, () => typed = true);
            portraitJitter?.Play();
            yield return new WaitUntil(() => typed);
            portraitJitter?.Stop();
        }

        // Realtime, not WaitForSeconds: TimeControlUI's pause button sets
        // Time.timeScale to 0, and a scaled wait there would hold the sequence
        // forever on a line the player has already read.
        if (holdAfterLine > 0f) yield return new WaitForSecondsRealtime(holdAfterLine);
    }

    /// <summary>
    /// Shrinks the bubble away and then slides the mentor off screen, returning
    /// only once it has actually gone. The two are sequenced rather than played
    /// together on purpose: the bubble collapsing first reads as the mentor
    /// finishing what it was saying and then leaving, where doing both at once
    /// reads as the whole thing being yanked off.
    /// </summary>
    public IEnumerator Dismiss()
    {
        portraitJitter?.Stop();

        if (bubblePop != null && bubblePop.IsOpen)
        {
            bool closed = false;
            bubblePop.PlayOut(() => closed = true);
            yield return new WaitUntil(() => closed);
        }

        if (IsPresent && portraitSlide != null)
        {
            isSliding = true;
            portraitSlide.PlayOut(() => isSliding = false);
            yield return new WaitUntil(() => !isSliding);
        }

        IsPresent = false;
    }

    /// <summary>Puts the mentor away with no animation at all — for the run where the tutorial isn't playing, so nothing is ever seen to arrive and leave. UISlidePanel and UIScalePop both snap themselves to hidden in Awake anyway; this is the explicit version of that, so a scene left with the mentor visibly parked on-screen in the editor still starts clean.</summary>
    public void HideImmediate()
    {
        portraitJitter?.Stop();
        if (bubblePop != null) bubblePop.PlayOut();
        IsPresent = false;
        gameObject.SetActive(false);
    }

    private void ApplyBlocked(bool blocked, bool animate)
    {
        if (group == null) return;

        float target = blocked ? 0f : 1f;

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);

        if (!animate || fadeDuration <= 0f || !isActiveAndEnabled)
        {
            group.alpha = target;
            return;
        }

        fadeRoutine = StartCoroutine(FadeRoutine(target));
    }

    private IEnumerator FadeRoutine(float target)
    {
        float from = group.alpha;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        group.alpha = target;
        fadeRoutine = null;
    }
}
