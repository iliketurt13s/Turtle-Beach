using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orchestrates the end-of-storm upgrade choice: draws 2 distinct cards from
/// Upgrade Pool (skipping non-stackable cards already picked this run),
/// shows the choice UI, and invokes a callback once the player selects one.
/// Other systems (BuildModeController, TurtleSelectionController,
/// CameraController) poll the static IsActive flag to stand down while this
/// is up, the same way they already stand down for BuildModeController.IsActive.
/// </summary>
public class UpgradeSelectionUI : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    [Tooltip("The full authored pool of upgrade cards to draw from. Add more here to expand the pool without touching this script.")]
    [SerializeField] private UpgradeCardDefinition[] upgradePool;

    [Header("UI References")]
    [Tooltip("Root object for the whole choice UI (backdrop + cards), toggled active/inactive by Show/Select.")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image backdrop;
    [Tooltip("Bottom-center text shown while hovering a card's icon.")]
    [SerializeField] private TMP_Text descriptionText;
    [Tooltip("The card view slots shown to the player (2 expected).")]
    [SerializeField] private UpgradeCardView[] cardSlots;

    private readonly HashSet<UpgradeCardDefinition> pickedNonStackable = new HashSet<UpgradeCardDefinition>();
    private Action pendingOnComplete;

    private void Awake()
    {
        IsActive = false;
        if (root != null) root.SetActive(false);
        HideDescription();
    }

    /// <summary>Draws up to 2 distinct eligible cards and shows the choice UI. Calls onComplete immediately if no cards are eligible.</summary>
    public void Show(Action onComplete)
    {
        List<UpgradeCardDefinition> eligible = new List<UpgradeCardDefinition>();
        if (upgradePool != null)
        {
            foreach (UpgradeCardDefinition card in upgradePool)
            {
                if (card == null) continue;
                if (!card.Stackable && pickedNonStackable.Contains(card)) continue;
                eligible.Add(card);
            }
        }

        if (eligible.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int drawCount = Mathf.Min(2, eligible.Count);
        List<UpgradeCardDefinition> drawn = new List<UpgradeCardDefinition>();
        for (int i = 0; i < drawCount; i++)
        {
            int index = UnityEngine.Random.Range(0, eligible.Count);
            drawn.Add(eligible[index]);
            eligible.RemoveAt(index);
        }

        pendingOnComplete = onComplete;
        IsActive = true;
        if (root != null) root.SetActive(true);
        HideDescription();

        for (int i = 0; i < cardSlots.Length; i++)
        {
            bool used = i < drawn.Count;
            cardSlots[i].gameObject.SetActive(used);
            if (used) cardSlots[i].Bind(drawn[i], this);
        }
    }

    /// <summary>Called by a hovered UpgradeCardView to show its description bottom-center.</summary>
    public void ShowDescription(string text)
    {
        if (descriptionText == null) return;
        descriptionText.text = text;
        descriptionText.gameObject.SetActive(true);
    }

    /// <summary>Called when the mouse leaves a card's icon.</summary>
    public void HideDescription()
    {
        if (descriptionText != null) descriptionText.gameObject.SetActive(false);
    }

    /// <summary>Called by a card's Select button. Applies the card's effect, tears down the UI, and resumes the game.</summary>
    public void Select(UpgradeCardDefinition card)
    {
        card.Apply();
        if (card is IGrantsFoodItem) BuildModeController.Instance?.EnsureFoodBuildingPlaced();
        if (!card.Stackable) pickedNonStackable.Add(card);

        IsActive = false;
        if (root != null) root.SetActive(false);

        Action callback = pendingOnComplete;
        pendingOnComplete = null;
        callback?.Invoke();
    }
}
