using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One member of an ExclusiveToggleGroup radio group (e.g. one of three
/// game-mode buttons, or one of three island-size buttons) — clicking it
/// selects it and stays visually "pressed" (MenuButtonVisuals' Pressed
/// Sprite) until another member of the same group is clicked, at which
/// point this one reverts to Unpressed Sprite. Subclasses MenuButtonVisuals
/// purely to reuse its sprite pair/hover-tint fields and hover tracking —
/// overriding IsActive to key off a persistent Selected flag (driven by the
/// group, not by this button's own pointer-down/up) rather than momentary
/// press-while-held. Selecting a member currently has no gameplay effect;
/// SelectedIndex on the group is the hook for whenever actual
/// game-mode/island-size behavior gets built.
/// </summary>
public class ExclusiveToggleButton : MenuButtonVisuals, IPointerClickHandler
{
    [Tooltip("Group this button belongs to — clicking this button asks the group to select it (and deselect every other member). Leave unassigned for a standalone toggle with no group exclusivity.")]
    [SerializeField] private ExclusiveToggleGroup group;

    private bool isSelected;

    public bool IsSelected => isSelected;

    protected override bool IsActive => isSelected;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (group != null) group.Select(this);
        else SetSelected(true);
    }

    /// <summary>Called by ExclusiveToggleGroup to actually change this button's persistent state — never call this directly on a grouped button, or the group won't find out and won't deselect the others.</summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        Refresh();
    }
}
