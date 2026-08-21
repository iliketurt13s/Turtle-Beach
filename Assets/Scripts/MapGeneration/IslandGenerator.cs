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

    [Header("Chunk Carving")]
    [Tooltip("How many land tiles are carved back out of the finished island, in chunks. Runs AFTER the island is generated and reduced to one landmass, so this is an exact count of tiles rather than a nudge to the shape field: ask for 150 and 150 go, every seed, every time. 0 disables carving entirely.\n\nThis is the knob the Crescents section above isn't — a crescent bite is subtracted from the shape field BEFORE thresholding, calibration and smoothing, so how much land it actually costs varies run to run. Set Crescent Chance to 0 if you want carving to be the only thing taking bites out of the island.")]
    [SerializeField, Min(0)] private int carveTileCount = 150;
    [Tooltip("How many separate chunks that budget is split across. 1 takes it all out in one bite; higher scatters the same total loss around the island as several smaller ones.")]
    [SerializeField, Min(1)] private int carveChunkCount = 3;
    [Tooltip("How unevenly the budget is split between chunks. 0 makes every chunk the same size; 1 lets one be anywhere from nothing up to double the average. The TOTAL removed is the same either way.")]
    [SerializeField, Range(0f, 1f)] private float carveChunkSizeVariance = 0.4f;
    [Tooltip("Chance each chunk starts from a coastal tile, biting into the island's outline, rather than from anywhere on it, opening an inland lagoon. 1 is all coast, 0 is all lagoons. Coastal bites read as erosion; lagoons read as damage.")]
    [SerializeField, Range(0f, 1f)] private float carveCoastalBias = 0.85f;
    [Tooltip("Carving stops rather than taking the island below this many tiles — a floor against a budget typed with an extra digit, since an island carved down to nothing has nowhere to put the nest.")]
    [SerializeField, Min(0)] private int carveMinRemainingLand = 60;
    [Tooltip("Fills carved water back in as SHALLOW water, however far from land it ends up. Strongly recommended on: TrashSpawner picks its clusters from open DEEP water and scores them by closeness to the map center (see PickClusterCenter), so a carved lagoon big enough to have deep water in the middle is the most attractive spawn site on the whole map — trash would spawn inside the island. Keeping carved water shallow also lets turtles swim across it, since they only ever refuse deep water.")]
    [SerializeField] private bool carvedWaterStaysShallow = true;

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

    [Serializable]
    private struct IslandSizePreset
    {
        public int width;
        public int height;
        [Range(0.05f, 0.9f)] public float targetLandFraction;
        [Tooltip("Overrides Blob Count/Blob Min Radius/Blob Max Radius below — Target Land Fraction is only a goal CalibrateLandThreshold aims for, but it can't manufacture land the blob field never reaches. A bigger map needs bigger/more blobs too, or the actual achieved area caps out near the same absolute ceiling regardless of map size (see IslandGenerator's new area log in GenerateIsland).")]
        public int blobCount;
        public float blobMinRadius;
        public float blobMaxRadius;
    }

    private const string GameModeIndexKey = "GameModeIndex";

    [Header("Game Mode Presets")]
    [Tooltip("Overrides Width/Height/Target Land Fraction/blob geometry above at Awake (before the very first generation), indexed by the game mode picked on the menu's options screen (0=Big Island, 1=Cove, 2=Archipelago) via PlayerPrefs \"GameModeIndex\" — see MainMenuController.StartGame. Index 2 (Archipelago) intentionally matches this class's own defaults above, so picking Archipelago changes nothing. Blob counts/radii below are rough estimates scaled to each preset's target land area, not measured — watch GenerateIsland's new area log (actual tiles vs. target fraction) and adjust until the achieved percentages actually track Big Island > Cove > Archipelago.")]
    [SerializeField] private IslandSizePreset[] gameModeSizePresets = new IslandSizePreset[3]
    {
        new IslandSizePreset { width = 112, height = 112, targetLandFraction = 0.28f, blobCount = 3, blobMinRadius = 9f, blobMaxRadius = 16f },
        new IslandSizePreset { width = 80, height = 80, targetLandFraction = 0.22f, blobCount = 2, blobMinRadius = 7f, blobMaxRadius = 12f },
        new IslandSizePreset { width = 64, height = 64, targetLandFraction = 0.2f, blobCount = 2, blobMinRadius = 5f, blobMaxRadius = 9f },
    };

    [Header("Ocean Ring")]
    [Tooltip("The Rule Tile drawn in a band of open water hugging the coastline, giving the ocean a shoreline that meets the shallows flush. Painted onto Water Tilemap alongside the plain Water Tile, NOT onto a layer of its own — which is the whole point: the ring has to see ocean on its seaward side to know not to draw a second coastline there, and a Rule Tile only ever sees its own tilemap. This MUST be an Ocean Rule Tile (Create > 2D > Tiles > Ocean Rule Tile), not a stock Rule Tile: a stock one tests neighbours with `other == this`, so it reads the plain tile beside the band as land and rings the band with a second coastline facing the open sea. Leave unassigned for a flat ocean with no ring.")]
    [SerializeField] private TileBase oceanRingTile;
    [Tooltip("How many cells deep the ring band reaches out from the shallows. Only these cells are Rule Tiles — everything beyond, including the whole outskirts rectangle, is the plain Water Tile — so this is the dial that decides how much rule evaluation a generation costs. Keep it just wide enough for the widest transition your tile set actually draws. 0 disables the ring.")]
    [SerializeField, Min(0)] private int oceanRingWidth = 1;

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

    /// <summary>Core map width in tiles, e.g. for anything computing a placement radius guaranteed to clear the whole map regardless of angle (see GarbagePatchSpawner).</summary>
    public int Width => width;

    /// <summary>Core map height in tiles. See Width.</summary>
    public int Height => height;

    /// <summary>How many tiles wide the shallow-water ring is, e.g. so a placement radius can clear it too. See Width.</summary>
    public int ShallowWaterRadius => shallowWaterRadius;

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

    private void Awake()
    {
        ApplyGameModePreset();
    }

    /// <summary>Runs once, before Start's first GenerateIsland — overrides the three size fields above from whichever game mode was picked on the menu (defaulting to Cove if the key is missing, e.g. GameScene opened directly in the Editor). Awake always finishes before any Start in the scene, so this ordering is safe regardless of component order. One-time application — later regenerations (island transitions) reuse the already-overridden fields.</summary>
    private void ApplyGameModePreset()
    {
        if (gameModeSizePresets == null || gameModeSizePresets.Length == 0) return;

        int index = Mathf.Clamp(PlayerPrefs.GetInt(GameModeIndexKey, 1), 0, gameModeSizePresets.Length - 1);
        IslandSizePreset preset = gameModeSizePresets[index];
        width = preset.width;
        height = preset.height;
        targetLandFraction = preset.targetLandFraction;
        blobCount = preset.blobCount;
        blobMinRadius = preset.blobMinRadius;
        blobMaxRadius = preset.blobMaxRadius;
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

        // Carving runs on the finished single landmass, not on the shape field,
        // which is the whole point of it — see IslandNoiseMap.CarveChunks. It
        // can sever an outlying limb, so the island is reduced to one piece
        // again straight afterwards; that second pass is why the achieved
        // count logged below can exceed the budget asked for.
        bool[,] carved = CarveChunksOutOfIsland(land);
        int carvedTileCount = CountLandTiles(carved);
        if (carvedTileCount > 0) land = IslandNoiseMap.KeepIslandContainingCenter(land);

        bool[,] shallow = IslandNoiseMap.BuildShallowWaterMask(land, shallowWaterRadius, edgeWaterMargin);
        if (carvedWaterStaysShallow) MarkCarvedWaterShallow(shallow, carved, land);

        int landTileCount = CountLandTiles(land);
        Debug.Log($"IslandGenerator: island area = {landTileCount} tiles on a {width}x{height} map ({(float)landTileCount / (width * height):P1} of total) — target land fraction was {targetLandFraction:P0}.");

        if (carveTileCount > 0)
        {
            Debug.Log($"IslandGenerator: carved {carvedTileCount} tiles out of the island across {carveChunkCount} chunk(s) — asked for {carveTileCount}. A shortfall means the island ran out of carvable land (see Carve Min Remaining Land); the area above is after carving, including anything the carve severed from the mainland.");
        }

        PaintTilemaps(land, shallow);
        SpawnTurtleNest();
        IslandGenerated?.Invoke();
    }

    /// <summary>Packs the serialized carving fields into IslandNoiseMap's settings struct and runs the pass, returning the mask of what it took. The protected center is deliberately the same radius ForceCenterLand stamps, rather than a field of its own — they describe one thing (the ground the nest stands on), and two numbers for it could disagree.</summary>
    private bool[,] CarveChunksOutOfIsland(bool[,] land)
    {
        IslandNoiseMap.ChunkCarveSettings settings = new IslandNoiseMap.ChunkCarveSettings
        {
            TileBudget = carveTileCount,
            ChunkCount = carveChunkCount,
            ChunkSizeVariance = carveChunkSizeVariance,
            CoastalBias = carveCoastalBias,
            ProtectedCenterRadius = centerLandGuaranteeRadius,
            MinRemainingLand = carveMinRemainingLand,
        };

        return IslandNoiseMap.CarveChunks(land, settings, seed);
    }

    /// <summary>Marks every carved cell as shallow water — see Carved Water Stays Shallow for why that matters. Skips any cell the second KeepIslandContainingCenter pass didn't actually leave as water (it can't turn water back into land, but the guard keeps this honest if that ever changes).</summary>
    private static void MarkCarvedWaterShallow(bool[,] shallow, bool[,] carved, bool[,] land)
    {
        int width = carved.GetLength(0);
        int height = carved.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!carved[x, y] || land[x, y]) continue;

                shallow[x, y] = true;
            }
        }
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

    /// <summary>Actual painted land-tile count from the final mask — distinct from Target Land Fraction, which is only a goal CalibrateLandThreshold aims for; the blob field (Blob Count/Min/Max Radius) can cap how much area is actually achievable regardless of what fraction is requested. Logged by GenerateIsland so island-size consistency across game modes can be checked directly rather than inferred from Width/Height/Target Land Fraction alone.</summary>
    private static int CountLandTiles(bool[,] land)
    {
        int count = 0;
        int landWidth = land.GetLength(0);
        int landHeight = land.GetLength(1);
        for (int y = 0; y < landHeight; y++)
        {
            for (int x = 0; x < landWidth; x++)
            {
                if (land[x, y]) count++;
            }
        }

        return count;
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

        bool[,] ring = BuildOceanRingMask(land, shallow);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;

                // The water tilemap is the OCEAN, not a backing sheet under
                // everything: land and shallow cells are left empty on it,
                // which is exactly what lets the ring tile see where the water
                // stops and draw its coastline there. Every consumer reads this
                // map through a "water AND NOT sand AND NOT shallow" test
                // anyway (IslandGenerator.IsDeepWater, TrashSpawner), so they
                // get the same answers as when it was painted solid. The one
                // caller that cares about the tilemap's EXTENT rather than its
                // contents is PathfindingManager, which takes its A* search
                // bounds from cellBounds — hence the border frame, which pins
                // those bounds to the full map however the island falls.
                bool isOcean = !land[x, y] && !shallow[x, y];
                bool onBorder = x == 0 || y == 0 || x == width - 1 || y == height - 1;

                if (isOcean) waterTiles[index] = ring != null && ring[x, y] ? oceanRingTile : waterTile;
                else waterTiles[index] = onBorder ? waterTile : null;

                shallowWaterTiles[index] = shallow[x, y] ? shallowWaterTile : null;
                sandTiles[index] = land[x, y] ? sandTile : null;
            }
        }

        waterTilemap.SetTilesBlock(bounds, waterTiles);
        if (shallowWaterTilemap != null) shallowWaterTilemap.SetTilesBlock(bounds, shallowWaterTiles);
        sandTilemap.SetTilesBlock(bounds, sandTiles);

        // All three refreshed, not just sand: Sand is a Rule Tile and Water
        // carries one too (the ring), and a Rule Tile picks its sprite from its
        // neighbours — which a block set is still filling in around it as it
        // goes, so a tile placed early can keep the sprite it chose while half
        // the map was still empty. Shallow Water is a plain tile today and
        // refreshing it costs nothing; it's included so making it a Rule Tile
        // later isn't a silent trap.
        waterTilemap.RefreshAllTiles();
        if (shallowWaterTilemap != null) shallowWaterTilemap.RefreshAllTiles();
        sandTilemap.RefreshAllTiles();
    }

    /// <summary>
    /// Which ocean cells get the ring tile: everything within Ocean Ring Width
    /// of land or shallow water, and nothing else. Null when there's no ring to
    /// draw, which the caller reads as "plain ocean everywhere".
    ///
    /// Reuses BuildShallowWaterMask, seeded with land AND shallow rather than
    /// land alone — it's a distance-from-a-mask flood fill, and the shallows
    /// are what the ocean actually meets. Nothing about it is specific to
    /// shallow water, and growing a band outward from a mask is exactly the job
    /// it already does.
    /// </summary>
    private bool[,] BuildOceanRingMask(bool[,] land, bool[,] shallow)
    {
        if (oceanRingTile == null || oceanRingWidth <= 0) return null;

        bool[,] coast = new bool[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                coast[x, y] = land[x, y] || shallow[x, y];
            }
        }

        // edgeWaterMargin 0: that argument keeps LAND off the map border, which
        // has nothing to do with a band of water, and passing the real one
        // would carve a gap in the ring wherever it ran near the edge.
        return IslandNoiseMap.BuildShallowWaterMask(coast, oceanRingWidth, 0);
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
