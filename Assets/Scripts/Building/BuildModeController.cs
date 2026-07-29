using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// Holding Shift activates build mode: shows a semi-transparent ghost of the
/// selected buildable snapped to the island's 1x1 grid, and left-clicking
/// places it if the player can afford its cost. TurtleSelectionController
/// checks the static IsActive flag to stand down while this is active.
/// </summary>
public class BuildModeController : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    /// <summary>Scene-wide singleton so upgrade cards (which live as prefab assets, not scene objects) can call Unlock without a serialized scene reference.</summary>
    public static BuildModeController Instance { get; private set; }

    [Header("Island Reference")]
    [SerializeField] private IslandGenerator islandGenerator;

    [Header("Buildables")]
    [Tooltip("All placeable building prefabs, each with a BuildableDefinition component. Cycle between them with the scroll wheel while in build mode. Array order is the scroll-cycle order; index 0 is selected by default.")]
    [SerializeField] private BuildableDefinition[] buildables;

    [Tooltip("Buildables placeable from game start (e.g. just the Turtle Bed). Everything else in Buildables begins locked; call Unlock to make more available later, e.g. from an upgrade card.")]
    [SerializeField] private BuildableDefinition[] initiallyUnlocked;

    private HashSet<BuildableDefinition> unlockedBuildables;

    private BuildableDefinition selectedBuildable;
    private int selectedIndex;

    /// <summary>Array-order snapshot of every configured buildable (locked or not), so a UI (e.g. BuildSelectionUI) can look up the buildable before/after SelectedIndex without its own reference to the Inspector-authored array.</summary>
    public IReadOnlyList<BuildableDefinition> Buildables => buildables;

    /// <summary>Current index into Buildables — always an unlocked entry, see SetSelectedIndex.</summary>
    public int SelectedIndex => selectedIndex;

    /// <summary>Raised whenever anything BuildSelectionUI would need to redraw about the current selection changes: a fresh SetSelectedIndex (selectedIndex/selectedBuildable and the ghost sprite already up to date by the time this fires), a successful placement of the selected buildable bumping its own price back up, or a price rollback (BuildableDefinition.PriceRolledBack) landing on the currently selected buildable. Lets UI refresh exactly on change instead of polling every frame.</summary>
    public event Action SelectedBuildableChanged;

    /// <summary>The Buildables entry with a TurtleBed component, found once in Awake — null if none is configured. Cached so UpdateTurtleBedAvailability doesn't need to scan the whole array every frame.</summary>
    private BuildableDefinition turtleBedBuildable;
    /// <summary>Last TurtleBed.AllBeds.Count seen by UpdateTurtleBedAvailability, so it only does work on the frame the count actually changes (placement or destruction) rather than every frame. -1 forces a check on the very first Update.</summary>
    private int lastKnownBedCount = -1;

    [Header("Ghost")]
    [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.5f;
    [Tooltip("Ghost tint shown when the current tile can't be built on (water, too close to the nest, or occupied by a resource/building/the nest).")]
    [SerializeField] private Color invalidColor = new Color(1f, 0.25f, 0.25f);
    [Tooltip("Ghost tint flashed briefly when the player clicks to place something they can't currently afford.")]
    [SerializeField] private Color insufficientFundsColor = new Color(1f, 0f, 0f);
    [Tooltip("How long the insufficient-funds flash stays up before the ghost goes back to normal position-based tinting.")]
    [SerializeField] private float insufficientFundsFlashDuration = 1f;

    [Header("Range Indicator")]
    [Tooltip("Segments used to approximate the range-preview circle shown under the ghost for a buildable implementing IHasPlacementRange (Fertilizer, Pet Rock, Watchtower, Campfire). Higher = smoother circle.")]
    [SerializeField, Range(8, 128)] private int rangeCircleSegments = 48;
    [SerializeField] private Color rangeCircleColor = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private float rangeCircleWidth = 0.06f;

    [Header("Click Detection")]
    [Tooltip("Max screen-pixel distance between press and release to count as a click rather than a drag.")]
    [SerializeField] private float clickDistanceThreshold = 6f;

    [Header("Placement Restrictions")]
    [Tooltip("Tiles, measured from the map center (where the nest always sits), kept clear of buildings.")]
    [SerializeField, Range(0, 20)] private int minDistanceFromNest = 3;
    [Tooltip("Maximum number of Turtle Beds that can be placed at once. Once reached, Turtle Bed is pulled out of the selectable/cyclable pool entirely (see UpdateTurtleBedAvailability) until a bed is destroyed drops the count back under this.")]
    [SerializeField, Min(0)] private int maxTurtleBeds = 5;

    private Camera cam;
    private GameObject ghostObject;
    private SpriteRenderer ghostRenderer;
    private LineRenderer rangeCircleRenderer;
    private IHasPlacementRange selectedRangeSource;
    private Color validGhostColor;
    private Vector3Int currentCell;
    private Vector2 pressPosition;

    /// <summary>True for the duration of InsufficientFundsFlashRoutine — suppresses UpdateGhostPosition's normal position-based tinting so the red flash isn't overwritten the very next frame.</summary>
    private bool isFlashingInsufficientFunds;
    private Coroutine insufficientFundsFlashCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("BuildModeController: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        cam = Camera.main;

        // Each BuildableDefinition lives on its buildable's prefab asset, not
        // a scene instance (this references it directly to read/instantiate)
        // — so its price-scaling state otherwise carries over from a
        // previous game rather than resetting like normal scene state does.
        ResetAllPriceScaling();

        unlockedBuildables = new HashSet<BuildableDefinition>();
        if (initiallyUnlocked != null)
        {
            foreach (BuildableDefinition b in initiallyUnlocked) unlockedBuildables.Add(b);
        }

        turtleBedBuildable = FindTurtleBedBuildable();

        BuildGhost();
        SetSelectedIndex(0);
        SetGhostVisible(false);

        BuildableDefinition.PriceRolledBack += HandlePriceRolledBack;
    }

    private void OnDestroy()
    {
        BuildableDefinition.PriceRolledBack -= HandlePriceRolledBack;

        if (Instance == this) Instance = null;
        if (ghostObject != null) Destroy(ghostObject);
    }

    /// <summary>Notifies UI (SelectedBuildableChanged) if the buildable a placed instance just rolled back the price of (see BuildableDefinition.RegisterDestruction) happens to be the one currently selected — its displayed cost is now stale.</summary>
    private void HandlePriceRolledBack(BuildableDefinition definition)
    {
        if (definition == selectedBuildable) SelectedBuildableChanged?.Invoke();
    }

    /// <summary>Resets every configured buildable's price scaling back to its Inspector-authored base (see BuildableDefinition.ResetPriceScaling).</summary>
    private void ResetAllPriceScaling()
    {
        if (buildables != null)
        {
            foreach (BuildableDefinition b in buildables)
            {
                if (b != null) b.ResetPriceScaling();
            }
        }
    }

    /// <summary>Selects a buildable by index, e.g. from a future selection UI or scroll-wheel cycling. Wraps out-of-range indices in either direction, skipping any buildable not yet unlocked.</summary>
    public void SetSelectedIndex(int index)
    {
        if (buildables == null || buildables.Length == 0) return;

        int wrapped = ((index % buildables.Length) + buildables.Length) % buildables.Length;
        int direction = wrapped >= selectedIndex ? 1 : -1;

        for (int i = 0; i < buildables.Length; i++)
        {
            int candidate = (((wrapped + i * direction) % buildables.Length) + buildables.Length) % buildables.Length;
            if (unlockedBuildables.Contains(buildables[candidate]))
            {
                selectedIndex = candidate;
                selectedBuildable = buildables[selectedIndex];
                RefreshGhostSprite();
                SelectedBuildableChanged?.Invoke();
                return;
            }
        }
    }

    /// <summary>Makes a buildable placeable from now on. Integration point for future "unlock a building" upgrade cards.</summary>
    public void Unlock(BuildableDefinition buildable)
    {
        if (buildable != null) unlockedBuildables.Add(buildable);
    }

    /// <summary>True if buildable is currently placeable — either authored in Initially Unlocked or unlocked mid-run via Unlock. Lets an upgrade card gate itself behind a specific building already being unlocked (see IRequiresBuilding), so "branch" upgrades only start appearing once their building does.</summary>
    public bool IsUnlocked(BuildableDefinition buildable) => buildable != null && unlockedBuildables.Contains(buildable);

    private BuildableDefinition FindTurtleBedBuildable()
    {
        if (buildables == null) return null;

        foreach (BuildableDefinition b in buildables)
        {
            if (b != null && b.GetComponent<TurtleBed>() != null) return b;
        }

        return null;
    }

    /// <summary>
    /// Pulls Turtle Bed out of the selectable/cyclable pool the instant Max
    /// Turtle Beds is reached, and puts it back — at whatever price it's
    /// scaled to (see BuildableDefinition.RegisterDestruction, which already
    /// rolls that price back down when a bed is destroyed) — the instant a
    /// bed count drop takes it back under the cap. Polled once a frame
    /// (cheap: just an int comparison) rather than wired to a specific
    /// destruction event, since a bed can disappear by several different
    /// means (BuildingHealth trash damage, ...) and this way doesn't need to
    /// know about any of them.
    /// </summary>
    private void UpdateTurtleBedAvailability()
    {
        if (turtleBedBuildable == null || maxTurtleBeds <= 0) return;

        int bedCount = TurtleBed.AllBeds.Count;
        if (bedCount == lastKnownBedCount) return;
        lastKnownBedCount = bedCount;

        bool shouldBeUnlocked = bedCount < maxTurtleBeds;
        bool currentlyUnlocked = unlockedBuildables.Contains(turtleBedBuildable);
        if (shouldBeUnlocked == currentlyUnlocked) return;

        if (shouldBeUnlocked)
        {
            unlockedBuildables.Add(turtleBedBuildable);
        }
        else
        {
            unlockedBuildables.Remove(turtleBedBuildable);
            // Ghost was showing the now-locked Turtle Bed — cycle forward to
            // whatever's next available instead of leaving it selected on
            // something the player can no longer place.
            if (selectedBuildable == turtleBedBuildable) SetSelectedIndex(selectedIndex);
        }
    }

    private void Update()
    {
        UpdateTurtleBedAvailability();

        if (UpgradeSelectionUI.IsActive || GarbagePatchCutsceneController.IsActive || GameOverUI.IsPaused) return;

        Keyboard keyboard = Keyboard.current;
        bool shiftHeld = keyboard != null && keyboard.shiftKey.isPressed;

        if (shiftHeld != IsActive)
        {
            IsActive = shiftHeld;
            SetGhostVisible(shiftHeld);
        }

        if (!IsActive) return;

        HandleScrollCycling();
        UpdateGhostPosition();
        HandleClick();
    }

    private void HandleScrollCycling()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        SetSelectedIndex(selectedIndex + (scroll > 0f ? 1 : -1));
    }

    private void BuildGhost()
    {
        ghostObject = new GameObject("BuildGhost");
        ghostRenderer = ghostObject.AddComponent<SpriteRenderer>();

        BuildRangeCircle();
    }

    /// <summary>Builds the (initially hidden) range-preview circle as a looped LineRenderer under the ghost, in local space so it's always centered on wherever the ghost currently sits. See RefreshPlacementRangeCircle for what drives its radius/visibility.</summary>
    private void BuildRangeCircle()
    {
        GameObject circleObject = new GameObject("PlacementRangeCircle");
        circleObject.transform.SetParent(ghostObject.transform, false);

        rangeCircleRenderer = circleObject.AddComponent<LineRenderer>();
        rangeCircleRenderer.loop = true;
        rangeCircleRenderer.useWorldSpace = false;
        rangeCircleRenderer.positionCount = rangeCircleSegments;
        rangeCircleRenderer.startWidth = rangeCircleWidth;
        rangeCircleRenderer.endWidth = rangeCircleWidth;
        rangeCircleRenderer.startColor = rangeCircleColor;
        rangeCircleRenderer.endColor = rangeCircleColor;
        rangeCircleRenderer.material = new Material(Shader.Find("Sprites/Default"));
        circleObject.SetActive(false);
    }

    private void RefreshGhostSprite()
    {
        if (ghostRenderer == null) return;

        selectedRangeSource = selectedBuildable != null ? selectedBuildable.GetComponent<IHasPlacementRange>() : null;
        if (rangeCircleRenderer != null) rangeCircleRenderer.gameObject.SetActive(false);

        SpriteRenderer sourceRenderer = selectedBuildable != null
            ? selectedBuildable.GetComponentInChildren<SpriteRenderer>()
            : null;

        if (sourceRenderer == null)
        {
            ghostRenderer.sprite = null;
            return;
        }

        ghostRenderer.sprite = sourceRenderer.sprite;
        ghostRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        ghostRenderer.sortingOrder = sourceRenderer.sortingOrder;

        Color color = sourceRenderer.color;
        color.a = ghostAlpha;
        validGhostColor = color;
        ghostRenderer.color = validGhostColor;

        // Keep the range circle (if any) drawing above the ghost sprite
        // regardless of which sorting layer/order the selected buildable's
        // own sprite uses.
        if (rangeCircleRenderer != null)
        {
            rangeCircleRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            rangeCircleRenderer.sortingOrder = sourceRenderer.sortingOrder + 1;
        }
    }

    /// <summary>Redraws the range-preview circle from the selected buildable's live IHasPlacementRange.PlacementRange every frame the ghost is up, so an upgrade-card range bonus picked mid-run is reflected immediately rather than only after reselecting the buildable. No-op (and hidden) for any buildable that doesn't implement the interface.</summary>
    private void RefreshPlacementRangeCircle()
    {
        if (rangeCircleRenderer == null) return;

        if (selectedRangeSource == null)
        {
            if (rangeCircleRenderer.gameObject.activeSelf) rangeCircleRenderer.gameObject.SetActive(false);
            return;
        }

        if (!rangeCircleRenderer.gameObject.activeSelf) rangeCircleRenderer.gameObject.SetActive(true);

        float radius = selectedRangeSource.PlacementRange;
        for (int i = 0; i < rangeCircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / rangeCircleSegments;
            rangeCircleRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private void SetGhostVisible(bool visible)
    {
        if (ghostObject != null) ghostObject.SetActive(visible);
    }

    private void UpdateGhostPosition()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null || islandGenerator == null) return;

        Vector3 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        world.z = 0f;

        Tilemap sand = islandGenerator.SandTilemap;
        if (sand == null) return;

        currentCell = sand.WorldToCell(world);
        Vector3 cellCenter = sand.GetCellCenterWorld(currentCell);
        ghostObject.transform.position = cellCenter;

        RefreshPlacementRangeCircle();

        if (isFlashingInsufficientFunds) return;

        bool valid = IsPlacementValid(currentCell, cellCenter, sand);
        Color tint = valid ? validGhostColor : invalidColor;
        tint.a = ghostAlpha;
        if (ghostRenderer != null) ghostRenderer.color = tint;
    }

    private void HandleClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = mouse.position.ReadValue();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            bool wasCameraDrag = CameraController.Instance != null && CameraController.Instance.WasDragging;
            if (!wasCameraDrag && Vector2.Distance(pressPosition, mouse.position.ReadValue()) <= clickDistanceThreshold)
            {
                TryPlace();
            }
        }
    }

    private void TryPlace()
    {
        if (selectedBuildable == null || ResourceManager.Instance == null || islandGenerator == null) return;

        Tilemap sand = islandGenerator.SandTilemap;
        if (sand == null) return;

        Vector3 cellCenter = sand.GetCellCenterWorld(currentCell);
        if (!IsPlacementValid(currentCell, cellCenter, sand)) return;
        if (!ResourceManager.Instance.TrySpend(selectedBuildable.Cost))
        {
            FlashInsufficientFunds();
            return;
        }

        selectedBuildable.RegisterPlacement();
        SelectedBuildableChanged?.Invoke();

        GameObject instance = Instantiate(selectedBuildable.gameObject, cellCenter, Quaternion.identity);
        instance.GetComponent<SquashAndStretch>()?.Play();

        // Instantiate gives this placed instance its own separate
        // BuildableDefinition clone, so link it back to the array entry
        // (selectedBuildable) that actually prices this buildable, so
        // destroying it can roll that price back down (see BuildableDefinition.OnDestroy).
        BuildableDefinition instanceDefinition = instance.GetComponent<BuildableDefinition>();
        if (instanceDefinition != null) instanceDefinition.LinkToMaster(selectedBuildable);

        WallAutoTile autoTile = instance.GetComponent<WallAutoTile>();
        if (autoTile != null) autoTile.Initialize(currentCell);

        TurtleBed turtleBed = instance.GetComponent<TurtleBed>();
        if (turtleBed != null) turtleBed.Initialize(islandGenerator);
    }

    /// <summary>Briefly tints the ghost insufficientFundsColor after a click that fails to afford the selected buildable, then hands tinting back to UpdateGhostPosition's normal position-based logic — feedback that the click registered but the player can't afford it yet, rather than the click silently doing nothing.</summary>
    private void FlashInsufficientFunds()
    {
        if (ghostRenderer == null) return;

        if (insufficientFundsFlashCoroutine != null) StopCoroutine(insufficientFundsFlashCoroutine);
        insufficientFundsFlashCoroutine = StartCoroutine(InsufficientFundsFlashRoutine());
    }

    private IEnumerator InsufficientFundsFlashRoutine()
    {
        isFlashingInsufficientFunds = true;

        Color tint = insufficientFundsColor;
        tint.a = ghostAlpha;
        ghostRenderer.color = tint;

        yield return new WaitForSeconds(insufficientFundsFlashDuration);

        isFlashingInsufficientFunds = false;
        insufficientFundsFlashCoroutine = null;
    }

    /// <summary>True if a building can be placed on this cell: on land, far enough from the nest, unoccupied, and — for a Turtle Bed specifically — under the Max Turtle Beds cap. This last check is normally already unreachable for Turtle Bed once UpdateTurtleBedAvailability locks it back out of selection at the cap; kept as a belt-and-suspenders guard against a stray one-frame gap between the count changing and the next Update's poll.</summary>
    private bool IsPlacementValid(Vector3Int cell, Vector3 cellCenter, Tilemap sand)
    {
        if (!sand.HasTile(cell)) return false;
        if (!IsFarEnoughFromNest(cell)) return false;
        if (!IsCellClear(cellCenter)) return false;
        if (IsAtTurtleBedCap()) return false;
        return true;
    }

    /// <summary>True if the selected buildable is a Turtle Bed and the number already placed has reached Max Turtle Beds.</summary>
    private bool IsAtTurtleBedCap()
    {
        if (selectedBuildable == null) return false;
        if (selectedBuildable.GetComponent<TurtleBed>() == null) return false;

        return TurtleBed.AllBeds.Count >= maxTurtleBeds;
    }

    /// <summary>True if this cell is far enough (in tiles) from the map center, where the nest always sits.</summary>
    private bool IsFarEnoughFromNest(Vector3Int cell)
    {
        if (minDistanceFromNest <= 0) return true;

        // Chebyshev distance from cell (0,0), matching how IslandGenerator
        // always centers the generated island (and the nest) on the world origin.
        int distance = Mathf.Max(Mathf.Abs(cell.x), Mathf.Abs(cell.y));
        return distance > minDistanceFromNest;
    }

    /// <summary>True if nothing solid (another building, nature, the nest, ...) already occupies this tile. Turtles never block placement, checked by ownership (GetComponentInParent&lt;TurtleAgent&gt;) rather than by layer — a turtle has several colliders across its hierarchy (main body, the small head hitbox used for harvest hits, ...) that don't all necessarily sit on the same layer, and a layer-mask exclusion would silently miss whichever ones don't. OverlapBoxAll (not OverlapBox) is required here since a single overlapping turtle collider would otherwise hide whatever else is also on the tile.</summary>
    private static bool IsCellClear(Vector3 cellCenter)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellCenter, Vector2.one * 0.9f, 0f);
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponentInParent<TurtleAgent>() == null) return false;
        }

        return true;
    }
}
