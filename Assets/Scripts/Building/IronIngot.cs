using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dropped by rocks (see ResourceNode.SpawnDrop / UpgradeManager.TryRollNodeDrop).
/// Unlike Coconut, this is a single-hit ambient pickup detected directly in
/// its own trigger (not routed through TurtleHeadHitbox/HandleHeadHit): the
/// instant any part of a turtle touches it, it picks a random currently-alive
/// building and gives it a temporary max-health bonus (see
/// BuildingHealth.ApplyTemporaryMaxHealthBonus), then destroys itself.
/// </summary>
public class IronIngot : MonoBehaviour
{
    [SerializeField] private int healthBonusAmount = 5;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<TurtleAgent>() == null) return;

        BuildingHealth target = PickRandomBuilding();
        target?.ApplyTemporaryMaxHealthBonus(healthBonusAmount);
        Destroy(gameObject);
    }

    private BuildingHealth PickRandomBuilding()
    {
        IReadOnlyList<BuildingHealth> all = BuildingHealth.AllBuildings;

        List<BuildingHealth> alive = new List<BuildingHealth>();
        foreach (BuildingHealth building in all)
        {
            if (building != null) alive.Add(building);
        }

        return alive.Count > 0 ? alive[Random.Range(0, alive.Count)] : null;
    }
}
