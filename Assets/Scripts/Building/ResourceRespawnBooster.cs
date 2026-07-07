using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared component behind Pet Rock (affects Rock) and Fertilizer (affects
/// Wood and Seaweed) — two prefabs, one script, configured with different
/// Affected Types. Speeds up respawn of any ResourceNode of a matching type
/// currently inside this building's trigger radius. Multiple overlapping
/// boosters stack linearly on a node (see ResourceNode.Update), not by the
/// node just taking whichever booster is strongest.
/// </summary>
public class ResourceRespawnBooster : MonoBehaviour
{
    [Tooltip("Resource types this building speeds up the respawn of.")]
    [SerializeField] private ResourceManager.ResourceType[] affectedTypes;
    [Tooltip("Additive bonus to respawn speed for every node in range, e.g. 1.0 = +100% (doubles) on its own. Stacks linearly with any other booster also in range of the same node.")]
    [SerializeField] private float respawnSpeedBonus = 1f;

    public float RespawnSpeedBonus => respawnSpeedBonus;

    private readonly HashSet<ResourceNode> boostedNodes = new HashSet<ResourceNode>();

    private void OnTriggerEnter2D(Collider2D other)
    {
        ResourceNode node = other.GetComponentInParent<ResourceNode>();
        if (node == null || !Affects(node.ResourceType) || !boostedNodes.Add(node)) return;

        node.RegisterBooster(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        ResourceNode node = other.GetComponentInParent<ResourceNode>();
        if (node == null || !boostedNodes.Remove(node)) return;

        node.UnregisterBooster(this);
    }

    private bool Affects(ResourceManager.ResourceType type)
    {
        if (affectedTypes == null) return false;

        foreach (ResourceManager.ResourceType affected in affectedTypes)
        {
            if (affected == type) return true;
        }

        return false;
    }

    private void OnDisable()
    {
        // Building destroyed/disabled while still boosting nodes — release them all.
        foreach (ResourceNode node in boostedNodes)
        {
            if (node != null) node.UnregisterBooster(this);
        }

        boostedNodes.Clear();
    }
}
