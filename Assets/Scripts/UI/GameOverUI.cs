using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Shows the game-over panel once the nest is destroyed. Polls
/// TurtleNest.Instance.IsDestroyed every frame rather than subscribing to
/// TurtleNest.NestDestroyed, since TurtleNest is runtime-spawned by
/// IslandGenerator at map center after generation — it doesn't exist at scene
/// load, so there's no reliable single moment to subscribe from (mirrors how
/// DayStormCycle.IsStorming and other cross-system flags are polled rather
/// than pushed throughout this project). Closes out the run's score
/// (ScoreManager.FinalizeRun, which is what applies the modifier multiplier,
/// banks a new high score, and awards the run's score as player XP) and then plays two pages inside one panel frame: first
/// ScoreBreakdownUI animates the result line by line, then that page is swapped
/// out for LevelUpPanelUI's level and XP bar, and only once THAT settles do
/// Restart (reloads the current scene fresh) and Menu (loads the Start Menu
/// scene) appear. The all-time high score is no longer shown here — it lives on
/// the start menu now (see MainMenuController).
///
/// The buttons deliberately sit outside both pages, as a direct child of the
/// panel, because they're also the pause menu's only content — parenting them
/// under the level page would mean pausing had to show a level page too. That
/// same arrangement is what lets a settings page (SettingsMenuUI) be dropped in
/// as a third page sharing the Back button rather than authoring a second one
/// — see GoBack.
///
/// The same panel doubles as a pause menu: Escape toggles it on/off (only
/// while the run isn't actually over — once Show() fires, Escape stops doing
/// anything, since there's no gameplay left to return to). This script is the
/// single reader of the Escape key for the whole gameplay scene, and decides
/// what that key means when more than one thing could claim it — right now,
/// closing the settings screen first, then an open build drawer, and only
/// otherwise pausing. See HandleEscape for why that priority is centralized
/// here rather than polled per system. While paused the
/// breakdown is hidden and Paused Text shows in its place, the buttons stay
/// available so the player can still quit out mid-run, and Time.timeScale
/// drops to 0 (restoring whatever it was — TimeControlUI's pause/double-speed
/// setting — on resume, rather than assuming 1). IsPaused is the static gate other
/// input systems (TurtleSelectionController, BuildModeController) check to
/// stand down while paused, mirroring how they already stand down for
/// UpgradeSelectionUI.IsActive etc. — necessary because those systems read
/// clicks straight off Physics2D.OverlapPoint rather than exclusively through
/// Unity's UI event system, so clicking this panel's own buttons would
/// otherwise also leak through to whatever's underneath. IsRunOver is its
/// companion for the OTHER state this one panel can be in: gating on IsPaused
/// alone covers the pause menu but not the results screen, since ShowResult
/// clears IsPaused on its way up. Anything standing down for one generally
/// wants both.
///
/// The same panel also doubles as the win screen: ShowWin() is called
/// directly by GarbagePatchCutsceneController once the garbage patch is
/// fully depleted (instead of the old "move to a new island" transition),
/// which shows the win title and folds the patch bonus into the run's trash
/// score, but otherwise reuses the exact same breakdown/slide-in/Restart/Menu
/// flow as a loss. Both paths funnel through ShowResult, which no-ops if
/// the panel is already up — so if the nest happens to be destroyed in the
/// same storm the patch depletes, whichever fires first wins and the other
/// is silently ignored rather than fighting over the panel's text.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    /// <summary>True while paused via Escape — never true once the run has actually ended (Show() clears it and Escape stops responding).</summary>
    public static bool IsPaused { get; private set; }

    /// <summary>
    /// True from the moment the result screen goes up (win or loss) for the
    /// rest of the scene's life.
    ///
    /// Static for the same reason IsPaused is — input systems have to stand
    /// down for a finished run too — and separate from it because IsPaused
    /// deliberately does NOT cover that case: ShowResult clears it, since a run
    /// that's over isn't "paused" and can't be resumed. A system that gates on
    /// IsPaused alone therefore goes right back to accepting input the instant
    /// the results appear (see BuildModeController, which would otherwise let
    /// the player keep placing buildings behind this panel).
    ///
    /// Doubles as this component's own "already shown" guard, rather than
    /// mirroring a private bool that could drift out of step with it.
    /// </summary>
    public static bool IsRunOver { get; private set; }

    [Tooltip("Root object for the game-over panel — reused as the pause menu (see class doc comment), hidden until either is shown.")]
    [SerializeField] private GameObject panel;
    [Tooltip("Panel's own UISlidePanel — Pause()/Show() call PlayIn explicitly (on top of whatever its own Play In On Enable does, redundant but harmless — covers the panel already being active from a rapid pause/resume) and Resume() calls PlayOut, only deactivating panel once it's actually back off-screen. Leave unassigned to fall back to instant SetActive on all three.")]
    [SerializeField] private UISlidePanel panelSlide;
    [Tooltip("Page one of the result screen: the animated score breakdown (title + per-category lines + final score). Hidden while merely paused.")]
    [SerializeField] private ScoreBreakdownUI scoreBreakdown;
    [Tooltip("Page two of the result screen: the player's level and XP bar, shown in the breakdown's place once it finishes. Hidden while merely paused. Leave unassigned to go straight from the breakdown to the buttons.")]
    [SerializeField] private LevelUpPanelUI levelPanel;
    [Tooltip("How long the finished score breakdown stays up before the panel swaps to the level page.")]
    [SerializeField] private float scorePageHoldDuration = 1.2f;
    [Tooltip("Parent of everything that belongs to PAUSING and nothing else: the Paused label and the Back button. Shown together on the pause path, hidden together the instant a run actually ends — neither means anything once the nest is gone. Keep it OUTSIDE both pages (a direct child of the panel), since each page is deactivated on the pause path.")]
    [SerializeField] private GameObject pauseOnlyRoot;
    [Tooltip("The Paused label itself, so Paused Message can be written into it. Put it INSIDE Pause Only Root — showing and hiding it is that root's job, not this field's.")]
    [SerializeField] private TMP_Text pausedText;
    [Tooltip("Parent of the Restart/Menu buttons — the two that appear on BOTH paths, which is why they aren't inside Pause Only Root. Keep it outside both pages too: pausing shows these immediately, while at the end of a run they're held back until both pages have played.")]
    [SerializeField] private GameObject buttonsRoot;
    [SerializeField] private string pausedMessage = "Game Paused";
    [Tooltip("Name of the Start Menu scene to load when Menu is pressed. Must exactly match that scene's file name and be added to Build Settings.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [Tooltip("This scene's own screen-wipe panel. Left unassigned = falls back to an instant scene load.")]
    [SerializeField] private SceneTransitionPanel transitionPanel;

    private float timeScaleBeforePause = 1f;

    private void Awake()
    {
        // Both are static, so force a clean state rather than relying on their
        // default values — they'd otherwise survive a scene reload (Restart) in
        // a build, and a stopped-and-restarted Play session in an Editor with
        // Domain Reload disabled, leaving a fresh run gated as if it were
        // already over.
        IsPaused = false;
        IsRunOver = false;

        if (panel != null) panel.SetActive(false);

        // Same reasoning as MainMenuController's: without this, Back/Escape hide
        // the panel instantly and it reads as a broken slide rather than an
        // unassigned field.
        if (panel != null && panelSlide == null)
        {
            Debug.LogWarning("GameOverUI: no Panel Slide assigned, so Back (and Escape) will hide the pause panel instantly instead of sliding it off. Add a UISlidePanel to the panel object and drag it into that field.", this);
        }

        if (pauseOnlyRoot == null)
        {
            Debug.LogWarning("GameOverUI: no Pause Only Root assigned. Parent the Paused label and the Back button under one object and drag it in — otherwise the Back button stays visible on the game-over screen, where it does nothing (Resume refuses to run once a run has ended).", this);
        }
    }

    private void Update()
    {
        if (IsRunOver)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleEscape();
        }

        if (TurtleNest.Instance == null || !TurtleNest.Instance.IsDestroyed) return;

        Show();
    }

    /// <summary>
    /// Escape backs out of the innermost thing that's open, rather than always
    /// meaning "pause": with the build drawer latched open it closes that and
    /// stops there, so a player dismissing the build menu doesn't get the pause
    /// menu thrown up behind it.
    ///
    /// The priority lives HERE, in the one script that owns the Escape key,
    /// rather than each system watching for Escape itself and standing down for
    /// the others. That's not a style preference: both would poll the same key
    /// on the same frame, and which one saw it first would come down to script
    /// execution order, so the drawer closing AND the game pausing off one
    /// keypress would be a real (and intermittent) outcome. One reader, one
    /// decision, no ordering dependency.
    ///
    /// Reads the LATCH (see BuildModeController.IsToggledOpen) rather than
    /// IsActive, because the latch is live while IsActive is only refreshed in
    /// that controller's own Update — which may not have run yet on the frame
    /// the hammer tab was clicked.
    /// </summary>
    private void HandleEscape()
    {
        // Innermost first: the settings screen sits over everything, including
        // the pause menu it can be opened from, so backing out of it must never
        // also toggle the pause underneath. SettingsMenuUI deliberately doesn't
        // read Escape itself in this scene for exactly that reason — see its
        // class doc comment.
        if (TryCloseSettings()) return;

        if (!IsPaused && BuildModeController.IsToggledOpen)
        {
            BuildModeController.Instance.SetBuildModeOpen(false);
            return;
        }

        TogglePause();
    }

    /// <summary>
    /// Closes the settings screen if it is up, reporting whether it did. The
    /// shared first step of both ways back — the Escape key and the Back
    /// button — so the two can't end up disagreeing about what "back" means
    /// while that screen is open.
    /// </summary>
    private static bool TryCloseSettings()
    {
        if (!SettingsMenuUI.IsOpen || SettingsMenuUI.Instance == null) return false;

        SettingsMenuUI.Instance.Close();
        return true;
    }

    /// <summary>
    /// What the pause menu's Back button should be wired to, so that ONE
    /// button serves both screens: it closes the settings screen if that's
    /// what's open, and only otherwise resumes. Exactly the decision Escape
    /// makes, taken from the same place, so the key and the button stay in
    /// step — press Back once to leave settings, again to return to the game.
    ///
    /// Closing goes through SettingsMenuUI.Close, exactly as that screen's own
    /// close button does — so it slides off under its UISlidePanel rather than
    /// popping out of existence, and there is one closing path to keep working
    /// rather than two. Nothing about the hierarchy has to change for this;
    /// the only requirement is that the Back button is still clickable while
    /// the settings screen is up, i.e. that screen's backdrop leaves Raycast
    /// Target off (see SettingsMenuUI's class doc comment for why nothing
    /// leaks through when it does).
    ///
    /// Wire the Back button straight to Resume instead if the settings screen
    /// keeps a close button of its own.
    /// </summary>
    public void GoBack()
    {
        if (TryCloseSettings()) return;

        Resume();
    }

    private void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    private void Pause()
    {
        IsPaused = true;
        timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;

        if (scoreBreakdown != null) scoreBreakdown.Hide();
        if (levelPanel != null) levelPanel.Hide();

        SetPauseOnlyVisible(true);
        if (pausedText != null) pausedText.text = pausedMessage;

        // Available straight away while paused (unlike at the end of a run,
        // where they wait for the breakdown) so quitting out mid-run doesn't
        // require sitting through anything.
        if (buttonsRoot != null) buttonsRoot.SetActive(true);

        if (panel != null) panel.SetActive(true);
        if (panelSlide != null) panelSlide.PlayIn();
    }

    /// <summary>
    /// Unpauses. Wire the pause menu's Back button to GoBack rather than
    /// straight to this if the settings screen shares that button — GoBack
    /// falls through to here once there's nothing left to back out of. Also
    /// what Escape ends up calling to unpause, so the two routes can't drift
    /// apart.
    ///
    /// No-ops once the run has actually ended. Update already refuses to let
    /// Escape reach here in that state, but a button click never goes through
    /// Update, and resuming a finished run would slide the result screen away
    /// and leave the player staring at a destroyed nest with no UI and no way
    /// back — so the guard is repeated rather than assumed.
    /// </summary>
    public void Resume()
    {
        if (IsRunOver) return;

        IsPaused = false;
        Time.timeScale = timeScaleBeforePause;

        if (panelSlide != null)
        {
            panelSlide.PlayOut(() => { if (panel != null) panel.SetActive(false); });
        }
        else if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void Show() => ShowResult(won: false);

    /// <summary>Called by GarbagePatchCutsceneController once the garbage patch is fully depleted, ending the run as a win instead of running the old island transition. Guarded the same as Show() — a no-op if the nest was already destroyed and the loss screen is already up.</summary>
    public void ShowWin() => ShowResult(won: true);

    private void ShowResult(bool won)
    {
        if (IsRunOver) return;

        IsRunOver = true;
        IsPaused = false;
        Time.timeScale = timeScaleBeforePause;

        // Before anything is displayed: this is what banks the garbage-patch
        // bonus, applies the modifier multiplier, awards the
        // run's XP, and freezes the totals the breakdown is about to read.
        ScoreManager.Instance?.FinalizeRun(won);

        // The Paused label and Back button belong to pausing alone — the run is
        // over, so there's no gameplay left to go back to and nothing is
        // "paused". Both live under one root precisely so this is a single call.
        SetPauseOnlyVisible(false);
        if (levelPanel != null) levelPanel.Hide();
        if (buttonsRoot != null) buttonsRoot.SetActive(false);

        if (panel != null) panel.SetActive(true);
        if (panelSlide != null) panelSlide.PlayIn();

        if (scoreBreakdown != null)
        {
            scoreBreakdown.Play(won, BeginLevelPage);
        }
        else
        {
            Debug.LogWarning("GameOverUI: no Score Breakdown assigned — skipping straight to the level page.");
            BeginLevelPage();
        }
    }

    /// <summary>Called once the score breakdown finishes. Holds the finished numbers on screen for a beat so they can actually be read, then swaps the panel's contents to the level page — same frame, same size, different page.</summary>
    private void BeginLevelPage()
    {
        if (levelPanel == null)
        {
            Debug.LogWarning("GameOverUI: no Level Panel assigned — showing the buttons with no level display.");
            ShowButtons();
            return;
        }

        StartCoroutine(SwapToLevelPage());
    }

    private IEnumerator SwapToLevelPage()
    {
        // Unscaled, like every other animation on this panel: it can be up at
        // any Time.timeScale TimeControlUI left behind.
        float elapsed = 0f;
        while (elapsed < scorePageHoldDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (scoreBreakdown != null) scoreBreakdown.Hide();
        levelPanel.Play(ShowButtons);
    }

    private void ShowButtons()
    {
        if (buttonsRoot != null) buttonsRoot.SetActive(true);
    }

    /// <summary>
    /// Shows or hides the pause-only furniture — the Paused label and the Back
    /// button — as one unit, which is the entire reason they share a root.
    ///
    /// Falls back to driving the label alone when no root is wired yet, so a
    /// half-finished hierarchy can't strand "Game Paused" on top of a finished
    /// run's results. The Back button can't be covered by that fallback (there's
    /// no separate reference to it any more), which is what the Awake warning is
    /// for.
    /// </summary>
    private void SetPauseOnlyVisible(bool visible)
    {
        if (pauseOnlyRoot != null)
        {
            pauseOnlyRoot.SetActive(visible);
            return;
        }

        if (pausedText != null) pausedText.gameObject.SetActive(visible);
    }

    /// <summary>Wire this up to the Restart button's OnClick() in the Inspector. Reloads the current scene fresh — every gameplay system (turtles, trash, resources, score) is scene-instantiated, so this alone resets a full run.</summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        string sceneName = SceneManager.GetActiveScene().name;
        if (transitionPanel != null)
        {
            transitionPanel.PlayCoverAndLoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning("GameOverUI: no Transition Panel assigned — restarting instantly with no wipe.");
            SceneManager.LoadScene(sceneName);
        }
    }

    /// <summary>Wire this up to the Menu button's OnClick() in the Inspector.</summary>
    public void ReturnToMenu()
    {
        Time.timeScale = 1f;
        if (transitionPanel != null)
        {
            transitionPanel.PlayCoverAndLoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning("GameOverUI: no Transition Panel assigned — returning to menu instantly with no wipe.");
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
