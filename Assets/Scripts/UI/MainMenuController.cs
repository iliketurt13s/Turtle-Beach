using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Lives in the Start Menu scene. Owns two screens under the same root: the
/// start screen (title, tutorial toggle, Play button) and an options screen
/// shown in its place once Play is pressed — ShowDifficultyScreen swaps
/// StartScreenPanel/DifficultyPanel's active state rather than loading
/// anything yet. The options screen hosts three independent ExclusiveToggleGroup
/// radio groups (difficulty, game mode, island size — see that class) plus its
/// own Play button, wired to StartGame, which is the actual trigger to load
/// the gameplay scene. Difficulty and game mode now have real effects on the
/// far side of the scene load: StartGame writes each group's SelectedIndex
/// into its own one-shot PlayerPrefs key (mirroring TutorialEnabledKey below),
/// read once by DayStormCycle (difficulty — trash budget/growth presets) and
/// by IslandGenerator/GarbagePatchSpawner (game mode — island size and rounds
/// before the island changes). Island size is still unwired, a future hookup.
/// StartGame loads the gameplay scene by name via SceneManager — both the
/// menu scene and the gameplay scene need to be added to Build Settings
/// (File > Build Settings > Add Open Scenes) for this to work in a built
/// game, not just the Editor.
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
    private const string DifficultyIndexKey = "DifficultyIndex";
    private const string GameModeIndexKey = "GameModeIndex";

    [Tooltip("Name of the gameplay scene to load when the options screen's Play button is pressed. Must exactly match that scene's file name and be added to Build Settings.")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [Tooltip("If checked when the options screen's Play button is pressed, the next playthrough starts with TutorialManager's scripted tutorial. One-shot — consumed by TutorialManager, not a persistent setting.")]
    [SerializeField] private Toggle tutorialToggle;

    [Tooltip("This scene's own screen-wipe panel. Left unassigned = falls back to an instant scene load.")]
    [SerializeField] private SceneTransitionPanel transitionPanel;

    [Header("Screens")]
    [Tooltip("Root of the start screen (title, tutorial toggle, Play button) — hidden while the options screen is up.")]
    [SerializeField] private GameObject startScreenPanel;
    [Tooltip("Root of the options screen (difficulty + game mode + island size toggle groups, and its own Play button) — hidden until the start screen's Play is pressed.")]
    [SerializeField] private GameObject difficultyPanel;

    [Header("Options")]
    [Tooltip("Easy/Medium/Hard radio group. Index order (0/1/2) must match DayStormCycle's Difficulty Presets array in the gameplay scene.")]
    [SerializeField] private ExclusiveToggleGroup difficultyGroup;
    [Tooltip("Big Island/Cove/Archipelago radio group. Index order (0/1/2) must match IslandGenerator's Game Mode Size Presets and GarbagePatchSpawner's Rounds Per Island By Mode arrays in the gameplay scene.")]
    [SerializeField] private ExclusiveToggleGroup gameModeGroup;

    private void Start()
    {
        if (tutorialToggle != null) tutorialToggle.isOn = PlayerPrefs.GetInt(TutorialEnabledKey, 1) == 1;

        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (startScreenPanel != null) startScreenPanel.SetActive(true);
    }

    /// <summary>Wire this up to the start screen's Play button OnClick() in the Inspector.</summary>
    public void ShowDifficultyScreen()
    {
        if (startScreenPanel != null) startScreenPanel.SetActive(false);
        if (difficultyPanel != null) difficultyPanel.SetActive(true);
    }

    /// <summary>Wire this up to the options screen's own Play button OnClick() in the Inspector — not the game-mode/island-size buttons, which only select (see ExclusiveToggleButton/ExclusiveToggleGroup).</summary>
    public void StartGame()
    {
        PlayerPrefs.SetInt(TutorialEnabledKey, tutorialToggle != null && tutorialToggle.isOn ? 1 : 0);
        PlayerPrefs.SetInt(DifficultyIndexKey, difficultyGroup != null ? difficultyGroup.SelectedIndex : -1);
        PlayerPrefs.SetInt(GameModeIndexKey, gameModeGroup != null ? gameModeGroup.SelectedIndex : -1);
        PlayerPrefs.Save();

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
