using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scene object that rolls, at random intervals throughout the day (never
/// while DayStormCycle.IsStorming), whether a new Jellyfish spawns in the
/// shallows — gated by UpgradeManager.JellyfishSpawnChance (raised by
/// JellyfishUpgradeCard) and a total population cap, mirroring
/// UpgradeManager.TryRollNodeDrop's per-event dice-roll shape. Picks a spawn
/// point the same way SeaweedPatchSpawner does: enumerate every Shallow Water
/// tile and pick one at random.
/// </summary>
public class JellyfishSpawner : MonoBehaviour
{
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private GameObject jellyfishPrefab;
    [Tooltip("Total jellyfish allowed on the map at once, across every spawn roll.")]
    [SerializeField] private int maxJellyfishOnMap = 6;

    [Header("Roll Timing")]
    [Tooltip("Roughly how many seconds between each spawn-chance roll during the day. Paused entirely while storming, so rolls only ever land sometime during daylight.")]
    [SerializeField] private float rollInterval = 5f;
    [Tooltip("How much the interval can randomly vary, e.g. 2 = anywhere from (interval - 2) to (interval + 2), so rolls don't land on a predictable beat.")]
    [SerializeField] private float rollIntervalVariance = 2f;

    private float rollTimer;

    private void Awake()
    {
        ResetRollTimer();
    }

    private void Update()
    {
        if (DayStormCycle.IsStorming) return;

        rollTimer -= Time.deltaTime;
        if (rollTimer > 0f) return;

        ResetRollTimer();
        TryRollSpawn();
    }

    private void ResetRollTimer()
    {
        rollTimer = rollInterval + Random.Range(-rollIntervalVariance, rollIntervalVariance);
    }

    /// <summary>Rolls once for a chance to spawn a new jellyfish, if under the population cap. No-ops quietly if the roll fails, the cap is reached, or nothing's wired up. Public so anything else that wants an extra one-off roll still can, but the periodic Update() above is what gives normal daytime spawns their randomness.</summary>
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
