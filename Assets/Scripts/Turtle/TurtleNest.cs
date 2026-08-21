using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The home base the player protects. IslandGenerator instantiates this prefab
/// at the exact center of the map after generation. Ends the game the moment a
/// trash item (see TrashItem) reaches it. No longer spawns turtles on its own
/// timer — SpawnTurtle is called externally by each placed TurtleBed instead,
/// so at least one bed is required to grow the turtle population at all.
///
/// Also where food (Seaweed/Coconut/JellyfishGuts) ends up: turtles deliver it
/// here exactly like Wood/Rock (see TurtleAgent.DeliverCarriedResources), and
/// ResourceManager's own per-type counts are the actual stockpile — there's no
/// separate storage counter on this class. While storming, each food type
/// sends out its own periodic "wave" — one unit to as many turtles as it can
/// (limited by how much of that type is stockpiled), flying each unit from
/// the nest to its turtle (reversing the same ResourcePopEffect delivery
/// animation) and consuming it from ResourceManager as it lands — on its own
/// independent cooldown (see FoodCooldowns/cooldownTimers), rather than
/// dumping the whole stockpile out in one lump sum at night start.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TurtleNest : MonoBehaviour
{
    private static readonly ResourceManager.ResourceType[] FoodTypes =
    {
        ResourceManager.ResourceType.Seaweed,
        ResourceManager.ResourceType.Coconut,
        ResourceManager.ResourceType.JellyfishGuts,
    };

    [Serializable]
    private struct FoodIcon
    {
        public ResourceManager.ResourceType type;
        public Sprite icon;
    }

    [Serializable]
    private struct FoodCooldown
    {
        public ResourceManager.ResourceType type;
        [Tooltip("Seconds between distribution waves for this food type while storming. E.g. 5 on Coconut sends one Coconut to as many turtles as it can (limited by stock) every 5 seconds, independently of the other food types' own cooldowns.")]
        public float cooldownSeconds;
    }

    [Header("Night Food Distribution")]
    [Tooltip("Sprite shown on the flying pop-effect for each food type as it's handed out to turtles.")]
    [SerializeField] private List<FoodIcon> foodIcons;
    [Tooltip("Pop-effect prefab flown from the nest to each turtle during distribution — reuses the same ResourcePopEffect component turtles use to fly resources back to the nest, just reversed.")]
    [SerializeField] private GameObject foodDistributionPopEffectPrefab;
    [Tooltip("Minimum delay between any two food dispenses, shared across every food type's waves (see ProcessDispenseQueue) — keeps consecutive dispenses visibly spaced out even when multiple types are ready to send at once, for the same sequential feel as delivery.")]
    [SerializeField] private float distributionStaggerDelay = 0.08f;
    [Tooltip("Per-food-type wave cooldown (seconds). Add one entry per ResourceManager.ResourceType you want distributed — a type left unconfigured falls back to defaultCooldownSeconds.")]
    [SerializeField] private List<FoodCooldown> foodCooldowns;
    [Tooltip("Cooldown used for any food type not explicitly listed in Food Cooldowns.")]
    [SerializeField] private float defaultCooldownSeconds = 5f;

    /// <summary>Counts down while storming (see Update); a type's next wave fires the instant its entry hits zero, then it's re-armed by adding (not resetting to) its configured cooldown, so any overshoot from a frame's Time.deltaTime doesn't accumulate into drift over a long storm.</summary>
    private readonly Dictionary<ResourceManager.ResourceType, float> cooldownTimers = new Dictionary<ResourceManager.ResourceType, float>();

    /// <summary>One dispense (a single food unit destined for a single turtle), queued rather than fired immediately so ProcessDispenseQueue can space every dispense out by distributionStaggerDelay even when multiple food types' waves land in the same or nearby frames.</summary>
    private struct FoodDispense
    {
        public ResourceManager.ResourceType Type;
        public TurtleAgent Turtle;
    }

    private readonly Queue<FoodDispense> dispenseQueue = new Queue<FoodDispense>();
    private Coroutine dispenseCoroutine;

    /// <summary>The currently active nest, if any. TurtleAgent instances are always runtime-spawned (via SpawnTurtle), so they can't hold a pre-wired scene reference the way scene-placed objects can — this lets them find the nest to guard during a storm.</summary>
    public static TurtleNest Instance { get; private set; }

    [Header("Turtle Spawning")]
    [Tooltip("Baby turtle prefab instantiated at the nest whenever a TurtleBed calls SpawnTurtle.")]
    [SerializeField] private GameObject turtlePrefab;
    [Tooltip("Parent transform spawned turtles are placed under. Defaults to this object if left empty.")]
    [SerializeField] private Transform turtleSpawnParent;

    [Header("Game Over")]
    [Tooltip("Invoked once, the moment a trash item reaches the nest. Wire up UI / game-over flow here in the Inspector.")]
    [SerializeField] private UnityEvent onNestDestroyed;

    /// <summary>Raised once, the moment a trash item reaches the nest.</summary>
    public event Action NestDestroyed;

    public bool IsDestroyed { get; private set; }

    private SquashAndStretch squashAndStretch;

    private void Awake()
    {
        squashAndStretch = GetComponent<SquashAndStretch>();
    }

    private void OnEnable()
    {
        Instance = this;
        DayStormCycle.StormStarted += ResetFoodCooldowns;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        DayStormCycle.StormStarted -= ResetFoodCooldowns;
    }

    /// <summary>Ticks every food type's cooldown down while storming AND while at least one piece of trash has actually reached the island/shallows (see IsTrashNearIsland) — the whole point of these buffs is defending against a threat that's actually here, so nothing dispenses while every piece of trash this round is still out crossing open ocean. Frozen (not ticking, not resetting) the rest of the time, so a storm that starts before trash arrives doesn't waste any cooldown progress before the fight actually begins. When a type's timer runs out, fires that type's wave if there's stock (see SendWave) or clears that type's buff off every turtle that has it if there isn't (see ClearBuffForAllTurtles) — a buff shouldn't outlast the food supply it's dispensed on.</summary>
    private void Update()
    {
        if (!DayStormCycle.IsStorming || !IsTrashNearIsland()) return;

        foreach (ResourceManager.ResourceType type in FoodTypes)
        {
            cooldownTimers.TryGetValue(type, out float timer);
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                if (ResourceManager.Instance != null && ResourceManager.Instance.GetCount(type) > 0)
                {
                    SendWave(type);
                }
                else
                {
                    ClearBuffForAllTurtles(type);
                }

                timer += GetCooldownSeconds(type);
            }

            cooldownTimers[type] = timer;
        }
    }

    /// <summary>True if any currently-alive trash has reached a non-deep-water cell (the island itself or the shallow ring around it) — used to hold off dispensing food until there's an actual threat present, rather than the moment a storm begins while every piece of trash is still crossing open ocean.</summary>
    private static bool IsTrashNearIsland()
    {
        if (PathfindingManager.Instance == null) return false;

        foreach (TrashHealth trash in TrashHealth.AllTrash)
        {
            if (trash == null) continue;
            if (!PathfindingManager.Instance.IsDeepWater(trash.transform.position)) return true;
        }

        return false;
    }

    /// <summary>Called the instant a storm begins (DayStormCycle.StormStarted) — arms every food type's cooldown at its full configured duration, so the first wave of a fresh night always waits a full cooldown before firing rather than immediately dumping a wave at storm start.</summary>
    private void ResetFoodCooldowns()
    {
        foreach (ResourceManager.ResourceType type in FoodTypes)
        {
            cooldownTimers[type] = GetCooldownSeconds(type);
        }
    }

    /// <summary>The authored cooldown for type, stretched by UpgradeManager.FoodCooldownMultiplier (the Picky Eaters run modifier, 1 when it isn't taken). Read fresh on every re-arm rather than cached at storm start, so it stays right whatever order managers happen to initialize in.</summary>
    private float GetCooldownSeconds(ResourceManager.ResourceType type)
    {
        float multiplier = UpgradeManager.Instance != null ? UpgradeManager.Instance.FoodCooldownMultiplier : 1f;
        return Mathf.Max(0.01f, GetAuthoredCooldownSeconds(type) * multiplier);
    }

    private float GetAuthoredCooldownSeconds(ResourceManager.ResourceType type)
    {
        if (foodCooldowns != null)
        {
            foreach (FoodCooldown entry in foodCooldowns)
            {
                if (entry.type == type) return Mathf.Max(0.01f, entry.cooldownSeconds);
            }
        }

        return Mathf.Max(0.01f, defaultCooldownSeconds);
    }

    /// <summary>Spawns one turtle at the nest's position. Called externally by a TurtleBed once its own placement delay elapses. Returns null if the nest is destroyed or has no turtle prefab configured.</summary>
    public GameObject SpawnTurtle() => SpawnUnit(turtlePrefab);

    /// <summary>Spawns one unit prefab at the nest's position, parented and positioned exactly like a turtle. The general form of SpawnTurtle, so anything that hatches at the nest (a crab recruit, see CrabRecruitUpgradeCard) shares the same destroyed-nest guard and spawn parent rather than instantiating itself. Returns null if the nest is destroyed or prefab is null.</summary>
    public GameObject SpawnUnit(GameObject prefab)
    {
        if (IsDestroyed || prefab == null) return null;

        Transform parent = turtleSpawnParent != null ? turtleSpawnParent : transform;
        return Instantiate(prefab, transform.position, Quaternion.identity, parent);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandlePotentialTrashContact(other.GetComponentInParent<TrashItem>());
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandlePotentialTrashContact(collision.collider.GetComponentInParent<TrashItem>());
    }

    private void HandlePotentialTrashContact(TrashItem trash)
    {
        if (trash == null || IsDestroyed) return;

        IsDestroyed = true;
        Debug.Log("TurtleNest: reached by trash — game over.");
        NestDestroyed?.Invoke();
        onNestDestroyed?.Invoke();
    }

    /// <summary>Called by Update the instant type's cooldown runs out with stock available. Queues one unit of type for as many eligible turtles as it can — limited by however much of that type is currently stockpiled, never more than one per turtle per wave — starting from a randomly rotated turtle offset each wave so the same turtles aren't always first in line. A turtle currently stationed at a Watchtower (IsParked) is skipped entirely (see GetEligibleTurtles) — it dispenses again once that turtle dismounts, whether that's the usual dawn dismissal or the player pulling it away mid-storm.</summary>
    private void SendWave(ResourceManager.ResourceType type)
    {
        if (ResourceManager.Instance == null) return;

        List<TurtleAgent> eligible = GetEligibleTurtles();
        if (eligible.Count == 0) return;

        int available = ResourceManager.Instance.GetCount(type);
        if (available <= 0) return;

        int sendCount = Mathf.Min(available, eligible.Count);
        int startOffset = UnityEngine.Random.Range(0, eligible.Count);

        for (int i = 0; i < sendCount; i++)
        {
            dispenseQueue.Enqueue(new FoodDispense { Type = type, Turtle = eligible[(startOffset + i) % eligible.Count] });
        }

        ResourceManager.Instance.Remove(type, sendCount);

        if (dispenseCoroutine == null) dispenseCoroutine = StartCoroutine(ProcessDispenseQueue());
    }

    /// <summary>Every currently-live turtle that isn't parked at a Watchtower right now — parked turtles are on duty, not out fighting/wandering, so they're skipped for food dispensing until they dismount (see Watchtower.DismissForDay/RecallForNight and the player's own ability to pull one away at any time). Crabs are skipped outright: they're their own unit (see CrabAgent), so run-wide turtle perks including these night rations never reach them.</summary>
    private static List<TurtleAgent> GetEligibleTurtles()
    {
        List<TurtleAgent> eligible = new List<TurtleAgent>();

        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle != null && !turtle.IsParked && !turtle.IsCrab) eligible.Add(turtle);
        }

        return eligible;
    }

    /// <summary>Drains dispenseQueue one dispense at a time, spaced by distributionStaggerDelay — shared across every food type's waves (rather than each wave running its own independent coroutine) so simultaneous or near-simultaneous waves from different types still visibly space their dispenses out instead of all landing at once.</summary>
    private IEnumerator ProcessDispenseQueue()
    {
        while (dispenseQueue.Count > 0)
        {
            FoodDispense dispense = dispenseQueue.Dequeue();
            if (dispense.Turtle != null) SpawnFoodFlight(dispense.Type, dispense.Turtle);

            yield return new WaitForSeconds(distributionStaggerDelay);
        }

        dispenseCoroutine = null;
    }

    /// <summary>Called by Update the instant type's cooldown runs out with nothing left in storage — turns that type's buff off on every turtle that currently has it (see TurtleAgent.ClearSeaweedBuff/ClearCoconutBuff/ClearJellyfishBuff), rather than letting it linger until dawn on food that ran out.</summary>
    private static void ClearBuffForAllTurtles(ResourceManager.ResourceType type)
    {
        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            ClearFoodBuff(type, turtle);
        }
    }

    private static void ClearFoodBuff(ResourceManager.ResourceType type, TurtleAgent turtle)
    {
        if (turtle == null) return;

        switch (type)
        {
            case ResourceManager.ResourceType.Seaweed:
                turtle.ClearSeaweedBuff();
                break;
            case ResourceManager.ResourceType.Coconut:
                turtle.ClearCoconutBuff();
                break;
            case ResourceManager.ResourceType.JellyfishGuts:
                turtle.ClearJellyfishBuff();
                break;
        }
    }

    /// <summary>Called by TurtleAgent.DeliverCarriedResources as each delivered unit's pop-effect lands here, and by SpawnFoodFlight as each food unit heads back out — covers "delivered to/from" the nest with one shared entry point.</summary>
    public void PlaySquash() => squashAndStretch?.Play();

    private void SpawnFoodFlight(ResourceManager.ResourceType type, TurtleAgent turtle)
    {
        PlaySquash();

        Vector3 targetPosition = turtle.transform.position;

        if (foodDistributionPopEffectPrefab != null)
        {
            GameObject instance = Instantiate(foodDistributionPopEffectPrefab, transform.position, Quaternion.identity);
            instance.GetComponent<ResourcePopEffect>()?.Initialize(
                GetFoodIcon(type), transform.position, targetPosition,
                () => ApplyFoodBuff(type, turtle));
        }
        else
        {
            ApplyFoodBuff(type, turtle); // no prefab wired yet — still grant the buff, just instantly
        }
    }

    private static void ApplyFoodBuff(ResourceManager.ResourceType type, TurtleAgent turtle)
    {
        if (turtle == null) return;

        switch (type)
        {
            case ResourceManager.ResourceType.Seaweed:
                turtle.ApplySeaweedBuff();
                break;
            case ResourceManager.ResourceType.Coconut:
                turtle.ApplyCoconutBuff();
                break;
            case ResourceManager.ResourceType.JellyfishGuts:
                turtle.ApplyJellyfishBuff();
                break;
        }
    }

    private Sprite GetFoodIcon(ResourceManager.ResourceType type)
    {
        if (foodIcons == null) return null;

        foreach (FoodIcon entry in foodIcons)
        {
            if (entry.type == type) return entry.icon;
        }

        return null;
    }
}
