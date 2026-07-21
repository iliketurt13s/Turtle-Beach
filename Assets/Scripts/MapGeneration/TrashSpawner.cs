using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scatters trash in loose clumps across open deep-water cells (never on land
/// or in the shallow-water ring around it) each time DayStormCycle starts a new
/// day, and fades out + clears whatever survived once DayStormCycle ends the storm.
/// Each round is given a rating budget (see DayStormCycle) rather than a flat
/// piece count: every spawn is a random prefab from Trash Prefabs whose own
/// TrashDefinition.Rating is affordable within what's left of the budget, so
/// harder (higher-rated) plastic types naturally start appearing once the
/// budget grows large enough to afford them.
/// </summary>
public class TrashSpawner : MonoBehaviour
{
    [Header("Island Reference")]
    [SerializeField] private IslandGenerator islandGenerator;

    [Header("Trash")]
    [Tooltip("Trash prefab variants to choose from at random.")]
    [SerializeField] private GameObject[] trashPrefabs;
    [Tooltip("How strongly rarer/smaller trash is favored over larger, higher-rated trash (e.g. a Pallet) whenever both are affordable within what's left of the round's budget. 0 = every affordable prefab is equally likely regardless of cost, so a Pallet gets picked exactly as often as a Bottle despite eating far more of the budget per spawn — the old behavior, and why pallets used to dominate. Higher values weight selection by 1/rating^this, so cheaper (smaller) trash is picked far more often and a round tends to produce many small pieces instead of being dominated by a couple of expensive ones.")]
    [SerializeField, Min(0f)] private float smallTrashBias = 1.5f;

    [Header("Clumping")]
    [Tooltip("Fewest loose clumps a round's trash can be split across (inclusive). Rolled fresh per round between this and Max Cluster Count.")]
    [SerializeField, Min(1)] private int minClusterCount = 2;
    [Tooltip("Most loose clumps a round's trash can be split across (inclusive). Rolled fresh per round between Min Cluster Count and this.")]
    [SerializeField, Min(1)] private int maxClusterCount = 4;
    [Tooltip("How far (in tiles) a piece of trash may land from its cluster's center cell.")]
    [SerializeField, Range(0f, 10f)] private float clusterRadius = 3f;
    [Tooltip("Preferred distance (in tiles) between cluster centers — a soft target, not a hard minimum: a candidate closer than this is penalized (see Separation Bias), not rejected outright, so clusters usually spread out to about this far apart but can still occasionally land closer.")]
    [SerializeField, Min(0f)] private float preferredClusterSeparation = 4f;
    [Tooltip("How strongly a cluster center candidate is penalized for falling short of Preferred Cluster Separation, relative to how strongly candidates are already preferred for landing close to the island. 0 = separation is ignored entirely (purely closest-to-island, as if this feature didn't exist). Higher values favor spacing clusters out more consistently, but a closer candidate can still win a given round if the alternatives sampled that round are all notably worse for island-closeness — it's a bias, not a wall.")]
    [SerializeField, Min(0f)] private float separationBias = 1f;
    [Tooltip("Random offset (world units) applied within each cell so trash doesn't look grid-snapped.")]
    [SerializeField, Range(0f, 0.5f)] private float positionJitter = 0.3f;
    [Tooltip("How many random candidate cells are sampled per cluster center, keeping whichever scores best on a combination of landing close to the island and satisfying Preferred Cluster Separation from every already-placed center (see PickClusterCenter). Higher = more consistent results; 1 = uniformly random across all open water.")]
    [SerializeField, Range(1, 10)] private int closenessBiasSamples = 4;

    [Header("Seed")]
    [Tooltip("Seed used for trash placement. Logged to the Console each spawn so a specific layout can be reproduced.")]
    [SerializeField] private int seed;
    [Tooltip("Check this and set Seed above to reproduce a specific trash layout.")]
    [SerializeField] private bool useFixedSeed = false;

    private readonly List<GameObject> spawnedTrash = new List<GameObject>();

    /// <summary>Safety cap on spawn attempts so a misconfigured (e.g. all-zero-rating) prefab pool can't loop forever.</summary>
    private const int MaxSpawnsPerRound = 500;

    public void SpawnRound(float ratingBudget)
    {
        Tilemap water = islandGenerator != null ? islandGenerator.WaterTilemap : null;
        Tilemap sand = islandGenerator != null ? islandGenerator.SandTilemap : null;
        Tilemap shallowWater = islandGenerator != null ? islandGenerator.ShallowWaterTilemap : null;
        if (water == null || sand == null)
        {
            Debug.LogWarning("TrashSpawner: Island Generator (with assigned Water/Sand Tilemaps) is required.");
            return;
        }

        if (trashPrefabs == null || trashPrefabs.Length == 0 || ratingBudget <= 0f) return;

        List<Vector3Int> waterCells = new List<Vector3Int>();
        BoundsInt bounds = water.cellBounds;
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (water.HasTile(cell) && !sand.HasTile(cell) && (shallowWater == null || !shallowWater.HasTile(cell)))
            {
                waterCells.Add(cell);
            }
        }

        if (waterCells.Count == 0) return;

        if (!useFixedSeed) seed = Environment.TickCount;
        Debug.Log($"TrashSpawner: seed = {seed}, rating budget = {ratingBudget} (check 'Use Fixed Seed' and set this value to reproduce this trash layout)");
        System.Random rng = new System.Random(seed);

        int rolledClusterCount = rng.Next(minClusterCount, Mathf.Max(minClusterCount, maxClusterCount) + 1);
        int clusters = Mathf.Max(1, Mathf.Min(rolledClusterCount, waterCells.Count));
        List<Vector3Int> clusterCenters = new List<Vector3Int>();
        for (int i = 0; i < clusters; i++)
        {
            clusterCenters.Add(PickClusterCenter(waterCells, clusterCenters, rng));
        }

        Transform nestTarget = islandGenerator.TurtleNestTransform;

        float remainingBudget = ratingBudget;
        int spawnedCount = 0;

        while (remainingBudget > 0f && spawnedCount < MaxSpawnsPerRound)
        {
            GameObject prefab = PickAffordablePrefab(remainingBudget, spawnedCount == 0, rng);
            if (prefab == null) break;

            Vector3Int center = clusterCenters[spawnedCount % clusterCenters.Count];
            Vector3Int cell = center;

            int attempts = 0;
            const int maxAttempts = 20;
            bool foundCell = false;
            while (attempts < maxAttempts)
            {
                attempts++;

                int dx = Mathf.RoundToInt((float)(rng.NextDouble() * 2f - 1f) * clusterRadius);
                int dy = Mathf.RoundToInt((float)(rng.NextDouble() * 2f - 1f) * clusterRadius);
                Vector3Int candidate = new Vector3Int(center.x + dx, center.y + dy, center.z);

                if (water.HasTile(candidate) && !sand.HasTile(candidate) && (shallowWater == null || !shallowWater.HasTile(candidate)))
                {
                    cell = candidate;
                    foundCell = true;
                    break;
                }
            }

            if (!foundCell) cell = center;

            Vector3 basePosition = water.GetCellCenterWorld(cell);
            Vector3 jitter = new Vector3(
                ((float)rng.NextDouble() * 2f - 1f) * positionJitter,
                ((float)rng.NextDouble() * 2f - 1f) * positionJitter,
                0f);

            Quaternion rotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);
            GameObject instance = Instantiate(prefab, basePosition + jitter, rotation, transform);
            TrashAgent agent = instance.GetComponent<TrashAgent>();
            if (agent != null) agent.Initialize(nestTarget);

            spawnedTrash.Add(instance);
            spawnedCount++;
            remainingBudget -= GetRating(prefab);
        }
    }

    /// <summary>
    /// Samples ClosenessBiasSamples random cells and keeps whichever scores
    /// best on a single combined score: squared distance to the map center (a
    /// stand-in for "closest to the island", since the island is always
    /// centered at the origin) plus a penalty for landing closer than
    /// PreferredClusterSeparation to any center already in existingCenters,
    /// scaled by SeparationBias. Nothing is ever outright rejected for being
    /// too close — it's a soft preference, not a wall, so with a small sample
    /// count a nearby candidate can still win if it's otherwise notably
    /// better-positioned toward the island than the alternatives sampled that
    /// round. Sampling just 1 candidate is equivalent to plain uniform
    /// selection.
    /// </summary>
    private Vector3Int PickClusterCenter(List<Vector3Int> cells, List<Vector3Int> existingCenters, System.Random rng)
    {
        float preferredSeparationSqr = preferredClusterSeparation * preferredClusterSeparation;

        Vector3Int best = default;
        long bestScore = long.MaxValue;

        for (int i = 0; i < closenessBiasSamples; i++)
        {
            Vector3Int candidate = cells[rng.Next(cells.Count)];

            long shortfall = 0;
            if (existingCenters.Count > 0)
            {
                long nearestExistingSqr = NearestSqrDistance(candidate, existingCenters);
                shortfall = Math.Max(0L, (long)preferredSeparationSqr - nearestExistingSqr);
            }

            long score = SqrDistanceFromCenter(candidate) + (long)(shortfall * separationBias);
            if (i == 0 || score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static long NearestSqrDistance(Vector3Int cell, List<Vector3Int> others)
    {
        long nearest = long.MaxValue;
        foreach (Vector3Int other in others)
        {
            long dx = cell.x - other.x;
            long dy = cell.y - other.y;
            long sqrDistance = dx * dx + dy * dy;
            if (sqrDistance < nearest) nearest = sqrDistance;
        }

        return nearest;
    }

    private static long SqrDistanceFromCenter(Vector3Int cell) => (long)cell.x * cell.x + (long)cell.y * cell.y;

    /// <summary>
    /// Picks a prefab whose rating fits within remainingBudget, weighted by
    /// 1/rating^smallTrashBias so smaller (lower-rated) trash is favored over
    /// larger trash rather than every affordable option being equally likely
    /// (see smallTrashBias). If allowOverBudget is true (first spawn of the
    /// round) and nothing fits, falls back to the single cheapest prefab so a
    /// round can never spawn zero trash just because the starting budget is
    /// smaller than every plastic type's rating.
    /// </summary>
    private GameObject PickAffordablePrefab(float remainingBudget, bool allowOverBudget, System.Random rng)
    {
        List<GameObject> affordable = new List<GameObject>();
        List<float> weights = new List<float>();
        float totalWeight = 0f;
        GameObject cheapest = null;
        float cheapestRating = float.MaxValue;

        foreach (GameObject prefab in trashPrefabs)
        {
            if (prefab == null) continue;

            float rating = GetRating(prefab);
            if (rating <= remainingBudget)
            {
                float weight = 1f / Mathf.Pow(Mathf.Max(rating, 0.01f), smallTrashBias);
                affordable.Add(prefab);
                weights.Add(weight);
                totalWeight += weight;
            }

            if (rating < cheapestRating)
            {
                cheapestRating = rating;
                cheapest = prefab;
            }
        }

        if (affordable.Count > 0) return PickWeighted(affordable, weights, totalWeight, rng);
        return allowOverBudget ? cheapest : null;
    }

    /// <summary>Weighted random pick over options/weights (parallel lists) — falls back to a plain uniform pick if every weight collapsed to 0 (shouldn't happen with a positive rating and the Mathf.Max floor above, but guards against a degenerate weight configuration regardless).</summary>
    private static GameObject PickWeighted(List<GameObject> options, List<float> weights, float totalWeight, System.Random rng)
    {
        if (totalWeight <= 0f) return options[rng.Next(options.Count)];

        float roll = (float)(rng.NextDouble() * totalWeight);
        float cumulative = 0f;
        for (int i = 0; i < options.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return options[i];
        }

        return options[options.Count - 1];
    }

    private static float GetRating(GameObject prefab)
    {
        TrashDefinition definition = prefab.GetComponent<TrashDefinition>();
        return definition != null ? definition.Rating : 1f;
    }

    /// <summary>True if any trash from the current round is still alive (used to know when a storm can end).</summary>
    public bool AnyTrashAlive()
    {
        foreach (GameObject trash in spawnedTrash)
        {
            if (trash != null) return true;
        }

        return false;
    }

    public void BeginFadeOutAndClear(float fadeOutDuration)
    {
        foreach (GameObject trash in spawnedTrash)
        {
            if (trash == null) continue;

            TrashAgent agent = trash.GetComponent<TrashAgent>();
            if (agent != null) agent.BeginFadeOut(fadeOutDuration);
        }

        spawnedTrash.Clear();
    }
}
