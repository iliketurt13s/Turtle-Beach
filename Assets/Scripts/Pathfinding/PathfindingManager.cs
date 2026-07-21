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
/// </summary>
public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; }

    [SerializeField] private IslandGenerator islandGenerator;
    [Tooltip("If set above 0, also blocks each nature obstacle's Chebyshev-neighboring cells (widens footprints). Default 0 matches actual collider sizes; raise this first if playtesting shows a mover clipping a tree/rock's edge.")]
    [SerializeField, Range(0, 3)] private int obstacleInflationRadius = 0;

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

    /// <summary>Finds a world-space waypoint path from start to goal avoiding nature, and — if avoidDeepWater is true — every deep-water cell too. Turtles always pass true (see TurtleAgent.BeginPathTo) so they can never path further out than the shallows; trash leaves this false since it must cross open water to reach shore. Turtles also pass allowDiagonalSqueeze true — they're small enough to cut a corner formed by two nature obstacles (e.g. moving diagonally between two resource nodes that are themselves diagonal from each other, or reaching one nestled behind such a pair) — but this never lets a path cut a corner across deep water regardless, since that's a hard depth limit, not a sizing issue; trash leaves it false, keeping the strict corner-cutting guard. extraObstacleInflation adds on top of the shared obstacleInflationRadius for this call only (e.g. a bigger piece of trash passing its own size so its route avoids gaps only wide enough for something smaller) — most callers leave it 0. Returns null if unavailable/unreachable (callers should fall back to a direct route only when avoidDeepWater is false — see BeginPathTo), or an empty list if no intermediate waypoints are needed.</summary>
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
    /// end inside the destination's own cell and self-block.
    /// </summary>
    public bool HasLineOfSight(Vector3 from, Vector3 to, Transform ignoreTarget = null)
    {
        Vector2 origin = from;
        Vector2 target = to;
        Vector2 offset = target - origin;
        float distance = offset.magnitude;
        if (distance < 0.0001f) return true;

        if (SegmentCrossesResourceNodeCell(origin, offset, distance, ignoreTarget)) return false;

        return !SegmentCrossesDeepWater(origin, offset, distance);
    }

    /// <summary>Samples the origin→(origin+offset) segment roughly once per grid cell (same technique as SegmentCrossesDeepWater), true if any sampled point's cell currently has a ResourceNode in it (per BuildBlockedCells — the whole cell, inflated the same as FindPath's obstacleInflationRadius, not just wherever that node's own collider happens to sit). ignoreTarget optionally exempts one specific node's own cell (see HasLineOfSight).</summary>
    private bool SegmentCrossesResourceNodeCell(Vector2 origin, Vector2 offset, float distance, Transform ignoreTarget)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        HashSet<Vector3Int> nodeCells = BuildBlockedCells(grid, obstacleInflationRadius);
        Vector3Int? ignoreCell = ignoreTarget != null ? grid.WorldToCell(ignoreTarget.position) : (Vector3Int?)null;

        float cellSize = Mathf.Max(grid.cellSize.x, grid.cellSize.y, 0.01f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / cellSize));

        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = origin + offset * (i / (float)steps);
            Vector3Int cell = grid.WorldToCell(point);
            if (ignoreCell.HasValue && cell == ignoreCell.Value) continue;
            if (nodeCells.Contains(cell)) return true;
        }

        return false;
    }

    /// <summary>Samples the origin→(origin+offset) segment roughly once per water cell, so a straight-line shortcut can never cut across a stretch of deep water lying between two otherwise-valid endpoints.</summary>
    private bool SegmentCrossesDeepWater(Vector2 origin, Vector2 offset, float distance)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        float cellSize = Mathf.Max(grid.cellSize.x, grid.cellSize.y, 0.01f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / cellSize));

        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = origin + offset * (i / (float)steps);
            if (IsDeepWater(point)) return true;
        }

        return false;
    }

    private HashSet<Vector3Int> BuildBlockedCells(Tilemap grid, int inflationRadius)
    {
        HashSet<Vector3Int> blocked = new HashSet<Vector3Int>();

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null) continue;

            Vector3Int cell = grid.WorldToCell(node.transform.position);
            blocked.Add(cell);

            if (inflationRadius <= 0) continue;

            for (int dx = -inflationRadius; dx <= inflationRadius; dx++)
            {
                for (int dy = -inflationRadius; dy <= inflationRadius; dy++)
                {
                    blocked.Add(new Vector3Int(cell.x + dx, cell.y + dy, cell.z));
                }
            }
        }

        return blocked;
    }

    /// <summary>True if worldPosition falls in deep water — used e.g. by idle wander to re-roll a target that would otherwise land in the ocean rather than only discovering it after BeginPathTo refuses to path there.</summary>
    public bool IsDeepWater(Vector3 worldPosition)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return false;

        return islandGenerator.IsDeepWater(grid.WorldToCell(worldPosition));
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
