using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Static helper (called from CoralReefUpgradeCard.Apply) that grows a clump
/// of coral in shallow water, following SeaweedPatchSpawner's shape exactly:
/// collect every shallow cell, pick one at random as the clump center, then
/// fill from the cells within patchRadius of it.
///
/// The one thing it adds over that spawner is occupancy. Coral is a wall, so
/// dropping it on top of a Seaweed node would quietly make that node
/// unreachable — every candidate cell already holding a ResourceNode (which
/// covers seaweed, and palms/rocks for free) or an existing piece of coral is
/// filtered out up front. Within a single call, the draw-without-replacement
/// that spawner already does keeps two pieces off the same cell.
/// </summary>
public static class CoralReefSpawner
{
    public static void SpawnPatch(IslandGenerator islandGenerator, GameObject coralPrefab, int cellCount, float patchRadius)
    {
        Tilemap shallow = islandGenerator != null ? islandGenerator.ShallowWaterTilemap : null;
        if (shallow == null || coralPrefab == null || cellCount <= 0) return;

        HashSet<Vector3Int> occupied = BuildOccupiedCells(shallow);

        List<Vector3Int> shallowCells = new List<Vector3Int>();
        foreach (Vector3Int cell in shallow.cellBounds.allPositionsWithin)
        {
            if (shallow.HasTile(cell) && !occupied.Contains(cell)) shallowCells.Add(cell);
        }

        if (shallowCells.Count == 0) return;

        Vector3Int center = shallowCells[Random.Range(0, shallowCells.Count)];
        Vector3 centerWorld = shallow.GetCellCenterWorld(center);

        List<Vector3Int> nearby = new List<Vector3Int>();
        foreach (Vector3Int cell in shallowCells)
        {
            if (Vector3.Distance(shallow.GetCellCenterWorld(cell), centerWorld) <= patchRadius) nearby.Add(cell);
        }

        int spawnCount = Mathf.Min(cellCount, nearby.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            int index = Random.Range(0, nearby.Count);
            Object.Instantiate(coralPrefab, shallow.GetCellCenterWorld(nearby[index]), Quaternion.identity);
            nearby.RemoveAt(index);
        }
    }

    /// <summary>Cells already holding something coral must not bury: any resource node (seaweed above all, but palms and rocks come free) and any coral already grown, whether by an earlier pick this run or by an earlier call this frame.</summary>
    private static HashSet<Vector3Int> BuildOccupiedCells(Tilemap shallow)
    {
        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node != null) occupied.Add(shallow.WorldToCell(node.transform.position));
        }

        foreach (CoralReef reef in CoralReef.AllReefs)
        {
            if (reef != null) occupied.Add(shallow.WorldToCell(reef.transform.position));
        }

        return occupied;
    }
}
