using System.Collections;
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
/// selection, a buildable just being unlocked, a placement bumping the
/// selected buildable's own price back up, and a price rollback landing on
/// it, so the cost numbers/icons here never need their own separate polling.
///
/// When the selection actually changes (scrolling forward/backward — detected
/// by comparing the newly-selected buildable's identity against whatever was
/// previously shown in the next/previous slot, since BuildModeController's own
/// wraparound/locked-skipping rules out any simple index-delta check), each of
/// the 3 slots crossfades: a temporary clone of the slot holds the outgoing
/// content and slides/fades out one side while the real Image swaps to the
/// incoming content and slides/fades in from the other, both visible and
/// moving at once. Any other refresh reason (unlock reveal, cost-only
/// refresh, first call, or too big a jump to sensibly animate) just snaps
/// instantly, matching the old behavior.
///
/// Slides up into view via UISlidePanel when build mode is entered and back
/// down when it's exited (BuildModeController.IsActive), rather than an
/// instant SetActive — see slidePanel's own tooltip for wiring.
/// </summary>
public class BuildSelectionUI : MonoBehaviour
{
    private const int PreviousSlot = 0;
    private const int SelectedSlot = 1;
    private const int NextSlot = 2;
    private const int SlotCount = 3;

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

    [Header("Transition")]
    [Tooltip("How far (anchoredPosition units) a slot's outgoing/incoming content slides from its own resting position during a scroll transition.")]
    [SerializeField] private float transitionSlideOffset = 80f;
    [SerializeField] private float transitionDuration = 0.25f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    /// <summary>Each slot's own authored resting anchoredPosition/localScale/alpha, captured once in Awake — a transition always animates outgoing/incoming content relative to its OWN slot's resting look, never borrowing another slot's.</summary>
    private struct SlotHome
    {
        public Vector2 position;
        public Vector3 scale;
        public float alpha;
    }

    private Image[] slotImages;
    private SlotHome[] slotHomes;
    private readonly BuildableDefinition[] shownBuildables = new BuildableDefinition[SlotCount];
    private readonly Coroutine[] slotRoutines = new Coroutine[SlotCount];
    private readonly GameObject[] slotClones = new GameObject[SlotCount];
    private bool hasShownBefore;

    private bool wasActive;

    private void Awake()
    {
        slotImages = new[] { previousImage, selectedImage, nextImage };
        slotHomes = new SlotHome[SlotCount];
        for (int i = 0; i < SlotCount; i++) slotHomes[i] = CaptureSlotHome(slotImages[i]);
    }

    private static SlotHome CaptureSlotHome(Image image)
    {
        if (image == null) return default;

        RectTransform rect = (RectTransform)image.transform;
        return new SlotHome { position = rect.anchoredPosition, scale = rect.localScale, alpha = image.color.a };
    }

    private void OnEnable()
    {
        if (buildModeController != null) buildModeController.SelectedBuildableChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (buildModeController != null) buildModeController.SelectedBuildableChanged -= Refresh;

        // Coroutines are auto-stopped by Unity on disable, but the clone
        // GameObjects they were driving aren't — sweep them so a disabled
        // panel never leaves orphaned clones behind.
        for (int i = 0; i < SlotCount; i++) CancelSlotTransition(i);
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

        BuildableDefinition[] newBuildables = new BuildableDefinition[SlotCount];
        newBuildables[PreviousSlot] = FindNearestUnlocked(buildables, index - 1, -1);
        newBuildables[SelectedSlot] = GetBuildableAt(buildables, index);
        newBuildables[NextSlot] = FindNearestUnlocked(buildables, index + 1, 1);

        int direction = 0;
        if (hasShownBefore && newBuildables[SelectedSlot] != shownBuildables[SelectedSlot])
        {
            if (newBuildables[SelectedSlot] == shownBuildables[NextSlot]) direction = 1;
            else if (newBuildables[SelectedSlot] == shownBuildables[PreviousSlot]) direction = -1;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (direction == 0)
            {
                CancelSlotTransition(i);
                SetSlot(slotImages[i], newBuildables[i]);
                ResetSlotTransform(i);
            }
            else
            {
                StartSlotTransition(i, newBuildables[i], direction);
            }
        }

        for (int i = 0; i < SlotCount; i++) shownBuildables[i] = newBuildables[i];
        hasShownBefore = true;

        BuildableDefinition selected = newBuildables[SelectedSlot];
        if (buildingNameText != null) buildingNameText.text = selected != null ? selected.DisplayName : string.Empty;

        SetCostText(woodCostText, selected, ResourceManager.ResourceType.Wood);
        SetCostText(rockCostText, selected, ResourceManager.ResourceType.Rock);
    }

    private void CancelSlotTransition(int i)
    {
        if (slotRoutines[i] != null)
        {
            StopCoroutine(slotRoutines[i]);
            slotRoutines[i] = null;
        }

        if (slotClones[i] != null)
        {
            Destroy(slotClones[i]);
            slotClones[i] = null;
        }
    }

    private void ResetSlotTransform(int i)
    {
        Image image = slotImages[i];
        if (image == null) return;

        SlotHome home = slotHomes[i];
        RectTransform rect = (RectTransform)image.transform;
        rect.anchoredPosition = home.position;
        rect.localScale = home.scale;

        Color color = image.color;
        color.a = home.alpha;
        image.color = color;
    }

    /// <summary>Clones the slot's current (outgoing) content into a temporary duplicate that slides/fades out one side while the real Image swaps to the incoming content and slides/fades in from the other — both visible and moving at once, so it reads as a genuine crossfade rather than a fade-out-then-fade-in.</summary>
    private void StartSlotTransition(int i, BuildableDefinition newBuildable, int direction)
    {
        Image image = slotImages[i];
        if (image == null) return;

        CancelSlotTransition(i);

        SlotHome home = slotHomes[i];
        RectTransform rect = (RectTransform)image.transform;

        Sprite oldSprite = image.sprite;
        bool hadContent = oldSprite != null && image.enabled;

        if (hadContent)
        {
            GameObject cloneObject = Instantiate(image.gameObject, image.transform.parent);
            cloneObject.name = image.gameObject.name + " (Transition Clone)";
            cloneObject.transform.SetSiblingIndex(image.transform.GetSiblingIndex());

            Image cloneImage = cloneObject.GetComponent<Image>();
            cloneImage.sprite = oldSprite;
            cloneImage.raycastTarget = false;

            RectTransform cloneRect = (RectTransform)cloneObject.transform;
            cloneRect.anchoredPosition = rect.anchoredPosition;
            cloneRect.localScale = rect.localScale;
            cloneImage.color = image.color;

            slotClones[i] = cloneObject;
        }

        Sprite newSprite = newBuildable != null ? newBuildable.Icon : null;
        image.sprite = newSprite;
        image.enabled = newSprite != null;

        Vector2 exitOffset = new Vector2(-direction * transitionSlideOffset, 0f);
        Vector2 enterOffset = new Vector2(direction * transitionSlideOffset, 0f);

        rect.anchoredPosition = home.position + enterOffset;
        rect.localScale = Vector3.zero;
        Color entryColor = image.color;
        entryColor.a = 0f;
        image.color = entryColor;

        slotRoutines[i] = StartCoroutine(SlotTransitionRoutine(i, home, home.position + exitOffset));
    }

    private IEnumerator SlotTransitionRoutine(int i, SlotHome home, Vector2 cloneExitPosition)
    {
        Image image = slotImages[i];
        RectTransform rect = (RectTransform)image.transform;
        Vector2 entryPosition = rect.anchoredPosition; // wherever StartSlotTransition just snapped it to

        GameObject clone = slotClones[i];
        RectTransform cloneRect = clone != null ? (RectTransform)clone.transform : null;
        Image cloneImage = clone != null ? clone.GetComponent<Image>() : null;
        Vector2 cloneStartPosition = cloneRect != null ? cloneRect.anchoredPosition : Vector2.zero;
        Vector3 cloneStartScale = cloneRect != null ? cloneRect.localScale : Vector3.one;
        float cloneStartAlpha = cloneImage != null ? cloneImage.color.a : 0f;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = transitionCurve.Evaluate(Mathf.Clamp01(elapsed / transitionDuration));

            rect.anchoredPosition = Vector2.LerpUnclamped(entryPosition, home.position, t);
            rect.localScale = Vector3.LerpUnclamped(Vector3.zero, home.scale, t);
            Color color = image.color;
            color.a = Mathf.LerpUnclamped(0f, home.alpha, t);
            image.color = color;

            if (cloneRect != null)
            {
                cloneRect.anchoredPosition = Vector2.LerpUnclamped(cloneStartPosition, cloneExitPosition, t);
                cloneRect.localScale = Vector3.LerpUnclamped(cloneStartScale, Vector3.zero, t);
                Color cloneColor = cloneImage.color;
                cloneColor.a = Mathf.LerpUnclamped(cloneStartAlpha, 0f, t);
                cloneImage.color = cloneColor;
            }

            yield return null;
        }

        rect.anchoredPosition = home.position;
        rect.localScale = home.scale;
        Color finalColor = image.color;
        finalColor.a = home.alpha;
        image.color = finalColor;

        if (clone != null) Destroy(clone);
        slotClones[i] = null;
        slotRoutines[i] = null;
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
