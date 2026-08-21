using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The build drawer: a full-height panel down one side of the screen listing
/// EVERY currently-unlocked buildable at once, one BuildMenuRow each, in
/// BuildModeController.Buildables' array order. The selected building is shown
/// by inflating its icon in place; a single cost readout travels up and down
/// the list to sit level with whichever icon that is, its numbers counting to
/// the new building's price as it goes.
///
/// This replaced a three-slot carousel that scrolled buildings past fixed
/// previous/selected/next positions. The list is a better fit now that the
/// panel is full-height: nothing is hidden, so the player can see everything
/// they own and how much it costs without scrolling to find out, and a row can
/// simply be clicked (see SelectRow) instead of scrolled to. Scrolling still
/// works and is still driven by BuildModeController.SetSelectedIndex — this
/// panel only ever reflects SelectedIndex, it never decides it.
///
/// The demolish entry (see DemolishTool) is drawn as an ordinary row like any
/// other, but always floated to the top of the list and separated from the
/// buildings below it by an extra gap — it's reached the same ways they are
/// (click it, or scroll onto it) while reading as the odd one out, which is
/// what it is: the one entry that takes something away instead of placing it.
/// It shows no price, since it has none — and the cost readout stays put
/// instead of travelling to meet it, since there would be nothing to read when
/// it arrived.
///
/// Rows are rebuilt only when the unlocked SET changes (see
/// RebuildRowsIfNeeded), not on every refresh — SelectedBuildableChanged also
/// fires for a placement bumping a price and for a price rolling back, and
/// tearing down every row for those would restart the animation and lose the
/// player's place. Positioning is done directly rather than through a Layout
/// Group, because the cost readout has to line itself up against a specific
/// row's position every time the selection moves, and a Layout Group would be
/// rewriting those positions underneath that.
///
/// Slides into view via UISlidePanel when build mode is entered and back out
/// when it's exited (BuildModeController.IsActive), rather than an instant
/// SetActive — see slidePanel's own tooltip for wiring, and set that
/// component's Direction to match whichever screen edge the drawer is docked
/// against. Build mode is entered by clicking the drawer's hammer tab
/// (BuildMenuToggleButton); this panel doesn't watch for that itself, it just
/// follows the one flag.
/// </summary>
public class BuildSelectionUI : MonoBehaviour
{
    [SerializeField] private BuildModeController buildModeController;

    [Header("UI References")]
    [Tooltip("The panel's own UISlidePanel — PlayIn() when build mode activates, PlayOut() when it deactivates. Set its Play In On Enable to off in the Inspector, since this script drives it directly rather than relying on GameObject activation.")]
    [SerializeField] private UISlidePanel slidePanel;
    [Tooltip("Parent the buildable rows are instantiated under. The list centres itself vertically on this object's own position, so anchor it at the middle of the panel and the rows stay centred there no matter how many buildings are unlocked. Must NOT have a Layout Group — see the class doc comment.")]
    [SerializeField] private RectTransform rowContainer;
    [Tooltip("Row prefab instantiated once per unlocked buildable. Its authored X position and scale are kept, so horizontal placement and base size are set on the prefab, not here.")]
    [SerializeField] private BuildMenuRow rowPrefab;
    [Tooltip("Name of the currently selected buildable. Parent it under Cost Group if you want it to travel with the cost readout rather than staying put.")]
    [SerializeField] private TMP_Text buildingNameText;
    [Tooltip("The whole cost readout, moved vertically to sit level with the selected row. Anything that should travel with it (labels, icons, the name text) belongs inside it as a child. Needn't share a parent with the rows — alignment goes via world space.")]
    [SerializeField] private RectTransform costGroup;
    [Tooltip("Wood/Rock cost of the selected buildable. Leave either unassigned if you're not showing that resource.")]
    [SerializeField] private TMP_Text woodCostText;
    [SerializeField] private TMP_Text rockCostText;
    [Tooltip("Optional: the numbers-and-icons half of the cost readout, hidden outright while an entry with no price is selected (the demolish row). Leave unassigned and the numbers are simply blanked instead, which leaves any resource icons beside them showing — assign this if the readout has icons.")]
    [SerializeField] private GameObject costAmountsGroup;

    [Header("Layout")]
    [Tooltip("Vertical gap between rows, in this canvas's units. The list keeps Buildables' array order top to bottom and centres the whole run on Row Container, so unlocking a building grows it evenly in both directions rather than extending it downward.")]
    [SerializeField] private float rowSpacing = 110f;
    [Tooltip("Extra gap inserted between the demolish row and the buildings below it, on top of Row Spacing — what sets it apart as a different kind of thing rather than just another building. Ignored when demolish isn't in the drawer, or when it's the only entry in it.")]
    [SerializeField] private float demolishSeparatorSpacing = 55f;

    [Header("Selection")]
    [Tooltip("Scale the selected building's icon inflates to.")]
    [SerializeField] private float selectedScale = 1.4f;
    [Tooltip("Scale every other building's icon sits at.")]
    [SerializeField] private float unselectedScale = 1f;
    [Tooltip("Opacity of the selected building.")]
    [SerializeField, Range(0f, 1f)] private float selectedAlpha = 1f;
    [Tooltip("Opacity of every other building — slightly see-through, so the selected one reads as the solid one even before you notice it's bigger. Fades on the same timeline as the inflate.")]
    [SerializeField, Range(0f, 1f)] private float unselectedAlpha = 0.6f;
    [Tooltip("Seconds the inflate, the cost readout's travel, and the cost count all take — they share one timeline on purpose, so the number lands exactly as the readout arrives.")]
    [SerializeField] private float transitionDuration = 0.2f;
    [Tooltip("Eases all three. An overshooting curve is fine and gives the icon a pop — the cost NUMBERS are clamped separately so they can't tick past the real price and walk back.")]
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private readonly List<BuildMenuRow> rows = new List<BuildMenuRow>();
    /// <summary>Buildables index shown by each row, parallel to rows — the cached "unlocked set" a refresh compares against to decide whether the list needs rebuilding at all.</summary>
    private readonly List<int> shownIndices = new List<int>();
    private readonly List<int> unlockedScratch = new List<int>();
    private readonly List<float> startScales = new List<float>();
    private readonly List<float> startAlphas = new List<float>();

    private Coroutine transitionRoutine;
    private bool hasShownBefore;
    private int displayedWoodCost;
    private int displayedRockCost;
    /// <summary>Whether the current selection is a priceless entry (the demolish row) whose cost readout should show nothing rather than a row of zeroes. Held as state rather than passed down, because SetCostTexts is also called from inside the transition coroutine's per-frame count.</summary>
    private bool selectionHasNoPrice;
    private bool wasActive;

    private void OnEnable()
    {
        if (buildModeController != null) buildModeController.SelectedBuildableChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (buildModeController != null) buildModeController.SelectedBuildableChanged -= Refresh;

        // Unity auto-stops the coroutine but leaves whatever half-interpolated
        // scales and position it was mid-way through, so the panel would come
        // back up with an icon stuck at some in-between size. Forgetting that
        // this has ever run makes the next refresh snap to a clean state
        // instead of animating out of a stale one.
        transitionRoutine = null;
        hasShownBefore = false;
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

    /// <summary>The live row currently showing buildableIndex (an index into BuildModeController.Buildables), or null if that buildable isn't unlocked or the list hasn't been built yet. Exists because rows are instantiated at runtime and destroyed whenever the unlocked set changes, so nothing outside this script can hold a reference to one — the tutorial's arrow has to ask for the Bed's row fresh each frame rather than being wired to it in the Inspector.</summary>
    public RectTransform GetRowRect(int buildableIndex)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (shownIndices[i] == buildableIndex) return rows[i] != null ? rows[i].Rect : null;
        }

        return null;
    }

    /// <summary>Called by a row's own button — selects that buildable, exactly as scrolling onto it would. Routed through BuildModeController rather than handled here so clicking and scrolling land in the same place, ghost and all.</summary>
    public void SelectRow(BuildMenuRow row)
    {
        if (buildModeController == null || row == null || row.BuildableIndex < 0) return;

        // step 0: this isn't a scroll, so there's no direction to record. The
        // clicked row is unlocked by construction, so the locked-skip search
        // lands on it immediately regardless of which way it would walk.
        buildModeController.SetSelectedIndex(row.BuildableIndex);
    }

    private void Refresh()
    {
        if (buildModeController == null) return;

        RebuildRowsIfNeeded();
        ApplySelection(animate: hasShownBefore);
        hasShownBefore = true;
    }

    /// <summary>Rebuilds the row list, but only if the set of unlocked buildables actually differs from what's on screen — see the class doc comment for why every refresh must not tear the list down.</summary>
    private void RebuildRowsIfNeeded()
    {
        IReadOnlyList<BuildableDefinition> buildables = buildModeController.Buildables;

        unlockedScratch.Clear();
        if (buildables != null)
        {
            for (int i = 0; i < buildables.Count; i++)
            {
                if (buildModeController.IsUnlocked(buildables[i])) unlockedScratch.Add(i);
            }
        }

        // Before the MatchesShown comparison, not after: shownIndices is
        // recorded from this list in the layout loop below, so reordering it
        // afterwards would leave a reordered cache being compared against a
        // freshly-built list that isn't, and every refresh would read as a
        // change and tear down the whole drawer.
        int demolishCount = FloatDemolishEntriesToTop(buildables);

        if (MatchesShown(unlockedScratch)) return;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] != null) Destroy(rows[i].gameObject);
        }

        rows.Clear();
        shownIndices.Clear();

        if (rowPrefab == null || rowContainer == null || buildables == null) return;

        // Centered on Row Container's own origin rather than hanging below it,
        // so the list stays vertically centred in a full-height panel however
        // many buildings are unlocked: one building sits dead centre, and each
        // new unlock grows the list symmetrically outward instead of pushing it
        // down the panel. The reflow that causes (every row shifts half a
        // spacing) is never seen — a building can only unlock from an upgrade
        // card, and UpgradeSelectionUI.IsActive forces the drawer shut for the
        // whole card pick, so the list is rebuilt off-screen.
        // Only a separator when there is actually something on both sides of
        // it — a drawer holding nothing but the demolish row shouldn't sit
        // off-centre for a gap below it that separates it from nothing.
        float separator = demolishCount > 0 && demolishCount < unlockedScratch.Count ? demolishSeparatorSpacing : 0f;
        float topOffset = ((unlockedScratch.Count - 1) * rowSpacing + separator) * 0.5f;

        for (int i = 0; i < unlockedScratch.Count; i++)
        {
            int index = unlockedScratch[i];

            BuildMenuRow row = Instantiate(rowPrefab);
            // worldPositionStays false: keeps the prefab's authored local
            // position/scale rather than having Unity compensate for the
            // parent's transform, so the row's X and base size come from the
            // prefab exactly as drawn.
            row.transform.SetParent(rowContainer, false);
            row.gameObject.SetActive(true);
            row.Bind(this, index, buildables[index].Icon);

            Vector2 position = row.Rect.anchoredPosition;
            float y = topOffset - i * rowSpacing - (i >= demolishCount ? separator : 0f);
            row.Rect.anchoredPosition = new Vector2(position.x, y);
            row.Rect.localScale = Vector3.one * unselectedScale;
            row.Alpha = unselectedAlpha;

            rows.Add(row);
            shownIndices.Add(index);
        }
    }

    /// <summary>
    /// Moves any demolish entry to the front of unlockedScratch, keeping
    /// everything else in Buildables' array order, and returns how many there
    /// are (realistically one, or none until it's unlocked).
    ///
    /// Done here rather than by asking whoever authored the array to put it
    /// first, because array order is also SCROLL order and the two want
    /// opposite things: the drawer wants demolish at the top, while the
    /// selection that build mode opens on is Buildables[0] and shouldn't be the
    /// tool that destroys things. Putting demolish LAST in the array satisfies
    /// both — the scroll wraps onto it from the first building, which is the
    /// row directly below it on screen anyway.
    /// </summary>
    private int FloatDemolishEntriesToTop(IReadOnlyList<BuildableDefinition> buildables)
    {
        if (buildables == null) return 0;

        int demolishCount = 0;
        for (int i = 0; i < unlockedScratch.Count; i++)
        {
            if (!DemolishTool.IsDemolishEntry(buildables[unlockedScratch[i]])) continue;

            // Safe to move underneath the loop: the entry only ever travels
            // from i to demolishCount, which is never past i, so nothing ahead
            // of the cursor is disturbed and nothing already behind it moves
            // out of reach.
            int index = unlockedScratch[i];
            unlockedScratch.RemoveAt(i);
            unlockedScratch.Insert(demolishCount, index);
            demolishCount++;
        }

        return demolishCount;
    }

    private bool MatchesShown(List<int> candidate)
    {
        if (candidate.Count != shownIndices.Count) return false;

        for (int i = 0; i < candidate.Count; i++)
        {
            if (candidate[i] != shownIndices[i]) return false;
        }

        return true;
    }

    /// <summary>Points every visual at the current selection — inflating its row, moving the cost readout level with it, and counting the numbers over. Animated unless this is the first refresh (or the panel was just re-enabled), which snaps.</summary>
    private void ApplySelection(bool animate)
    {
        BuildableDefinition selected = SelectedBuildable();

        if (buildingNameText != null) buildingNameText.text = selected != null ? selected.DisplayName : string.Empty;

        // Set before the counts below (and before the coroutine that animates
        // them) so a readout that's about to be blanked never flashes a frame
        // of zeroes first.
        selectionHasNoPrice = DemolishTool.IsDemolishEntry(selected);
        if (costAmountsGroup != null) costAmountsGroup.SetActive(!selectionHasNoPrice);

        int targetWood = CostOf(selected, ResourceManager.ResourceType.Wood);
        int targetRock = CostOf(selected, ResourceManager.ResourceType.Rock);
        // Held where it is rather than travelling to the demolish row: that row
        // has no price, so there is nothing for the readout to arrive with, and
        // sliding a blank frame up the panel reads as the UI having lost track
        // of itself. Expressed as "target the position it's already at" rather
        // than as a skipped transition, so the row still inflates and the
        // numbers still settle on the same timeline — only the travel is off.
        float targetY = selectionHasNoPrice && costGroup != null
            ? costGroup.localPosition.y
            : SelectedRowY();

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        if (!animate || transitionDuration <= 0f || !isActiveAndEnabled)
        {
            for (int i = 0; i < rows.Count; i++) SetRowLook(i, TargetScaleFor(i), TargetAlphaFor(i));
            SetCostGroupY(targetY);
            SetCostTexts(targetWood, targetRock);
            return;
        }

        transitionRoutine = StartCoroutine(TransitionRoutine(targetY, targetWood, targetRock));
    }

    private IEnumerator TransitionRoutine(float targetY, int targetWood, int targetRock)
    {
        // Snapshotted rather than assumed to be the resting values: a scroll
        // landing mid-transition restarts this, and picking up from wherever
        // things actually are is what keeps fast scrolling smooth instead of
        // jumping back to the last settled state on every input.
        startScales.Clear();
        startAlphas.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            startScales.Add(rows[i] != null ? rows[i].Rect.localScale.x : unselectedScale);
            startAlphas.Add(rows[i] != null ? rows[i].Alpha : unselectedAlpha);
        }

        float startY = costGroup != null ? costGroup.localPosition.y : 0f;
        int startWood = displayedWoodCost;
        int startRock = displayedRockCost;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = transitionCurve.Evaluate(Mathf.Clamp01(elapsed / transitionDuration));

            for (int i = 0; i < rows.Count; i++)
            {
                SetRowLook(
                    i,
                    Mathf.LerpUnclamped(startScales[i], TargetScaleFor(i), t),
                    Mathf.LerpUnclamped(startAlphas[i], TargetAlphaFor(i), t));
            }

            SetCostGroupY(Mathf.LerpUnclamped(startY, targetY, t));

            // Clamped where the scale and position above are not: an
            // overshooting curve reads as a nice pop on the icon, but on a
            // price it would tick past the real number and count back down to
            // it, which looks like the cost changed twice.
            float numberT = Mathf.Clamp01(t);
            SetCostTexts(
                Mathf.RoundToInt(Mathf.Lerp(startWood, targetWood, numberT)),
                Mathf.RoundToInt(Mathf.Lerp(startRock, targetRock, numberT)));

            yield return null;
        }

        for (int i = 0; i < rows.Count; i++) SetRowLook(i, TargetScaleFor(i), TargetAlphaFor(i));
        SetCostGroupY(targetY);
        SetCostTexts(targetWood, targetRock);
        transitionRoutine = null;
    }

    private bool IsSelectedRow(int rowIndex) => shownIndices[rowIndex] == buildModeController.SelectedIndex;

    private float TargetScaleFor(int rowIndex) => IsSelectedRow(rowIndex) ? selectedScale : unselectedScale;

    private float TargetAlphaFor(int rowIndex) => IsSelectedRow(rowIndex) ? selectedAlpha : unselectedAlpha;

    /// <summary>Applies one row's two selection visuals together — they're always driven from the same t, so keeping them in one call is what stops a future edit from animating one and forgetting the other.</summary>
    private void SetRowLook(int rowIndex, float scale, float alpha)
    {
        BuildMenuRow row = rows[rowIndex];
        if (row == null) return;

        row.Rect.localScale = new Vector3(scale, scale, 1f);
        // Clamped on arrival rather than at the lerp: an overshooting curve is
        // wanted on the scale (it gives the icon its pop) but alpha has nowhere
        // to overshoot to, so it just rides the same t and saturates.
        row.Alpha = Mathf.Clamp01(alpha);
    }

    /// <summary>Where the cost readout should sit to line up with the selected row, expressed in the readout's OWN parent space. Routed through world space so the readout doesn't have to share a parent with the rows — it can live anywhere in the panel and still track them.</summary>
    private float SelectedRowY()
    {
        if (costGroup == null) return 0f;

        BuildMenuRow selectedRow = SelectedRow();
        if (selectedRow == null) return costGroup.localPosition.y;

        Vector3 world = selectedRow.Rect.position;
        Transform parent = costGroup.parent;
        return parent != null ? parent.InverseTransformPoint(world).y : world.y;
    }

    private void SetCostGroupY(float y)
    {
        if (costGroup == null) return;

        Vector3 position = costGroup.localPosition;
        costGroup.localPosition = new Vector3(position.x, y, position.z);
    }

    private void SetCostTexts(int wood, int rock)
    {
        displayedWoodCost = wood;
        displayedRockCost = rock;

        // The counts are still tracked while blank (displayedWoodCost above),
        // so selecting a real building next counts up from the right number
        // rather than from whatever was last visible.
        if (woodCostText != null) woodCostText.text = selectionHasNoPrice ? string.Empty : wood.ToString();
        if (rockCostText != null) rockCostText.text = selectionHasNoPrice ? string.Empty : rock.ToString();
    }

    private BuildMenuRow SelectedRow()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (shownIndices[i] == buildModeController.SelectedIndex) return rows[i];
        }

        return null;
    }

    private BuildableDefinition SelectedBuildable()
    {
        IReadOnlyList<BuildableDefinition> buildables = buildModeController.Buildables;
        int index = buildModeController.SelectedIndex;
        if (buildables == null || index < 0 || index >= buildables.Count) return null;

        return buildables[index];
    }

    /// <summary>Reads type's amount out of buildable's live (already price-scaled) Cost — 0 if buildable doesn't cost that type at all, or nothing is selected.</summary>
    private static int CostOf(BuildableDefinition buildable, ResourceManager.ResourceType type)
    {
        if (buildable == null) return 0;

        foreach (ResourceManager.ResourceCost cost in buildable.Cost)
        {
            if (cost.type == type) return cost.amount;
        }

        return 0;
    }
}
