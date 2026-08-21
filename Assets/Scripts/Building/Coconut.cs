using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dropped by trees (see ResourceNode.SpawnDrop / UpgradeManager.TryRollNodeDrop).
/// Targetable/clickable like a resource node (see TurtleSelectionController).
/// A shared hit counter (like TrashHealth) — every hit adds one Coconut unit
/// to the attacking turtle's carried-food (see TurtleAgent.CollectResourceUnit),
/// same as a Seaweed node's per-hit harvest, and once the counter reaches
/// Hits Required this object is destroyed.
/// </summary>
public class Coconut : MonoBehaviour
{
    /// <summary>Every currently-alive coconut, so TurtleAgent can find the nearest one to continue toward after finishing/losing its current target (mirrors JellyfishAgent.AllJellyfish/TrashHealth.allTrash).</summary>
    private static readonly List<Coconut> allCoconuts = new List<Coconut>();
    public static IReadOnlyList<Coconut> AllCoconuts => allCoconuts;

    [SerializeField] private int hitsRequired = 4;

    private int hitsTaken;

    private void OnEnable() => allCoconuts.Add(this);
    private void OnDisable() => allCoconuts.Remove(this);

    /// <summary>Called by TurtleAgent.HandleHeadHit when a turtle's head touches this coconut. Returns true if this hit was the one that consumed/destroyed it — callers must not rely on a `this == null` check afterward, since Destroy() only takes effect at end of frame, not immediately.</summary>
    public bool RegisterHit(TurtleAgent attacker)
    {
        if (attacker == null) return false;

        hitsTaken++;

        // Same double-harvest roll a ResourceNode hit gets (see
        // TurtleAgent.HandleHeadHit) — the Barnacle Rakes card is what makes
        // this ever return 2 for a food type. hitsTaken deliberately stays at
        // one per hit regardless, so the upgrade yields more per bump without
        // also making coconuts run out in fewer bumps.
        int amount = UpgradeManager.Instance != null
            ? UpgradeManager.Instance.RollHarvestAmount(ResourceManager.ResourceType.Coconut, attacker)
            : 1;

        for (int i = 0; i < amount; i++)
        {
            if (!attacker.CollectResourceUnit(ResourceManager.ResourceType.Coconut, transform.position)) break; // full — no loss, just stop adding
        }

        bool consumed = hitsTaken >= hitsRequired;
        if (consumed) Destroy(gameObject);
        return consumed;
    }
}
