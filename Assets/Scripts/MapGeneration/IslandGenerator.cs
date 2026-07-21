using System;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Orchestrates procedural island generation: combines IslandNoiseMap's
/// blob-chain shape field (see GenerateBlobField — several soft circular
/// blobs chained and branched together, with an optional subtractive bite for
/// crescents) with its noise/land-mask math and PlaceholderTileFactory's
/// placeholder art, painting the result into a Water tilemap (fills the whole
/// map), a Shallow Water tilemap (a ring around the coastline), and a Sand
/// tilemap (land cells only). The blob chain can branch into abstract shapes —
/// wide peninsulas, crescents, narrow necks — but generation always keeps
/// exactly one connected island: the map center is forced to land (so the
/// turtle nest, spawned there, is never stranded in water) and everything not
/// connected to that center point is discarded. Assumes the Grid cell size is 1x1.
/// Optionally also paints a much larger purely-visual ring of plain deep water
/// around this core map (see PaintDeepWaterOutskirts/HalfWidth/HalfHeight) so
/// CameraController has more open ocean to zoom/pan into without ever
/// affecting island generation, pathfinding, or trash spawning, which all key
/// off the core map's own bounds instead.
/// </summary>
[RequireComponent(typeof(Grid))]
public class IslandGenerator : MonoBehaviour
{
    [Header("Tilemaps")]
    [Tooltip("Tilemap that fills the entire map rectangle, underlying the whole island.")]
    [SerializeField] private Tilemap waterTilemap;
    [Tooltip("Tilemap painted in a ring around every landmass, between the water and the sand.")]
    [SerializeField] private Tilemap shallowWaterTilemap;
    [Tooltip("Tilemap painted only on land cells, on top of the water.")]
    [SerializeField] private Tilemap sandTilemap;
    [Tooltip("Optional: tilemap painted with plain deep water across a much larger rectangle surrounding the core map (see Outskirt Margin below), purely so the ocean visually extends past the play area when the camera zooms/pans out. Never read by island generation, pathfinding (PathfindingManager), or trash spawning (TrashSpawner) — those all still only ever consider Water Tilemap's core Width x Height. Leave unassigned to skip painting outskirts entirely.")]
    [SerializeField] private Tilemap deepWaterOutskirtsTilemap;

    [Header("Map Size")]
    [Tooltip("Map width in tiles.")]
    [SerializeField] private int width = 64;
    [Tooltip("Map height in tiles.")]
    [SerializeField] private int height = 64;

    [Header("Noise")]
    [Tooltip("Larger values produce bigger, smoother landmasses.")]
    [SerializeField] private float noiseScale = 20f;
    [Tooltip("Number of noise layers summed together. 1 = a perfectly smooth blob.")]
    [SerializeField] private int octaves = 3;
    [Tooltip("How much each additional octave's amplitude shrinks.")]
    [SerializeField, Range(0f, 1f)] private float persistence = 0.5f;
    [Tooltip("How much each additional octave's frequency grows.")]
    [SerializeField] private float lacunarity = 2f;

    [Header("Blob Edge Shape")]
    [Tooltip("Normalized radius (0-1, relative to each blob's own radius) out to which land potential stays full before fading toward that blob's edge.")]
    [SerializeField, Range(0f, 1f)] private float falloffStart = 0.35f;
    [Tooltip("Exponent shaping how sharply land potential fades out toward each blob's edge.")]
    [SerializeField] private float falloffStrength = 3f;
    [Tooltip("How much each blob's radius bulges/pulls in depending on direction, producing elongated or lobed outlines instead of a perfect circle. 0 = perfectly round.")]
    [SerializeField, Range(0f, 0.6f)] private float shapeVariance = 0.15f;
    [Tooltip("Roughly how many bulges/pinches appear around each blob's perimeter. Lower = broad, oval-like variation; higher = more, smaller lobes.")]
    [SerializeField] private float shapeFrequency = 3f;
    [Tooltip("How pronounced small local coastline notches (coves) are, layered on top of the broader shape variance above. 0 = no coves.")]
    [SerializeField, Range(0f, 0.4f)] private float coveStrength = 0.05f;
    [Tooltip("Roughly how many coves appear around each blob's perimeter. Higher than Shape Frequency so coves read as small local detail, not extra broad lobes.")]
    [SerializeField] private float coveFrequency = 8f;
    [Tooltip("Target fraction of the map (outside the edge margin) that should end up as land. The actual cutoff is auto-calibrated each run so island size stays roughly consistent across seeds.")]
    [SerializeField, Range(0.05f, 0.9f)] private float targetLandFraction = 0.2f;
    [Tooltip("Tiles at every map border forced to water, regardless of the noise/falloff formula. Guarantees land never touches the edge.")]
    [SerializeField] private int edgeWaterMargin = 2;

    [Header("Landmass Blobs")]
    [Tooltip("How many soft circular blobs are chained together to build the landmass shape. More blobs = more elaborate/branching shapes.")]
    [SerializeField] private int blobCount = 2;
    [Tooltip("Minimum radius (tiles) of an individual blob.")]
    [SerializeField] private float blobMinRadius = 5f;
    [Tooltip("Maximum radius (tiles) of an individual blob.")]
    [SerializeField] private float blobMaxRadius = 9f;
    [Tooltip("How much each chained blob overlaps its parent. High values (e.g. 0.7) fuse blobs into one wide connected neck reaching away from the cluster (a peninsula). Low/negative values space blobs apart into narrower necks or gaps — since only one island ever survives (see Single Island below), a blob spaced too far from the rest gets discarded entirely rather than becoming a separate island.")]
    [SerializeField, Range(-0.3f, 0.9f)] private float blobChainOverlap = 0.6f;

    [Header("Crescents")]
    [Tooltip("Chance each generation carves a concave bite out of one blob (weighted toward larger blobs), producing a crescent/moon-shaped landmass.")]
    [SerializeField, Range(0f, 1f)] private float crescentChance = 0.15f;
    [Tooltip("The bite's radius, as a fraction of its host blob's radius.")]
    [SerializeField, Range(0.3f, 0.9f)] private float crescentBiteRadiusFactor = 0.7f;
    [Tooltip("How far the bite's center is offset from its host blob's center, as a fraction of the host's radius.")]
    [SerializeField, Range(0.2f, 0.9f)] private float crescentBiteOffsetFactor = 0.55f;

    [Header("Single Island")]
    [Tooltip("Chebyshev-radius disc around the map center always forced to land. Guarantees the TurtleNest (always at cell (0,0)) is never stranded in water, and doubles as the seed point generation floods outward from to discard every other separate blob/speck — this is what keeps the result to always exactly one island, however abstract its shape gets.")]
    [SerializeField] private int centerLandGuaranteeRadius = 2;

    [Header("Smoothing")]
    [Tooltip("Cellular-automata smoothing passes applied after thresholding. Removes single-cell specks and 1-tile-wide spits/bridges so the coastline stays compatible with a small hand-drawn edge/corner tile set. Kept low so small coves survive.")]
    [SerializeField] private int smoothingIterations = 2;
    [Tooltip("Of a cell's 8 neighbors, how many must be land for the cell itself to become/stay land during smoothing. Higher = rounder, more conservative coastlines (and fewer surviving coves).")]
    [SerializeField, Range(1, 8)] private int smoothingNeighborThreshold = 4;

    [Header("Deep Water Outskirts")]
    [Tooltip("Extra tiles of plain deep water painted on every side of the core map onto Deep Water Outskirts Tilemap above, purely for visual/camera purposes — the core map (island shape, trash spawn range, pathfinding grid) is entirely unaffected. 0 = no outskirts painted even if the tilemap is assigned.")]
    [SerializeField, Min(0)] private int outskirtMargin = 150;

    [Header("Shallow Water")]
    [Tooltip("How many tiles wide the shallow water ring is around every landmass. Large enough values bridge nearby islands together.")]
    [SerializeField] private int shallowWaterRadius = 3;

    [Header("Seed")]
    [Tooltip("Seed used for this generation. Logged to the Console each run so a specific layout can be reproduced.")]
    [SerializeField] private int seed;
    [Tooltip("Check this and set Seed above to reproduce a specific layout logged from a previous run.")]
    [SerializeField] private bool useFixedSeed = false;

    [Header("Placeholder Art")]
    [Tooltip("Leave unassigned to auto-generate a placeholder tile. Assign a real Tile asset to use imported art instead.")]
    [SerializeField] private TileBase waterTile;
    [SerializeField] private TileBase shallowWaterTile;
    [SerializeField] private TileBase sandTile;
    [SerializeField] private Color waterColor = new Color(0.227f, 0.431f, 0.647f);
    [SerializeField] private Color shallowWaterColor = new Color(0.376f, 0.616f, 0.792f);
    [SerializeField] private Color sandColor = new Color(0.851f, 0.769f, 0.561f);

    [Header("Turtle Nest")]
    [Tooltip("Turtle nest prefab (home base) instantiated at the exact center of the map after generation.")]
    [SerializeField] private GameObject turtleNestPrefab;
    [Tooltip("Parent transform the nest is placed under. Defaults to this object if left empty.")]
    [SerializeField] private Transform turtleNestParent;

    private GameObject spawnedNest;

    /// <summary>Raised after tiles are painted each generation, e.g. for scenery spawners to re-scatter props.</summary>
    public event Action IslandGenerated;

    /// <summary>The tilemap holding land cells, useful for anything that needs to know valid land positions.</summary>
    public Tilemap SandTilemap => sandTilemap;

    /// <summary>The tilemap filling every cell (land and water alike), useful for anything that needs to know valid water positions (i.e. cells here without a matching SandTilemap cell).</summary>
    public Tilemap WaterTilemap => waterTilemap;

    /// <summary>The tilemap holding the shallow-water ring around every landmass, useful for anything that needs to tell shallow from deep water (e.g. keeping trash out of the shallows).</summary>
    public Tilemap ShallowWaterTilemap => shallowWaterTilemap;

    /// <summary>The currently spawned turtle nest, valid once IslandGenerated has fired.</summary>
    public Transform TurtleNestTransform => spawnedNest != null ? spawnedNest.transform : null;

    /// <summary>The TurtleNest component on the currently spawned nest, valid once IslandGenerated has fired.</summary>
    public TurtleNest TurtleNestInstance => spawnedNest != null ? spawnedNest.GetComponent<TurtleNest>() : null;

    /// <summary>Half the outermost painted water's width in world units — the core map (Width) plus the purely-visual outskirts (see Outskirt Margin) on each side, whether or not a Deep Water Outskirts Tilemap is actually assigned. Always centered at world origin with 1x1 cell size, so the painted area spans [-HalfWidth, HalfWidth] on X. Useful for anything that needs to keep itself (e.g. CameraController) from going past the outermost painted water's edge. Island generation, pathfinding, and trash spawning all key off Water Tilemap's core bounds instead, unaffected by outskirts.</summary>
    public float HalfWidth => width / 2f + outskirtMargin;

    /// <summary>Half the outermost painted water's height in world units. See HalfWidth.</summary>
    public float HalfHeight => height / 2f + outskirtMargin;

    /// <summary>True if cell is water but neither shallow water nor land — i.e. water deep enough that turtles should never be able to path into it (see PathfindingManager's avoidDeepWater). Returns false (treated as safe) if the required tilemaps aren't assigned.</summary>
    public bool IsDeepWater(Vector3Int cell)
    {
        if (waterTilemap == null || !waterTilemap.HasTile(cell)) return false;
        if (sandTilemap != null && sandTilemap.HasTile(cell)) return false;
        if (shallowWaterTilemap != null && shallowWaterTilemap.HasTile(cell)) return false;
        return true;
    }

    private void Start()
    {
        GenerateIsland();
    }

    [ContextMenu("Generate Island")]
    public void GenerateIsland()
    {
        if (waterTilemap == null || sandTilemap == null)
        {
            Debug.LogWarning("IslandGenerator: Water Tilemap and Sand Tilemap must be assigned.");
            return;
        }

        ResolveSeed();
        EnsurePlaceholderTiles();

        IslandNoiseMap.BlobFieldSettings blobSettings = new IslandNoiseMap.BlobFieldSettings
        {
            BlobCount = blobCount,
            BlobMinRadius = blobMinRadius,
            BlobMaxRadius = blobMaxRadius,
            ChainOverlap = blobChainOverlap,
            EdgeFalloffStart = falloffStart,
            EdgeFalloffStrength = falloffStrength,
            ShapeVariance = shapeVariance,
            ShapeFrequency = shapeFrequency,
            CoveStrength = coveStrength,
            CoveFrequency = coveFrequency,
            CrescentChance = crescentChance,
            CrescentBiteRadiusFactor = crescentBiteRadiusFactor,
            CrescentBiteOffsetFactor = crescentBiteOffsetFactor,
        };

        float[,] noise = IslandNoiseMap.GenerateNoiseMap(width, height, noiseScale, octaves, persistence, lacunarity, seed);
        float[,] shapeField = IslandNoiseMap.GenerateBlobField(width, height, blobSettings, seed);
        float threshold = IslandNoiseMap.CalibrateLandThreshold(noise, shapeField, targetLandFraction, edgeWaterMargin);
        bool[,] land = IslandNoiseMap.BuildLandMask(noise, shapeField, threshold, edgeWaterMargin);
        land = IslandNoiseMap.SmoothLandMask(land, smoothingIterations, smoothingNeighborThreshold, edgeWaterMargin);
        IslandNoiseMap.ForceCenterLand(land, centerLandGuaranteeRadius);
        land = IslandNoiseMap.KeepIslandContainingCenter(land);
        bool[,] shallow = IslandNoiseMap.BuildShallowWaterMask(land, shallowWaterRadius, edgeWaterMargin);

        PaintTilemaps(land, shallow);
        SpawnTurtleNest();
        IslandGenerated?.Invoke();
    }

    private void SpawnTurtleNest()
    {
        if (turtleNestPrefab == null) return;

        if (spawnedNest != null)
        {
            if (Application.isPlaying) Destroy(spawnedNest);
            else DestroyImmediate(spawnedNest);
        }

        Vector3 center = sandTilemap.GetCellCenterWorld(Vector3Int.zero);
        Transform parent = turtleNestParent != null ? turtleNestParent : transform;
        spawnedNest = Instantiate(turtleNestPrefab, center, Quaternion.identity, parent);
    }

    private void ResolveSeed()
    {
        if (!useFixedSeed)
        {
            seed = System.Guid.NewGuid().GetHashCode();
        }

        Debug.Log($"IslandGenerator: seed = {seed} (check 'Use Fixed Seed' and set this value to reproduce this layout)");
    }

    private void EnsurePlaceholderTiles()
    {
        if (waterTile == null) waterTile = PlaceholderTileFactory.CreateSolidColorTile(waterColor, "Water_Placeholder");
        if (shallowWaterTile == null) shallowWaterTile = PlaceholderTileFactory.CreateSolidColorTile(shallowWaterColor, "ShallowWater_Placeholder");
        if (sandTile == null) sandTile = PlaceholderTileFactory.CreateSolidColorTile(sandColor, "Sand_Placeholder");
    }

    private void PaintTilemaps(bool[,] land, bool[,] shallow)
    {
        waterTilemap.ClearAllTiles();
        sandTilemap.ClearAllTiles();
        if (shallowWaterTilemap != null) shallowWaterTilemap.ClearAllTiles();
        if (deepWaterOutskirtsTilemap != null) deepWaterOutskirtsTilemap.ClearAllTiles();

        PaintDeepWaterOutskirts();

        BoundsInt bounds = new BoundsInt(-width / 2, -height / 2, 0, width, height, 1);

        TileBase[] waterTiles = new TileBase[width * height];
        TileBase[] shallowWaterTiles = new TileBase[width * height];
        TileBase[] sandTiles = new TileBase[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                waterTiles[index] = waterTile;
                shallowWaterTiles[index] = shallow[x, y] ? shallowWaterTile : null;
                sandTiles[index] = land[x, y] ? sandTile : null;
            }
        }

        waterTilemap.SetTilesBlock(bounds, waterTiles);
        if (shallowWaterTilemap != null) shallowWaterTilemap.SetTilesBlock(bounds, shallowWaterTiles);
        sandTilemap.SetTilesBlock(bounds, sandTiles);
        sandTilemap.RefreshAllTiles();
    }

    /// <summary>Fills a much larger rectangle surrounding the core map (see Outskirt Margin) with plain deep water tiles on Deep Water Outskirts Tilemap, purely so the ocean visually extends past the play area — never read by island generation, pathfinding, or trash spawning, which all only ever look at the core Water Tilemap. No-op if the tilemap isn't assigned or the margin is 0.</summary>
    private void PaintDeepWaterOutskirts()
    {
        if (deepWaterOutskirtsTilemap == null || outskirtMargin <= 0) return;

        int outerWidth = width + outskirtMargin * 2;
        int outerHeight = height + outskirtMargin * 2;
        BoundsInt outerBounds = new BoundsInt(-outerWidth / 2, -outerHeight / 2, 0, outerWidth, outerHeight, 1);

        TileBase[] outerWaterTiles = new TileBase[outerWidth * outerHeight];
        for (int i = 0; i < outerWaterTiles.Length; i++) outerWaterTiles[i] = waterTile;

        deepWaterOutskirtsTilemap.SetTilesBlock(outerBounds, outerWaterTiles);
    }
}
