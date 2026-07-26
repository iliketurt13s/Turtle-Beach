using System.Collections;
using UnityEngine;

/// <summary>
/// Lingering acid puddle spawned by BatteryAcidOnDeath at a Battery's death
/// point — a separate object, since the Battery's own GameObject (and any
/// coroutine running on it) is destroyed the instant it dies, before a
/// damage-over-time effect could tick more than once. Deals 1 damage per
/// second to every Watchtower within Radius, once per second, for exactly
/// Total Damage seconds — so duration always equals the total damage dealt
/// (e.g. 8 damage = 8 seconds), then self-destroys.
///
/// Finds nearby Watchtowers via BuildingHealth.AllBuildings (a plain registry
/// every building already adds itself to) plus a distance check, rather than
/// a Physics2D overlap query — this sidesteps any dependency on collider
/// setup or the Physics2D layer collision matrix, which a physics query would
/// otherwise be silently at the mercy of.
///
/// BatteryAcidOnDeath registers this puddle with TrashSpawner.RegisterExternalSpawn
/// right after creating it, so DayStormCycle.AnyTrashAlive() stays true (and the
/// storm can't end + heal every Watchtower back to full) until the puddle's
/// last tick fires and it self-destroys below.
/// </summary>
public class BatteryAcidPuddle : MonoBehaviour
{
    private float radius;
    private int totalDamage;

    public void Initialize(float radius, int totalDamage)
    {
        this.radius = radius;
        this.totalDamage = totalDamage;
        StartCoroutine(TickRoutine());
    }

    private IEnumerator TickRoutine()
    {
        for (int i = 0; i < totalDamage; i++)
        {
            DealTick();
            yield return new WaitForSeconds(1f);
        }

        Destroy(gameObject);
    }

    private void DealTick()
    {
        float sqrRadius = radius * radius;
        int hitCount = 0;

        foreach (BuildingHealth building in BuildingHealth.AllBuildings)
        {
            if (building == null) continue;

            Watchtower tower = building.GetComponent<Watchtower>();
            if (tower == null) continue;

            if (((Vector2)building.transform.position - (Vector2)transform.position).sqrMagnitude > sqrRadius) continue;

            building.ApplyDamage(1);
            hitCount++;
        }

        Debug.Log($"BatteryAcidPuddle: tick dealt 1 damage to {hitCount} Watchtower(s) within {radius} units.");
    }
}
