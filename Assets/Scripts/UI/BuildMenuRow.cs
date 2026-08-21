using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One buildable's entry in the build drawer's list — an icon the player can
/// click to select that building, as an alternative to scrolling to it. One of
/// these is instantiated per UNLOCKED buildable by BuildSelectionUI, which owns
/// positioning and the inflate animation; this component is just the icon, the
/// click, and the link back to which entry of BuildModeController.Buildables it
/// stands for.
///
/// Holds no state of its own beyond that link, so BuildSelectionUI is free to
/// destroy and rebuild the whole list whenever the unlocked set changes without
/// anything here needing to be preserved.
/// </summary>
public class BuildMenuRow : MonoBehaviour
{
    [Tooltip("The building's icon. Leave unassigned to use the first Image found in this prefab's children — assign it explicitly if the row has a background/frame Image that would otherwise be picked up instead.")]
    [SerializeField] private Image iconImage;
    [Tooltip("Button that selects this buildable. Leave unassigned to use one on this same GameObject.")]
    [SerializeField] private Button button;

    private BuildSelectionUI owner;
    private RectTransform rect;
    private CanvasGroup canvasGroup;

    /// <summary>Index into BuildModeController.Buildables that this row stands for — what gets selected when it's clicked. -1 until Bind runs.</summary>
    public int BuildableIndex { get; private set; } = -1;

    /// <summary>Resolved lazily rather than in Awake: a row is bound immediately after Instantiate, and Awake never runs at all for a prefab authored inactive.</summary>
    public RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

    /// <summary>
    /// This row's opacity, so BuildSelectionUI can fade the buildings that
    /// aren't selected. Driven through a CanvasGroup rather than the icon
    /// Image's own colour so the whole row fades together — a frame, a
    /// backing plate, a label added to the prefab later all come along without
    /// anything here needing to know they exist.
    ///
    /// The CanvasGroup is added automatically if the prefab doesn't already
    /// carry one, so there's nothing to remember to wire and no half-faded row
    /// if someone builds a new prefab and forgets. A CanvasGroup below full
    /// alpha still takes clicks, so a faded row stays selectable.
    /// </summary>
    public float Alpha
    {
        get => Group.alpha;
        set => Group.alpha = Mathf.Clamp01(value);
    }

    private CanvasGroup Group
    {
        get
        {
            if (canvasGroup != null) return canvasGroup;

            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            return canvasGroup;
        }
    }

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    /// <summary>Points this row at one buildable. Called once right after Instantiate; a row is never re-bound to a different buildable, the list is rebuilt instead.</summary>
    public void Bind(BuildSelectionUI selectionUI, int buildableIndex, Sprite icon)
    {
        owner = selectionUI;
        BuildableIndex = buildableIndex;

        if (iconImage == null) iconImage = GetComponentInChildren<Image>();
        if (iconImage == null) return;

        iconImage.sprite = icon;
        // Disabled rather than left showing a blank white quad if a buildable
        // has no sprite anywhere in its prefab (see BuildableDefinition.Icon).
        iconImage.enabled = icon != null;
    }

    private void HandleClick()
    {
        if (owner != null) owner.SelectRow(this);
    }
}
