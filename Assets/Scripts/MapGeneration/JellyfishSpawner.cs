using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scene object polled once per round (see DayStormCycle.BeginDay) to roll
/// whether a new Jellyfish spawns in the shallows, gated by
/// UpgradeManager.JellyfishSpawnChance (raised by JellyfishUpgradeCard) and a
/// total population cap — mirrors UpgradeManager.TryRollNodeDrop's per-event
/// dice-roll shape, just triggered once per round instead of per harvest hit.
/// Picks a spawn point the same way SeaweedPatchSpawner does: enumerate every
/// Shallow Water tile and pick one at random.
/// </summary>
public class JellyfishSpawner : MonoBehaviour
{
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private GameObject jellyfishPrefab;
    [Tooltip("Total jellyfish allowed on the map at once, across every spawn roll.")]
    [SerializeField] private int maxJellyfishOnMap = 6;

    /// <summary>Rolls once for a chance to spawn a new jellyfish, if under the population cap. No-ops quietly if the roll fails, the cap is reached, or nothing's wired up.</summary>
    public void TryRollSpawn()
    {
        if (JellyfishAgent.AllJellyfish.Count >= maxJellyfishOnMap) return;
        if (islandGenerator == null || jellyfishPrefab == null) return;

        float chance = UpgradeManager.Instance != null ? UpgradeManager.Instance.JellyfishSpawnChance : 0f;
        if (chance <= 0f || Random.value >= chance) return;

        if (!TryPickShallowWaterCell(out Vector3Int cell)) return;

        Tilemap shallow = islandGenerator.ShallowWaterTilemap;
        GameObject instance = Instantiate(jellyfishPrefab, shallow.GetCellCenterWorld(cell), Quaternion.identity);
        instance.GetComponent<JellyfishAgent>()?.Initialize(islandGenerator);
    }

    private bool TryPickShallowWaterCell(out Vector3Int result)
    {
        Tilemap shallow = islandGenerator.ShallowWaterTilemap;
        result = default;
        if (shallow == null) return false;

        List<Vector3Int> shallowCells = new List<Vector3Int>();
        foreach (Vector3Int cell in shallow.cellBounds.allPositionsWithin)
        {
            if (shallow.HasTile(cell)) shallowCells.Add(cell);
        }

        if (shallowCells.Count == 0) return false;

        result = shallowCells[Random.Range(0, shallowCells.Count)];
        return true;
    }
}
