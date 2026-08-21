using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Three clickable icons — pause, normal speed, double speed — that set
/// Time.timeScale directly (0 / 1 / 2). Whichever speed is currently active
/// swaps to its own Selected graphic so the player can see the current state
/// at a glance; the other two show their Unselected graphic.
///
/// Each icon carries its own authored sprite pair rather than one shared
/// highlight treatment applied to a single graphic, so "selected" can be drawn
/// however each icon needs it (a different frame, a lit-up variant, a
/// completely separate piece of art) instead of being limited to what a tint
/// can express. Colour is left entirely alone here — an icon's Image tint is
/// whatever the Inspector says it is, at every moment.
///
/// These stay parked off-screen for the whole of the tutorial's scripted
/// portion and slide in once it ends (see Reveal Panel). The tutorial no longer
/// teaches the speed controls, and it freezes the day clock while it runs, so
/// through those steps they'd be three buttons that change nothing visible on a
/// clock that isn't moving — and pausing mid-lesson only makes the mentor's
/// unscaled animations play on over a frozen world. Holding them back also
/// keeps the bottom-left corner clear for the mentor, which stands exactly
/// where they live.
/// </summary>
public class TimeControlUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button doubleButton;

    [Header("Icons")]
    [SerializeField] private Image pauseIcon;
    [SerializeField] private Image normalIcon;
    [SerializeField] private Image doubleIcon;

    [Header("Icon Graphics")]
    [Tooltip("Sprite pair for the pause icon.")]
    [SerializeField] private IconSprites pauseSprites = new IconSprites();
    [Tooltip("Sprite pair for the normal-speed icon.")]
    [SerializeField] private IconSprites normalSprites = new IconSprites();
    [Tooltip("Sprite pair for the double-speed icon.")]
    [SerializeField] private IconSprites doubleSprites = new IconSprites();

    [Header("Reveal")]
    [Tooltip("This panel's own UISlidePanel, held off-screen until the tutorial's scripted portion finishes and then played in. Set its Play In On Enable to OFF, since this script drives it. Leave unassigned for controls that are simply visible from the first frame — but then they're on screen through the whole tutorial, on top of the mentor.")]
    [SerializeField] private UISlidePanel revealPanel;

    /// <summary>One icon's two authored states. Grouped per icon rather than kept as six loose fields so the Inspector reads as "pause: these two, normal: these two", which is how they're actually authored and how a mismatched pair is spotted.</summary>
    [System.Serializable]
    private class IconSprites
    {
        [Tooltip("Shown while this speed is the active one.")]
        public Sprite selected;
        [Tooltip("Shown while this speed is not the active one.")]
        public Sprite unselected;
    }

    private bool hasRevealed;

    private void Awake()
    {
        // Time.timeScale is a global engine value, not one of our own statics —
        // if Domain Reload is disabled it can carry over from a previous Play
        // session, so force a clean start the same way other static state in
        // this project defensively resets in Awake.
        Time.timeScale = 1f;

        if (pauseButton != null) pauseButton.onClick.AddListener(() => SetTimeScale(0f));
        if (normalButton != null) normalButton.onClick.AddListener(() => SetTimeScale(1f));
        if (doubleButton != null) doubleButton.onClick.AddListener(() => SetTimeScale(2f));

        RefreshIcons();
    }

    /// <summary>
    /// Slides the controls in the first frame the tutorial isn't holding them
    /// back. Polled rather than driven by a call from TutorialManager, matching
    /// how everything else in this project follows the phase flags — and so
    /// there is nothing to go wrong in the far more common case where no
    /// tutorial is running at all, which simply reveals on frame one.
    ///
    /// Checking here rather than in Start is what makes that safe: Unity runs
    /// every Start before any Update, so TutorialManager has already had its
    /// chance to raise IsActive by the time this first looks. Reading the flag
    /// from Start instead would be a race on script execution order.
    /// </summary>
    private void Update()
    {
        if (hasRevealed || TutorialManager.IsActive) return;

        hasRevealed = true;
        revealPanel?.PlayIn();
    }

    private void SetTimeScale(float scale)
    {
        // The settings screen pauses the game itself and puts back whatever the
        // speed was when it closes, so a speed picked from behind it would be
        // silently thrown away a moment later. Gating it here is also what lets
        // that screen's backdrop stop blocking raycasts: these three are the
        // only UI in the scene that acts on a click without checking a phase
        // flag first, so with them standing down there is nothing left behind
        // it to click into — which is what keeps the pause menu's Back button
        // reachable underneath.
        if (SettingsMenuUI.IsOpen) return;

        Time.timeScale = scale;
        RefreshIcons();
    }

    private void RefreshIcons()
    {
        ApplySprite(pauseIcon, pauseSprites, Mathf.Approximately(Time.timeScale, 0f));
        ApplySprite(normalIcon, normalSprites, Mathf.Approximately(Time.timeScale, 1f));
        ApplySprite(doubleIcon, doubleSprites, Mathf.Approximately(Time.timeScale, 2f));
    }

    /// <summary>Swaps one icon to the state it should be showing. A pair with that state left unassigned is skipped rather than blanked — a half-authored pair then reads as "this icon never changes", which is obvious on screen and recoverable, where clearing the sprite would leave an invisible button the player can still click.</summary>
    private static void ApplySprite(Image icon, IconSprites sprites, bool selected)
    {
        if (icon == null || sprites == null) return;

        Sprite sprite = selected ? sprites.selected : sprites.unselected;
        if (sprite != null) icon.sprite = sprite;
    }
}
