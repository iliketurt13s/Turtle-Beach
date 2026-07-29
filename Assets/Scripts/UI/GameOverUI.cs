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
/// than pushed throughout this project). Displays this run's final score
/// alongside the all-time high score (see ScoreManager) and offers Restart
/// (reloads the current scene fresh) and Menu (loads the Start Menu scene).
///
/// The same panel doubles as a pause menu: Escape toggles it on/off (only
/// while the run isn't actually over — once Show() fires, Escape stops doing
/// anything, since there's no gameplay left to return to). While paused, the
/// score text is repurposed to read Paused Message instead of the real score,
/// the high score text is hidden entirely, and Time.timeScale drops to 0
/// (restoring whatever it was — TimeControlUI's pause/double-speed setting —
/// on resume, rather than assuming 1). IsPaused is the static gate other
/// input systems (TurtleSelectionController, BuildModeController) check to
/// stand down while paused, mirroring how they already stand down for
/// UpgradeSelectionUI.IsActive etc. — necessary because those systems read
/// clicks straight off Physics2D.OverlapPoint rather than exclusively through
/// Unity's UI event system, so clicking this panel's own buttons would
/// otherwise also leak through to whatever's underneath.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    /// <summary>True while paused via Escape — never true once the run has actually ended (Show() clears it and Escape stops responding).</summary>
    public static bool IsPaused { get; private set; }

    [Tooltip("Root object for the game-over panel — reused as the pause menu (see class doc comment), hidden until either is shown.")]
    [SerializeField] private GameObject panel;
    [Tooltip("Panel's own UISlidePanel — Pause()/Show() call PlayIn explicitly (on top of whatever its own Play In On Enable does, redundant but harmless — covers the panel already being active from a rapid pause/resume) and Resume() calls PlayOut, only deactivating panel once it's actually back off-screen. Leave unassigned to fall back to instant SetActive on all three.")]
    [SerializeField] private UISlidePanel panelSlide;
    [Tooltip("Shows the real final score once the run ends, or Paused Message below while merely paused.")]
    [SerializeField] private TMP_Text finalScoreText;
    [Tooltip("Shows the all-time high score once the run ends. Hidden entirely while merely paused.")]
    [SerializeField] private TMP_Text highScoreText;
    [SerializeField] private string pausedMessage = "Game Paused";
    [Tooltip("Name of the Start Menu scene to load when Menu is pressed. Must exactly match that scene's file name and be added to Build Settings.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [Tooltip("This scene's own screen-wipe panel. Left unassigned = falls back to an instant scene load.")]
    [SerializeField] private SceneTransitionPanel transitionPanel;

    private bool shown;
    private float timeScaleBeforePause = 1f;

    private void Awake()
    {
        IsPaused = false;
        if (panel != null) panel.SetActive(false);
    }

    private void Update()
    {
        if (shown)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }

        if (TurtleNest.Instance == null || !TurtleNest.Instance.IsDestroyed) return;

        Show();
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

        if (finalScoreText != null)
        {
            finalScoreText.gameObject.SetActive(true);
            finalScoreText.text = pausedMessage;
        }
        if (highScoreText != null) highScoreText.gameObject.SetActive(false);

        if (panel != null) panel.SetActive(true);
        if (panelSlide != null) panelSlide.PlayIn();
    }

    private void Resume()
    {
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

    private void Show()
    {
        shown = true;
        IsPaused = false;
        Time.timeScale = timeScaleBeforePause;

        int finalScore = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentScore : 0;
        int highScore = ScoreManager.Instance != null ? ScoreManager.Instance.HighScore : 0;

        if (finalScoreText != null)
        {
            finalScoreText.gameObject.SetActive(true);
            finalScoreText.text = $"Score: {finalScore}";
        }
        if (highScoreText != null)
        {
            highScoreText.gameObject.SetActive(true);
            highScoreText.text = $"High Score: {highScore}";
        }

        if (panel != null) panel.SetActive(true);
        if (panelSlide != null) panelSlide.PlayIn();
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
