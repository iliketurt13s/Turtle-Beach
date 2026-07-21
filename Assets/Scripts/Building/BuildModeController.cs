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

    /// <summary>The Buildables entry with a TurtleBed component, found once in Awake — null if none is configured. Cached so UpdateTurtleBedAvailability doesn't need to scan the whole array every frame.</summary>
    private BuildableDefinition turtleBedBuildable;
    /// <summary>Last TurtleBed.AllBeds.Count seen by UpdateTurtleBedAvailability, so it only does work on the frame the count actually changes (placement or destruction) rather than every frame. -1 forces a check on the very first Update.</summary>
    private int lastKnownBedCount = -1;

    [Header("Ghost")]
    [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.5f;
    [Tooltip("Ghost tint shown when the current tile can't be built on (water, too close to the nest, or occupied by a resource/building/the nest).")]
    [SerializeField] private Color invalidColor = new Color(1f, 0.25f, 0.25f);

    [Header("Cost Display")]
    [Tooltip("No icons, by design — just two plain numbers below the ghost: Wood (brown) and Rock (dark grey).")]
    [SerializeField] private float costTextVerticalOffset = 0.6f;
    [SerializeField] private float costTextHorizontalSpacing = 0.3f;
    [SerializeField] private float costTextFontSize = 2.5f;
    [SerializeField] private bool costTextBold = false;
    [Tooltip("Leave unassigned to use the default built-in font.")]
    [SerializeField] private Font costTextFont;
    [SerializeField] private Color woodCostColor = new Color(0.45f, 0.29f, 0.13f);
    [SerializeField] private Color rockCostColor = new Color(0.3f, 0.3f, 0.3f);

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
    private TextMesh woodCostText;
    private TextMesh rockCostText;
    private Color validGhostColor;
    private Vector3Int currentCell;
    private Vector2 pressPosition;

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

    /// <summary>Keeps the ghost's displayed cost numbers in sync if the buildable a placed instance just rolled back the price of (see BuildableDefinition.RegisterDestruction) happens to be the one currently selected.</summary>
    private void HandlePriceRolledBack(BuildableDefinition definition)
    {
        if (definition == selectedBuildable) RefreshCostText();
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

        if (UpgradeSelectionUI.IsActive) return;

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

        woodCostText = CreateCostText("WoodCostText", new Vector3(-costTextHorizontalSpacing, -costTextVerticalOffset, 0f), woodCostColor);
        rockCostText = CreateCostText("RockCostText", new Vector3(costTextHorizontalSpacing, -costTextVerticalOffset, 0f), rockCostColor);
    }

    private TextMesh CreateCostText(string name, Vector3 localPosition, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(ghostObject.transform, false);
        textObject.transform.localPosition = localPosition;

        TextMesh text = textObject.AddComponent<TextMesh>();
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.characterSize = costTextFontSize * 0.1f;
        text.fontSize = 96;
        text.fontStyle = costTextBold ? FontStyle.Bold : FontStyle.Normal;

        // TextMesh doesn't repoint its renderer's material when you assign a
        // custom Font via script — without this the glyphs are there but
        // invisible, still rendering with the default font's material/texture.
        // Also, TextMesh's own implicit default font isn't a dynamic font, so
        // the fontSize/fontStyle overrides above log "only supported for
        // dynamic fonts" unless a dynamic font (custom or this built-in
        // fallback) is explicitly assigned.
        Font font = costTextFont != null ? costTextFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        textObject.GetComponent<MeshRenderer>().material = font.material;

        text.color = color;
        text.text = "0";

        return text;
    }

    private void RefreshGhostSprite()
    {
        if (ghostRenderer == null) return;

        SpriteRenderer sourceRenderer = selectedBuildable != null
            ? selectedBuildable.GetComponentInChildren<SpriteRenderer>()
            : null;

        if (sourceRenderer == null)
        {
            ghostRenderer.sprite = null;
            RefreshCostText();
            return;
        }

        ghostRenderer.sprite = sourceRenderer.sprite;
        ghostRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        ghostRenderer.sortingOrder = sourceRenderer.sortingOrder;

        Color color = sourceRenderer.color;
        color.a = ghostAlpha;
        validGhostColor = color;
        ghostRenderer.color = validGhostColor;

        // Keep the cost numbers drawing above the ghost sprite regardless of
        // which sorting layer/order the selected buildable's own sprite uses.
        SetCostTextSorting(woodCostText, sourceRenderer.sortingLayerID, sourceRenderer.sortingOrder + 1);
        SetCostTextSorting(rockCostText, sourceRenderer.sortingLayerID, sourceRenderer.sortingOrder + 1);

        RefreshCostText();
    }

    private static void SetCostTextSorting(TextMesh text, int sortingLayerID, int sortingOrder)
    {
        if (text == null) return;

        Renderer renderer = text.GetComponent<Renderer>();
        if (renderer == null) return;

        renderer.sortingLayerID = sortingLayerID;
        renderer.sortingOrder = sortingOrder;
    }

    /// <summary>Reads Wood/Rock out of the selected buildable's cost (0 if it doesn't cost that type, or nothing is selected) and updates the two ghost cost numbers.</summary>
    private void RefreshCostText()
    {
        int woodCost = 0;
        int rockCost = 0;

        if (selectedBuildable != null)
        {
            foreach (ResourceManager.ResourceCost cost in selectedBuildable.Cost)
            {
                if (cost.type == ResourceManager.ResourceType.Wood) woodCost = cost.amount;
                else if (cost.type == ResourceManager.ResourceType.Rock) rockCost = cost.amount;
            }
        }

        if (woodCostText != null) woodCostText.text = woodCost.ToString();
        if (rockCostText != null) rockCostText.text = rockCost.ToString();
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
        if (!ResourceManager.Instance.TrySpend(selectedBuildable.Cost)) return;

        selectedBuildable.RegisterPlacement();
        RefreshCostText();

        GameObject instance = Instantiate(selectedBuildable.gameObject, cellCenter, Quaternion.identity);

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
