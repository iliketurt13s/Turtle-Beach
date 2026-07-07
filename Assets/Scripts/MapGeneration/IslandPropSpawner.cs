using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scatters the game's nature prefabs (trees, rocks, etc. — each carrying a
/// ResourceNode, so turtles can harvest them) across land (sand) cells after
/// the island generates: a random prefab per spawn, jittered within its cell
/// so placement doesn't look grid-snapped. Subscribes to
/// IslandGenerator.IslandGenerated so it automatically re-scatters whenever the
/// island regenerates. There's no separate purely-decorative prop layer —
/// every nature object spawned here is a real, collectible resource.
/// </summary>
public class IslandPropSpawner : MonoBehaviour
{
    [Header("Island Reference")]
    [Tooltip("Generator whose Sand Tilemap defines valid spawn locations. Also drives auto re-spawn whenever the island regenerates.")]
    [SerializeField] private IslandGenerator islandGenerator;

    [Header("Nature")]
    [Tooltip("Nature prefab variants (trees, rocks, etc.) to choose from at random. Each should carry a ResourceNode so turtles can harvest it.")]
    [SerializeField] private GameObject[] natureObjects;
    [Tooltip("Roughly what fraction of eligible land cells (after the water/center margins below) get a nature object. Scales the total spawned with island size instead of a fixed number.")]
    [SerializeField, Range(0f, 1f)] private float resourceDensity = 0.08f;

    [Header("Placement")]
    [Tooltip("Parent transform spawned props are placed under. Defaults to this object if left empty.")]
    [SerializeField] private Transform propsParent;
    [Tooltip("Random offset (world units) applied within each cell so props don't look grid-snapped.")]
    [SerializeField, Range(0f, 0.5f)] private float positionJitter = 0.3f;
    [Tooltip("Randomly mirror each prop horizontally for extra visual variety.")]
    [SerializeField] private bool randomizeFlip = true;
    [Tooltip("Minimum distance, in tiles, a spawn point must keep from any water cell. 0 = props may spawn right up to the coastline.")]
    [SerializeField, Range(0, 10)] private int minDistanceFromWater = 1;
    [Tooltip("Tiles, measured from the map center, kept clear of nature objects so nothing spawns on or crowds the turtle nest.")]
    [SerializeField, Range(0, 10)] private int minDistanceFromCenter = 2;

    [Header("Seed")]
    [Tooltip("Seed used for prop placement. Logged to the Console each spawn so a specific layout can be reproduced.")]
    [SerializeField] private int seed;
    [Tooltip("Check this and set Seed above to reproduce a specific prop layout.")]
    [SerializeField] private bool useFixedSeed = false;

    private readonly List<GameObject> spawnedProps = new List<GameObject>();

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += SpawnProps;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= SpawnProps;
    }

    [ContextMenu("Spawn Props")]
    public void SpawnProps()
    {
        Tilemap sandTilemap = islandGenerator != null ? islandGenerator.SandTilemap : null;
        if (sandTilemap == null)
        {
            Debug.LogWarning("IslandPropSpawner: Island Generator (with an assigned Sand Tilemap) is required.");
            return;
        }

        ClearProps();

        if (natureObjects == null || natureObjects.Length == 0) return;

        List<Vector3Int> landCells = new List<Vector3Int>();
        BoundsInt bounds = sandTilemap.cellBounds;
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (sandTilemap.HasTile(cell) && IsFarEnoughFromWater(cell, sandTilemap) && IsFarEnoughFromCenter(cell))
            {
                landCells.Add(cell);
            }
        }

        if (landCells.Count == 0) return;

        // Scales with however big/small this run's island turned out, rather than a fixed count.
        int spawnCount = Mathf.RoundToInt(landCells.Count * resourceDensity);
        if (spawnCount <= 0) return;

        if (!useFixedSeed) seed = Environment.TickCount;
        Debug.Log($"IslandPropSpawner: seed = {seed} (check 'Use Fixed Seed' and set this value to reproduce this prop layout)");
        System.Random rng = new System.Random(seed);

        HashSet<Vector3Int> usedCells = new HashSet<Vector3Int>();
        Transform parent = propsParent != null ? propsParent : transform;

        SpawnCategory(natureObjects, spawnCount, sandTilemap, landCells, usedCells, rng, parent);
    }

    private bool IsFarEnoughFromWater(Vector3Int cell, Tilemap sandTilemap)
    {
        if (minDistanceFromWater <= 0) return true;

        for (int dy = -minDistanceFromWater; dy <= minDistanceFromWater; dy++)
        {
            for (int dx = -minDistanceFromWater; dx <= minDistanceFromWater; dx++)
            {
                Vector3Int neighbor = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);
                if (!sandTilemap.HasTile(neighbor)) return false;
            }
        }

        return true;
    }

    private bool IsFarEnoughFromCenter(Vector3Int cell)
    {
        if (minDistanceFromCenter <= 0) return true;

        // Chebyshev distance in tiles from the map's center cell (0,0), matching how
        // IslandGenerator always centers the generated island (and the turtle nest)
        // on the world origin.
        int distance = Mathf.Max(Mathf.Abs(cell.x), Mathf.Abs(cell.y));
        return distance > minDistanceFromCenter;
    }

    private void SpawnCategory(GameObject[] prefabs, int count, Tilemap sandTilemap, List<Vector3Int> landCells, HashSet<Vector3Int> usedCells, System.Random rng, Transform parent)
    {
        if (prefabs == null || prefabs.Length == 0 || count <= 0) return;

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = count * 20;

        while (spawned < count && attempts < maxAttempts && usedCells.Count < landCells.Count)
        {
            attempts++;

            Vector3Int cell = landCells[rng.Next(landCells.Count)];
            if (!usedCells.Add(cell)) continue;

            GameObject prefab = prefabs[rng.Next(prefabs.Length)];
            Vector3 basePosition = sandTilemap.GetCellCenterWorld(cell);
            Vector3 jitter = new Vector3(
                ((float)rng.NextDouble() * 2f - 1f) * positionJitter,
                ((float)rng.NextDouble() * 2f - 1f) * positionJitter,
                0f);

            GameObject instance = Instantiate(prefab, basePosition + jitter, Quaternion.identity, parent);

            if (randomizeFlip && rng.Next(2) == 0)
            {
                Vector3 scale = instance.transform.localScale;
                scale.x *= -1f;
                instance.transform.localScale = scale;
            }

            spawnedProps.Add(instance);
            spawned++;
        }
    }

    private void ClearProps()
    {
        foreach (GameObject prop in spawnedProps)
        {
            if (prop == null) continue;

            if (Application.isPlaying) Destroy(prop);
            else DestroyImmediate(prop);
        }

        spawnedProps.Clear();
    }
}
