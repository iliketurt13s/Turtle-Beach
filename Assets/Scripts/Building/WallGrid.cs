using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which grid cells currently have a wall, so each WallAutoTile can look
/// up its neighbors without needing its own tilemap reference.
/// </summary>
public static class WallGrid
{
    private static readonly Dictionary<Vector3Int, WallAutoTile> walls = new Dictionary<Vector3Int, WallAutoTile>();

    public static void Register(Vector3Int cell, WallAutoTile wall) => walls[cell] = wall;

    public static void Unregister(Vector3Int cell, WallAutoTile wall)
    {
        if (walls.TryGetValue(cell, out WallAutoTile existing) && existing == wall)
        {
            walls.Remove(cell);
        }
    }

    public static bool HasWallAt(Vector3Int cell) => walls.ContainsKey(cell);

    public static bool TryGetWall(Vector3Int cell, out WallAutoTile wall) => walls.TryGetValue(cell, out wall);
}
