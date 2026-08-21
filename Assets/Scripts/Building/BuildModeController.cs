using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// Build mode shows a semi-transparent ghost of the selected buildable snapped
/// to the island's 1x1 grid, and left-clicking places it if the player can
/// afford its cost. TurtleSelectionController checks the static IsActive flag
/// to stand down while this is active, and BuildSelectionUI slides its HUD in
/// and out off the same flag.
///
/// ONE way in: the on-screen hammer tab (see ToggleBuildMode, driven by
/// BuildMenuToggleButton). It LATCHES — build mode stays on until the tab is
/// clicked again, right-click dismisses it (HandleRightClickDismiss), or Escape
/// does (GameOverUI.HandleEscape) — rather than lasting only as long as some
/// input is held. There was previously a hold-Shift route as well; it was
/// removed rather than kept as a shortcut, because a modifier key isn't
/// discoverable and a second, momentary way to be in build mode meant every
/// question about the state ("is it open?", "should this click close it?") had
/// two answers.
///
/// Escape closing the drawer is implemented in GameOverUI, not here — it owns
/// the Escape key for the whole scene so that one keypress can't both close
/// this and open the pause menu. See GameOverUI.HandleEscape.
///
/// Phases that own the screen (the end-of-storm upgrade pick, the garbage-patch
/// cutscene, the pause menu, a finished run) don't suspend this controller —
/// they force build mode OFF, by dropping the tab's latch in Update. The
/// distinction matters and is easy to get
/// wrong: suspending it strands IsActive at whatever it was when the phase
/// began, and since that flag is what the HUD and ghost follow, both stay on
/// screen for the whole phase with no input path left to dismiss them. See
/// Update.
/// </summary>
public class BuildModeController : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    /// <summary>Scene-wide singleton so upgrade cards (which live as prefab assets, not scene objects) can call Unlock without a serialized scene reference.</summary>
    public static BuildModeController Instance { get; private set; }

    /// <summary>
    /// True while a phase that owns the screen is up, in which case build mode
    /// is forced off no matter what the player is pressing or has latched open.
    /// Public because BuildMenuToggleButton hides itself off the same condition
    /// — a build button floating over the upgrade pick or the game-over panel
    /// would be both clickable and useless. Kept as one expression here rather
    /// than re-listed there, so a new blocking phase only has to be added once.
    /// </summary>
    public static bool IsBlockedByPhase =>
        UpgradeSelectionUI.IsActive
        || GarbagePatchCutsceneController.IsActive
        || GameOverUI.IsPaused
        // Not covered by IsPaused: GameOverUI clears that on its way to showing
        // results (a finished run isn't paused, it's over), which otherwise
        // handed build mode straight back the moment the player lost — ghost,
        // HUD and all — and let them keep placing buildings behind the
        // game-over panel.
        || GameOverUI.IsRunOver
        // The settings screen is modal and pauses the game behind it, so it
        // owns the screen exactly the way the phases above do. Listed here
        // rather than beside each of them, which is what makes the hammer tab,
        // the tutorial mentor and the drawer itself stand down for it for free.
        || SettingsMenuUI.IsOpen;

    /// <summary>The live latch — flipped the instant the hammer tab is clicked. IsActive is Update's once-a-frame view of the same thing (latch AND no blocking phase), which is what everything polling build mode should read; this exists for the one caller that needs the state as of RIGHT NOW rather than as of the last frame, GameOverUI.HandleEscape, which may run before this controller's own Update on the frame a click lands.</summary>
    public static bool IsToggledOpen => Instance != null && Instance.isToggledOpen;

    private bool isToggledOpen;

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

    [Header("Demolish")]
    [Tooltip("Tint laid over the building the demolish cursor is currently on, so it's obvious which one a click would take down. Applied through the same IHoverTintable path HoverTargetOutline uses for hovered resources/buildings — there's no contention between the two, since that script stands down entirely while build mode is active.")]
    [SerializeField] private Color demolishTintColor = new Color(1f, 0.35f, 0.35f);

    [Header("Click Detection")]
    [Tooltip("Max screen-pixel distance between press and release to count as a click rather than a drag.")]
    [SerializeField] private float clickDistanceThreshold = 6f;

    [Header("Placement Restrictions")]
    [Tooltip("Tiles, measured from the map center (where the nest always sits), kept clear of buildings.")]
    [SerializeField, Range(0, 20)] private int minDistanceFromNest = 3;
    [Tooltip("Maximum number of Turtle Beds that can be placed at once. Once reached, Turtle Bed is pulled out of the selectable/cyclable pool entirely (see UpdateTurtleBedAvailability) until a bed is destroyed drops the count back under this.")]
    [SerializeField, Min(0)] private int maxTurtleBeds = 5;
    [Tooltip("Fewest Turtle Beds the demolish tool will leave standing. A run with no beds can never hatch another turtle, so demolishing the last one is an unrecoverable position the player can walk into with a single misclick — the cursor simply refuses to target it, the same as it refuses empty ground. Only demolition is held to this; a bed lost to trash is a loss the storm earned. 0 turns the protection off.")]
    [SerializeField, Min(0)] private int minTurtleBeds = 1;

    /// <summary>The selected entry's DemolishTool, or null for a normal buildable — the single "is the demolish tool selected" test, refreshed by RefreshGhostSprite on every selection change so the click and ghost paths don't each re-ask the prefab.</summary>
    private DemolishTool demolishTool;
    /// <summary>The building the demolish cursor is currently highlighting, so the tint can be reverted the moment the cursor moves off it (or build mode ends).</summary>
    private BuildableDefinition demolishHoverTarget;
    private IHoverTintable demolishHoverTintable;

    private Camera cam;
    private GameObject ghostObject;
    private SpriteRenderer ghostRenderer;
    private LineRenderer rangeCircleRenderer;
    private IHasPlacementRange selectedRangeSource;
    private Color validGhostColor;
    private Vector3Int currentCell;
    private Vector2 pressPosition;
    /// <summary>Whether the current left-button press began over UI (see HandleClick), so the release can be ignored even if the pointer has since moved off the panel.</summary>
    private bool pressStartedOverUI;
    /// <summary>Whether the ghost is currently hidden purely because the pointer is over UI, as opposed to build mode being off — kept so Update only calls SetGhostVisible on an actual change.</summary>
    private bool isGhostHiddenByUI;

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

        // IsActive is static, so force a clean state the same way
        // DayStormCycle.IsStorming/UpgradeSelectionUI.IsActive do — it survives
        // a scene reload (Restart) in a build, and a stopped-and-restarted Play
        // session in an Editor with Domain Reload disabled. Left stale-true, the
        // build HUD slides itself in on the first frame of a fresh run without
        // the tab ever being clicked, since BuildSelectionUI starts from
        // wasActive == false and reads that as build mode having just been
        // entered.
        IsActive = false;

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
        SetDemolishHover(null);

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

    /// <summary>
    /// +1/-1 for the direction the selection was last actively scrolled in, or
    /// 0 when the last change wasn't a scroll at all (the initial selection, or
    /// a forced re-select after Turtle Bed hits its cap).
    ///
    /// Exists purely for BuildSelectionUI's crossfade: that panel normally
    /// infers which way to slide by checking whether the new selection is the
    /// one it was previously showing in its next or previous slot, but now that
    /// the strip wraps, one or two unlocked buildables put the SAME buildable
    /// in both of those slots and that comparison can no longer tell the
    /// directions apart. Nothing about placement or cycling reads this.
    /// </summary>
    public int LastSelectionStep { get; private set; }

    /// <summary>Selects a buildable by index, e.g. from a future selection UI or scroll-wheel cycling. Wraps out-of-range indices in either direction, skipping any buildable not yet unlocked. step is the scroll direction that prompted this (+1/-1) — leave it 0 for a selection that isn't the player scrolling. It does two jobs: it's recorded for LastSelectionStep (so BuildSelectionUI knows which way to animate the strip), and it's the direction the locked-buildable skip search walks, so the selection can only ever move the way the player actually scrolled.</summary>
    public void SetSelectedIndex(int index, int step = 0)
    {
        if (buildables == null || buildables.Length == 0) return;

        LastSelectionStep = step;

        int wrapped = ((index % buildables.Length) + buildables.Length) % buildables.Length;

        // Which way to walk when the requested entry is locked and we have to
        // keep looking. This MUST be the direction the player actually
        // scrolled, not something inferred from the indices: comparing wrapped
        // against selectedIndex gets it exactly backwards whenever a scroll
        // wraps around the ends of the array. Scrolling down from index 0 asks
        // for -1, which wraps to the LAST index — a bigger number than the
        // current one — so the comparison read that as moving forward and
        // searched forward, walking up the array from the far end. With most
        // buildables locked for most of a run, that search almost always had
        // somewhere to go, so scrolling down quietly selected something ahead
        // of the current buildable and the strip animated as if the player had
        // scrolled up. step is 0 only for calls that aren't the player
        // scrolling at all (the initial selection, the forced re-select when
        // Turtle Bed hits its cap), and forward is the documented behavior for
        // those.
        int direction = step != 0 ? step : 1;

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

    /// <summary>Makes a buildable placeable from now on. Integration point for future "unlock a building" upgrade cards. Notifies UI (SelectedBuildableChanged) immediately, so a newly-unlocked buildable shows up in BuildSelectionUI's adjacent slots right away rather than only after the player next scrolls.</summary>
    public void Unlock(BuildableDefinition buildable)
    {
        if (buildable != null)
        {
            unlockedBuildables.Add(buildable);
            SelectedBuildableChanged?.Invoke();
        }
    }

    /// <summary>True if buildable is currently placeable — either authored in Initially Unlocked or unlocked mid-run via Unlock. Lets an upgrade card gate itself behind a specific building already being unlocked (see IRequiresBuilding), so "branch" upgrades only start appearing once their building does. Null-safe against the set itself, since BuildSelectionUI.OnEnable refreshes (and so asks this) unconditionally, and there's no ordering guarantee that this component's Awake ran first if either object is enabled later rather than being live at scene load.</summary>
    public bool IsUnlocked(BuildableDefinition buildable) => buildable != null && unlockedBuildables != null && unlockedBuildables.Contains(buildable);

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

    /// <summary>Flips the build menu open/closed — the hammer tab's onClick target (see BuildMenuToggleButton), and the only way into build mode. LATCHES: it stays open until clicked again, Escape dismisses it, or a phase forces it off.</summary>
    public void ToggleBuildMode() => SetBuildModeOpen(!isToggledOpen);

    /// <summary>Opens or closes the latched build menu directly, for a caller that knows which it wants (e.g. a tutorial step, or closing it on some other UI opening). Refuses to open while a phase owns the screen — Update would just force it back off on the same frame, and the button would look broken.</summary>
    public void SetBuildModeOpen(bool open)
    {
        if (open && IsBlockedByPhase) return;

        isToggledOpen = open;
    }

    private void Update()
    {
        UpdateTurtleBedAvailability();

        // Folded into wantsBuildMode rather than early-returning above the sync
        // below, which is what this used to do and is exactly how the build HUD
        // got stuck on screen: IsActive is written nowhere else, so returning
        // before it FROZE it at whatever it was the frame the phase began.
        // BuildSelectionUI only ever reacts to a CHANGE in the flag, so it never
        // played its panel back out, and SetGhostVisible(false) below is on the
        // same unreached line so the placement ghost stayed parked in the world
        // too. Worse, the cutscene's flag is deliberately never cleared once the
        // patch depletes, so a win left the HUD up with nothing able to take it
        // back down. Clearing the latch instead drops build mode the instant a
        // phase starts, while still suppressing scroll/click for the duration
        // via the !IsActive return below.
        //
        // The latch is CLEARED rather than merely ignored for the duration, so
        // a run that ends (or a menu that opens) while the build drawer is out
        // doesn't spring it back open the moment that phase lets go.
        bool blocked = IsBlockedByPhase;
        if (blocked) isToggledOpen = false;

        bool wantsBuildMode = !blocked && isToggledOpen;

        if (wantsBuildMode != IsActive)
        {
            IsActive = wantsBuildMode;
            isGhostHiddenByUI = false;
            SetGhostVisible(wantsBuildMode);
        }

        if (!IsActive)
        {
            SetDemolishHover(null);
            return;
        }

        if (HandleRightClickDismiss()) return;

        // The ghost follows the mouse, so with the drawer docked down one side
        // of the screen it would otherwise sit behind the panel previewing a
        // placement the click can't make anyway (see HandleClick's UI guard).
        // Hidden rather than just left un-clickable, so the panel doesn't have
        // a building sliding around underneath it.
        bool pointerOverUI = IsPointerOverUI();
        if (pointerOverUI != isGhostHiddenByUI)
        {
            isGhostHiddenByUI = pointerOverUI;
            SetGhostVisible(!pointerOverUI);
            // The demolish cursor goes away with the ghost, so drop its
            // highlight too rather than leaving a building tinted red under a
            // drawer the player is only browsing.
            if (pointerOverUI) SetDemolishHover(null);
        }

        // Scroll still cycles while the pointer is over the drawer — that's the
        // main way the player is expected to browse it (see BuildSelectionUI).
        HandleScrollCycling();
        if (!pointerOverUI) UpdateGhostPosition();
        HandleClick();
    }

    /// <summary>True if the mouse is currently over any UI graphic that takes raycasts — the build drawer, its hammer tab, the resource HUD. Guards world clicks so pressing a button doesn't ALSO place a building underneath it. TurtleSelectionController repeats this check for its own clicks rather than sharing one helper, the same way the two already keep separate copies of the click-vs-drag threshold — keep them in sync. See that copy for the full-screen-overlay hazard this brings with it: a decorative graphic left flagged Raycast Target silently eats every gameplay click while it's up.</summary>
    private static bool IsPointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    /// <summary>
    /// Right-click backs out of build mode, the same "dismiss this" gesture
    /// Escape performs. Returns true if it fired, so the rest of the frame's
    /// build handling is skipped rather than acting on a mode the player has
    /// just closed.
    ///
    /// Unlike Escape, this one lives here rather than in GameOverUI. Escape had
    /// to be centralized because the pause menu wanted the same key at the same
    /// time and script execution order would have decided the winner; right
    /// click has no such contest — TurtleSelectionController is the only other
    /// reader and it stands down entirely while IsActive, so nothing else can
    /// see this press. Deliberately not gated on the pointer being over the
    /// world either: right-clicking the drawer itself should dismiss it just as
    /// readily as right-clicking the island.
    /// </summary>
    private bool HandleRightClickDismiss()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.wasPressedThisFrame) return false;

        SetBuildModeOpen(false);
        return true;
    }

    private void HandleScrollCycling()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        int step = scroll > 0f ? 1 : -1;
        SetSelectedIndex(selectedIndex + step, step);
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
        demolishTool = selectedBuildable != null ? selectedBuildable.GetComponent<DemolishTool>() : null;
        SetDemolishHover(null);
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

        // Ahead of the insufficient-funds check rather than inside the normal
        // tinting path below: demolishing costs nothing and so can never raise
        // that flash itself, but a flash raised by the buildable selected
        // BEFORE this one may still be counting down, and letting it suppress
        // the demolish cursor's targeting for a second would look like the tool
        // had simply stopped working.
        if (demolishTool != null)
        {
            BuildableDefinition target = FindDemolishTarget(cellCenter);
            SetDemolishHover(target);

            Color demolishTint = target != null ? validGhostColor : invalidColor;
            demolishTint.a = ghostAlpha;
            if (ghostRenderer != null) ghostRenderer.color = demolishTint;
            return;
        }

        if (isFlashingInsufficientFunds) return;

        bool valid = IsPlacementValid(currentCell, cellCenter, sand);
        Color tint = valid ? validGhostColor : invalidColor;
        tint.a = ghostAlpha;
        if (ghostRenderer != null) ghostRenderer.color = tint;
    }

    /// <summary>
    /// Switches which building the demolish cursor is highlighting, reverting
    /// the previous one first — pass null to just clear it. Mirrors
    /// HoverTargetOutline.SetHoverTintTarget, including the no-op when the
    /// target hasn't actually changed, since this runs every frame the cursor
    /// moves.
    ///
    /// The tint goes through IHoverTintable, adding a BuildingHoverTint if the
    /// building doesn't already carry one. Added rather than required, because
    /// that component is only authored onto INTERACTABLE buildings today (it
    /// exists for the Watchtower's hover) while everything placeable can be
    /// demolished — requiring it would mean the highlight silently worked on
    /// some buildings and not others, and would put a wiring step on every
    /// future building prefab.
    /// </summary>
    private void SetDemolishHover(BuildableDefinition target)
    {
        // ReferenceEquals, not ==: the highlighted building can be destroyed
        // out from under this (trash levelling a wall the cursor happens to be
        // resting on), and Unity's == reports a destroyed object as equal to
        // null — so the ordinary comparison would take the "nothing changed"
        // path on the very frame the target disappears, and leave a dangling
        // tint reference to throw on the next real change.
        if (ReferenceEquals(demolishHoverTarget, target)) return;

        // Guarded on the building rather than the interface: IHoverTintable is
        // an interface reference, so ?. sees a destroyed component as non-null
        // and would call through to renderers that no longer exist. The
        // BuildableDefinition it came from is a UnityEngine.Object and answers
        // that question honestly.
        if (demolishHoverTarget != null) demolishHoverTintable?.RevertTint();

        demolishHoverTarget = target;
        demolishHoverTintable = null;

        if (target == null) return;

        demolishHoverTintable = target.GetComponent<IHoverTintable>();
        if (demolishHoverTintable == null) demolishHoverTintable = target.gameObject.AddComponent<BuildingHoverTint>();
        demolishHoverTintable.ApplyTint(demolishTintColor);
    }

    /// <summary>
    /// The placed building occupying this cell, or null if there isn't one.
    /// Tests the same box IsCellClear checks placement against, so what the
    /// demolish cursor highlights is exactly what sits under the ghost — and
    /// skips creatures for the same reason that does, since a turtle swimming
    /// over a wall shouldn't hide it.
    ///
    /// Keyed on BuildableDefinition rather than BuildingHealth: it's the
    /// component that means "a player put this here", it's what carries the
    /// cost to refund, and it's what keeps the nest (which has neither) off
    /// limits.
    ///
    /// Anything CanDemolish refuses is skipped here rather than rejected later
    /// at the click, so the highlight and the click can't disagree about what's
    /// takeable: a protected building reads as empty ground to both, and the
    /// cursor is already tinted invalid over it before anything is pressed.
    /// </summary>
    private BuildableDefinition FindDemolishTarget(Vector3 cellCenter)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellCenter, Vector2.one * 0.9f, 0f);

        BuildableDefinition best = null;
        float bestDistance = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            if (IsAnimal(hit)) continue;

            BuildableDefinition building = hit.GetComponentInParent<BuildableDefinition>();
            if (building == null || !CanDemolish(building)) continue;

            // Nearest to the cell the ghost is on, so a neighbouring building
            // whose collider merely clips into this box can't be taken instead
            // of the one the player is actually pointing at.
            float distance = ((Vector2)(building.transform.position - cellCenter)).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = building;
        }

        return best;
    }

    /// <summary>
    /// Whether this placed building may be demolished at all. The one rule
    /// today is Min Turtle Beds: the last bed can't be taken down, because
    /// beds are the only source of new turtles and a run with none left is
    /// unwinnable with no way back.
    ///
    /// The mirror of IsAtTurtleBedCap on the placement side, and polled the
    /// same way off TurtleBed.AllBeds rather than tracked, so it can't drift
    /// out of step with beds appearing and disappearing by any of the several
    /// means they do.
    /// </summary>
    private bool CanDemolish(BuildableDefinition building)
    {
        if (building == null) return false;
        if (minTurtleBeds <= 0) return true;
        if (building.GetComponent<TurtleBed>() == null) return true;

        return TurtleBed.AllBeds.Count > minTurtleBeds;
    }

    private void HandleClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = mouse.position.ReadValue();
            // Latched at press, not just re-tested at release: a click that
            // starts on the drawer and drifts off it before release is still
            // the player operating the UI, not aiming at the world.
            pressStartedOverUI = IsPointerOverUI();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            bool overUI = pressStartedOverUI || IsPointerOverUI();
            pressStartedOverUI = false;
            if (overUI) return;

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

        if (demolishTool != null)
        {
            TryDemolish(sand);
            return;
        }

        Vector3 cellCenter = sand.GetCellCenterWorld(currentCell);
        if (!IsPlacementValid(currentCell, cellCenter, sand)) return;

        // Snapshotted before it's spent, and kept for RecordPaidCost below:
        // Cost recomputes on every access and RegisterPlacement is about to
        // bump it, so re-reading it afterwards would record the price of the
        // NEXT one of these rather than what this placement actually cost.
        IReadOnlyList<ResourceManager.ResourceCost> price = selectedBuildable.Cost;
        if (!ResourceManager.Instance.TrySpend(price))
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
        if (instanceDefinition != null)
        {
            instanceDefinition.LinkToMaster(selectedBuildable);
            instanceDefinition.RecordPaidCost(price);
        }

        WallAutoTile autoTile = instance.GetComponent<WallAutoTile>();
        if (autoTile != null) autoTile.Initialize(currentCell);

        TurtleBed turtleBed = instance.GetComponent<TurtleBed>();
        if (turtleBed != null) turtleBed.Initialize(islandGenerator);
    }

    /// <summary>Takes down the building under the cursor and hands its refund back to the nest (see DemolishTool.Demolish). Silently does nothing on an empty cell — the cursor is already tinted invalid there, so a click doing nothing is exactly what the player was shown.</summary>
    private void TryDemolish(Tilemap sand)
    {
        Vector3 cellCenter = sand.GetCellCenterWorld(currentCell);
        BuildableDefinition target = FindDemolishTarget(cellCenter);
        if (target == null) return;

        // Cleared while the building is still alive, so its renderers get their
        // original colours put back before it goes rather than this holding a
        // reference to a destroyed object to revert later.
        SetDemolishHover(null);

        demolishTool.Demolish(target);
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

    /// <summary>True if nothing solid (another building, nature, the nest, ...) already occupies this tile. Living creatures never block placement (see IsAnimal) — they wander constantly, so a tile they happen to be standing on is not meaningfully occupied, and refusing the placement just makes the player wait for something to swim off. OverlapBoxAll (not OverlapBox) is required here since a single overlapping creature collider would otherwise hide whatever else is also on the tile.</summary>
    private static bool IsCellClear(Vector3 cellCenter)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellCenter, Vector2.one * 0.9f, 0f);
        foreach (Collider2D hit in hits)
        {
            if (!IsAnimal(hit)) return false;
        }

        return true;
    }

    /// <summary>
    /// True if this collider belongs to a living creature rather than to
    /// scenery — turtles and crabs (both TurtleAgent, since a crab is a Turtle
    /// prefab variant) and drifting jellyfish. These are transient occupants of
    /// a tile, not obstacles, so they're skipped when deciding whether a cell
    /// is free to build on.
    ///
    /// Checked by ownership (GetComponentInParent) rather than by physics
    /// layer: a creature has several colliders across its hierarchy — a turtle
    /// has its main body plus the small head hitbox that registers harvest
    /// hits — and they don't all necessarily sit on the same layer, so a
    /// layer-mask exclusion would silently miss whichever ones don't and let
    /// half a turtle keep blocking placement.
    ///
    /// A future creature is one more line here. Deliberately NOT extended to
    /// trash, which is scenery-with-a-grudge rather than wildlife: whether you
    /// can build on a tile a piece of trash is currently rolling across is a
    /// gameplay call, not an oversight.
    /// </summary>
    private static bool IsAnimal(Collider2D collider)
    {
        return collider.GetComponentInParent<TurtleAgent>() != null
            || collider.GetComponentInParent<JellyfishAgent>() != null;
    }
}
