using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One modifier in the menu's pick grid, laid out four to a row. The tile is
/// nothing but its own button face: each modifier authors an unpressed and a
/// pressed sprite that already include both the artwork and its background, so
/// Bind pushes that pair straight into MenuButtonVisuals and there's no
/// separate icon child to composite on top. Instantiated once per catalog
/// entry by ModifierSelectionUI, all clones of one shared prefab — which is
/// exactly why the sprite pair lives on the MODIFIER rather than on this
/// prefab.
///
/// Carries no text either: hovering (or clicking) a tile pushes that
/// modifier's name, description and multiplier bonus into the shared detail
/// panel beside the grid, exactly the way UpgradeCardView feeds the shared
/// name/description text on UpgradeSelectionUI rather than every card
/// carrying its own tooltip. That keeps the grid readable at icon size no
/// matter how many modifiers eventually exist.
///
/// The multi-select sibling of ExclusiveToggleButton: both subclass
/// MenuButtonVisuals purely to reuse its sprite pair, hover tint and hover
/// tracking, and both override IsActive so the "Pressed" sprite means
/// persistently selected rather than momentarily held. The difference is what
/// a click does — ExclusiveToggleButton hands off to a group that deselects
/// every sibling (radio behavior, used for difficulty), whereas this toggles
/// its own flag and leaves every other tile alone, since the player may take
/// as many modifiers at once as they like.
///
/// Needs an Image on this same GameObject (MenuButtonVisuals requires one) —
/// that Image is the tile, and it's also what makes the whole tile a click and
/// hover target.
///
/// A tile can also be LOCKED: the modifier is in the catalog but the player's
/// level hasn't reached it yet (see ProgressionCatalog). Locked tiles hold
/// their place in the grid rather than being filtered out — they wear the lock
/// overlay, take MenuButtonVisuals' disabled tint, and refuse to toggle.
/// Hovering one still calls through to the detail panel, but only so it can
/// report the level that unlocks it: ShowDetails withholds the name,
/// description and score bonus of anything locked, so the tile reveals that
/// something is there without revealing what.
/// </summary>
public class ModifierTileView : MenuButtonVisuals, IPointerClickHandler
{
    [Tooltip("Lock graphic drawn over this tile while its modifier is still locked. Leave it INACTIVE on the prefab — it's switched on per tile in Bind. Uncheck its Raycast Target so it doesn't sit between the pointer and the tile.")]
    [SerializeField] private GameObject lockOverlay;

    private GameModifierDefinition boundModifier;
    private ModifierSelectionUI owner;
    private bool isSelected;
    private bool isLocked;

    public GameModifierDefinition Modifier => boundModifier;
    public bool IsSelected => isSelected;

    /// <summary>True while the player's level is below this modifier's unlock level. A locked tile can never be selected, so it can never reach the saved id list either.</summary>
    public bool IsLocked => isLocked;

    protected override bool IsActive => isSelected;

    /// <summary>Called by ModifierSelectionUI right after instantiating this tile. startSelected restores a pick carried over from the last run (see GameModifierSelection) rather than always starting unticked; locked suppresses that restore entirely.</summary>
    public void Bind(GameModifierDefinition modifier, ModifierSelectionUI owner, bool startSelected, bool locked)
    {
        boundModifier = modifier;
        this.owner = owner;
        isLocked = locked;
        // Forced off rather than taken on trust: a selection saved by a build
        // where this was unlocked — or saved before a progress reset — must not
        // survive as a ticked-but-locked tile that then scores for the run.
        isSelected = !locked && startSelected;

        // Nulls fall back to whatever pair the prefab itself carries, so a
        // modifier whose art isn't drawn yet shows a generic tile instead of
        // an empty one.
        SetSprites(modifier.UnpressedSprite, modifier.PressedSprite);

        if (lockOverlay != null) lockOverlay.SetActive(locked);
        SetInteractable(!locked);

        // The click juice is a separate component listening for the same
        // pointer event, so ignoring the click below isn't enough on its own —
        // a locked tile would still squash invitingly under a click that does
        // nothing.
        UIClickSquash clickSquash = GetComponent<UIClickSquash>();
        if (clickSquash != null) clickSquash.enabled = !locked;

        Refresh();
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        // Locked tiles included, but see ShowDetails — all a locked one gets to
        // say is which level unlocks it.
        if (boundModifier != null) owner?.ShowDetails(boundModifier);
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        owner?.EndHoverDetails();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (boundModifier == null || isLocked) return;

        isSelected = !isSelected;
        Refresh();
        owner?.HandleTileToggled(this);
    }
}
