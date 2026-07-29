using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space build-mode HUD, meant to sit at the lower-middle of the
/// screen: three icons — the nearest unlocked buildable before SelectedIndex,
/// the currently selected one, and the nearest unlocked one after it, in
/// BuildModeController.Buildables' own array order — plus a name label and
/// Wood/Rock cost numbers for whichever buildable is selected (middle icon).
/// A locked buildable is skipped over rather than shown (searching outward
/// until an unlocked one turns up or the array end is reached), so a locked
/// entry sitting right next to the selection never reads as if it were
/// available; a slot is left blank (no sprite, disabled) only once the array
/// actually runs out on that side, with no wraparound. Refreshed on
/// BuildModeController.SelectedBuildableChanged — that event covers a fresh
/// selection, a placement bumping the selected buildable's own price back up,
/// and a price rollback landing on it, so the cost numbers here never need
/// their own separate polling. This replaces the old world-space cost text
/// BuildModeController used to draw under the placement ghost.
///
/// Slides up into view via UISlidePanel when build mode is entered and back
/// down when it's exited (BuildModeController.IsActive), rather than an
/// instant SetActive — see slidePanel's own tooltip for wiring.
/// </summary>
public class BuildSelectionUI : MonoBehaviour
{
    [SerializeField] private BuildModeController buildModeController;

    [Header("UI References")]
    [Tooltip("The panel's own UISlidePanel — PlayIn() when build mode activates, PlayOut() when it deactivates. Set its Play In On Enable to off in the Inspector, since this script drives it directly rather than relying on GameObject activation.")]
    [SerializeField] private UISlidePanel slidePanel;
    [SerializeField] private Image previousImage;
    [SerializeField] private Image selectedImage;
    [SerializeField] private Image nextImage;
    [SerializeField] private TMP_Text buildingNameText;
    [Tooltip("Wood/Rock cost of the selected (middle) buildable. Leave either unassigned if you're not showing that resource.")]
    [SerializeField] private TMP_Text woodCostText;
    [SerializeField] private TMP_Text rockCostText;

    private bool wasActive;

    private void OnEnable()
    {
        if (buildModeController != null) buildModeController.SelectedBuildableChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (buildModeController != null) buildModeController.SelectedBuildableChanged -= Refresh;
    }

    private void Update()
    {
        bool isActive = BuildModeController.IsActive;
        if (isActive == wasActive) return;

        wasActive = isActive;
        if (slidePanel == null) return;

        if (isActive) slidePanel.PlayIn();
        else slidePanel.PlayOut();
    }

    private void Refresh()
    {
        if (buildModeController == null) return;

        int index = buildModeController.SelectedIndex;
        var buildables = buildModeController.Buildables;

        SetSlot(previousImage, FindNearestUnlocked(buildables, index - 1, -1));
        BuildableDefinition selected = GetBuildableAt(buildables, index);
        SetSlot(selectedImage, selected);
        SetSlot(nextImage, FindNearestUnlocked(buildables, index + 1, 1));

        if (buildingNameText != null) buildingNameText.text = selected != null ? selected.DisplayName : string.Empty;

        SetCostText(woodCostText, selected, ResourceManager.ResourceType.Wood);
        SetCostText(rockCostText, selected, ResourceManager.ResourceType.Rock);
    }

    /// <summary>Walks from startIndex in step's direction (-1/+1) until it finds a buildable BuildModeController.IsUnlocked accepts, or falls off the array — this is what an adjacent slot should actually show, since a locked buildable in between isn't reachable by scrolling either (see BuildModeController.SetSelectedIndex). No wraparound: reaching either end without finding one just returns null (blank slot).</summary>
    private BuildableDefinition FindNearestUnlocked(IReadOnlyList<BuildableDefinition> buildables, int startIndex, int step)
    {
        for (int i = startIndex; i >= 0 && buildables != null && i < buildables.Count; i += step)
        {
            BuildableDefinition candidate = buildables[i];
            if (buildModeController.IsUnlocked(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>Reads type's amount out of buildable's live (already price-scaled) Cost — 0 if buildable doesn't cost that type at all, or nothing is selected.</summary>
    private static void SetCostText(TMP_Text text, BuildableDefinition buildable, ResourceManager.ResourceType type)
    {
        if (text == null) return;

        int amount = 0;
        if (buildable != null)
        {
            foreach (ResourceManager.ResourceCost cost in buildable.Cost)
            {
                if (cost.type == type)
                {
                    amount = cost.amount;
                    break;
                }
            }
        }

        text.text = amount.ToString();
    }

    /// <summary>Null (not a wrapped-around index) for anything outside [0, buildables.Count) — the whole point being an edge slot reads as blank rather than looping to the other end of the array, unlike BuildModeController.SetSelectedIndex's own wrap-around cycling.</summary>
    private static BuildableDefinition GetBuildableAt(IReadOnlyList<BuildableDefinition> buildables, int index)
    {
        if (buildables == null || index < 0 || index >= buildables.Count) return null;
        return buildables[index];
    }

    private static void SetSlot(Image image, BuildableDefinition buildable)
    {
        if (image == null) return;

        Sprite sprite = buildable != null ? buildable.Icon : null;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
