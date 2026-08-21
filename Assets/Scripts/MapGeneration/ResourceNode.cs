using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marker + type tag for a harvestable resource node (tree, rock, seaweed...).
/// Attach to the root of a harvestable nature prefab, alongside its Collider2D,
/// so a colliding TurtleAgent can find this via GetComponentInParent&lt;ResourceNode&gt;().
/// Never destroyed by harvesting — instead, after HitsToDeplete hits it goes
/// dormant (Visual deactivated, stops yielding anything) until RespawnDuration
/// elapses, then reactivates. The countdown only ticks during the day — it
/// freezes for the full duration of a storm and resumes exactly where it left
/// off once day returns, rather than continuing to progress overnight.
/// Nearby ResourceRespawnBooster buildings (Pet Rock/Fertilizer) can speed up
/// that respawn by registering themselves here; multiple registered boosters
/// stack linearly, not by taking the strongest one.
/// </summary>
public class ResourceNode : MonoBehaviour
{
    [Tooltip("Resource a turtle receives for physically colliding with this node.")]
    [SerializeField] private ResourceManager.ResourceType resourceType;

    [Header("Depletion")]
    [Tooltip("How many successful harvest hits before this node goes dormant.")]
    [SerializeField] private int hitsToDeplete = 6;
    [Tooltip("Seconds a dormant node takes to reactivate, before any booster speedup. Only counts down during the day — frozen for the whole duration of a storm.")]
    [SerializeField] private float respawnDuration = 20f;
    [Tooltip("Deactivated while this node is depleted, reactivated when it respawns.")]
    [SerializeField] private GameObject visual;

    [Header("Drop")]
    [Tooltip("Optional pickup spawned on a chance roll each harvest hit (e.g. Coconut for Wood, Iron Ingot for Rock) — see UpgradeManager.TryRollNodeDrop.")]
    [SerializeField] private GameObject dropPrefab;
    [SerializeField] private float dropSpawnRadius = 0.5f;

    [Header("Audio")]
    [Tooltip("Played once per successful harvest hit — the tree/rock impact. Authored per prefab rather than looked up from Resource Type, so a new kind of node just brings its own clips instead of needing a case added anywhere. Leave the clip list empty for a silent node (seaweed, say).")]
    [SerializeField] private SoundEffect harvestHitSound = new SoundEffect();

    public ResourceManager.ResourceType ResourceType => resourceType;

    /// <summary>False while this node is depleted/dormant.</summary>
    public bool IsHarvestable => !isDepleted;

    /// <summary>Every currently-enabled nature node, so PathfindingManager can treat them as obstacles (mirrors TrashHealth.allTrash/TurtleAgent.allTurtles). A depleted node stays registered — its collider persists even while dormant, so it remains a physical obstacle.</summary>
    private static readonly List<ResourceNode> allNodes = new List<ResourceNode>();
    public static IReadOnlyList<ResourceNode> AllNodes => allNodes;

    private int hitsTaken;
    private bool isDepleted;

    /// <summary>Extra harvest hits this node yields before going dormant, on top of the authored Hits To Deplete — see SetBonusHitsToDeplete.</summary>
    private int bonusHitsToDeplete;

    /// <summary>What this node is actually worth per cycle right now. Read at the moment of a hit rather than baked in at Deplete time, so a bonus granted mid-cycle applies to the tree the player is already chopping instead of only the next one.</summary>
    private int EffectiveHitsToDeplete => Mathf.Max(1, hitsToDeplete + bonusHitsToDeplete);
    private float respawnTimer;
    private readonly HashSet<ResourceRespawnBooster> activeBoosters = new HashSet<ResourceRespawnBooster>();
    private SquashAndStretch squashAndStretch;

    private void Awake() => squashAndStretch = GetComponent<SquashAndStretch>();

    private void OnEnable() => allNodes.Add(this);
    private void OnDisable() => allNodes.Remove(this);

    private void Update()
    {
        if (!isDepleted) return;

        // Freeze the countdown during a storm — trash is battering the island
        // at night, not respawning it — and simply resume where it left off
        // once day returns.
        if (DayStormCycle.IsStorming) return;

        float multiplier = 1f;
        foreach (ResourceRespawnBooster booster in activeBoosters)
        {
            if (booster != null) multiplier += booster.RespawnSpeedBonus;
        }

        respawnTimer -= Time.deltaTime * multiplier;
        if (respawnTimer <= 0f) Reactivate();
    }

    /// <summary>Called once per successful harvest hit. Deplets this node once HitsToDeplete is reached.</summary>
    public void RegisterHarvestHit()
    {
        if (isDepleted) return;

        squashAndStretch?.Play();
        // Alongside the squash rather than inside Deplete: this is the impact
        // of one hit, so it fires on every one of them, including the last.
        harvestHitSound.Play(transform.position);

        hitsTaken++;
        if (hitsTaken >= EffectiveHitsToDeplete) Deplete();
    }

    private void Deplete()
    {
        isDepleted = true;
        hitsTaken = 0;
        respawnTimer = respawnDuration;

        if (visual != null) visual.SetActive(false);
    }

    private void Reactivate()
    {
        isDepleted = false;

        if (visual != null) visual.SetActive(true);
    }

    /// <summary>
    /// Sets how many extra harvest hits this node yields before going dormant
    /// — i.e. how much more resource one cycle of it is worth.
    ///
    /// Overwrites rather than accumulates, because the caller (see PlanterPot)
    /// polls a run-wide cumulative total and pushes it every time it changes;
    /// an additive setter would compound that total into itself on every poll.
    /// </summary>
    public void SetBonusHitsToDeplete(int bonus) => bonusHitsToDeplete = Mathf.Max(0, bonus);

    public void RegisterBooster(ResourceRespawnBooster booster) => activeBoosters.Add(booster);
    public void UnregisterBooster(ResourceRespawnBooster booster) => activeBoosters.Remove(booster);

    /// <summary>Instantiates this node's configured drop (Coconut/Iron Ingot/...) nearby, if one is assigned.</summary>
    public void SpawnDrop()
    {
        if (dropPrefab == null) return;

        Vector2 offset = Random.insideUnitCircle * dropSpawnRadius;
        Instantiate(dropPrefab, transform.position + (Vector3)offset, Quaternion.identity);
    }
}
