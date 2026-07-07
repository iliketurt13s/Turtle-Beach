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
/// ResourceNode-derived part of the blocked-cell set is rebuilt fresh on every
/// call rather than kept incrementally in sync: calls are infrequent (only on
/// turtle order-changes and once per trash spawn, never per-frame) and node
/// counts are modest, so this is cheap and avoids invalidation bugs. The
/// deep-water part is cached instead (see deepWaterCells) since, unlike
/// ResourceNode positions, it never changes at runtime.
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

    /// <summary>Finds a world-space waypoint path from start to goal avoiding nature, and — if avoidDeepWater is true — every deep-water cell too. Turtles always pass true (see TurtleAgent.BeginPathTo) so they can never path further out than the shallows; trash leaves this false since it must cross open water to reach shore. Returns null if unavailable/unreachable (callers should fall back to a direct route only when avoidDeepWater is false — see BeginPathTo), or an empty list if no intermediate waypoints are needed.</summary>
    public List<Vector3> FindPath(Vector3 start, Vector3 goal, bool avoidDeepWater = false)
    {
        Tilemap grid = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        if (grid == null) return null;

        Vector3Int startCell = grid.WorldToCell(start);
        Vector3Int goalCell = grid.WorldToCell(goal);

        HashSet<Vector3Int> blocked = BuildBlockedCells(grid);
        if (avoidDeepWater) blocked.UnionWith(GetDeepWaterCells(grid));

        // The destination and starting cell are always reachable regardless of
        // what occupies them — a mover needs to path onto a resource/building's
        // own cell to bounce-interact with it, and a mover whose current
        // position happens to round onto a "blocked" cell (physics jitter)
        // should never immediately fail to path at all.
        blocked.Remove(startCell);
        blocked.Remove(goalCell);

        List<Vector3Int> cellPath = AStarPathfinder.FindPathCells(startCell, goalCell, grid.cellBounds, blocked);
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
    /// True if no nature obstacle sits between from and to — used so a mover
    /// chasing a visible target (e.g. a turtle aggro-chasing trash) can skip
    /// pathfinding entirely and just steer straight at it. Ignores everything
    /// except ResourceNode colliders (the turtle's/target's own colliders,
    /// buildings, other trash, etc. never block line of sight).
    /// </summary>
    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector2 origin = from;
        Vector2 target = to;
        Vector2 offset = target - origin;
        float distance = offset.magnitude;
        if (distance < 0.0001f) return true;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, offset / distance, distance);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider != null && hit.collider.GetComponentInParent<ResourceNode>() != null) return false;
        }

        return true;
    }

    private HashSet<Vector3Int> BuildBlockedCells(Tilemap grid)
    {
        HashSet<Vector3Int> blocked = new HashSet<Vector3Int>();

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null) continue;

            Vector3Int cell = grid.WorldToCell(node.transform.position);
            blocked.Add(cell);

            if (obstacleInflationRadius <= 0) continue;

            for (int dx = -obstacleInflationRadius; dx <= obstacleInflationRadius; dx++)
            {
                for (int dy = -obstacleInflationRadius; dy <= obstacleInflationRadius; dy++)
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
