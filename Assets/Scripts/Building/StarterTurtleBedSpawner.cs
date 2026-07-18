using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Spawns one Turtle Bed at a random point around the nest as soon as the
/// island generates, so the player starts with a turtle-producing building
/// without needing to construct one first.
/// </summary>
public class StarterTurtleBedSpawner : MonoBehaviour
{
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private GameObject turtleBedPrefab;
    [Tooltip("Distance from the nest the starting Turtle Bed spawns at, in a random direction.")]
    [SerializeField] private float spawnDistance = 5f;

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += SpawnStarterBed;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= SpawnStarterBed;
    }

    private void SpawnStarterBed()
    {
        if (turtleBedPrefab == null || islandGenerator == null) return;

        Tilemap sand = islandGenerator.SandTilemap;
        Transform nest = islandGenerator.TurtleNestTransform;
        if (sand == null || nest == null) return;

        const int maxAttempts = 30;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * spawnDistance;
            Vector3 candidatePosition = nest.position + offset;

            Vector3Int cell = sand.WorldToCell(candidatePosition);
            if (!sand.HasTile(cell)) continue;

            Vector3 cellCenter = sand.GetCellCenterWorld(cell);
            if (!IsCellClear(cellCenter)) continue;

            GameObject instance = Instantiate(turtleBedPrefab, cellCenter, Quaternion.identity);
            TurtleBed bed = instance.GetComponent<TurtleBed>();
            if (bed != null) bed.Initialize(islandGenerator);
            return;
        }

        Debug.LogWarning("StarterTurtleBedSpawner: couldn't find a valid spot for the starting Turtle Bed after multiple attempts.");
    }

    /// <summary>True if nothing solid (nature, another building, the nest, ...) already occupies this tile. Turtles never block placement, checked by ownership rather than by layer — see BuildModeController.IsCellClear for why.</summary>
    private static bool IsCellClear(Vector3 cellCenter)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(cellCenter, Vector2.one * 0.9f, 0f);
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponentInParent<TurtleAgent>() == null) return false;
        }

        return true;
    }
}
