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
/// budget grows large enough to afford them. The number of clumps a round is
/// split across also skews higher as the budget grows (see Cluster Count
/// Budget Reference/Bias), so a bigger, plastic-heavy round tends to spread
/// out into more clusters rather than just a few denser ones.
/// </summary>
public class TrashSpawner : MonoBehaviour
{
    /// <summary>Scene-wide singleton so upgrade cards (which live as prefab assets, not scene objects) can call Unlock without a serialized scene reference — mirrors BuildModeController.Instance.</summary>
    public static TrashSpawner Instance { get; private set; }

    [Header("Island Reference")]
    [SerializeField] private IslandGenerator islandGenerator;

    [Header("Trash")]
    [Tooltip("Every trash prefab that could ever spawn, locked or not. Cycle/round picking (see PickAffordablePrefab) only ever considers the subset currently unlocked (see Initially Unlocked/Unlock).")]
    [SerializeField] private GameObject[] trashPrefabs;
    [Tooltip("Trash prefabs spawnable from game start (e.g. Bottle/Box/Pallet). Everything else in Trash Prefabs begins locked; call Unlock to make more available later, e.g. from a hazard upgrade card.")]
    [SerializeField] private GameObject[] initiallyUnlocked;

    [Tooltip("How many times more likely a hazard-unlocked trash type (not in Initially Unlocked, but Unlocked later via a hazard upgrade card) is picked relative to a starting trash type, on top of the rarity weighting below. 1 = no preference.")]
    [SerializeField, Min(1f)] private float hazardUnlockedWeightMultiplier = 2f;

    private HashSet<GameObject> unlockedTrash;
    private HashSet<GameObject> initiallyUnlockedSet;
    [Tooltip("How strongly rarer/smaller trash is favored over larger, higher-rated trash (e.g. a Pallet) whenever both are affordable within what's left of the round's budget. 0 = every affordable prefab is equally likely regardless of cost, so a Pallet gets picked exactly as often as a Bottle despite eating far more of the budget per spawn — the old behavior, and why pallets used to dominate. Higher values weight selection by 1/rating^this, so cheaper (smaller) trash is picked far more often and a round tends to produce many small pieces instead of being dominated by a couple of expensive ones.")]
    [SerializeField, Min(0f)] private float smallTrashBias = 1.5f;

    [Header("Clumping")]
    [Tooltip("Fewest loose clumps a round's trash can be split across (inclusive). Rolled fresh per round between this and Max Cluster Count, skewed toward the max as the round's rating budget grows (see Cluster Count Budget Reference/Bias below).")]
    [SerializeField, Min(1)] private int minClusterCount = 2;
    [Tooltip("Most loose clumps a round's trash can be split across (inclusive). Rolled fresh per round between Min Cluster Count and this.")]
    [SerializeField, Min(1)] private int maxClusterCount = 4;
    [Tooltip("Rating budget at which the cluster count roll is fully skewed toward Max Cluster Count. A round with this much budget or more almost always rolls near the max; a round with little budget stays close to a uniform roll between Min and Max.")]
    [SerializeField, Min(0.01f)] private float clusterCountBudgetReference = 40f;
    [Tooltip("How strongly a higher rating budget pulls the cluster count roll toward Max Cluster Count. 0 = no bias at all — a plain uniform roll between Min and Max regardless of budget (the old behavior). Higher = a big, plastic-heavy round reaches the max cluster count far more reliably instead of still sometimes landing near the min.")]
    [SerializeField, Min(0f)] private float clusterCountBudgetBias = 2f;
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

    /// <summary>Extra clumps a round's trash is split across, from the Scattered Tide run modifier (see AddClusterCountBonus). Added to BOTH ends of the Min/Max roll, so it lifts the whole range rather than only widening it — the point of that modifier is that every round arrives on more fronts at once, not that some rounds might.</summary>
    private int clusterCountBonus;

    private readonly List<GameObject> spawnedTrash = new List<GameObject>();

    /// <summary>Safety cap on spawn attempts so a misconfigured (e.g. all-zero-rating) prefab pool can't loop forever.</summary>
    private const int MaxSpawnsPerRound = 500;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("TrashSpawner: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        unlockedTrash = new HashSet<GameObject>();
        initiallyUnlockedSet = new HashSet<GameObject>();
        if (initiallyUnlocked != null)
        {
            foreach (GameObject prefab in initiallyUnlocked)
            {
                unlockedTrash.Add(prefab);
                initiallyUnlockedSet.Add(prefab);
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Makes prefab spawnable from now on (e.g. from a run modifier). No-op if already unlocked. Warns if prefab isn't in Trash Prefabs at all — unlocking something the round picker never iterates is silently useless, and since these are asset references it's easy to point a modifier at a different prefab asset than the one wired into the pool.</summary>
    public void Unlock(GameObject prefab)
    {
        if (prefab == null) return;

        bool inPool = false;
        if (trashPrefabs != null)
        {
            foreach (GameObject pooled in trashPrefabs)
            {
                if (pooled == prefab) { inPool = true; break; }
            }
        }

        if (!inPool)
        {
            Debug.LogError($"TrashSpawner: \"{prefab.name}\" was unlocked but is NOT in this spawner's Trash Prefabs array, so it can never spawn. Add that exact prefab asset to Trash Prefabs.");
            return;
        }

        if (unlockedTrash.Add(prefab))
        {
            Debug.Log($"TrashSpawner: unlocked \"{prefab.name}\" (rating {GetRating(prefab)}) — it can spawn once a round's budget can afford it.");
        }
    }

    /// <summary>
    /// Permanently raises how many separate clumps every future round's trash
    /// is scattered into, for the Scattered Tide run modifier. Additive so
    /// repeat applications compose.
    ///
    /// Deliberately touches the cluster count only, not the rating budget:
    /// the same amount of trash arriving as six loose fronts instead of two
    /// dense ones is the whole hardship, because a defense built to hold one
    /// approach now has to hold the whole coastline.
    /// </summary>
    public void AddClusterCountBonus(int extraClusters)
    {
        clusterCountBonus += Mathf.Max(0, extraClusters);
        Debug.Log($"TrashSpawner: cluster count bonus now +{clusterCountBonus} (rounds now split across {minClusterCount + clusterCountBonus}-{Mathf.Max(minClusterCount, maxClusterCount) + clusterCountBonus} clumps).");
    }

    /// <summary>Tracks an externally-instantiated trash instance (e.g. TrashDefinition.SpawnDeathDrops) in the same round-tracking list SpawnRound uses, so AnyTrashAlive/BeginFadeOutAndClear correctly account for it too instead of leaving it to wander after the storm ends.</summary>
    public void RegisterExternalSpawn(GameObject trash)
    {
        if (trash != null) spawnedTrash.Add(trash);
    }

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

        // Uniform between Min/Max would give a plastic-heavy round the same
        // spread of possible cluster counts as a light one. Instead roll
        // u^(1/k) rather than a plain u — for k > 1 that biases the result
        // toward 1 (i.e. toward Max Cluster Count), and k itself grows with
        // how far this round's budget sits past the reference point, so a
        // bigger round more reliably nets more, bigger clumps of trash.
        // The bonus shifts both ends of the roll rather than being folded into
        // clusterRange, so the budget bias below still spans exactly the
        // authored width and a modified round is uniformly more scattered
        // instead of merely more variable.
        int effectiveMinClusters = minClusterCount + clusterCountBonus;
        int clusterRange = Mathf.Max(minClusterCount, maxClusterCount) - minClusterCount;
        float budgetRatio = ratingBudget / clusterCountBudgetReference;
        float biasExponent = 1f / (1f + clusterCountBudgetBias * budgetRatio);
        int rolledClusterCount = effectiveMinClusters + Mathf.RoundToInt(Mathf.Pow((float)rng.NextDouble(), biasExponent) * clusterRange);
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
    /// (see smallTrashBias), further multiplied by hazardUnlockedWeightMultiplier
    /// for anything not in Initially Unlocked (i.e. unlocked later via a hazard
    /// upgrade card) so newly-unlocked trash types show up more than their raw
    /// rarity alone would suggest. If allowOverBudget is true (first spawn of
    /// the round) and nothing fits, falls back to the single cheapest prefab so
    /// a round can never spawn zero trash just because the starting budget is
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
            if (prefab == null || (unlockedTrash != null && !unlockedTrash.Contains(prefab))) continue;

            float rating = GetRating(prefab);
            if (rating <= remainingBudget)
            {
                float weight = 1f / Mathf.Pow(Mathf.Max(rating, 0.01f), smallTrashBias);
                if (initiallyUnlockedSet != null && !initiallyUnlockedSet.Contains(prefab)) weight *= hazardUnlockedWeightMultiplier;
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
