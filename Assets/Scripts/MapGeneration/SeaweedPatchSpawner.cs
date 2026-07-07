using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Static helper (called from SeaweedUpgradeCard.Apply) that grows a small
/// patch of Seaweed resource nodes at a random spot, constrained to ONLY
/// shallow-water tiles. Follows the same cell-enumeration idiom
/// IslandPropSpawner/TrashSpawner each already duplicate independently, just
/// against IslandGenerator.ShallowWaterTilemap directly.
/// </summary>
public static class SeaweedPatchSpawner
{
    public static void SpawnPatch(IslandGenerator islandGenerator, GameObject seaweedNodePrefab, int nodeCount, float patchRadius)
    {
        Tilemap shallow = islandGenerator != null ? islandGenerator.ShallowWaterTilemap : null;
        if (shallow == null || seaweedNodePrefab == null || nodeCount <= 0) return;

        List<Vector3Int> shallowCells = new List<Vector3Int>();
        BoundsInt bounds = shallow.cellBounds;
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (shallow.HasTile(cell)) shallowCells.Add(cell);
        }

        if (shallowCells.Count == 0) return;

        Vector3Int center = shallowCells[Random.Range(0, shallowCells.Count)];
        Vector3 centerWorld = shallow.GetCellCenterWorld(center);

        List<Vector3Int> nearby = new List<Vector3Int>();
        foreach (Vector3Int cell in shallowCells)
        {
            if (Vector3.Distance(shallow.GetCellCenterWorld(cell), centerWorld) <= patchRadius) nearby.Add(cell);
        }

        int spawnCount = Mathf.Min(nodeCount, nearby.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            int index = Random.Range(0, nearby.Count);
            Object.Instantiate(seaweedNodePrefab, shallow.GetCellCenterWorld(nearby[index]), Quaternion.identity);
            nearby.RemoveAt(index);
        }
    }
}
