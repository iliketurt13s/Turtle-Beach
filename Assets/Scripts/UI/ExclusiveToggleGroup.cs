using System;
using UnityEngine;

/// <summary>
/// Radio-button grouping for ExclusiveToggleButton — one instance per
/// mutually-exclusive set (e.g. the three game-mode buttons, separately the
/// three island-size buttons). Selecting any member deselects every other
/// member in Members; nothing else reads SelectedIndex yet, but it's the
/// eventual hook for whenever actual game-mode/island-size logic exists.
/// Re-applies Default Selected Index every time this group's screen becomes
/// active (OnEnable) rather than remembering a previous session's pick.
/// </summary>
public class ExclusiveToggleGroup : MonoBehaviour
{
    [SerializeField] private ExclusiveToggleButton[] members;
    [Tooltip("Index into Members selected by default whenever this group becomes active. -1 = none selected until the player picks one. Defaults to 1 (the middle option of the usual 3-button groups) — set explicitly if a group has a different size or preferred default.")]
    [SerializeField] private int defaultSelectedIndex = 1;

    private ExclusiveToggleButton selected;

    /// <summary>Index of the currently selected member within Members, or -1 if none.</summary>
    public int SelectedIndex => selected != null ? Array.IndexOf(members, selected) : -1;

    private void OnEnable()
    {
        if (defaultSelectedIndex >= 0 && defaultSelectedIndex < members.Length)
        {
            Select(members[defaultSelectedIndex]);
        }
    }

    public void Select(ExclusiveToggleButton button)
    {
        if (selected == button) return;

        if (selected != null) selected.SetSelected(false);
        selected = button;
        if (selected != null) selected.SetSelected(true);
    }
}
