using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static A* search over a Vector3Int grid (matching the 1x1 tilemap cells the
/// rest of the project already uses). 8-directional movement with a
/// corner-cutting guard (optionally relaxed for small movers — see
/// allowDiagonalSqueeze on FindPathCells) and an octile-distance heuristic,
/// since this game's movement is free-form 2D physics, not a strict
/// 4-directional grid.
///
/// The per-cell bookkeeping (g-scores, predecessors, the closed set) is held in
/// FLAT ARRAYS indexed off bounds, not dictionaries keyed by Vector3Int, and
/// those arrays are reused between calls rather than reallocated. Every step of
/// a search touches them several times per expanded cell, so hashing a
/// three-int struct for each of those lookups was the bulk of a search's cost;
/// an array index is a handful of instructions and allocates nothing after the
/// first call. The scratch buffers are static because this is a static class
/// driven by one manager on Unity's single gameplay thread — a search is never
/// re-entrant, and nothing holds a reference to them across a call (the
/// returned path is always a fresh list).
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

    /// <summary>Cost of the best route found to each cell so far. PositiveInfinity means "not reached yet", which is what lets a plain "is this cheaper?" comparison double as the visited test — no parallel occupancy array needed.</summary>
    private static float[] bestG = Array.Empty<float>();
    /// <summary>Predecessor of each cell, stored as its index PLUS ONE so that a cleared array (all zeroes) reads as "no predecessor" — index 0 is a real cell (the bounds corner) and couldn't be used as the sentinel.</summary>
    private static int[] cameFrom = Array.Empty<int>();
    private static bool[] closed = Array.Empty<bool>();
    private static readonly MinHeap open = new MinHeap();
    private static readonly List<Vector3Int> pathScratch = new List<Vector3Int>();

    /// <summary>Binary min-heap over cell INDICES ordered by F-score. Uses lazy deletion (stale/duplicate pushes for an already-closed cell are simply skipped on pop) rather than an indexed decrease-key, since the grid is small.</summary>
    private class MinHeap
    {
        private readonly List<(int Index, float F)> items = new List<(int, float)>();

        public int Count => items.Count;

        public void Clear() => items.Clear();

        public void Push(int index, float f)
        {
            items.Add((index, f));
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[parent].F <= items[i].F) break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
            }
        }

        public int Pop()
        {
            int result = items[0].Index;
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

    /// <summary>
    /// Finds a path of cells from startCell to goalCell, avoiding blockedCells,
    /// within bounds. Returns null if unreachable.
    ///
    /// By default a diagonal move is refused whenever either flanking
    /// orthogonal cell is blocked (see the corner-cutting comment below) — pass
    /// allowDiagonalSqueeze true (small movers only — see
    /// PathfindingManager.FindPath) to permit cutting corners formed by
    /// ordinary blockedCells, while alwaysCornerBlockedCells (if given) still
    /// unconditionally blocks the corner regardless, for obstacles no mover
    /// should ever cut through no matter how small (deep water: a depth limit,
    /// not a sizing issue).
    ///
    /// alwaysCornerBlockedCells is treated as blocking MOVEMENT as well as
    /// corners. It used to be merged into blockedCells by the caller before the
    /// call, which cost a copy of the whole set (deep water alone is well over a
    /// thousand cells on a normal map) on every single request; taking it as a
    /// second set and testing both is the same answer without the copy.
    ///
    /// exemptCellA/B are never treated as blocked whatever the two sets say —
    /// the caller's start and goal cells. Passed in rather than removed from a
    /// private copy of blockedCells, for the same reason: the set handed over is
    /// shared with every other caller this frame and must not be mutated.
    /// </summary>
    public static List<Vector3Int> FindPathCells(
        Vector3Int startCell,
        Vector3Int goalCell,
        BoundsInt bounds,
        HashSet<Vector3Int> blockedCells,
        bool allowDiagonalSqueeze = false,
        HashSet<Vector3Int> alwaysCornerBlockedCells = null,
        Vector3Int? exemptCellA = null,
        Vector3Int? exemptCellB = null)
    {
        if (!bounds.Contains(startCell) || !bounds.Contains(goalCell)) return null;
        if (startCell == goalCell) return new List<Vector3Int> { startCell };

        int width = bounds.size.x;
        int height = bounds.size.y;
        int cellCount = width * height;
        if (cellCount <= 0) return null;

        int xMin = bounds.xMin;
        int yMin = bounds.yMin;
        int z = startCell.z;

        EnsureCapacity(cellCount);

        // bestG is filled rather than cleared because its "unreached" value is
        // infinity, not zero; the other two want zero and take the memset.
        for (int i = 0; i < cellCount; i++) bestG[i] = float.PositiveInfinity;
        Array.Clear(cameFrom, 0, cellCount);
        Array.Clear(closed, 0, cellCount);
        open.Clear();

        bool IsBlocked(Vector3Int cell)
        {
            if (exemptCellA.HasValue && cell == exemptCellA.Value) return false;
            if (exemptCellB.HasValue && cell == exemptCellB.Value) return false;
            if (blockedCells != null && blockedCells.Contains(cell)) return true;

            return alwaysCornerBlockedCells != null && alwaysCornerBlockedCells.Contains(cell);
        }

        int startIndex = (startCell.x - xMin) + (startCell.y - yMin) * width;
        int goalIndex = (goalCell.x - xMin) + (goalCell.y - yMin) * width;

        bestG[startIndex] = 0f;
        open.Push(startIndex, Heuristic(startCell, goalCell));

        while (open.Count > 0)
        {
            int currentIndex = open.Pop();
            if (closed[currentIndex]) continue;
            closed[currentIndex] = true;

            if (currentIndex == goalIndex) return ReconstructPath(goalIndex, width, xMin, yMin, z);

            int currentX = xMin + currentIndex % width;
            int currentY = yMin + currentIndex / width;
            float currentG = bestG[currentIndex];

            foreach (Vector3Int offset in Neighbors)
            {
                int nx = currentX + offset.x;
                int ny = currentY + offset.y;
                if (nx < xMin || ny < yMin || nx >= xMin + width || ny >= yMin + height) continue;

                int neighborIndex = (nx - xMin) + (ny - yMin) * width;
                if (closed[neighborIndex]) continue;

                Vector3Int neighbor = new Vector3Int(nx, ny, z);
                if (IsBlocked(neighbor)) continue;

                bool isDiagonal = offset.x != 0 && offset.y != 0;
                if (isDiagonal)
                {
                    // Disallow cutting the corner between two obstacle-adjacent
                    // cells — a mover has physical size and shouldn't clip
                    // through a gap that's only geometrically open at a point.
                    // A small enough mover can be told to ignore this against
                    // ordinary obstacles (allowDiagonalSqueeze) — e.g. squeezing
                    // diagonally between two resource nodes that are themselves
                    // diagonal from each other — but alwaysCornerBlockedCells
                    // still applies unconditionally either way.
                    Vector3Int flankA = new Vector3Int(nx, currentY, z);
                    Vector3Int flankB = new Vector3Int(currentX, ny, z);

                    if (allowDiagonalSqueeze)
                    {
                        if (alwaysCornerBlockedCells != null
                            && (alwaysCornerBlockedCells.Contains(flankA) || alwaysCornerBlockedCells.Contains(flankB)))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (IsBlocked(flankA) || IsBlocked(flankB)) continue;
                    }
                }

                float tentativeG = currentG + (isDiagonal ? DiagonalCost : OrthogonalCost);
                // Infinity for an unreached cell, so this one comparison covers
                // both "never seen" and "seen but by a worse route".
                if (tentativeG >= bestG[neighborIndex]) continue;

                bestG[neighborIndex] = tentativeG;
                cameFrom[neighborIndex] = currentIndex + 1;
                open.Push(neighborIndex, tentativeG + Heuristic(neighbor, goalCell));
            }
        }

        return null;
    }

    /// <summary>Grows the scratch arrays to cover a grid of cellCount cells. They only ever grow, so a map regeneration at the same size reuses them untouched.</summary>
    private static void EnsureCapacity(int cellCount)
    {
        if (bestG.Length >= cellCount) return;

        bestG = new float[cellCount];
        cameFrom = new int[cellCount];
        closed = new bool[cellCount];
    }

    private static float Heuristic(Vector3Int a, Vector3Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (DiagonalCost - 2f) * Mathf.Min(dx, dy);
    }

    /// <summary>Walks cameFrom back from the goal and returns the path start-first. Built in a reused list but handed back as a fresh one, since callers keep and modify what they're given.</summary>
    private static List<Vector3Int> ReconstructPath(int goalIndex, int width, int xMin, int yMin, int z)
    {
        pathScratch.Clear();

        int cursor = goalIndex;
        while (true)
        {
            pathScratch.Add(new Vector3Int(xMin + cursor % width, yMin + cursor / width, z));

            int previous = cameFrom[cursor];
            if (previous == 0) break;

            cursor = previous - 1;
        }

        pathScratch.Reverse();
        return new List<Vector3Int>(pathScratch);
    }
}
