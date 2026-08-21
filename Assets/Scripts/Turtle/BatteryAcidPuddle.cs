using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lingering acid puddle spawned by BatteryAcidOnDeath at a Battery's death
/// point — a separate object, since the Battery's own GameObject (and any
/// coroutine running on it) is destroyed the instant it dies, before a
/// damage-over-time effect could tick more than once. Ticks once per second
/// against every building within Radius for Tick Count seconds, then
/// self-destroys. Every building, not just Watchtowers — acid pooling on the
/// ground has no reason to care what it's dissolving, and restricting it to
/// towers made it look inert whenever a Battery died next to a Turtle Bed or
/// a Wall.
///
/// Each tick deals 1 damage PLUS UpgradeManager.TrashDamageBonus, the same
/// sum BuildingHealth.OnCollisionEnter2D applies to a physical trash hit — so
/// a run modifier that makes trash hit harder makes battery acid burn harder
/// too, rather than the acid being the one damage source in the game that
/// quietly ignores it. Read live each tick rather than snapshotted at spawn,
/// matching how every other consumer of that bonus reads it.
///
/// Note this means duration and total damage are no longer the same number
/// (they were back when every tick was exactly 1): the puddle always lasts
/// Tick Count seconds, but deals Tick Count x (1 + bonus) over that span.
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
    /// <summary>Damage one tick deals before UpgradeManager.TrashDamageBonus is added on top. The puddle's overall strength is tuned through BatteryAcidOnDeath's Acid Damage (how many ticks it gets), not through this.</summary>
    private const int BaseDamagePerTick = 1;

    private float radius;
    private int tickCount;

    /// <summary>tickCount is how many one-second ticks the puddle lasts, not its total damage — see the class doc comment, each tick's damage scales with UpgradeManager.TrashDamageBonus.</summary>
    public void Initialize(float radius, int tickCount)
    {
        this.radius = radius;
        this.tickCount = tickCount;
        StartCoroutine(TickRoutine());
    }

    private IEnumerator TickRoutine()
    {
        for (int i = 0; i < tickCount; i++)
        {
            DealTick();
            yield return new WaitForSeconds(1f);
        }

        Destroy(gameObject);
    }

    /// <summary>Reused across ticks rather than allocated per tick — this fires once a second per puddle, and several puddles can overlap late in a storm.</summary>
    private readonly List<BuildingHealth> tickTargets = new List<BuildingHealth>();

    /// <summary>
    /// Collects every building in range FIRST, then damages them, rather
    /// than damaging as it walks BuildingHealth.AllBuildings. A tick that
    /// lands a killing blow destroys that building, and BuildingHealth removes
    /// itself from AllBuildings in OnDisable — mutating the very list the walk
    /// is enumerating, which throws InvalidOperationException and aborts the
    /// rest of the tick (so surviving towers in range silently took no damage
    /// that second). Snapshotting decouples the two phases entirely.
    /// </summary>
    private void DealTick()
    {
        float sqrRadius = radius * radius;

        tickTargets.Clear();
        foreach (BuildingHealth building in BuildingHealth.AllBuildings)
        {
            if (building == null) continue;
            if (((Vector2)building.transform.position - (Vector2)transform.position).sqrMagnitude > sqrRadius) continue;

            tickTargets.Add(building);
        }

        // Same shape as BuildingHealth.OnCollisionEnter2D's own damage sum, so
        // the acid and a physical trash hit scale together off one bonus.
        int damage = BaseDamagePerTick + (UpgradeManager.Instance != null ? UpgradeManager.Instance.TrashDamageBonus : 0);

        int hitCount = 0;
        foreach (BuildingHealth target in tickTargets)
        {
            // Re-checked against Unity's overloaded null: an earlier target in
            // this same tick may already have been destroyed since the sweep.
            if (target == null) continue;

            target.ApplyDamage(damage, ignoreCooldown: true);
            hitCount++;
        }

        tickTargets.Clear();

        Debug.Log($"BatteryAcidPuddle: tick dealt {damage} damage to {hitCount} building(s) within {radius} units.");
    }
}
