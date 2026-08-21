using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one place the player's chosen modifiers cross the Menu -> GameScene
/// boundary. PlayerPrefs is this project's only cross-scene channel (there are
/// no DontDestroyOnLoad objects anywhere — see SceneTransitionPanel's doc
/// comment), and this static holds both the key and the encoding so the writer
/// (ModifierSelectionUI, via MainMenuController.StartGame) and the reader
/// (GameModifierManager) can't drift apart on either.
///
/// Unlike the "TutorialEnabled" flag, this is deliberately NOT consumed on
/// read: GameOverUI.Restart() is a plain scene reload, so leaving the value in
/// place is what makes a restart keep the same modifier set instead of
/// silently dropping to an unmodified run. It's likewise re-read by the menu
/// on the way back, so returning to the menu shows the last run's picks still
/// ticked.
/// </summary>
public static class GameModifierSelection
{
    private const string SelectedModifiersKey = "SelectedModifiers";
    private const char Separator = ',';

    /// <summary>Reads back the ids saved by Save, or an empty list on a fresh install / an unmodified run. Ids are returned raw — the caller resolves them through GameModifierCatalog and skips any that no longer exist.</summary>
    public static List<string> LoadIds()
    {
        List<string> ids = new List<string>();

        string raw = PlayerPrefs.GetString(SelectedModifiersKey, string.Empty);
        Debug.Log($"GameModifierSelection: loaded \"{SelectedModifiersKey}\" = \"{raw}\"");
        if (string.IsNullOrEmpty(raw)) return ids;

        foreach (string id in raw.Split(Separator))
        {
            string trimmed = id.Trim();
            if (!string.IsNullOrEmpty(trimmed)) ids.Add(trimmed);
        }

        return ids;
    }

    /// <summary>Writes the selected ids and flushes immediately, since the very next thing the caller does is load another scene. An empty collection writes an empty string rather than deleting the key, so a player who deselects everything gets a genuinely unmodified run instead of falling back to whatever was saved last time.</summary>
    public static void SaveIds(IEnumerable<string> ids)
    {
        string raw = ids != null ? string.Join(Separator.ToString(), ids) : string.Empty;
        PlayerPrefs.SetString(SelectedModifiersKey, raw);
        PlayerPrefs.Save();
        Debug.Log($"GameModifierSelection: saved \"{SelectedModifiersKey}\" = \"{raw}\"");
    }
}
