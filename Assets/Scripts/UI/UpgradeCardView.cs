using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One upgrade card slot: an icon and a select button. Hovering the icon
/// shows this card's name and description via the owning UpgradeSelectionUI
/// (shared text elements, not per-card tooltips); clicking either the icon or
/// Select applies it.
/// </summary>
public class UpgradeCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Button selectButton;

    private UpgradeCardDefinition boundCard;
    private UpgradeSelectionUI owner;
    private bool isHazard;

    /// <summary>Called by UpgradeSelectionUI each time this slot is (re)used for a newly drawn card. isHazard flags whether card came from the hazard pool (ShowHazardChoice) rather than the normal helpful one, so hovering tints the shared name/description text red instead of white — see OnPointerEnter.</summary>
    public void Bind(UpgradeCardDefinition card, UpgradeSelectionUI owner, bool isHazard)
    {
        boundCard = card;
        this.owner = owner;
        this.isHazard = isHazard;

        if (iconImage != null) iconImage.sprite = card.Icon;

        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(HandleSelectClicked);
        }
    }

    private void HandleSelectClicked()
    {
        owner?.Select(this, boundCard);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner == null || boundCard == null) return;

        owner.ShowName(boundCard.DisplayName, isHazard);
        owner.ShowDescription(boundCard.Description, isHazard);
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
