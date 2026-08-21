using UnityEngine;

/// <summary>
/// The player's permanent, cross-run progress: how much XP they've earned in
/// total across every playthrough, and how far the menu has got through
/// announcing the modifier unlocks that XP has bought.
///
/// Static and PlayerPrefs-backed for exactly the same reasons as
/// GameModifierSelection: PlayerPrefs is this project's only cross-scene
/// channel (there are no DontDestroyOnLoad objects anywhere), and holding both
/// the keys and their meaning in one place is what stops the writer
/// (ScoreManager, at the end of a run) and the readers (LevelUpPanelUI,
/// MenuProgressionUI, ModifierSelectionUI) from drifting apart on either.
///
/// Only the raw XP total is stored — never the level. Level, progress into the
/// current level, and which modifiers are unlocked are all DERIVED from this
/// number by ProgressionCatalog, so retuning the XP curve retroactively
/// re-levels an existing save instead of leaving a stored level number
/// disagreeing with the curve that produced it. XP is cumulative and never
/// spent; unlocking a modifier costs nothing, it just happens as the total
/// crosses a threshold.
///
/// The level math itself deliberately lives on ProgressionCatalog rather than
/// here, because it needs the authored XP curve and the modifier list, both of
/// which are serialized data on a prefab asset that a static class can't reach.
/// </summary>
public static class PlayerProgression
{
    private const string TotalXpKey = "PlayerTotalXp";
    private const string AnnouncedLevelKey = "AnnouncedUnlockLevel";
    private const string TutorialCompletedKey = "TutorialCompleted";

    /// <summary>
    /// True once the player has finished the scripted tutorial at least once,
    /// ever. Gates the menu's modifier grid (see ModifierSelectionUI) so a
    /// brand-new player meets the base game before being offered ways to bend
    /// it.
    ///
    /// Deliberately NOT the same thing as the "TutorialEnabled" key
    /// MainMenuController writes and TutorialManager consumes. That one is a
    /// one-shot request — "run the tutorial on the next playthrough" — cleared
    /// the moment the tutorial starts, so it says nothing about whether the
    /// player ever saw it through. This is the permanent record, written only
    /// once the sequence has actually been completed, and it lives here with
    /// the rest of the player's cross-run progress for the same reason the XP
    /// total does: one writer in the gameplay scene, one reader in the menu,
    /// and PlayerPrefs the only channel between them.
    /// </summary>
    public static bool HasCompletedTutorial
    {
        get => PlayerPrefs.GetInt(TutorialCompletedKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(TutorialCompletedKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Lifetime XP across every run ever played. 0 on a fresh install. Feed this to ProgressionCatalog to get a level out of it.</summary>
    public static int TotalXp => PlayerPrefs.GetInt(TotalXpKey, 0);

    /// <summary>
    /// The highest level whose "Modifier Unlocked" popup the main menu has
    /// already shown. Unlike TutorialEnabled — the other one-shot flag in this
    /// project — this isn't a bool that gets cleared, because the player can
    /// gain several levels in a single run: storing the level itself lets
    /// MenuProgressionUI work out exactly which unlocks it still owes the
    /// player and show them all, rather than losing every one but the last.
    /// </summary>
    public static int AnnouncedLevel
    {
        get => PlayerPrefs.GetInt(AnnouncedLevelKey, 0);
        set
        {
            PlayerPrefs.SetInt(AnnouncedLevelKey, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Banks a run's XP. Called once per run by ScoreManager.FinalizeRun, which
    /// is already guarded against running twice — and negative or zero amounts
    /// are ignored here anyway, since XP is only ever earned and a run that
    /// somehow scored nothing shouldn't churn a PlayerPrefs write.
    ///
    /// Flushes immediately for the same reason GameModifierSelection.SaveIds
    /// does: the very next thing that happens after a run ends is usually a
    /// scene load, or the player quitting outright.
    /// </summary>
    public static void AddXp(int amount)
    {
        if (amount <= 0) return;

        int total = TotalXp + amount;
        PlayerPrefs.SetInt(TotalXpKey, total);
        PlayerPrefs.Save();

        Debug.Log($"PlayerProgression: +{amount} XP, lifetime total now {total}.");
    }

    /// <summary>Wipes all progress back to a brand-new player: level 0, nothing unlocked, and the tutorial un-completed so the modifier grid is gated again. Purely for testing — reach it from MenuProgressionUI's context menu, or Tools > Turtle Beach in the Editor menu bar.</summary>
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(TotalXpKey);
        PlayerPrefs.DeleteKey(AnnouncedLevelKey);
        PlayerPrefs.DeleteKey(TutorialCompletedKey);
        PlayerPrefs.Save();

        Debug.Log("PlayerProgression: progress reset — back to level 0, no modifiers unlocked, tutorial un-completed.");
    }
}
