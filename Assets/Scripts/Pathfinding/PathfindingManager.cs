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
/// also avoids every deep-water cell, for turtles specifically. The
/// ResourceNode-derived part of the blocked-cell set (BuildBlockedCells) is
/// rebuilt fresh on every call — both FindPath's and HasLineOfSight's, the
/// latter now also keying off it (see SegmentCrossesResourceNodeCell) rather
/// than raw collider shapes, so a mover's line-of-sight shortcut always
/// agrees with what a full path would actually allow — rather than kept
/// incrementally in sync: node counts are modest, so rebuilding is cheap
/// even at HasLineOfSight's per-frame call rate during an aggro chase, and
/// this avoids invalidation bugs. The deep-water part is cached instead (see
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

    private void InvalidateDeepWaterCache() => deepWaterCells = null;

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
        foreach (Vector3Int cell in BuildBlockedCells(grid, obstacleInflationRadius))
        {
            Gizmos.color = coreCells.Contains(cell) ? new Color(1f, 0.15f, 0.1f, 0.65f) : new Color(1f, 0.85f, 0f, 0.35f);
            Gizmos.DrawWireCube(grid.GetCellCenterWorld(cell), cellSize);
        }
    }

    /// <summary>Finds a world-space waypoint path from start to goal avoiding nature, and — if avoidDeepWater is true — every deep-water cell too. Turtles always pass true (see TurtleAgent.BeginPathTo) so they can never path further out than the shallows; trash leaves this false since it must cross open water to reach shore. allowDiagonalSqueeze, if true, lets a diagonal move cut the corner between two obstacle-adjacent cells that are themselves diagonal from each other, rather than requiring at least one open — turtles pass this true (see the class doc comment for exactly what it does and doesn't permit: it can never open a path between two obstacles that are directly beside or above/below each other, only ones diagonal from each other, so it's safe for a small mover); trash leaves it false, keeping the strict corner-cutting guard. This never lets a path cut a corner across deep water regardless of the flag, since that's a hard depth limit, not a sizing issue. extraObstacleInflation adds on top of the shared obstacleInflationRadius for this call only (e.g. a bigger piece of trash passing its own size so its route avoids gaps only wide enough for something smaller) — most callers leave it 0. Returns null if unavailable/unreachable (callers should fall back to a direct route only when avoidDeepWater is false — see BeginPathTo), or an empty list if no intermediate waypoints are needed.</summary>
    public List<Vector3> FindPath(Vector3 start, Vector3 goal, bool avoidDeepWater = false, bool allowDiagonalSqueeze = false, int extraObstacleInflation = 0)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return null;

        Vector3Int startCell = grid.WorldToCell(start);
        Vector3Int goalCell = grid.WorldToCell(goal);

        HashSet<Vector3Int> blocked = BuildBlockedCells(grid, obstacleInflationRadius + Mathf.Max(0, extraObstacleInflation));
        HashSet<Vector3Int> deepWater = avoidDeepWater ? GetDeepWaterCells(grid) : null;
        if (deepWater != null) blocked.UnionWith(deepWater);

        // The destination and starting cell are always reachable regardless of
        // what occupies them — a mover needs to path onto a resource/building's
        // own cell to bounce-interact with it, and a mover whose current
        // position happens to round onto a "blocked" cell (physics jitter)
        // should never immediately fail to path at all. Exception: a deep-water
        // goal cell stays blocked when avoidDeepWater is set, or this exemption
        // would defeat the whole point — a mover that must avoid deep water can
        // never treat a deep-water destination as reachable.
        blocked.Remove(startCell);
        if (deepWater == null || !deepWater.Contains(goalCell)) blocked.Remove(goalCell);

        List<Vector3Int> cellPath = AStarPathfinder.FindPathCells(startCell, goalCell, grid.cellBounds, blocked, allowDiagonalSqueeze, deepWater);
        if (cellPath == null) return null;
        if (cellPath.Count <= 1) return new List<Vector3>();

        List<Vector3> waypoints = new List<Vector3>(cellPath.Count - 1);
        for (int i = 1; i < cellPath.Count; i++)
        {
            waypoints.Add(grid.GetCellCenterWorld(cellPath[i]));
        }

        return waypoints;
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
    /// keeps the strict corner guard regardless, same as FindPath.
    /// </summary>
    public bool HasLineOfSight(Vector3 from, Vector3 to, Transform ignoreTarget = null, int extraObstacleInflation = 0, bool allowDiagonalSqueeze = false)
    {
        Vector2 origin = from;
        Vector2 target = to;
        if (Vector2.Distance(origin, target) < 0.0001f) return true;

        if (SegmentCrossesResourceNodeCell(origin, target, ignoreTarget, extraObstacleInflation, allowDiagonalSqueeze)) return false;

        return !SegmentCrossesDeepWater(origin, target);
    }

    /// <summary>True if any grid cell the origin→target segment actually passes through (per SegmentCrossesBlockedCell — every cell it touches, not a handful of sampled points) currently has a ResourceNode in it (per BuildBlockedCells — the whole cell, inflated the same as FindPath's obstacleInflationRadius plus extraObstacleInflation on top, not just wherever that node's own collider happens to sit). extraObstacleInflation widens the effective footprint to account for the mover's own width (e.g. a turtle's aggro-chase shortcut), so a gap too narrow for the mover's actual body correctly still blocks line of sight, matching FindPath's own extraObstacleInflation parameter for the same concern. ignoreTarget optionally exempts one specific node's own cell (see HasLineOfSight).</summary>
    private bool SegmentCrossesResourceNodeCell(Vector2 origin, Vector2 target, Transform ignoreTarget, int extraObstacleInflation, bool allowDiagonalSqueeze)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        HashSet<Vector3Int> nodeCells = BuildBlockedCells(grid, obstacleInflationRadius + Mathf.Max(0, extraObstacleInflation));
        Vector3Int? ignoreCell = ignoreTarget != null ? grid.WorldToCell(ignoreTarget.position) : (Vector3Int?)null;

        bool IsBlocked(Vector3Int cell) => nodeCells.Contains(cell) && (!ignoreCell.HasValue || cell != ignoreCell.Value);

        return SegmentCrossesBlockedCell(grid, origin, target, IsBlocked, allowDiagonalSqueeze);
    }

    /// <summary>True if any grid cell the origin→target segment actually passes through is deep water, so a straight-line shortcut can never cut across a stretch of it lying between two otherwise-valid endpoints. Always uses the strict corner guard (allowDiagonalSqueeze: false) regardless of what the resource-obstacle check was allowed — deep water is a hard depth limit, not a sizing issue, same as FindPath's own alwaysCornerBlockedCells treatment of it.</summary>
    private bool SegmentCrossesDeepWater(Vector2 origin, Vector2 target)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        HashSet<Vector3Int> deepWater = GetDeepWaterCells(grid);
        return SegmentCrossesBlockedCell(grid, origin, target, deepWater.Contains, allowDiagonalSqueeze: false);
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
    /// </summary>
    private static bool SegmentCrossesBlockedCell(Tilemap grid, Vector2 origin, Vector2 target, Func<Vector3Int, bool> isBlocked, bool allowDiagonalSqueeze)
    {
        Vector3Int cell = grid.WorldToCell(origin);
        Vector3Int endCell = grid.WorldToCell(target);

        if (isBlocked(cell)) return true;
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
                if (!allowDiagonalSqueeze)
                {
                    Vector3Int flankA = new Vector3Int(cell.x + stepX, cell.y, cell.z);
                    Vector3Int flankB = new Vector3Int(cell.x, cell.y + stepY, cell.z);
                    if (isBlocked(flankA) || isBlocked(flankB)) return true;
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
            if (cell == endCell) return false;
        }

        return false;
    }

    private HashSet<Vector3Int> BuildBlockedCells(Tilemap grid, float inflationRadius)
    {
        HashSet<Vector3Int> nodeCells = new HashSet<Vector3Int>();
        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node != null) nodeCells.Add(grid.WorldToCell(node.transform.position));
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

    /// <summary>Finds the nearest non-deep-water cell center to worldPosition — used to give a turtle chasing a target that's drifted into deep water (e.g. storm trash) a concrete shoreline point to swim to and hold at, instead of freezing wherever it happened to be. Searches outward ring by ring (Chebyshev distance) since the island's exact shape isn't known analytically. Falls back to worldPosition itself if the whole map is deep water or no grid exists — shouldn't happen with a real generated island.</summary>
    public Vector3 NearestNonDeepWaterPoint(Vector3 worldPosition)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return worldPosition;

        Vector3Int center = grid.WorldToCell(worldPosition);
        if (!IsDeepWater(worldPosition)) return grid.GetCellCenterWorld(center);

        HashSet<Vector3Int> deepWater = GetDeepWaterCells(grid);
        int maxRadius = Mathf.Max(grid.cellBounds.size.x, grid.cellBounds.size.y);

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
