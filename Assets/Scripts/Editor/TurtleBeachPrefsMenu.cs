using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only menu items under Tools > Turtle Beach for inspecting and wiping
/// the PlayerPrefs this game stores. Everything that survives between runs goes
/// through PlayerPrefs — it's the only cross-scene channel this project has, as
/// there are no DontDestroyOnLoad objects anywhere — so "start over as a
/// brand-new player" means clearing keys, and doing that by hand is fiddly
/// enough that it tends not to get done while testing.
///
/// Lives under an Editor folder, which Unity compiles into an editor-only
/// assembly and strips from builds. That's required, not stylistic: UnityEditor
/// doesn't exist in a player, so this file would break the build anywhere else.
///
/// The key list below is a mirror, not the source of truth — each key is owned
/// by the class that reads and writes it (PlayerProgression, GameModifierSelection, GameSettings,
/// MainMenuController, ScoreManager). It exists so Log Current Values can print
/// something readable, and the reset items deliberately call
/// PlayerPrefs.DeleteAll rather than walking it, so a key added later is still
/// wiped by a reset even if nobody remembers to list it here.
/// </summary>
public static class TurtleBeachPrefsMenu
{
    /// <summary>Display-name/key pairs for the readout only — see the class doc comment on why the reset paths don't use this.</summary>
    private static readonly (string Label, string Key)[] KnownKeys =
    {
        ("Lifetime XP", "PlayerTotalXp"),
        ("Announced unlock level", "AnnouncedUnlockLevel"),
        ("Tutorial completed (gates modifiers)", "TutorialCompleted"),
        ("Tutorial armed for next run", "TutorialEnabled"),
        ("High score", "HighScore"),
        ("Game mode index", "GameModeIndex"),
        ("Selected modifiers", "SelectedModifiers"),
        ("Camera drag speed", "SettingCameraDragSpeed"),
        ("Camera zoom speed", "SettingCameraZoomSpeed"),
        ("Music volume", "SettingMusicVolume"),
        ("Sound effects volume", "SettingSfxVolume"),
    };

    [MenuItem("Tools/Turtle Beach/Log Current PlayerPrefs", priority = 0)]
    private static void LogCurrentValues()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder("Turtle Beach PlayerPrefs:\n");

        foreach ((string label, string key) in KnownKeys)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                builder.AppendLine($"  {label} ({key}): <not set>");
                continue;
            }

            builder.AppendLine($"  {label} ({key}): {ReadAnyType(key)}");
        }

        Debug.Log(builder.ToString());
    }

    /// <summary>
    /// Best-effort readable value for a key whose type isn't known here. No
    /// typed getter tells you what a key holds, and asking for the wrong type
    /// either throws or quietly hands back the default depending on the
    /// platform's backend — so every getter is tried in turn (string, then the
    /// two numeric ones, floats before ints since the settings keys are floats)
    /// and anything that comes back empty or zero falls through to the next.
    /// A genuine 0 therefore prints as 0 either way, which is all this readout
    /// needs.
    /// </summary>
    private static string ReadAnyType(string key)
    {
        try
        {
            string asString = PlayerPrefs.GetString(key, string.Empty);
            if (!string.IsNullOrEmpty(asString)) return asString;
        }
        catch (System.Exception) { }

        try
        {
            float asFloat = PlayerPrefs.GetFloat(key, 0f);
            if (asFloat != 0f) return asFloat.ToString("0.###");
        }
        catch (System.Exception) { }

        try
        {
            return PlayerPrefs.GetInt(key, 0).ToString();
        }
        catch (System.Exception)
        {
            return "<unreadable>";
        }
    }

    [MenuItem("Tools/Turtle Beach/Reset ALL PlayerPrefs", priority = 20)]
    private static void ResetAll()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Reset all PlayerPrefs?",
            "Wipes EVERY PlayerPrefs key for this project — lifetime XP and level, unlocked modifiers, tutorial state, high score, and menu selections. The game will behave exactly as it does on a fresh install.\n\nThis cannot be undone.",
            "Reset everything",
            "Cancel");

        if (!confirmed) return;

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        // GameSettings caches its values in statics (they sit on hot paths), so
        // a Play session running right now would otherwise carry on using the
        // settings that were just deleted.
        GameSettings.Reload();

        Debug.Log("Turtle Beach: all PlayerPrefs deleted — the next Play is a brand-new player.");
    }

    [MenuItem("Tools/Turtle Beach/Reset Settings Only", priority = 22)]
    private static void ResetSettingsOnly()
    {
        // Defers to the owning class for the same reason Reset Progression Only
        // does — it knows what the settings consist of and what their defaults
        // are, and it updates its own cache on the way through.
        GameSettings.ResetToDefaults();
        Debug.Log("Turtle Beach: camera and volume settings reset to their defaults.");
    }

    [MenuItem("Tools/Turtle Beach/Reset Progression Only", priority = 21)]
    private static void ResetProgressionOnly()
    {
        // Defers to the owning class rather than deleting keys here, so this
        // can't fall out of step with what progression actually consists of.
        PlayerProgression.ResetProgress();
    }

    [MenuItem("Tools/Turtle Beach/Mark Tutorial Completed", priority = 40)]
    private static void MarkTutorialCompleted()
    {
        PlayerProgression.HasCompletedTutorial = true;
        Debug.Log("Turtle Beach: tutorial marked completed — the menu's modifier grid is unlocked.");
    }

    [MenuItem("Tools/Turtle Beach/Mark Tutorial NOT Completed", priority = 41)]
    private static void MarkTutorialNotCompleted()
    {
        PlayerProgression.HasCompletedTutorial = false;
        Debug.Log("Turtle Beach: tutorial marked not completed — the menu's modifier grid is locked again.");
    }
}
