using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scatters purely cosmetic sprites (no collider, no ResourceNode, no
/// gameplay effect) across land after the island generates: each decoration
/// is a bare GameObject + SpriteRenderer built entirely in code, placed at a
/// continuous random world position (rejection-sampled against the Sand
/// Tilemap, not snapped to any cell center) with a full random rotation.
/// Subscribes to IslandGenerator.IslandGenerated so it automatically
/// re-scatters whenever the island regenerates.
/// </summary>
public class DecorationSpawner : MonoBehaviour
{
    [Header("Island Reference")]
    [Tooltip("Generator whose Sand Tilemap defines valid land bounds. Also drives auto re-spawn whenever the island regenerates.")]
    [SerializeField] private IslandGenerator islandGenerator;

    [Header("Decorations")]
    [Tooltip("Sprite variants chosen from at random for each decoration spawned. Purely visual — no collider or component beyond a SpriteRenderer.")]
    [SerializeField] private Sprite[] decorationSprites;
    [Tooltip("Roughly what fraction of land cells get a decoration. Scales the total spawned with island size instead of a fixed count.")]
    [SerializeField, Range(0f, 1f)] private float decorationDensity = 0.1f;

    [Header("Placement")]
    [Tooltip("Parent transform spawned decorations are placed under. Defaults to this object if left empty.")]
    [SerializeField] private Transform decorationsParent;
    [Tooltip("Per decoration, how many random points to try before giving up on that slot. Only matters if land is a small fraction of the tilemap's bounding box.")]
    [SerializeField, Min(1)] private int maxPlacementAttemptsPerDecoration = 30;

    [Header("Rendering")]
    [Tooltip("Order within the sorting layer below.")]
    [SerializeField] private int orderInLayer = 0;
    [Tooltip("Numeric ID of the sorting layer every spawned decoration's SpriteRenderer is assigned to — the value shown next to each layer under Project Settings > Tags and Layers > Sorting Layers, not its name.")]
    [SerializeField] private int sortingLayerID = 0;

    [Header("Seed")]
    [Tooltip("Seed used for decoration placement. Logged to the Console each spawn so a specific layout can be reproduced.")]
    [SerializeField] private int seed;
    [Tooltip("Check this and set Seed above to reproduce a specific decoration layout.")]
    [SerializeField] private bool useFixedSeed = false;

    private readonly List<GameObject> spawnedDecorations = new List<GameObject>();

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += SpawnDecorations;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= SpawnDecorations;
    }

    [ContextMenu("Spawn Decorations")]
    public void SpawnDecorations()
    {
        Tilemap sandTilemap = islandGenerator != null ? islandGenerator.SandTilemap : null;
        if (sandTilemap == null)
        {
            Debug.LogWarning("DecorationSpawner: Island Generator (with an assigned Sand Tilemap) is required.");
            return;
        }

        ClearDecorations();

        if (decorationSprites == null || decorationSprites.Length == 0) return;

        BoundsInt cellBounds = sandTilemap.cellBounds;
        int landCellCount = 0;
        foreach (Vector3Int cell in cellBounds.allPositionsWithin)
        {
            if (sandTilemap.HasTile(cell)) landCellCount++;
        }

        if (landCellCount == 0) return;

        // Scales with however big/small this run's island turned out, rather than a fixed count.
        int spawnCount = Mathf.RoundToInt(landCellCount * decorationDensity);
        if (spawnCount <= 0) return;

        if (!useFixedSeed) seed = Environment.TickCount;
        Debug.Log($"DecorationSpawner: seed = {seed} (check 'Use Fixed Seed' and set this value to reproduce this decoration layout)");
        System.Random rng = new System.Random(seed);

        Vector3 worldMin = sandTilemap.CellToWorld(cellBounds.min);
        Vector3 worldMax = sandTilemap.CellToWorld(cellBounds.max);
        Transform parent = decorationsParent != null ? decorationsParent : transform;

        for (int i = 0; i < spawnCount; i++)
        {
            if (!TryFindRandomLandPoint(sandTilemap, worldMin, worldMax, rng, out Vector3 point)) continue;

            Sprite sprite = decorationSprites[rng.Next(decorationSprites.Length)];
            spawnedDecorations.Add(CreateDecoration(sprite, point, (float)rng.NextDouble() * 360f, parent));
        }

        Debug.Log($"DecorationSpawner: spawned {spawnedDecorations.Count} decorations across {landCellCount} land cells (target was {spawnCount} from {decorationDensity:P0} density).");
    }

    /// <summary>Rejection-samples a continuous world point (not a cell center) within the Sand Tilemap's bounding box until one lands on an actual land cell, up to Max Placement Attempts Per Decoration. This is what keeps decorations from looking grid-aligned — the accept/reject check is per-cell, but the accepted point itself can fall anywhere within that cell.</summary>
    private bool TryFindRandomLandPoint(Tilemap sandTilemap, Vector3 worldMin, Vector3 worldMax, System.Random rng, out Vector3 point)
    {
        for (int attempt = 0; attempt < maxPlacementAttemptsPerDecoration; attempt++)
        {
            float x = Mathf.Lerp(worldMin.x, worldMax.x, (float)rng.NextDouble());
            float y = Mathf.Lerp(worldMin.y, worldMax.y, (float)rng.NextDouble());
            Vector3 candidate = new Vector3(x, y, 0f);

            if (sandTilemap.HasTile(sandTilemap.WorldToCell(candidate)))
            {
                point = candidate;
                return true;
            }
        }

        point = default;
        return false;
    }

    private GameObject CreateDecoration(Sprite sprite, Vector3 position, float rotationZ, Transform parent)
    {
        GameObject decoration = new GameObject($"Decoration_{sprite.name}");
        decoration.transform.SetParent(parent, worldPositionStays: false);
        decoration.transform.position = position;
        decoration.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);

        SpriteRenderer spriteRenderer = decoration.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingLayerID = sortingLayerID;
        spriteRenderer.sortingOrder = orderInLayer;

        return decoration;
    }

    private void ClearDecorations()
    {
        foreach (GameObject decoration in spawnedDecorations)
        {
            if (decoration == null) continue;

            if (Application.isPlaying) Destroy(decoration);
            else DestroyImmediate(decoration);
        }

        spawnedDecorations.Clear();
    }
}
