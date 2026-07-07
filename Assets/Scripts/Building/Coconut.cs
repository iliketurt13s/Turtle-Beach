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
    [SerializeField] private int hitsRequired = 4;

    private int hitsTaken;

    /// <summary>Called by TurtleAgent.HandleHeadHit when a turtle's head touches this coconut.</summary>
    public void RegisterHit(TurtleAgent attacker)
    {
        if (attacker == null) return;

        hitsTaken++;
        attacker.CollectResourceUnit(ResourceManager.ResourceType.Coconut, transform.position);

        if (hitsTaken >= hitsRequired) Destroy(gameObject);
    }
}
