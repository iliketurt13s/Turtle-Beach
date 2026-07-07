using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row of the resource HUD: an icon and a count. ResourceHUD instantiates
/// one of these the first time a given resource type is collected and keeps
/// updating its count from then on.
/// </summary>
public class ResourceCounterRow : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;

    public void Initialize(Sprite icon)
    {
        if (iconImage != null) iconImage.sprite = icon;
    }

    public void SetCount(int count)
    {
        if (countText != null) countText.text = count.ToString();
    }
}
