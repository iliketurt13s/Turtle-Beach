using UnityEngine;

/// <summary>
/// Scene-wide singleton (same rationale as ResourceManager/UpgradeManager: turtles
/// and trash are runtime-spawned with no scene-authored reference) tracking this
/// run's score. Points are bucketed by where they came from rather than summed
/// into one number, because the end-of-run screen breaks them down line by line
/// (see ScoreBreakdownUI): resources delivered and trash destroyed each track
/// their own running count AND their own point total, since resources are worth
/// a flat 1 point per unit but trash is worth its TrashDefinition.Rating.
///
/// One multiplier stacks on top of the base at the very end: the run modifiers
/// the player opted into, which compound among themselves (see
/// GameModifierManager.CalculateMultiplier) so several stacked modifiers
/// escalate sharply instead of creeping up. It's deliberately applied once in
/// FinalizeRun rather than folded into every AddScore call, so the number the
/// breakdown shows for "how much did the multiplier earn me" is exact rather
/// than an accumulation of per-award rounding.
///
/// There used to be a second, earlier multiplier here for an Easy/Medium/Hard
/// difficulty pick. It's gone: two multipliers stacked in a fixed order made a
/// score hard to reason about, and modifiers already cover "make this run worth
/// more" in a way the player chooses explicitly. Nothing reads the
/// "DifficultyIndex" PlayerPrefs key any more — a value left in it by an older
/// build is simply ignored rather than migrated.
///
/// Beating the garbage patch has no line of its own — it folds a flat
/// Patch Defeated Trash Bonus into the trash bucket, on the reasoning that the
/// patch is simply the largest piece of trash in the run.
///
/// Unlike the old behavior, the high score is persisted in FinalizeRun rather
/// than live on every AddScore. It has to be: neither multiplier exists until
/// the run actually ends, so a live-saved value would only ever be the
/// unmultiplied base and would be beaten by essentially every later run. It's
/// read back by MainMenuController, which shows it on the start screen.
///
/// FinalizeRun is also where the run's score is banked as player XP (see
/// PlayerProgression) — the two are the same number, deliberately, so "score
/// more" and "unlock more" are one goal rather than two. It happens here for
/// the same reason the high score does, and the pre-run total is snapshotted
/// in Awake so LevelUpPanelUI can animate the bar from where the player
/// started this run to where they finished it.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    private const string HighScoreKey = "HighScore";

    public static ScoreManager Instance { get; private set; }

    [Tooltip("Flat bonus folded into the Trash Destroyed points if the run ends by depleting the garbage patch — the patch is treated as the run's biggest piece of trash rather than getting a score line of its own.")]
    [SerializeField] private int patchDefeatedTrashBonus = 500;

    /// <summary>How many individual resource units turtles delivered to the nest this run.</summary>
    public int ResourcesDelivered { get; private set; }
    /// <summary>Points earned from those deliveries (1 each, so currently equal to the count — kept separate so the rate can change without touching the breakdown UI).</summary>
    public int ResourcePoints { get; private set; }

    /// <summary>How many individual pieces of trash died this run.</summary>
    public int TrashDestroyed { get; private set; }
    /// <summary>Points earned from those kills, plus Patch Defeated Trash Bonus once FinalizeRun runs on a win.</summary>
    public int TrashPoints { get; private set; }

    /// <summary>True once FinalizeRun has been told the run ended by depleting the garbage patch.</summary>
    public bool PatchDefeated { get; private set; }

    /// <summary>True once FinalizeRun has run — the multipliers and totals below are only meaningful after that.</summary>
    public bool IsFinalized { get; private set; }

    /// <summary>This run's modifier multiplier, or 1 if no GameModifierManager is present.</summary>
    public float ModifierMultiplier => GameModifierManager.Instance != null ? GameModifierManager.Instance.ScoreMultiplier : 1f;

    /// <summary>Raw points before the modifier multiplier.</summary>
    public int BaseScore => ResourcePoints + TrashPoints;
    /// <summary>Points the modifier multiplier alone earned, i.e. what the breakdown's Modifiers line shows.</summary>
    public int ModifierBonus => FinalScore - BaseScore;
    /// <summary>The run's actual score: base, times modifiers.</summary>
    public int FinalScore => Mathf.RoundToInt(BaseScore * ModifierMultiplier);

    public int HighScore { get; private set; }

    /// <summary>The player's lifetime XP as it stood when this run began, captured before FinalizeRun adds to it — what LevelUpPanelUI animates the progress bar up FROM.</summary>
    public int XpBeforeRun { get; private set; }

    /// <summary>XP this run earned, which is simply its FinalScore. 0 until FinalizeRun has run.</summary>
    public int XpAwarded { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        // Same snapshot-on-load idiom as the high score above, and needed for
        // the same reason: once FinalizeRun writes, the stored value is the
        // post-run one, and the end-of-run panel still needs the before.
        XpBeforeRun = PlayerProgression.TotalXp;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called once per resource unit a turtle delivers to the nest (see TurtleAgent.DeliverCarriedResources). No-ops once the nest is destroyed, so the score freezes at game over rather than still ticking up from deliveries already in flight that frame.</summary>
    public void AddResourceScore(int amount)
    {
        if (amount == 0 || IsRunOver()) return;

        ResourcesDelivered++;
        ResourcePoints += amount;
    }

    /// <summary>Called once per piece of trash destroyed (see TrashHealth.Die), with that trash type's own Rating-derived value. Same nest-destroyed guard as AddResourceScore.</summary>
    public void AddTrashScore(int amount)
    {
        if (amount == 0 || IsRunOver()) return;

        TrashDestroyed++;
        TrashPoints += amount;
    }

    private bool IsRunOver() => IsFinalized || (TurtleNest.Instance != null && TurtleNest.Instance.IsDestroyed);

    /// <summary>
    /// Closes out the run: folds the garbage-patch bonus into the trash bucket
    /// if the player won, locks scoring so nothing in flight can still change
    /// the totals the breakdown is about to animate, persists a new high score,
    /// and banks the run's score as XP. Called by GameOverUI before it shows
    /// the breakdown, on both the win and the loss path. Safe to call more than
    /// once — GameOverUI already guards against showing twice, but a double
    /// call here would otherwise award the patch bonus and the XP twice.
    /// </summary>
    public void FinalizeRun(bool patchDefeated)
    {
        if (IsFinalized) return;

        PatchDefeated = patchDefeated;
        if (patchDefeated) TrashPoints += patchDefeatedTrashBonus;

        IsFinalized = true;

        if (GameModifierManager.Instance == null)
        {
            Debug.LogWarning("ScoreManager: no GameModifierManager in the scene — modifier multiplier defaulting to x1.00. Add the component (and assign its Catalog) if modifiers are meant to score.");
        }

        Debug.Log($"ScoreManager: run finalized — resources {ResourcePoints} + trash {TrashPoints} (patch defeated: {PatchDefeated}) = base {BaseScore}, " +
                  $"x{ModifierMultiplier:0.00} modifiers = FINAL {FinalScore}.");

        if (FinalScore > HighScore)
        {
            HighScore = FinalScore;
            PlayerPrefs.SetInt(HighScoreKey, HighScore);
            PlayerPrefs.Save();
        }

        // Awarded unconditionally, unlike the high score — every run adds to
        // the lifetime total, not just a record-setting one.
        XpAwarded = FinalScore;
        PlayerProgression.AddXp(XpAwarded);
    }
}
