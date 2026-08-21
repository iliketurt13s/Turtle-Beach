using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure noise/shape math for procedural island generation. No Unity scene
/// dependencies (no MonoBehaviour, no Tilemap) so it can be reasoned about and
/// tested independently of how the result gets painted.
/// </summary>
public static class IslandNoiseMap
{
    /// <summary>
    /// Fractal (multi-octave) Perlin noise sampled per cell, normalized to 0..1.
    /// Mathf.PerlinNoise has no seed parameter, so the seed is used to derive a
    /// random sample offset instead.
    /// </summary>
    public static float[,] GenerateNoiseMap(int width, int height, float noiseScale, int octaves, float persistence, float lacunarity, int seed)
    {
        float[,] map = new float[width, height];

        System.Random rng = new System.Random(seed);
        float offsetX = rng.Next(-100000, 100000);
        float offsetY = rng.Next(-100000, 100000);

        float safeScale = Mathf.Max(noiseScale, 0.0001f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;
                float maxPossible = 0f;

                for (int octave = 0; octave < octaves; octave++)
                {
                    float sampleX = (x + offsetX) / safeScale * frequency;
                    float sampleY = (y + offsetY) / safeScale * frequency;

                    float v = Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f;
                    noiseHeight += v * amplitude;
                    maxPossible += amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                noiseHeight /= Mathf.Max(maxPossible, 0.0001f);
                map[x, y] = (noiseHeight + 1f) / 2f;
            }
        }

        return map;
    }

    /// <summary>Bundles GenerateBlobField's many tunables into one call instead of a long parameter list.</summary>
    public struct BlobFieldSettings
    {
        public int BlobCount;
        public float BlobMinRadius;
        public float BlobMaxRadius;
        public float ChainOverlap;
        public float EdgeFalloffStart;
        public float EdgeFalloffStrength;
        public float ShapeVariance;
        public float ShapeFrequency;
        public float CoveStrength;
        public float CoveFrequency;
        public float CrescentChance;
        public float CrescentBiteRadiusFactor;
        public float CrescentBiteOffsetFactor;
    }

    private struct Blob
    {
        public Vector2 Center;
        public float Radius;
    }

    /// <summary>
    /// Builds land potential (0..1 per cell) from several soft circular "blobs"
    /// chained together — each branching off a random earlier blob rather than
    /// only ever the most recent one, so arms can radiate from different points —
    /// then unioned, with an optional smaller "bite" blob subtracted from one of
    /// them. Chaining with heavy overlap (<see cref="BlobFieldSettings.ChainOverlap"/>)
    /// fuses blobs into a wide connected neck reaching away from the cluster (a
    /// peninsula); little/no overlap spaces them apart into narrower necks or
    /// gaps (anything left disconnected from the map center gets discarded
    /// later, see KeepIslandContainingCenter, so the result is always exactly
    /// one island). The subtracted bite carves a concave arc out of its host
    /// blob (a crescent). Every blob's edge (including the
    /// bite's) gets the same lobed/coved warp the old single-center falloff used,
    /// just recentered on that blob instead of the map center.
    /// </summary>
    public static float[,] GenerateBlobField(int width, int height, BlobFieldSettings settings, int seed)
    {
        float[,] field = new float[width, height];

        System.Random rng = new System.Random(seed);
        float shapeOffsetX = rng.Next(-100000, 100000);
        float shapeOffsetY = rng.Next(-100000, 100000);
        // Distinct offset from the broad-lobe warp above so the cove pass samples an
        // unrelated part of the noise field instead of just retracing the same curve.
        float coveOffsetX = rng.Next(-100000, 100000);
        float coveOffsetY = rng.Next(-100000, 100000);

        List<Blob> blobs = BuildBlobChain(width, height, settings, rng);
        Blob? bite = BuildCrescentBite(blobs, settings, rng);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = 0f;
                foreach (Blob blob in blobs)
                {
                    value = Mathf.Max(value, BlobInfluence(x, y, blob, settings, shapeOffsetX, shapeOffsetY, coveOffsetX, coveOffsetY));
                }

                if (bite.HasValue)
                {
                    float biteInfluence = BlobInfluence(x, y, bite.Value, settings, shapeOffsetX, shapeOffsetY, coveOffsetX, coveOffsetY);
                    value = Mathf.Max(0f, value - biteInfluence);
                }

                field[x, y] = value;
            }
        }

        return field;
    }

    private static List<Blob> BuildBlobChain(int width, int height, BlobFieldSettings settings, System.Random rng)
    {
        int count = Mathf.Max(1, settings.BlobCount);
        List<Blob> blobs = new List<Blob>(count)
        {
            // Arbitrary construction anchor — RecenterOnMass below re-centers the
            // whole finished chain on the map's center regardless of which
            // direction it happened to branch out toward, so this starting point
            // doesn't bias the final result.
            new Blob { Center = new Vector2(width / 2f, height / 2f), Radius = RandomInRange(rng, settings.BlobMinRadius, settings.BlobMaxRadius) }
        };

        for (int i = 1; i < count; i++)
        {
            // A random existing blob, not just the previous one, so arms can
            // branch off different points instead of only ever extending a line.
            Blob parent = blobs[rng.Next(blobs.Count)];
            float radius = RandomInRange(rng, settings.BlobMinRadius, settings.BlobMaxRadius);
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float distance = (parent.Radius + radius) * (1f - settings.ChainOverlap);

            Vector2 center = parent.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            blobs.Add(new Blob { Center = center, Radius = radius });
        }

        RecenterOnMass(blobs, width, height);

        for (int i = 0; i < blobs.Count; i++)
        {
            Blob blob = blobs[i];
            blob.Center = ClampToInterior(blob.Center, blob.Radius, width, height);
            blobs[i] = blob;
        }

        return blobs;
    }

    /// <summary>
    /// Shifts every blob by the same offset so the chain's radius-weighted
    /// center of mass lands exactly on the map's center cell — this is what
    /// keeps the generated shape (whichever direction its arms happened to
    /// branch toward) visually centered on the map/nest/camera instead of
    /// drifting off to one side.
    /// </summary>
    private static void RecenterOnMass(List<Blob> blobs, int width, int height)
    {
        Vector2 weightedSum = Vector2.zero;
        float weightTotal = 0f;
        foreach (Blob blob in blobs)
        {
            float weight = blob.Radius * blob.Radius;
            weightedSum += blob.Center * weight;
            weightTotal += weight;
        }

        if (weightTotal <= 0f) return;

        Vector2 centerOfMass = weightedSum / weightTotal;
        Vector2 offset = new Vector2(width / 2f, height / 2f) - centerOfMass;

        for (int i = 0; i < blobs.Count; i++)
        {
            Blob blob = blobs[i];
            blob.Center += offset;
            blobs[i] = blob;
        }
    }

    /// <summary>Keeps a chained blob mostly on-map so a branching arm reads as an actual peninsula instead of being invisibly chopped off by the border.</summary>
    private static Vector2 ClampToInterior(Vector2 center, float radius, int width, int height)
    {
        float margin = radius * 0.5f;
        center.x = Mathf.Clamp(center.x, margin, width - margin);
        center.y = Mathf.Clamp(center.y, margin, height - margin);
        return center;
    }

    /// <summary>Rolls whether this run gets a crescent bite at all, then picks a host blob (weighted toward larger ones) and places the bite offset from its center.</summary>
    private static Blob? BuildCrescentBite(List<Blob> blobs, BlobFieldSettings settings, System.Random rng)
    {
        if (rng.NextDouble() >= settings.CrescentChance) return null;

        float totalRadius = 0f;
        foreach (Blob blob in blobs) totalRadius += blob.Radius;

        float pick = (float)(rng.NextDouble() * totalRadius);
        Blob host = blobs[0];
        float accumulated = 0f;
        foreach (Blob blob in blobs)
        {
            accumulated += blob.Radius;
            if (pick <= accumulated)
            {
                host = blob;
                break;
            }
        }

        float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
        float offset = host.Radius * settings.CrescentBiteOffsetFactor;
        Vector2 center = host.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * offset;

        return new Blob { Center = center, Radius = host.Radius * settings.CrescentBiteRadiusFactor };
    }

    private static float RandomInRange(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    /// <summary>Same warped-radial-falloff formula the old single-center GenerateFalloffMap used, evaluated around one blob's own center/radius instead of the map's.</summary>
    private static float BlobInfluence(int x, int y, Blob blob, BlobFieldSettings settings, float shapeOffsetX, float shapeOffsetY, float coveOffsetX, float coveOffsetY)
    {
        float dx = x - blob.Center.x;
        float dy = y - blob.Center.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(blob.Radius, 0.0001f);

        if (settings.ShapeVariance > 0f || settings.CoveStrength > 0f)
        {
            // Sample noise around a small circle (rather than by raw angle) so it
            // wraps seamlessly at the 0/360 degree boundary instead of seaming.
            float angle = Mathf.Atan2(dy, dx);
            float warp = 0f;

            if (settings.ShapeVariance > 0f)
            {
                float sampleX = Mathf.Cos(angle) * settings.ShapeFrequency + shapeOffsetX;
                float sampleY = Mathf.Sin(angle) * settings.ShapeFrequency + shapeOffsetY;
                warp += (Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f) * settings.ShapeVariance;
            }

            if (settings.CoveStrength > 0f)
            {
                // Same technique, higher frequency and lower strength: small local
                // notches/coves layered on top of the broad lobing above.
                float sampleX = Mathf.Cos(angle) * settings.CoveFrequency + coveOffsetX;
                float sampleY = Mathf.Sin(angle) * settings.CoveFrequency + coveOffsetY;
                warp += (Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f) * settings.CoveStrength;
            }

            distance *= 1f + warp;
        }

        distance = Mathf.Clamp01(distance);
        float falloff = 1f - Mathf.Clamp01((distance - settings.EdgeFalloffStart) / Mathf.Max(1f - settings.EdgeFalloffStart, 0.0001f));
        return Mathf.Pow(falloff, Mathf.Max(settings.EdgeFalloffStrength, 0.0001f));
    }

    /// <summary>
    /// Combines noise and falloff into a land/water mask, then forces a water margin
    /// at the map border regardless of the formula, so land never touches the edge.
    /// </summary>
    public static bool[,] BuildLandMask(float[,] noise, float[,] falloff, float landThreshold, int edgeWaterMargin)
    {
        int width = noise.GetLength(0);
        int height = noise.GetLength(1);
        bool[,] land = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool nearEdge = x < edgeWaterMargin || y < edgeWaterMargin ||
                                 x >= width - edgeWaterMargin || y >= height - edgeWaterMargin;

                land[x, y] = !nearEdge && (noise[x, y] * falloff[x, y] > landThreshold);
            }
        }

        return land;
    }

    /// <summary>
    /// Binary-searches a noise*falloff cutoff so the resulting land area (within the
    /// non-margin interior) lands close to a target fraction of the map, keeping
    /// island size roughly consistent across different seeds instead of drifting
    /// with each run's random noise amplitude.
    /// </summary>
    public static float CalibrateLandThreshold(float[,] noise, float[,] falloff, float targetLandFraction, int edgeWaterMargin, int iterations = 20)
    {
        int width = noise.GetLength(0);
        int height = noise.GetLength(1);

        int eligibleCells = Mathf.Max((width - 2 * edgeWaterMargin) * (height - 2 * edgeWaterMargin), 1);
        int targetLandCells = Mathf.RoundToInt(Mathf.Clamp01(targetLandFraction) * eligibleCells);

        float low = 0f;
        float high = 1f;
        float threshold = 0.5f;

        for (int i = 0; i < iterations; i++)
        {
            threshold = (low + high) / 2f;

            int landCells = 0;
            for (int y = edgeWaterMargin; y < height - edgeWaterMargin; y++)
            {
                for (int x = edgeWaterMargin; x < width - edgeWaterMargin; x++)
                {
                    if (noise[x, y] * falloff[x, y] > threshold) landCells++;
                }
            }

            // Higher threshold admits less land, so narrow toward whichever half contains the target.
            if (landCells > targetLandCells) low = threshold;
            else high = threshold;
        }

        return threshold;
    }

    /// <summary>
    /// Cellular-automata majority smoothing: a cell becomes/stays land only if at
    /// least <paramref name="neighborLandThreshold"/> of its 8 neighbors are land,
    /// otherwise it becomes water. Run over several iterations this erases
    /// single-cell specks, thin 1-tile-wide spits/bridges, and diagonal-only
    /// touches that a small hand-drawn edge/corner tile set can't represent.
    /// </summary>
    public static bool[,] SmoothLandMask(bool[,] land, int iterations, int neighborLandThreshold, int edgeWaterMargin)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        bool[,] current = land;

        for (int i = 0; i < iterations; i++)
        {
            bool[,] next = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int landNeighbors = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            if (current[nx, ny]) landNeighbors++;
                        }
                    }

                    next[x, y] = landNeighbors >= neighborLandThreshold;
                }
            }

            current = next;
        }

        ForceEdgeMargin(current, edgeWaterMargin);
        return current;
    }

    /// <summary>
    /// Flood-fills (4-connected) from the map's center cell and keeps only that
    /// single connected landmass, discarding every other separate blob/speck the
    /// blob-chain and noise produced — this is what guarantees generation always
    /// produces exactly one island, however abstract its branching/crescent
    /// shape gets. Must run after ForceCenterLand, which guarantees the center
    /// cell itself is land (the flood fill's seed point).
    /// </summary>
    public static bool[,] KeepIslandContainingCenter(bool[,] land)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        bool[,] result = new bool[width, height];

        int centerX = width / 2;
        int centerY = height / 2;
        if (!land[centerX, centerY]) return result; // shouldn't happen if ForceCenterLand ran first

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        result[centerX, centerY] = true;
        queue.Enqueue(new Vector2Int(centerX, centerY));

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();

            TryEnqueueIfLandAndUnvisited(cell.x + 1, cell.y, land, result, queue, width, height);
            TryEnqueueIfLandAndUnvisited(cell.x - 1, cell.y, land, result, queue, width, height);
            TryEnqueueIfLandAndUnvisited(cell.x, cell.y + 1, land, result, queue, width, height);
            TryEnqueueIfLandAndUnvisited(cell.x, cell.y - 1, land, result, queue, width, height);
        }

        return result;
    }

    private static void TryEnqueueIfLandAndUnvisited(int x, int y, bool[,] land, bool[,] visited, Queue<Vector2Int> queue, int width, int height)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        if (!land[x, y] || visited[x, y]) return;

        visited[x, y] = true;
        queue.Enqueue(new Vector2Int(x, y));
    }

    /// <summary>Tuning for CarveChunks — see there for what each one does to the result.</summary>
    public struct ChunkCarveSettings
    {
        /// <summary>Total land tiles to remove. The carve hits this exactly unless it runs out of carvable land first.</summary>
        public int TileBudget;
        /// <summary>How many separate contiguous chunks that budget is split across.</summary>
        public int ChunkCount;
        /// <summary>0-1. How unevenly the budget is split — 0 makes every chunk the same size, 1 lets one be anywhere from nothing to double the average.</summary>
        public float ChunkSizeVariance;
        /// <summary>0-1. Chance each chunk starts from a coastal tile (biting into the outline) rather than anywhere on the island (opening a lagoon).</summary>
        public float CoastalBias;
        /// <summary>Chebyshev radius around the map center that is never carved — the nest's ground. Match ForceCenterLand's radius.</summary>
        public int ProtectedCenterRadius;
        /// <summary>Carving stops rather than taking the island below this many tiles. A floor against a budget typed with an extra digit.</summary>
        public int MinRemainingLand;
    }

    /// <summary>
    /// Removes TileBudget land tiles from an already-finished land mask, in
    /// ChunkCount contiguous chunks, in place.
    ///
    /// The deliberate counterpart to the crescent bite in GenerateBlobField.
    /// That one is a chance-weighted subtraction from the shape FIELD, before
    /// thresholding, calibration and smoothing have had their say — so how much
    /// land it actually costs is an emergent property of the whole pipeline and
    /// differs run to run even at identical settings. Here the amount IS the
    /// input, applied to the finished mask, where a tile removed is a tile
    /// removed.
    ///
    /// Each chunk grows outward from its own seed cell one tile at a time,
    /// taking a random cell off the growing region's border each step. That's
    /// deliberately not a stamped disc: a disc would come out perfectly round,
    /// and since its area goes as the square of its radius, only coarsely
    /// sizeable. Frontier growth is ragged, always connected, and lands on
    /// exactly the requested count.
    ///
    /// Two guards keep the result playable. The Chebyshev disc around the map
    /// center (the nest's ground — see ForceCenterLand) is never carved, and
    /// carving stops rather than taking the island below Min Remaining Land.
    /// Carving CAN still sever an outlying limb from the island, though, so run
    /// KeepIslandContainingCenter again afterwards — that's what discards it,
    /// and it's why the finished island can end up smaller than the budget
    /// alone would predict.
    ///
    /// Returns the mask of exactly which cells were taken, so the caller can
    /// treat carved water differently from open ocean (see IslandGenerator's
    /// Carved Water Stays Shallow).
    /// </summary>
    public static bool[,] CarveChunks(bool[,] land, ChunkCarveSettings settings, int seed)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        bool[,] carved = new bool[width, height];

        int chunkCount = Mathf.Max(0, settings.ChunkCount);
        if (chunkCount == 0 || settings.TileBudget <= 0) return carved;

        int budget = Mathf.Min(settings.TileBudget, CountLand(land) - Mathf.Max(0, settings.MinRemainingLand));
        if (budget <= 0) return carved;

        // Offset from the caller's seed rather than used raw: the blob chain
        // was drawn from that same seed, and a carve replaying the same
        // sequence would tie where every chunk lands to the shape it's cutting
        // into. Still fully reproducible from the logged seed.
        System.Random rng = new System.Random(seed + 7919);

        int[] chunkSizes = SplitCarveBudget(budget, chunkCount, settings.ChunkSizeVariance, rng);
        int removedTotal = 0;
        int plannedSoFar = 0;

        for (int i = 0; i < chunkSizes.Length; i++)
        {
            plannedSoFar += chunkSizes[i];

            // Each chunk owes its own share PLUS anything earlier chunks failed
            // to deliver. A chunk CAN come up short — it seeds on a fragment an
            // earlier chunk cut loose from the mainland and eats the whole
            // thing before filling its share — and without carrying that
            // forward the pass quietly returns less land than was asked for,
            // which is the entire thing this system exists to stop doing.
            int want = Mathf.Min(plannedSoFar - removedTotal, budget - removedTotal);
            if (want <= 0) continue;

            Vector2Int? start = PickCarveSeed(land, settings, rng);
            if (start == null) break;

            removedTotal += GrowCarve(land, carved, start.Value, want, settings.ProtectedCenterRadius, rng);
        }

        // Last resort, and rare: even the final chunk came up short, so open
        // extra ones until the budget is met. This is the one case that can
        // produce more chunks than were asked for — a deliberate trade, since
        // Chunk Count describes how the loss is distributed while Tile Budget
        // describes how much of it there is, and the amount is the promise.
        // Terminates on its own: every pass either takes at least one tile or
        // finds nothing left to take.
        while (removedTotal < budget)
        {
            Vector2Int? start = PickCarveSeed(land, settings, rng);
            if (start == null) break;

            int removed = GrowCarve(land, carved, start.Value, budget - removedTotal, settings.ProtectedCenterRadius, rng);
            if (removed <= 0) break;

            removedTotal += removed;
        }

        return carved;
    }

    /// <summary>Divides budget across count chunks, jittered by variance. The rounding remainder goes to the last chunk, so the sizes always add back up to exactly budget rather than drifting a few tiles under it.</summary>
    private static int[] SplitCarveBudget(int budget, int count, float variance, System.Random rng)
    {
        float clampedVariance = Mathf.Clamp01(variance);
        float[] weights = new float[count];
        float total = 0f;

        for (int i = 0; i < count; i++)
        {
            weights[i] = 1f + ((float)rng.NextDouble() * 2f - 1f) * clampedVariance;
            total += weights[i];
        }

        int[] sizes = new int[count];

        // Every weight came out at zero (variance 1 and a very unlucky draw) —
        // fall back to an even split rather than dividing by zero.
        if (total <= 0f)
        {
            for (int i = 0; i < count; i++) sizes[i] = budget / count;
            sizes[count - 1] += budget - budget / count * count;
            return sizes;
        }

        int assigned = 0;
        for (int i = 0; i < count - 1; i++)
        {
            sizes[i] = Mathf.RoundToInt(budget * (weights[i] / total));
            assigned += sizes[i];
        }

        sizes[count - 1] = Mathf.Max(0, budget - assigned);
        return sizes;
    }

    /// <summary>Picks the land cell a chunk grows from: a coastal one (at least one water neighbor) with probability Coastal Bias, otherwise anywhere on the island. Coastal seeds eat into the outline, which is what carving usually wants; an inland one opens a lagoon instead. Never returns a cell inside the protected center. Null once there is no eligible land left at all.</summary>
    private static Vector2Int? PickCarveSeed(bool[,] land, ChunkCarveSettings settings, System.Random rng)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);

        List<Vector2Int> anywhere = new List<Vector2Int>();
        List<Vector2Int> coastal = new List<Vector2Int>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!land[x, y] || IsProtectedCenter(x, y, width, height, settings.ProtectedCenterRadius)) continue;

                Vector2Int cell = new Vector2Int(x, y);
                anywhere.Add(cell);
                if (HasWaterNeighbor(land, x, y)) coastal.Add(cell);
            }
        }

        bool preferCoast = coastal.Count > 0 && rng.NextDouble() < Mathf.Clamp01(settings.CoastalBias);
        List<Vector2Int> pool = preferCoast ? coastal : anywhere;

        return pool.Count > 0 ? pool[rng.Next(pool.Count)] : (Vector2Int?)null;
    }

    /// <summary>Grows one chunk from start until budget tiles have been taken or it runs out of land to spread into, clearing them from land and recording them in carved. Returns how many it actually took, which is short of budget exactly when it ran out of room — see CarveChunks, which makes that up elsewhere.</summary>
    private static int GrowCarve(bool[,] land, bool[,] carved, Vector2Int start, int budget, int protectedRadius, System.Random rng)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);

        List<Vector2Int> frontier = new List<Vector2Int> { start };
        int removed = 0;

        while (removed < budget && frontier.Count > 0)
        {
            // Swap-remove: the cell is chosen at random anyway, so keeping the
            // frontier in the order it was built costs a shuffle down the list
            // and buys nothing.
            int index = rng.Next(frontier.Count);
            Vector2Int cell = frontier[index];
            frontier[index] = frontier[frontier.Count - 1];
            frontier.RemoveAt(frontier.Count - 1);

            // A cell reaches the frontier once per land neighbor that queued
            // it, and an earlier chunk may have taken it in between — so this
            // is the normal case, not an error case.
            if (!land[cell.x, cell.y]) continue;
            if (IsProtectedCenter(cell.x, cell.y, width, height, protectedRadius)) continue;

            land[cell.x, cell.y] = false;
            carved[cell.x, cell.y] = true;
            removed++;

            EnqueueCarveNeighbor(land, frontier, cell.x + 1, cell.y, width, height);
            EnqueueCarveNeighbor(land, frontier, cell.x - 1, cell.y, width, height);
            EnqueueCarveNeighbor(land, frontier, cell.x, cell.y + 1, width, height);
            EnqueueCarveNeighbor(land, frontier, cell.x, cell.y - 1, width, height);
        }

        return removed;
    }

    private static void EnqueueCarveNeighbor(bool[,] land, List<Vector2Int> frontier, int x, int y, int width, int height)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        if (!land[x, y]) return;

        frontier.Add(new Vector2Int(x, y));
    }

    /// <summary>True if this land cell touches water on any of its four sides — the map's own edge counting as water, so a cell there still reads as coast.</summary>
    private static bool HasWaterNeighbor(bool[,] land, int x, int y)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);

        if (x + 1 >= width || !land[x + 1, y]) return true;
        if (x - 1 < 0 || !land[x - 1, y]) return true;
        if (y + 1 >= height || !land[x, y + 1]) return true;
        if (y - 1 < 0 || !land[x, y - 1]) return true;

        return false;
    }

    private static bool IsProtectedCenter(int x, int y, int width, int height, int radius)
    {
        if (radius < 0) return false;

        return Mathf.Abs(x - width / 2) <= radius && Mathf.Abs(y - height / 2) <= radius;
    }

    private static int CountLand(bool[,] land)
    {
        int count = 0;
        int width = land.GetLength(0);
        int height = land.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (land[x, y]) count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Stamps a guaranteed-land disc (Chebyshev radius) around the map's center
    /// cell, in place. Guarantees the TurtleNest — always spawned at cell (0,0)
    /// — never gets stranded in water, and gives KeepIslandContainingCenter a
    /// reliable seed point to flood-fill from.
    /// </summary>
    public static void ForceCenterLand(bool[,] land, int radius)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        int centerX = width / 2;
        int centerY = height / 2;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = centerX + dx;
                int y = centerY + dy;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;

                land[x, y] = true;
            }
        }
    }

    /// <summary>
    /// Multi-source 8-connected BFS outward from every land cell, up to
    /// <paramref name="radius"/> steps: marks the cells reached (but not land
    /// itself) as shallow water. A pure function of the land mask and a fixed
    /// radius, so its shape always mirrors the coastline it surrounds and needs
    /// no noise/seed of its own — including bridging across a narrow neck's two
    /// facing shores if it's thin enough. Respects the same edge water margin
    /// as the land mask, so open deep water still frames the whole map.
    /// </summary>
    public static bool[,] BuildShallowWaterMask(bool[,] land, int radius, int edgeWaterMargin)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);
        bool[,] shallow = new bool[width, height];
        if (radius <= 0) return shallow;

        int[,] distance = new int[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                distance[x, y] = land[x, y] ? 0 : -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (land[x, y]) queue.Enqueue(new Vector2Int(x, y));

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int nextDistance = distance[cell.x, cell.y] + 1;
            if (nextDistance > radius) continue;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = cell.x + dx;
                    int ny = cell.y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    if (distance[nx, ny] != -1) continue;

                    distance[nx, ny] = nextDistance;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                shallow[x, y] = !land[x, y] && distance[x, y] > 0 && distance[x, y] <= radius;
            }
        }

        ForceEdgeMargin(shallow, edgeWaterMargin);
        return shallow;
    }

    private static void ForceEdgeMargin(bool[,] land, int edgeWaterMargin)
    {
        int width = land.GetLength(0);
        int height = land.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool nearEdge = x < edgeWaterMargin || y < edgeWaterMargin ||
                                 x >= width - edgeWaterMargin || y >= height - edgeWaterMargin;
                if (nearEdge) land[x, y] = false;
            }
        }
    }
}
