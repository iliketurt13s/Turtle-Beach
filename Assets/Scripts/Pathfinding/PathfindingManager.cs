using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scene-wide singleton (turtles/trash are runtime-spawned with no
/// scene-authored reference, same rationale as ResourceManager/UpgradeManager)
/// wrapping AStarPathfinder with this project's world/cell conventions.
/// Always avoids "nature" (ResourceNode) obstacles — buildings are never
/// avoided, since turtles already pass through non-interactable ones and
/// trash is meant to ram walls. Optionally (see FindPath's avoidDeepWater)
/// also avoids every deep-water cell, for turtles specifically, and
/// (see FindPath's avoidCoral, on by default) every CoralReef cell, which
/// turtles alone opt out of since a reef is a wall for trash only. The
/// ResourceNode-derived part of the blocked-cell set (BuildBlockedCells) is
/// rebuilt fresh on every call — both FindPath's and HasLineOfSight's, the
/// latter now also keying off it (see SegmentCrossesResourceNodeCell) rather
/// than raw collider shapes, so a mover's line-of-sight shortcut always
/// agrees with what a full path would actually allow — rather than kept
/// incrementally in sync: node counts are modest, so rebuilding is cheap
/// even at HasLineOfSight's per-frame call rate during an aggro chase, and
/// this avoids invalidation bugs. "Fresh" means once per frame, shared by
/// every caller asking for the same set that frame (see blockedCellCache) —
/// still never stale across a frame boundary, so the invalidation-proof
/// property holds, but a storm's worth of chasing movers no longer each pay
/// for their own scan. The set handed back is consequently SHARED and must be
/// treated as read-only; FindPath copies it before applying its own
/// per-call mutations. The deep-water part is cached outright (see
/// deepWaterCells) since, unlike ResourceNode positions, it never changes at
/// runtime.
///
/// HasLineOfSight walks every grid cell a segment actually passes through
/// (SegmentCrossesBlockedCell, a proper voxel/grid traversal) rather than
/// sampling a fixed number of points along it — fixed sampling could skip
/// over a cell a diagonal-ish line only clips near a corner, which used to
/// let an aggro-chasing turtle believe it had a clear shot straight between
/// two resource nodes it could never actually fit through, then keep trying
/// to force its way in. This part is a pure accuracy fix, always on — a
/// segment with nothing along it still reports a clear line of sight exactly
/// as before, so a turtle's smooth direct-line diagonal steering in the open
/// is untouched. Separately, whenever a segment passes exactly through the
/// corner shared by four cells, the walk (and AStarPathfinder's own
/// corner-cutting guard, for a full path) requires both flanking cells open
/// UNLESS allowDiagonalSqueeze permits cutting it — the two flanks of any
/// single corner are always diagonal to *each other*, so this only ever
/// concerns squeezing between two obstacles that are themselves diagonal
/// from each other (e.g. one at a cell's top-right, another at its
/// bottom-left), never two obstacles directly beside or above/below each
/// other, which have no gap between them at all regardless of the flag.
/// Turtles pass this true (they're small enough to fit that genuine
/// diagonal gap) for both FindPath and HasLineOfSight, so the two always
/// agree on what a turtle can and can't squeeze through.
///
/// On top of that whole-cell model, HasLineOfSight takes a moverRadius (world
/// units, supplied by each mover from its own collider — see
/// TurtleAgent.MoverLineOfSightRadius; never a single number hardcoded here,
/// since a crab isn't a turtle). The center-line walk alone answers "does the
/// segment ENTER a blocked cell", which a line running flush along a blocked
/// cell's edge passes cleanly — and then the mover's actual body, which has
/// width, overhangs into that cell and grinds along the obstacle. With a
/// radius the walk additionally asks "does the segment come within moverRadius
/// of a blocked cell's rectangle" (SegmentViolatesClearance, a true
/// segment-to-AABB distance test against the cells neighboring the walk), so
/// the answer accounts for the body rather than a zero-width ray. moverRadius
/// 0 skips that phase entirely and reproduces the pure center-line behavior
/// exactly, so a caller that hasn't opted in is unaffected. The one deliberate
/// exemption: when allowDiagonalSqueeze is true, the two flanking cells of a
/// corner the center line legitimately crosses are exempt from the clearance
/// test — that corner IS the physical gap the flag exists to license, and
/// clearance would otherwise quietly close it back up. Nowhere else. Since the
/// two flanks of any one corner are always diagonal to each other (see above),
/// this can never exempt a wall of cardinally-adjacent obstacles.
/// </summary>
public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; }

    [SerializeField] private IslandGenerator islandGenerator;
    [Tooltip("If set above 0, also blocks nature obstacles' neighboring cells within this Euclidean distance (widens footprints). Fractional values matter: ~1.0 first adds just the orthogonal neighbors, ~1.41+ pulls in the diagonals too, so raising this is a gradual widening rather than a jump straight to a full ring. Default 0 matches actual collider sizes; raise this first if playtesting shows a mover clipping a tree/rock's edge.")]
    [SerializeField, Range(0f, 3f)] private float obstacleInflationRadius = 0f;

    [Header("Gizmos")]
    [Tooltip("Draw every cell obstacleInflationRadius currently blocks around each resource node in the Scene view, so its effect is visible without hand-tracing an A* result. Node's own cell in red, cells only blocked because of the inflation in yellow. Runs in edit mode too, but nodes are usually runtime-spawned, so nothing draws until Play generates the island.")]
    [SerializeField] private bool showObstacleGizmos = true;

    // Deep water never changes at runtime once the island is generated (unlike
    // ResourceNode positions, which come and go), so it's cached lazily instead
    // of rescanned every FindPath call — invalidated only if the island
    // regenerates.
    private HashSet<Vector3Int> deepWaterCells;

    // Frame-scoped obstacle cache. The blocked set is still rebuilt fresh
    // rather than kept incrementally in sync (see the class doc comment — that
    // is what makes invalidation bugs impossible), just no longer rebuilt
    // several times within one frame: during a storm every chasing mover asks
    // for the same set with the same key on the same frame, and the clearance
    // phase queries it more than the old center-line-only check did. Cleared
    // implicitly by the frame stamp, so it can never outlive a frame boundary.
    // Play mode only — the gizmo path deliberately keeps rebuilding, so
    // dragging obstacleInflationRadius in the Inspector redraws immediately.
    private readonly Dictionary<(float Inflation, bool IncludeCoral), HashSet<Vector3Int>> blockedCellCache =
        new Dictionary<(float, bool), HashSet<Vector3Int>>();
    private int blockedCellCacheFrame = -1;

    /// <summary>
    /// Completed routes, keyed by everything that can change the answer. Unlike
    /// the blocked-cell cache above this survives across frames, because the
    /// grid it searches is static between map changes — the same turtle asking
    /// again after delivering, or a second turtle setting off from the cell a
    /// first one just left, is the common case and it re-derives an identical
    /// path. Entries are handed out as COPIES (see FindPath): callers keep the
    /// list they're given and edit it (TurtleAgent trims and stitches legs
    /// together), which would otherwise corrupt the cached route for everyone.
    /// </summary>
    private readonly Dictionary<(Vector3Int Start, Vector3Int Goal, bool AvoidDeepWater, bool DiagonalSqueeze, float ExtraInflation, bool AvoidCoral), List<Vector3>> pathCache =
        new Dictionary<(Vector3Int, Vector3Int, bool, bool, float, bool), List<Vector3>>();

    /// <summary>What the obstacle set looked like when pathCache was last known good — see RefreshPathCacheValidity.</summary>
    private int pathCacheSignature = int.MinValue;

    // Reused by the line-of-sight walk instead of allocating per call: it runs
    // per-frame per chasing mover. Safe as static state because the walk is
    // never re-entrant — SegmentCrossesResourceNodeCell and
    // SegmentCrossesDeepWater call it one after the other, never nested, and
    // Unity gameplay code is single-threaded.
    private static readonly List<Vector3Int> visitedCells = new List<Vector3Int>();
    private static readonly HashSet<Vector3Int> cornerExemptCells = new HashSet<Vector3Int>();
    private static readonly HashSet<Vector3Int> clearanceCheckedCells = new HashSet<Vector3Int>();

    // One warning per session (per domain reload) — see WarnIfMoverRadiusTooBigForWaypoints.
    private static bool warnedOversizedMoverRadius;

    /// <summary>True if the completed-route cache should be consulted at all. Exists to be switched off, so the cost of pathfinding with and without it can be compared in a profiler without editing code.</summary>
    public bool CachePaths { get; set; } = true;

    /// <summary>Most routes held at once. Reached in practice only by movers setting off from a great many distinct cells; the whole cache is dropped rather than evicted one entry at a time, since rebuilding it costs one search per route that's actually asked for again.</summary>
    private const int MaxCachedPaths = 512;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PathfindingManager: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += InvalidateDeepWaterCache;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= InvalidateDeepWaterCache;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void InvalidateDeepWaterCache()
    {
        deepWaterCells = null;
        // A new map invalidates every route on the old one, and unlike the
        // obstacle signature below this is a change the signature can't see:
        // regeneration can easily leave the node and reef counts identical.
        pathCache.Clear();
    }

    /// <summary>
    /// Drops every cached route if the obstacle set has changed since they were
    /// found. Keyed on how many nodes and reefs exist rather than on where they
    /// are, which is exact here because neither ever moves — a node can only
    /// enter or leave the registry, and either changes the count. Island
    /// regeneration is the one case this can't see, and InvalidateDeepWaterCache
    /// covers that directly.
    /// </summary>
    private void RefreshPathCacheValidity()
    {
        int signature = ResourceNode.AllNodes.Count * 397 ^ CoralReef.AllReefs.Count;
        if (signature == pathCacheSignature) return;

        pathCacheSignature = signature;
        pathCache.Clear();
    }

    /// <summary>Highlights, for every registered ResourceNode, exactly the cells BuildBlockedCells would mark blocked at the current obstacleInflationRadius — a node's own cell in red, any neighbor pulled in purely by the inflation in yellow — so raising the radius in the Inspector shows its effect live rather than requiring a Play-mode path trace to confirm.</summary>
    private void OnDrawGizmos()
    {
        if (!showObstacleGizmos || islandGenerator == null) return;

        Tilemap grid = islandGenerator.WaterTilemap;
        if (grid == null) return;

        HashSet<Vector3Int> coreCells = new HashSet<Vector3Int>();
        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node != null) coreCells.Add(grid.WorldToCell(node.transform.position));
        }

        Vector3 cellSize = grid.cellSize;
        // Drawn with coral included — the fullest obstacle set anything in the
        // scene actually paths against (trash), so a reef shows up in the gizmos.
        foreach (Vector3Int cell in BuildBlockedCells(grid, obstacleInflationRadius, includeCoral: true))
        {
            Gizmos.color = coreCells.Contains(cell) ? new Color(1f, 0.15f, 0.1f, 0.65f) : new Color(1f, 0.85f, 0f, 0.35f);
            Gizmos.DrawWireCube(grid.GetCellCenterWorld(cell), cellSize);
        }
    }

    /// <summary>Finds a world-space waypoint path from start to goal avoiding nature, and — if avoidDeepWater is true — every deep-water cell too. Turtles always pass true (see TurtleAgent.BeginPathTo) so they can never path further out than the shallows; trash leaves this false since it must cross open water to reach shore. allowDiagonalSqueeze, if true, lets a diagonal move cut the corner between two obstacle-adjacent cells that are themselves diagonal from each other, rather than requiring at least one open — turtles pass this true (see the class doc comment for exactly what it does and doesn't permit: it can never open a path between two obstacles that are directly beside or above/below each other, only ones diagonal from each other, so it's safe for a small mover); trash leaves it false, keeping the strict corner-cutting guard. This never lets a path cut a corner across deep water regardless of the flag, since that's a hard depth limit, not a sizing issue. extraObstacleInflation adds on top of the shared obstacleInflationRadius for this call only (e.g. a bigger piece of trash passing its own size so its route avoids gaps only wide enough for something smaller) — most callers leave it 0. It's a float on the same fractional scale as obstacleInflationRadius (~1.0 reaches the orthogonal neighbors, ~1.41 the diagonals), so whole-number callers still read naturally while a caller can also ask for less than a full ring. avoidCoral, on by default, treats a Coral Reef as an obstacle like any resource node; turtles pass false (see CoralReef — the reef is a wall for trash only, and they swim straight through it) while trash takes the default. Returns null if unavailable/unreachable (callers should fall back to a direct route only when avoidDeepWater is false — see BeginPathTo), or an empty list if no intermediate waypoints are needed.</summary>
    public List<Vector3> FindPath(Vector3 start, Vector3 goal, bool avoidDeepWater = false, bool allowDiagonalSqueeze = false, float extraObstacleInflation = 0f, bool avoidCoral = true)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return null;

        Vector3Int startCell = grid.WorldToCell(start);
        Vector3Int goalCell = grid.WorldToCell(goal);

        float inflation = obstacleInflationRadius + Mathf.Max(0f, extraObstacleInflation);
        var cacheKey = (startCell, goalCell, avoidDeepWater, allowDiagonalSqueeze, inflation, avoidCoral);

        if (CachePaths)
        {
            RefreshPathCacheValidity();
            if (pathCache.TryGetValue(cacheKey, out List<Vector3> cached))
            {
                // A cached null means "searched, unreachable" — worth keeping,
                // since a failed search is the most expensive kind (it expands
                // everything it can reach before giving up) and a mover with
                // nowhere to go asks again and again.
                return cached != null ? new List<Vector3>(cached) : null;
            }
        }

        // Used in place, NOT copied: BuildBlockedCells hands back a set shared
        // with every other caller on this frame, and the exemptions below now
        // go to the search as cells rather than being applied by mutating a
        // private copy. Deep water likewise goes across as its own set instead
        // of being unioned in, which used to mean copying a four-figure number
        // of cells on every single request.
        HashSet<Vector3Int> blocked = BuildBlockedCells(grid, inflation, avoidCoral);
        HashSet<Vector3Int> deepWater = avoidDeepWater ? GetDeepWaterCells(grid) : null;

        // The destination and starting cell are always reachable regardless of
        // what occupies them — a mover needs to path onto a resource/building's
        // own cell to bounce-interact with it, and a mover whose current
        // position happens to round onto a "blocked" cell (physics jitter)
        // should never immediately fail to path at all. Exception: a deep-water
        // goal cell stays blocked when avoidDeepWater is set, or this exemption
        // would defeat the whole point — a mover that must avoid deep water can
        // never treat a deep-water destination as reachable.
        Vector3Int? goalExemption = deepWater == null || !deepWater.Contains(goalCell) ? goalCell : (Vector3Int?)null;

        List<Vector3Int> cellPath = AStarPathfinder.FindPathCells(
            startCell, goalCell, grid.cellBounds, blocked, allowDiagonalSqueeze, deepWater, startCell, goalExemption);

        List<Vector3> waypoints = null;
        if (cellPath != null)
        {
            waypoints = new List<Vector3>(Mathf.Max(0, cellPath.Count - 1));
            for (int i = 1; i < cellPath.Count; i++)
            {
                waypoints.Add(grid.GetCellCenterWorld(cellPath[i]));
            }
        }

        if (CachePaths) StorePath(cacheKey, waypoints);

        // Copied on the way out for the same reason cached hits are: callers
        // trim and stitch what they're handed (see TurtleAgent.BeginPathTo and
        // FindPathOutOfDeepWaterIfNeeded), and the cache is holding this list.
        return waypoints != null ? new List<Vector3>(waypoints) : null;
    }

    /// <summary>Files a completed route under key. The whole cache is dropped on overflow rather than evicting a single entry — there's no access ordering kept to evict by, and the routes that matter are re-found the next time they're asked for.</summary>
    private void StorePath((Vector3Int, Vector3Int, bool, bool, float, bool) key, List<Vector3> waypoints)
    {
        if (pathCache.Count >= MaxCachedPaths) pathCache.Clear();

        pathCache[key] = waypoints;
    }

    /// <summary>
    /// True if no nature obstacle sits between from and to AND the straight
    /// segment between them never dips into deep water — used so a mover
    /// chasing a visible target (e.g. a turtle aggro-chasing trash, or one
    /// closing the last couple tiles on a resource node it's harvesting) can
    /// skip pathfinding entirely and just steer straight at it. The deep-water
    /// check matters even when neither endpoint itself is in deep water: two
    /// shallow-water/shore points on either side of a bay can still have open
    /// ocean directly between them, and without this a turtle would swim
    /// straight across it to close the gap — this is the same "never cross
    /// open ocean" rule FindPath's avoidDeepWater already enforces for normal
    /// pathfinding, just also applied to this direct-steer shortcut. The
    /// obstacle check blocks on the same whole-cell model FindPath's own
    /// BuildBlockedCells uses (see SegmentCrossesResourceNodeCell) rather than
    /// the ResourceNode colliders' actual (usually much smaller) physical
    /// shapes — otherwise a straight line can thread a "gap" between two
    /// adjacent nodes' real colliders that's narrower than a full cell, which
    /// a mover respecting the grid could never actually fit through, letting
    /// it weave somewhere FindPath itself would have routed around.
    /// Everything else (the turtle's/target's own colliders, buildings, other
    /// trash, etc.) never blocks line of sight. ignoreTarget optionally
    /// exempts one specific ResourceNode's own cell from that check — needed
    /// when to itself is a ResourceNode, since the line would otherwise always
    /// end inside the destination's own cell and self-block. allowDiagonalSqueeze
    /// mirrors FindPath's own parameter of the same name and meaning — see its
    /// tooltip — but only for the resource-obstacle check; deep water always
    /// keeps the strict corner guard regardless, same as FindPath. avoidCoral
    /// likewise mirrors FindPath's parameter, so a mover's shortcut and its
    /// full path agree about coral too. moverRadius (world units, 0 = the old
    /// zero-width line) is how wide the asking mover physically is, and makes
    /// the obstacle check reject a line that merely runs FLUSH past a blocked
    /// cell rather than only one that enters it — see the class doc comment for
    /// the geometry and for the one case it deliberately still allows (a
    /// diagonal squeeze under allowDiagonalSqueeze). Each mover passes its own
    /// collider-derived value; nothing here assumes a size.
    /// </summary>
    public bool HasLineOfSight(Vector3 from, Vector3 to, Transform ignoreTarget = null, float extraObstacleInflation = 0f, bool allowDiagonalSqueeze = false, bool avoidCoral = true, float moverRadius = 0f)
    {
        Vector2 origin = from;
        Vector2 target = to;
        if (Vector2.Distance(origin, target) < 0.0001f) return true;

        if (moverRadius > 0f) WarnIfMoverRadiusTooBigForWaypoints(moverRadius);

        if (SegmentCrossesResourceNodeCell(origin, target, ignoreTarget, extraObstacleInflation, allowDiagonalSqueeze, avoidCoral, moverRadius)) return false;

        return !SegmentCrossesDeepWater(origin, target);
    }

    /// <summary>
    /// Warns once (per domain reload) if a mover asks for line of sight with a
    /// radius bigger than half a grid cell. FindPath hands back cell CENTERS as
    /// waypoints, so a mover only reliably fits the routes it's given while its
    /// radius stays inside half a cell — past that it can clip an obstacle
    /// while faithfully following a path that the grid considers clear, which
    /// looks like a pathfinding bug and isn't one. Deliberately not clamped:
    /// silently shrinking the radius would hide exactly the setup mistake this
    /// is meant to surface, so a future bigger creature fails loudly here
    /// instead of clipping mysteriously in play. The fix for such a mover is
    /// extraObstacleInflation on BOTH FindPath and HasLineOfSight (widening the
    /// obstacles themselves, which moves the waypoints too), with moverRadius
    /// left to handle only the sub-cell flush-alongside case.
    /// </summary>
    private void WarnIfMoverRadiusTooBigForWaypoints(float moverRadius)
    {
        if (warnedOversizedMoverRadius) return;

        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return;

        float halfCell = Mathf.Min(grid.cellSize.x, grid.cellSize.y) * 0.5f;
        if (moverRadius <= halfCell) return;

        warnedOversizedMoverRadius = true;
        Debug.LogWarning($"PathfindingManager: a mover passed moverRadius {moverRadius:0.###} to HasLineOfSight, which is larger than half a grid cell ({halfCell:0.###}). FindPath's waypoints are cell centers, so a mover this wide cannot reliably follow the paths it is given — give it extraObstacleInflation on both FindPath and HasLineOfSight instead of relying on the line-of-sight radius alone. Shown once per play session.", this);
    }

    /// <summary>True if any grid cell the origin→target segment actually passes through (per SegmentCrossesBlockedCell — every cell it touches, not a handful of sampled points) currently has a ResourceNode in it (per BuildBlockedCells — the whole cell, inflated the same as FindPath's obstacleInflationRadius plus extraObstacleInflation on top, not just wherever that node's own collider happens to sit). extraObstacleInflation widens the effective footprint to account for the mover's own width (e.g. a turtle's aggro-chase shortcut), so a gap too narrow for the mover's actual body correctly still blocks line of sight, matching FindPath's own extraObstacleInflation parameter for the same concern. ignoreTarget optionally exempts one specific node's own cell (see HasLineOfSight).</summary>
    private bool SegmentCrossesResourceNodeCell(Vector2 origin, Vector2 target, Transform ignoreTarget, float extraObstacleInflation, bool allowDiagonalSqueeze, bool avoidCoral, float moverRadius)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        HashSet<Vector3Int> nodeCells = BuildBlockedCells(grid, obstacleInflationRadius + Mathf.Max(0f, extraObstacleInflation), avoidCoral);
        Vector3Int? ignoreCell = ignoreTarget != null ? grid.WorldToCell(ignoreTarget.position) : (Vector3Int?)null;

        // The clearance phase asks this same delegate rather than the raw set,
        // so the ignoreTarget exemption covers it for free — a mover closing on
        // a node it's harvesting isn't blocked by that node's own cell whether
        // the line enters it or merely runs near it. Nothing to duplicate.
        bool IsBlocked(Vector3Int cell) => nodeCells.Contains(cell) && (!ignoreCell.HasValue || cell != ignoreCell.Value);

        return SegmentCrossesBlockedCell(grid, origin, target, IsBlocked, allowDiagonalSqueeze, moverRadius);
    }

    /// <summary>True if any grid cell the origin→target segment actually passes through is deep water, so a straight-line shortcut can never cut across a stretch of it lying between two otherwise-valid endpoints. Always uses the strict corner guard (allowDiagonalSqueeze: false) regardless of what the resource-obstacle check was allowed — deep water is a hard depth limit, not a sizing issue, same as FindPath's own alwaysCornerBlockedCells treatment of it. Passes moverRadius 0 for the same reason: how wide a mover is has no bearing on how deep the water is, and a turtle skimming the edge of an open-ocean cell is fine as long as its center line stays out — so this stays the pure center-line check it has always been.</summary>
    private bool SegmentCrossesDeepWater(Vector2 origin, Vector2 target)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        HashSet<Vector3Int> deepWater = GetDeepWaterCells(grid);
        return SegmentCrossesBlockedCell(grid, origin, target, deepWater.Contains, allowDiagonalSqueeze: false, moverRadius: 0f);
    }

    /// <summary>
    /// Walks every cell the origin→target segment geometrically passes
    /// through — a voxel/grid traversal (Amanatides-Woo style), not fixed-
    /// interval sampling — stepping one grid boundary at a time so no cell
    /// the line actually clips is ever skipped, however shallow the angle.
    /// Whenever the segment passes exactly through the corner shared by four
    /// cells, treats it like a diagonal move: with allowDiagonalSqueeze false,
    /// requires both orthogonal flanking cells to be open (same rule
    /// AStarPathfinder.FindPathCells applies to a diagonal step) rather than
    /// letting isBlocked's per-cell check alone decide — a real mover has
    /// width and can't thread a gap that's only open at a single point. With
    /// it true, skips that flank check entirely (same as FindPathCells) —
    /// note the two flanking cells of any single corner are always diagonal
    /// to *each other*, never two cardinally-adjacent cells (a straight wall
    /// of side-by-side obstacles), so this never opens a path through one of
    /// those regardless of the flag; it only ever concerns a squeeze between
    /// two obstacles that are themselves diagonal from each other. isBlocked
    /// is queried starting from the very first cell (origin's own), so a
    /// caller whose start happens to sit in a "blocked" cell still gets a
    /// meaningful answer rather than an immediate false positive/negative
    /// depending on how the walk began.
    ///
    /// With a non-zero moverRadius a second phase runs after the walk
    /// (SegmentViolatesClearance), rejecting segments that pass within
    /// moverRadius of a blocked cell without ever entering it. It runs after
    /// rather than during the walk on purpose: the diagonal-squeeze exemptions
    /// are discovered by the walk, and a violation can be spotted a step or two
    /// BEFORE the corner tie that exempts it, so checking inline would reject
    /// legitimate squeezes depending purely on which end of the segment the
    /// mover happened to be. At moverRadius 0 the phase is skipped outright and
    /// this method behaves exactly as it always has.
    /// </summary>
    private static bool SegmentCrossesBlockedCell(Tilemap grid, Vector2 origin, Vector2 target, Func<Vector3Int, bool> isBlocked, bool allowDiagonalSqueeze, float moverRadius)
    {
        bool useClearance = moverRadius > 0f;
        if (useClearance)
        {
            visitedCells.Clear();
            cornerExemptCells.Clear();
        }

        if (TraverseCenterLine(grid, origin, target, isBlocked, allowDiagonalSqueeze, useClearance)) return true;

        return useClearance && SegmentViolatesClearance(grid, origin, target, isBlocked, moverRadius);
    }

    /// <summary>The center-line walk itself — see SegmentCrossesBlockedCell. True if the segment enters a blocked cell (or, under the strict corner guard, tries to thread a blocked corner). recordForClearance additionally logs every visited cell and every squeeze-exempt corner flank for the clearance phase to consult; it changes nothing about the answer.</summary>
    private static bool TraverseCenterLine(Tilemap grid, Vector2 origin, Vector2 target, Func<Vector3Int, bool> isBlocked, bool allowDiagonalSqueeze, bool recordForClearance)
    {
        Vector3Int cell = grid.WorldToCell(origin);
        Vector3Int endCell = grid.WorldToCell(target);

        if (isBlocked(cell)) return true;
        if (recordForClearance) visitedCells.Add(cell);
        if (cell == endCell) return false;

        Vector2 offset = target - origin;
        int stepX = offset.x > 0f ? 1 : (offset.x < 0f ? -1 : 0);
        int stepY = offset.y > 0f ? 1 : (offset.y < 0f ? -1 : 0);

        float cellSizeX = Mathf.Max(grid.cellSize.x, 0.0001f);
        float cellSizeY = Mathf.Max(grid.cellSize.y, 0.0001f);

        Vector3 cellOrigin = grid.CellToWorld(cell);
        float boundaryX = cellOrigin.x + (stepX > 0 ? cellSizeX : 0f);
        float boundaryY = cellOrigin.y + (stepY > 0 ? cellSizeY : 0f);

        float tMaxX = stepX != 0 ? (boundaryX - origin.x) / offset.x : float.PositiveInfinity;
        float tMaxY = stepY != 0 ? (boundaryY - origin.y) / offset.y : float.PositiveInfinity;
        float tDeltaX = stepX != 0 ? cellSizeX / Mathf.Abs(offset.x) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? cellSizeY / Mathf.Abs(offset.y) : float.PositiveInfinity;

        // Every step advances exactly one cell along one (or, at a corner
        // tie, both) axis, so a walk from cell to endCell can never take
        // more than this many steps — a safety bound against looping forever
        // on a degenerate/edge-case segment rather than trusting the float
        // math to land exactly on endCell.
        int maxSteps = Mathf.Abs(endCell.x - cell.x) + Mathf.Abs(endCell.y - cell.y) + 4;

        for (int i = 0; i < maxSteps; i++)
        {
            bool isCornerTie = stepX != 0 && stepY != 0 && Mathf.Abs(tMaxX - tMaxY) <= 0.0001f;

            if (isCornerTie)
            {
                Vector3Int flankA = new Vector3Int(cell.x + stepX, cell.y, cell.z);
                Vector3Int flankB = new Vector3Int(cell.x, cell.y + stepY, cell.z);

                if (!allowDiagonalSqueeze)
                {
                    if (isBlocked(flankA) || isBlocked(flankB)) return true;
                }
                else if (recordForClearance)
                {
                    // The single place body width is deliberately ignored: this
                    // corner is the diagonal gap allowDiagonalSqueeze exists to
                    // license, and a mover with any real radius is necessarily
                    // within that radius of both flanks while threading it — so
                    // applying clearance here would close every squeeze the
                    // flag is supposed to keep open, and silently disagree with
                    // FindPath, which still allows the same corner. Exempting
                    // by cell (not by "skip this step") keeps it narrow: only
                    // these two cells, which are always diagonal to each other,
                    // are ever spared, so it can't open a wall of adjacent
                    // obstacles. Any other blocked cell near the segment,
                    // including at this same step, still blocks normally.
                    cornerExemptCells.Add(flankA);
                    cornerExemptCells.Add(flankB);
                }

                cell = new Vector3Int(cell.x + stepX, cell.y + stepY, cell.z);
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                cell = new Vector3Int(cell.x + stepX, cell.y, cell.z);
                tMaxX += tDeltaX;
            }
            else
            {
                cell = new Vector3Int(cell.x, cell.y + stepY, cell.z);
                tMaxY += tDeltaY;
            }

            if (isBlocked(cell)) return true;
            if (recordForClearance) visitedCells.Add(cell);
            if (cell == endCell) return false;
        }

        return false;
    }

    /// <summary>
    /// The body-width half of the check: true if the segment passes within
    /// moverRadius of any blocked cell's rectangle, even one its center line
    /// never enters. Sweeps a square window of ceil(moverRadius / cellSize)
    /// cells around every cell the walk visited — for the sub-cell radii real
    /// movers have, that's just the 8 neighbors — and measures the true
    /// distance from the segment to each blocked neighbor's world-space AABB.
    /// Windows of consecutive visited cells overlap heavily, so cells are
    /// deduplicated through a reused set rather than re-measured. Corner flanks
    /// the walk marked exempt (see TraverseCenterLine) are skipped, which is
    /// what keeps a licensed diagonal squeeze open.
    /// </summary>
    private static bool SegmentViolatesClearance(Tilemap grid, Vector2 origin, Vector2 target, Func<Vector3Int, bool> isBlocked, float moverRadius)
    {
        Vector2 cellSize = new Vector2(Mathf.Max(grid.cellSize.x, 0.0001f), Mathf.Max(grid.cellSize.y, 0.0001f));
        int window = Mathf.CeilToInt(moverRadius / Mathf.Min(cellSize.x, cellSize.y));
        if (window <= 0) return false;

        clearanceCheckedCells.Clear();

        for (int i = 0; i < visitedCells.Count; i++)
        {
            Vector3Int visited = visitedCells[i];

            for (int dx = -window; dx <= window; dx++)
            {
                for (int dy = -window; dy <= window; dy++)
                {
                    Vector3Int neighbor = new Vector3Int(visited.x + dx, visited.y + dy, visited.z);

                    if (!clearanceCheckedCells.Add(neighbor)) continue;
                    if (cornerExemptCells.Contains(neighbor)) continue;
                    if (!isBlocked(neighbor)) continue;

                    Rect rect = new Rect((Vector2)grid.CellToWorld(neighbor), cellSize);
                    if (SegmentToRectDistance(origin, target, rect) < moverRadius - 0.0001f) return true;
                }
            }
        }

        return false;
    }

    /// <summary>Shortest distance from segment a→b to an axis-aligned rectangle: 0 if the two touch or overlap at all, otherwise the closest approach to its outline. Allocation-free (all struct math), since this runs a couple dozen times per line-of-sight check per chasing mover.</summary>
    private static float SegmentToRectDistance(Vector2 a, Vector2 b, Rect rect)
    {
        // Catches both "an endpoint is inside" and "the whole segment is
        // inside" — the latter touches no edge, so the edge sweep below would
        // otherwise report a positive distance for a segment sitting squarely
        // within the rectangle.
        if (rect.Contains(a) || rect.Contains(b)) return 0f;

        Vector2 bottomLeft = rect.min;
        Vector2 topRight = rect.max;
        Vector2 bottomRight = new Vector2(topRight.x, bottomLeft.y);
        Vector2 topLeft = new Vector2(bottomLeft.x, topRight.y);

        float distance = SegmentToSegmentDistance(a, b, bottomLeft, bottomRight);
        distance = Mathf.Min(distance, SegmentToSegmentDistance(a, b, bottomRight, topRight));
        distance = Mathf.Min(distance, SegmentToSegmentDistance(a, b, topRight, topLeft));
        distance = Mathf.Min(distance, SegmentToSegmentDistance(a, b, topLeft, bottomLeft));
        return distance;
    }

    /// <summary>Shortest distance between two segments in 2D: 0 if they cross, otherwise the smallest of the four endpoint-to-other-segment distances (the closest approach of two non-crossing segments is always at one of their endpoints).</summary>
    private static float SegmentToSegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (SegmentsProperlyIntersect(a, b, c, d)) return 0f;

        float distance = PointToSegmentDistance(a, c, d);
        distance = Mathf.Min(distance, PointToSegmentDistance(b, c, d));
        distance = Mathf.Min(distance, PointToSegmentDistance(c, a, b));
        distance = Mathf.Min(distance, PointToSegmentDistance(d, a, b));
        return distance;
    }

    /// <summary>True if the two segments cross at a single interior point. Deliberately reports false for the collinear/touching-at-an-endpoint cases rather than special-casing them: those all have an endpoint lying on the other segment, so the endpoint sweep in SegmentToSegmentDistance already returns 0 for them.</summary>
    private static bool SegmentsProperlyIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(d - c, a - c);
        float d2 = Cross(d - c, b - c);
        float d3 = Cross(b - a, c - a);
        float d4 = Cross(b - a, d - a);

        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
            && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Cross(Vector2 lhs, Vector2 rhs) => lhs.x * rhs.y - lhs.y * rhs.x;

    private static float PointToSegmentDistance(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        float t = lengthSquared > 0.0000001f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared) : 0f;
        return Vector2.Distance(point, a + t * ab);
    }

    /// <summary>Builds the obstacle cell set. includeCoral folds CoralReef.AllReefs in alongside the resource nodes, giving coral the same inflation and gap-closing treatment as a palm tree — callers that pass false (turtles, which physically pass through coral) see nature obstacles only. The returned set is SHARED with every other caller using the same key on the same frame (see blockedCellCache) — treat it as read-only and copy it if you need to mutate, as FindPath does.</summary>
    private HashSet<Vector3Int> BuildBlockedCells(Tilemap grid, float inflationRadius, bool includeCoral)
    {
        if (!Application.isPlaying) return BuildBlockedCellsUncached(grid, inflationRadius, includeCoral);

        if (blockedCellCacheFrame != Time.frameCount)
        {
            blockedCellCacheFrame = Time.frameCount;
            blockedCellCache.Clear();
        }

        (float, bool) key = (inflationRadius, includeCoral);
        if (blockedCellCache.TryGetValue(key, out HashSet<Vector3Int> cached)) return cached;

        HashSet<Vector3Int> built = BuildBlockedCellsUncached(grid, inflationRadius, includeCoral);
        blockedCellCache[key] = built;
        return built;
    }

    private HashSet<Vector3Int> BuildBlockedCellsUncached(Tilemap grid, float inflationRadius, bool includeCoral)
    {
        HashSet<Vector3Int> nodeCells = new HashSet<Vector3Int>();
        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node != null) nodeCells.Add(grid.WorldToCell(node.transform.position));
        }

        if (includeCoral)
        {
            foreach (CoralReef reef in CoralReef.AllReefs)
            {
                if (reef != null) nodeCells.Add(grid.WorldToCell(reef.transform.position));
            }
        }

        HashSet<Vector3Int> blocked = new HashSet<Vector3Int>(nodeCells);

        int ringRadius = Mathf.CeilToInt(inflationRadius);
        if (ringRadius > 0)
        {
            foreach (Vector3Int cell in nodeCells)
            {
                for (int dx = -ringRadius; dx <= ringRadius; dx++)
                {
                    for (int dy = -ringRadius; dy <= ringRadius; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        // Euclidean, not Chebyshev, so a fractional radius can include
                        // orthogonal neighbors (distance 1) without yet reaching the
                        // diagonals (distance ~1.41) — see the field's tooltip.
                        if (Mathf.Sqrt(dx * dx + dy * dy) > inflationRadius + 0.0001f) continue;
                        blocked.Add(new Vector3Int(cell.x + dx, cell.y + dy, cell.z));
                    }
                }
            }
        }

        // Closes a single-cell orthogonal gap between two resource nodes
        // exactly two cells apart in the same row/column — narrower in
        // practice than a turtle's body even though the grid alone would call
        // it passable. Deliberately does NOT touch two nodes diagonal from
        // each other: that gap is comfortably turtle-width, and turtles are
        // meant to cut straight across it (see allowDiagonalSqueeze) — the
        // old uniform-radius inflation above used to block this same middle
        // cell as a side effect of closing the orthogonal gap, which also
        // wrecked the diagonal cut between an unrelated pair of nodes sharing
        // that cell as one of their flanks. Checking only the +X/+Y direction
        // from every node still finds every such pair exactly once.
        foreach (Vector3Int cell in nodeCells)
        {
            BlockOrthogonalGapIfPresent(cell, new Vector3Int(2, 0, 0), nodeCells, blocked);
            BlockOrthogonalGapIfPresent(cell, new Vector3Int(0, 2, 0), nodeCells, blocked);
        }

        return blocked;
    }

    private static void BlockOrthogonalGapIfPresent(Vector3Int cell, Vector3Int doubleOffset, HashSet<Vector3Int> nodeCells, HashSet<Vector3Int> blocked)
    {
        if (!nodeCells.Contains(cell + doubleOffset)) return;

        blocked.Add(cell + doubleOffset / 2);
    }

    /// <summary>True if worldPosition falls in deep water — used e.g. by idle wander to re-roll a target that would otherwise land in the ocean rather than only discovering it after BeginPathTo refuses to path there.</summary>
    public bool IsDeepWater(Vector3 worldPosition)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        return islandGenerator.IsDeepWater(grid.WorldToCell(worldPosition));
    }

    /// <summary>True if worldPosition falls on a painted Sand tile — the simple land/water split (unlike IsDeepWater, which only tells deep water apart from shallow water+land) used e.g. by TurtleLocomotion to recolor its wake/trail particles by surface. Returns false (treated as water) if the Sand Tilemap isn't assigned.</summary>
    public bool IsOnLand(Vector3 worldPosition)
    {
        Tilemap sand = islandGenerator != null ? islandGenerator.SandTilemap : null;
        if (sand == null) return false;

        return sand.HasTile(sand.WorldToCell(worldPosition));
    }

    /// <summary>Finds the nearest non-deep-water cell center to worldPosition — used to give a turtle chasing a target that's drifted into deep water (e.g. storm trash) a concrete shoreline point to swim to and hold at, instead of freezing wherever it happened to be. Searches outward ring by ring (Chebyshev distance) since the island's exact shape isn't known analytically, preferring cells that aren't obstacles either so the turtle isn't sent to hold station inside a palm tree or a reef; a ring offering only blocked-but-shallow cells is remembered as a fallback and used only if no open cell turns up anywhere, since standing in an obstacle still beats standing in the ocean. Falls back to worldPosition itself if the whole map is deep water or no grid exists — shouldn't happen with a real generated island.</summary>
    public Vector3 NearestNonDeepWaterPoint(Vector3 worldPosition)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return worldPosition;

        HashSet<Vector3Int> deepWater = GetDeepWaterCells(grid);
        HashSet<Vector3Int> blocked = BuildBlockedCells(grid, obstacleInflationRadius, includeCoral: true);

        // Still IsDeepWater rather than the cell set for the mover's own cell:
        // the set only covers cellBounds, and a position that has drifted
        // outside it must not read as "shallow, stay put".
        Vector3Int center = grid.WorldToCell(worldPosition);
        if (!IsDeepWater(worldPosition) && !blocked.Contains(center)) return grid.GetCellCenterWorld(center);

        int maxRadius = Mathf.Max(grid.cellBounds.size.x, grid.cellBounds.size.y);
        Vector3Int blockedFallback = default;
        float blockedFallbackSqrDistance = float.MaxValue;
        bool hasBlockedFallback = false;

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            Vector3Int best = default;
            float bestSqrDistance = float.MaxValue;
            bool found = false;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Only the ring's perimeter — smaller radii already covered the interior.
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;

                    Vector3Int cell = new Vector3Int(center.x + dx, center.y + dy, center.z);
                    if (!grid.cellBounds.Contains(cell) || deepWater.Contains(cell)) continue;

                    Vector3 cellCenter = grid.GetCellCenterWorld(cell);
                    float sqrDistance = ((Vector2)cellCenter - (Vector2)worldPosition).sqrMagnitude;

                    if (blocked.Contains(cell))
                    {
                        if (sqrDistance < blockedFallbackSqrDistance)
                        {
                            blockedFallbackSqrDistance = sqrDistance;
                            blockedFallback = cell;
                            hasBlockedFallback = true;
                        }

                        continue;
                    }

                    if (sqrDistance < bestSqrDistance)
                    {
                        bestSqrDistance = sqrDistance;
                        best = cell;
                        found = true;
                    }
                }
            }

            if (found) return grid.GetCellCenterWorld(best);
        }

        if (hasBlockedFallback) return grid.GetCellCenterWorld(blockedFallback);

        return worldPosition;
    }

    private HashSet<Vector3Int> GetDeepWaterCells(Tilemap grid)
    {
        if (deepWaterCells != null) return deepWaterCells;

        deepWaterCells = new HashSet<Vector3Int>();
        if (islandGenerator == null) return deepWaterCells;

        foreach (Vector3Int cell in grid.cellBounds.allPositionsWithin)
        {
            if (islandGenerator.IsDeepWater(cell)) deepWaterCells.Add(cell);
        }

        return deepWaterCells;
    }
}
