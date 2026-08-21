using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Lives in the Start Menu scene. Owns two screens under the same root: the
/// start screen (title, high score, tutorial toggle, Play button) and an
/// options screen shown over the top of it once Play is pressed —
/// ShowDifficultyScreen just activates DifficultyPanel rather than loading
/// anything yet, and deliberately leaves StartScreenPanel active so the title
/// screen stays visible behind it. Nothing reorders anything to make that
/// work: DifficultyPanel is already the later sibling in the Canvas, and
/// sibling order IS draw order for UI. The options screen hosts the scrollable
/// modifier list (see ModifierSelectionUI) and its own Play button wired to
/// StartGame, which is the actual trigger to load the gameplay scene. StartGame
/// hands the setup across the scene load through PlayerPrefs — this project has
/// no DontDestroyOnLoad objects, so that's the only channel. StartGame loads the
/// gameplay scene by name via SceneManager — both the menu scene and the
/// gameplay scene need to be added to Build Settings (File > Build Settings >
/// Add Open Scenes) for this to work in a built game, not just the Editor.
///
/// Two radio groups have been removed from the options screen over time, and
/// they were retired in DIFFERENT ways — worth knowing before adding a third
/// setting here. Game mode (Big Island/Cove/Archipelago) still has live readers
/// in IslandGenerator and GarbagePatchSpawner, so GameModeIndex is still
/// written, pinned to Cove (1), and written unconditionally on every StartGame
/// so a stale index from back when the buttons existed can't leak through and
/// silently change the island size. Difficulty (Easy/Medium/Hard) had every
/// reader deleted with it — DayStormCycle's trash presets and ScoreManager's
/// score multiplier are both gone — so "DifficultyIndex" is simply no longer
/// written or read by anything, and a value an older build left in PlayerPrefs
/// is inert rather than needing to be overwritten.
///
/// The all-time high score lives here rather than on the game-over screen: the
/// run's own score breakdown is what matters at the end of a run (see
/// ScoreBreakdownUI), while the number worth beating belongs where the player
/// decides how to play. Read straight out of PlayerPrefs since ScoreManager
/// only exists in the gameplay scene.
///
/// The tutorial toggle is a one-shot flag, not a persistent setting: checking
/// it arms exactly the next StartGame press to start with TutorialManager's
/// scripted intro (see TutorialManager, which reads and immediately clears
/// this same PlayerPrefs key in the gameplay scene). Synced from PlayerPrefs
/// in Start, defaulting to checked the very first time this ever runs (no
/// PlayerPrefs key yet) so a brand-new player gets the tutorial without
/// having to know to opt in; returning to this menu after that playthrough —
/// or after TutorialManager consumed the flag — shows the box unchecked
/// again, since the key now explicitly exists as 0 rather than being absent. It
/// lives on the start screen (independent of both toggle groups above, which
/// live on the options screen) but stays readable from there since hiding a
/// panel with SetActive doesn't reset its children's state.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    private const string TutorialEnabledKey = "TutorialEnabled";
    private const string GameModeIndexKey = "GameModeIndex";
    private const string HighScoreKey = "HighScore";

    /// <summary>Cove — the middle island-size/patch-health preset. See class doc comment: the game mode buttons are gone, but IslandGenerator and GarbagePatchSpawner still index their preset arrays by this key.</summary>
    private const int LockedGameModeIndex = 1;

    [Tooltip("Name of the gameplay scene to load when the options screen's Play button is pressed. Must exactly match that scene's file name and be added to Build Settings.")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [Tooltip("If checked when the options screen's Play button is pressed, the next playthrough starts with TutorialManager's scripted tutorial. One-shot — consumed by TutorialManager, not a persistent setting.")]
    [SerializeField] private Toggle tutorialToggle;

    [Tooltip("This scene's own screen-wipe panel. Left unassigned = falls back to an instant scene load.")]
    [SerializeField] private SceneTransitionPanel transitionPanel;

    [Tooltip("Shows the all-time high score on the start screen (moved here from the game-over screen). Set once in Start from PlayerPrefs — see class doc comment.")]
    [SerializeField] private TMP_Text highScoreText;
    [Tooltip("Format for High Score Text — {0} is the score.")]
    [SerializeField] private string highScoreFormat = "High Score: {0}";

    [Header("Screens")]
    [Tooltip("Root of the start screen (title, tutorial toggle, Play button) — hidden while the options screen is up.")]
    [SerializeField] private GameObject startScreenPanel;
    [Tooltip("Root of the options screen (the modifier list and its own Play button) — hidden until the start screen's Play is pressed. Still called DifficultySelect in the scene from back when it held the difficulty buttons; the object is fine, only the name is a leftover.")]
    [SerializeField] private GameObject difficultyPanel;
    [Tooltip("The options screen's own UISlidePanel, so Back slides it away instead of popping it out of existence — it's only deactivated once it's actually off-screen. The DifficultySelect object already has this component; just drag it in. Leave unassigned to fall back to an instant hide.")]
    [SerializeField] private UISlidePanel difficultyPanelSlide;

    [Header("Options")]
    [Tooltip("The options screen's scrollable modifier list. Whichever modifiers are ticked there are handed to the gameplay scene on Play.")]
    [SerializeField] private ModifierSelectionUI modifierSelection;

    private void Start()
    {
        if (tutorialToggle != null) tutorialToggle.isOn = PlayerPrefs.GetInt(TutorialEnabledKey, 1) == 1;
        if (highScoreText != null) highScoreText.text = string.Format(highScoreFormat, PlayerPrefs.GetInt(HighScoreKey, 0));

        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (startScreenPanel != null) startScreenPanel.SetActive(true);

        // Back falls back to an instant hide without this, which looks like the
        // slide is broken rather than unwired — so say so once at load instead
        // of leaving it to be discovered by pressing the button.
        if (difficultyPanel != null && difficultyPanelSlide == null)
        {
            Debug.LogWarning("MainMenuController: no Difficulty Panel Slide assigned, so Back will hide the options screen instantly instead of sliding it off. Drag the UISlidePanel component that's already on the DifficultySelect object into that field.", this);
        }
    }

    /// <summary>Wire this up to the start screen's Play button OnClick() in the Inspector. The start screen is deliberately left ACTIVE underneath — the options screen is a later sibling in the Canvas, so it simply draws over the top rather than the two swapping places.</summary>
    public void ShowDifficultyScreen()
    {
        if (difficultyPanel != null) difficultyPanel.SetActive(true);
    }

    /// <summary>
    /// Wire this up to the options screen's Back button OnClick() in the
    /// Inspector. Slides the options screen away, revealing the start screen
    /// that was sitting behind it the whole time — nothing needs re-activating
    /// here, because ShowDifficultyScreen never hid it (see its doc comment).
    ///
    /// Deactivating is deferred to the slide's completion callback rather than
    /// done up front, or the panel would vanish instantly and the slide-out
    /// would never be seen. Pressing Play again re-activates it, and
    /// UISlidePanel's own Play In On Enable slides it straight back in.
    /// </summary>
    public void HideDifficultyScreen()
    {
        if (difficultyPanel == null) return;

        if (difficultyPanelSlide != null)
        {
            difficultyPanelSlide.PlayOut(() => difficultyPanel.SetActive(false));
        }
        else
        {
            difficultyPanel.SetActive(false);
        }
    }

    /// <summary>Wire this up to the options screen's own Play button OnClick() in the Inspector — not the modifier tiles, which only select (see ModifierTileView).</summary>
    public void StartGame()
    {
        PlayerPrefs.SetInt(TutorialEnabledKey, tutorialToggle != null && tutorialToggle.isOn ? 1 : 0);
        PlayerPrefs.SetInt(GameModeIndexKey, LockedGameModeIndex);
        PlayerPrefs.Save();

        if (modifierSelection == null)
        {
            Debug.LogWarning("MainMenuController: no Modifier Selection assigned — this run starts with NO modifiers no matter what was ticked. Assign the ModifierSelectionUI component to this field in the Inspector.");
        }

        // Saves (and flushes) on its own — deliberately after the block above
        // rather than folded into it, since the id list is a string key with
        // its own encoding owned by GameModifierSelection.
        List<string> selectedIds = modifierSelection != null ? modifierSelection.SelectedIds : null;
        GameModifierSelection.SaveIds(selectedIds);

        Debug.Log($"MainMenuController: starting run — {(selectedIds != null ? selectedIds.Count : 0)} modifier(s) selected.");

        if (transitionPanel != null)
        {
            transitionPanel.PlayCoverAndLoadScene(gameplaySceneName);
        }
        else
        {
            Debug.LogWarning("MainMenuController: no Transition Panel assigned — loading instantly with no wipe.");
            SceneManager.LoadScene(gameplaySceneName);
        }
    }
}
