using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static A* search over a Vector3Int grid (matching the 1x1 tilemap cells the
/// rest of the project already uses). 8-directional movement with a
/// corner-cutting guard and an octile-distance heuristic, since this game's
/// movement is free-form 2D physics, not a strict 4-directional grid. Called
/// infrequently (never per-frame — see PathfindingManager), so a simple
/// dictionary-keyed open/closed set is used instead of a bounds-offset array.
/// </summary>
public static class AStarPathfinder
{
    private const float OrthogonalCost = 1f;
    private static readonly float DiagonalCost = Mathf.Sqrt(2f);

    private static readonly Vector3Int[] Neighbors =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
        new Vector3Int(1, 1, 0), new Vector3Int(1, -1, 0),
        new Vector3Int(-1, 1, 0), new Vector3Int(-1, -1, 0),
    };

    /// <summary>Binary min-heap over cells ordered by F-score. Uses lazy deletion (stale/duplicate pushes for an already-closed cell are simply skipped on pop) rather than an indexed decrease-key, since the grid is small and calls are infrequent.</summary>
    private class MinHeap
    {
        private readonly List<(Vector3Int Cell, float F)> items = new List<(Vector3Int, float)>();

        public int Count => items.Count;

        public void Push(Vector3Int cell, float f)
        {
            items.Add((cell, f));
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[parent].F <= items[i].F) break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
            }
        }

        public Vector3Int Pop()
        {
            Vector3Int result = items[0].Cell;
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                int right = i * 2 + 2;
                int smallest = i;
                if (left < items.Count && items[left].F < items[smallest].F) smallest = left;
                if (right < items.Count && items[right].F < items[smallest].F) smallest = right;
                if (smallest == i) break;
                (items[smallest], items[i]) = (items[i], items[smallest]);
                i = smallest;
            }

            return result;
        }
    }

    /// <summary>Finds a path of cells from startCell to goalCell, avoiding blockedCells, within bounds. Returns null if unreachable.</summary>
    public static List<Vector3Int> FindPathCells(Vector3Int startCell, Vector3Int goalCell, BoundsInt bounds, HashSet<Vector3Int> blockedCells)
    {
        if (!bounds.Contains(startCell) || !bounds.Contains(goalCell)) return null;
        if (startCell == goalCell) return new List<Vector3Int> { startCell };

        MinHeap open = new MinHeap();
        HashSet<Vector3Int> closed = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, float> bestG = new Dictionary<Vector3Int, float>();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new Dictionary<Vector3Int, Vector3Int>();

        bestG[startCell] = 0f;
        open.Push(startCell, Heuristic(startCell, goalCell));

        while (open.Count > 0)
        {
            Vector3Int current = open.Pop();
            if (closed.Contains(current)) continue;
            closed.Add(current);

            if (current == goalCell) return ReconstructPath(cameFrom, current);

            foreach (Vector3Int offset in Neighbors)
            {
                Vector3Int neighbor = current + offset;
                if (!bounds.Contains(neighbor) || blockedCells.Contains(neighbor) || closed.Contains(neighbor)) continue;

                bool isDiagonal = offset.x != 0 && offset.y != 0;
                if (isDiagonal)
                {
                    // Disallow cutting the corner between two obstacle-adjacent
                    // cells — a mover has physical size and shouldn't clip
                    // through a gap that's only geometrically open at a point.
                    Vector3Int flankA = new Vector3Int(current.x + offset.x, current.y, current.z);
                    Vector3Int flankB = new Vector3Int(current.x, current.y + offset.y, current.z);
                    if (!bounds.Contains(flankA) || blockedCells.Contains(flankA)) continue;
                    if (!bounds.Contains(flankB) || blockedCells.Contains(flankB)) continue;
                }

                float tentativeG = bestG[current] + (isDiagonal ? DiagonalCost : OrthogonalCost);
                if (bestG.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG) continue;

                bestG[neighbor] = tentativeG;
                cameFrom[neighbor] = current;
                open.Push(neighbor, tentativeG + Heuristic(neighbor, goalCell));
            }
        }

        return null;
    }

    private static float Heuristic(Vector3Int a, Vector3Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (DiagonalCost - 2f) * Mathf.Min(dx, dy);
    }

    private static List<Vector3Int> ReconstructPath(Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int current)
    {
        List<Vector3Int> path = new List<Vector3Int> { current };
        while (cameFrom.TryGetValue(current, out Vector3Int previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }
}
