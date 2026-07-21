using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single scene-wide tally of harvested resources. Turtles are spawned at
/// runtime by TurtleNest with no scene-authored reference to wire per-instance,
/// so this exposes itself as a simple singleton other systems can reach via
/// Instance instead.
/// </summary>
public class ResourceManager : MonoBehaviour
{
    public enum ResourceType { Wood, Rock, Seaweed, Coconut, JellyfishGuts }

    /// <summary>True for the resource types that get distributed to turtles as a night buff (see TurtleNest.SendWave) rather than only ever being spent on buildings.</summary>
    public static bool IsFoodType(ResourceType type) => type == ResourceType.Seaweed || type == ResourceType.Coconut || type == ResourceType.JellyfishGuts;

    [Serializable]
    public struct ResourceCost
    {
        public ResourceType type;
        public int amount;
    }

    public static ResourceManager Instance { get; private set; }

    /// <summary>Raised whenever a resource total changes: (type, new total).</summary>
    public event Action<ResourceType, int> ResourceChanged;

    private readonly Dictionary<ResourceType, int> counts = new Dictionary<ResourceType, int>
    {
        { ResourceType.Wood, 0 },
        { ResourceType.Rock, 0 },
        { ResourceType.Seaweed, 0 },
        { ResourceType.Coconut, 0 },
        { ResourceType.JellyfishGuts, 0 },
    };

    public int Wood => counts[ResourceType.Wood];
    public int Rock => counts[ResourceType.Rock];
    public int Seaweed => counts[ResourceType.Seaweed];
    public int Coconut => counts[ResourceType.Coconut];
    public int JellyfishGuts => counts[ResourceType.JellyfishGuts];

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("ResourceManager: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Add(ResourceType type, int amount = 1)
    {
        if (amount == 0) return;

        counts[type] += amount;
        ResourceChanged?.Invoke(type, counts[type]);
    }

    public int GetCount(ResourceType type) => counts[type];

    /// <summary>Subtracts amount from type's total (clamped at 0), firing ResourceChanged same as Add/TrySpend. Used by TurtleNest to take stockpiled food back out once it's been handed out to turtles as a night buff.</summary>
    public void Remove(ResourceType type, int amount)
    {
        if (amount <= 0) return;

        counts[type] = Mathf.Max(0, counts[type] - amount);
        ResourceChanged?.Invoke(type, counts[type]);
    }

    public bool CanAfford(IReadOnlyList<ResourceCost> costs)
    {
        foreach (ResourceCost cost in costs)
        {
            if (counts[cost.type] < cost.amount) return false;
        }

        return true;
    }

    /// <summary>Deducts all costs atomically (all-or-nothing) if affordable, firing ResourceChanged per type spent.</summary>
    public bool TrySpend(IReadOnlyList<ResourceCost> costs)
    {
        if (!CanAfford(costs)) return false;

        foreach (ResourceCost cost in costs)
        {
            if (cost.amount == 0) continue;

            counts[cost.type] -= cost.amount;
            ResourceChanged?.Invoke(cost.type, counts[cost.type]);
        }

        return true;
    }
}
