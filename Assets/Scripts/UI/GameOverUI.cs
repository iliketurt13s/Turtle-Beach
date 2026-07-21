using TMPro;
using UnityEngine;
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
/// </summary>
public class GameOverUI : MonoBehaviour
{
    [Tooltip("Root object for the game-over panel, hidden until the nest is destroyed.")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text finalScoreText;
    [SerializeField] private TMP_Text highScoreText;
    [Tooltip("Name of the Start Menu scene to load when Menu is pressed. Must exactly match that scene's file name and be added to Build Settings.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private bool shown;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void Update()
    {
        if (shown) return;
        if (TurtleNest.Instance == null || !TurtleNest.Instance.IsDestroyed) return;

        Show();
    }

    private void Show()
    {
        shown = true;

        int finalScore = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentScore : 0;
        int highScore = ScoreManager.Instance != null ? ScoreManager.Instance.HighScore : 0;

        if (finalScoreText != null) finalScoreText.text = $"Score: {finalScore}";
        if (highScoreText != null) highScoreText.text = $"High Score: {highScore}";

        if (panel != null) panel.SetActive(true);
    }

    /// <summary>Wire this up to the Restart button's OnClick() in the Inspector. Reloads the current scene fresh — every gameplay system (turtles, trash, resources, score) is scene-instantiated, so this alone resets a full run.</summary>
    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Wire this up to the Menu button's OnClick() in the Inspector.</summary>
    public void ReturnToMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
