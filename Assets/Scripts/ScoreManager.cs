using UnityEngine;

/// <summary>
/// Scene-wide singleton (same rationale as ResourceManager/UpgradeManager: turtles
/// and trash are runtime-spawned with no scene-authored reference) tracking this
/// run's score and the all-time high score. High score is persisted via
/// PlayerPrefs and kept in sync live — every AddScore call that pushes CurrentScore
/// past the stored HighScore immediately saves the new value — so GameOverUI can
/// just read HighScore directly whenever the game ends, rather than needing a
/// separate "submit final score" step.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    private const string HighScoreKey = "HighScore";

    public static ScoreManager Instance { get; private set; }

    public int CurrentScore { get; private set; }
    public int HighScore { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Adds amount (may be called with a positive amount any number of times per frame — trash kills and resource deliveries both funnel through here) and immediately persists a new all-time high the instant this run's score passes it. No-ops once the nest is destroyed, so the score freezes at whatever it was at game over rather than still ticking up from deliveries/kills already in flight that frame.</summary>
    public void AddScore(int amount)
    {
        if (amount == 0) return;
        if (TurtleNest.Instance != null && TurtleNest.Instance.IsDestroyed) return;

        CurrentScore += amount;

        if (CurrentScore > HighScore)
        {
            HighScore = CurrentScore;
            PlayerPrefs.SetInt(HighScoreKey, HighScore);
            PlayerPrefs.Save();
        }
    }
}
