using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One upgrade card slot: an icon and a select button. Hovering the icon
/// shows this card's description via the owning UpgradeSelectionUI (a shared
/// bottom-center text, not a per-card tooltip); clicking Select applies it.
/// </summary>
public class UpgradeCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Button selectButton;

    private UpgradeCardDefinition boundCard;
    private UpgradeSelectionUI owner;

    /// <summary>Called by UpgradeSelectionUI each time this slot is (re)used for a newly drawn card.</summary>
    public void Bind(UpgradeCardDefinition card, UpgradeSelectionUI owner)
    {
        boundCard = card;
        this.owner = owner;

        if (iconImage != null) iconImage.sprite = card.Icon;

        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(HandleSelectClicked);
        }
    }

    private void HandleSelectClicked()
    {
        owner?.Select(boundCard);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner != null && boundCard != null) owner.ShowDescription(boundCard.Description);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HideDescription();
    }
}
