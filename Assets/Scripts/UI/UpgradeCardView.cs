using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One upgrade card slot: an icon and a select button. Hovering the icon
/// shows this card's name and description via the owning UpgradeSelectionUI
/// (shared text elements, not per-card tooltips); clicking either the icon or
/// Select applies it.
///
/// Branch cards — ones that only entered the draw pool because something they
/// build on is already owned — additionally get a small badge spawned over
/// their icon (see UpdateBranchBadge), so "this improves a thing you have"
/// reads at a glance rather than only from the hovered description.
/// </summary>
public class UpgradeCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Button selectButton;

    [Header("Branch Badge")]
    [Tooltip("Spawned over the icon of any card that's an improvement to something already owned — a building-branch card (IRequiresBuilding, e.g. a Campfire upgrade) or an upgrade-branch one (IRequiresUpgrade, e.g. a Barnacle improvement). Instantiated once on first need and then just shown/hidden as slots are reused. Leave unassigned for no badge.")]
    [SerializeField] private GameObject branchBadgePrefab;
    [Tooltip("Badge position relative to the TOP-LEFT corner of the icon, which is where (0,0) puts it. +X moves it right, +Y moves it up — so a small positive X with a small negative Y tucks it just inside the icon's corner.")]
    [SerializeField] private Vector2 branchBadgeOffset = Vector2.zero;

    private UpgradeCardDefinition boundCard;
    private UpgradeSelectionUI owner;
    private GameObject branchBadgeInstance;

    /// <summary>Called by UpgradeSelectionUI each time this slot is (re)used for a newly drawn card.</summary>
    public void Bind(UpgradeCardDefinition card, UpgradeSelectionUI owner)
    {
        boundCard = card;
        this.owner = owner;

        if (iconImage != null) iconImage.sprite = card.Icon;
        UpdateBranchBadge(card);

        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(HandleSelectClicked);
        }
    }

    /// <summary>
    /// Shows the badge for a card that only became drawable because a
    /// prerequisite was already earned, and hides it for a card available from
    /// the start. Both gate interfaces count, since both mean the same thing to
    /// a player — IRequiresBuilding is "you built the thing this improves"
    /// (its prerequisite is itself an upgrade card, the building unlock) and
    /// IRequiresUpgrade is "you took the upgrade this improves". Asking the
    /// interfaces rather than keeping a separate flag on the card means a new
    /// branch card is badged automatically by virtue of being gated at all,
    /// with nothing extra to remember to tick on its prefab.
    ///
    /// The badge is created lazily on first need and then reused: slots are
    /// re-bound every storm, so instantiating per bind would churn objects for
    /// the whole run.
    /// </summary>
    private void UpdateBranchBadge(UpgradeCardDefinition card)
    {
        bool isBranchCard = card is IRequiresBuilding || card is IRequiresUpgrade;

        if (isBranchCard && branchBadgeInstance == null) branchBadgeInstance = CreateBranchBadge();
        if (branchBadgeInstance != null) branchBadgeInstance.SetActive(isBranchCard);
    }

    private GameObject CreateBranchBadge()
    {
        if (branchBadgePrefab == null || iconImage == null) return null;

        GameObject badge = Instantiate(branchBadgePrefab, iconImage.rectTransform);
        badge.name = branchBadgePrefab.name;
        badge.transform.localScale = Vector3.one;

        // Pinned to the icon's top-left corner regardless of how the icon is
        // sized or anchored, so the offset above stays meaningful if the card
        // art is ever resized.
        //
        // Tested rather than cast: a prefab built as a plain sprite (Create >
        // 2D Object) has an ordinary Transform, and a hard cast threw an
        // InvalidCastException that took the whole upgrade screen down with it.
        // A badge really does need to be a UI object to render over a Screen
        // Space canvas at all, so this warns rather than silently half-working.
        if (badge.transform is RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = branchBadgeOffset;
        }
        else
        {
            Debug.LogWarning($"UpgradeCardView: Branch Badge Prefab \"{branchBadgePrefab.name}\" has no RectTransform, so it can't anchor to the icon's corner and won't draw over the card. Rebuild it as a UI object (GameObject > UI > Image) rather than a sprite.", this);
            badge.transform.localPosition = branchBadgeOffset;
        }

        // Purely decorative: without this the badge would be the raycast hit
        // over its own corner, and while the click would still bubble up to
        // this component, it's cleaner not to have it intercepting anything.
        Graphic graphic = badge.GetComponent<Graphic>();
        if (graphic != null) graphic.raycastTarget = false;

        return badge;
    }

    private void HandleSelectClicked()
    {
        owner?.Select(this, boundCard);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner == null || boundCard == null) return;

        owner.ShowName(boundCard.DisplayName);
        owner.ShowDescription(boundCard.Description);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HideName();
        owner?.HideDescription();
    }

    /// <summary>Fires for a click anywhere on this card's own Graphic (the icon — see OnPointerEnter/Exit, which already key off the same area for hover), so clicking the icon selects this card exactly like clicking Select does. Select's own Button.onClick keeps working independently (it's a separate event path), so both remain valid ways to choose a card.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        HandleSelectClicked();
    }
}
