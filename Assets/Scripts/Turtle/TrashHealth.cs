using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives a piece of trash hit points that turtles chip away at by physically
/// attacking it (see TurtleAgent's aggro behavior) — only a turtle's head
/// counts as a hit, not its shell, so damage is dealt via the head's trigger
/// collider rather than this object's own solid collision. No visible health
/// bar — with lots of trash on screen at once a bar per instance was too
/// cluttered. Destroys the trash once health reaches zero. Also maintains a
/// registry so TurtleAgent can find the nearest living trash within its aggro
/// distance.
/// </summary>
public class TrashHealth : MonoBehaviour
{
    private static readonly List<TrashHealth> allTrash = new List<TrashHealth>();

    [SerializeField] private int maxHealth = 3;
    [SerializeField] private int damagePerHit = 1;

    private int currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        allTrash.Add(this);
    }

    private void OnDisable()
    {
        allTrash.Remove(this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TurtleAgent attacker = other.GetComponentInParent<TurtleAgent>();
        if (attacker == null) return;

        int baseDamage = damagePerHit + attacker.BonusDamageToTrash;
        bool isCrit = Random.value < attacker.CritChance;
        int totalDamage = isCrit ? baseDamage * 2 : baseDamage;
        if (isCrit) Debug.Log($"TrashHealth: critical hit! {totalDamage} damage (base {baseDamage})");

        currentHealth -= totalDamage;
        if (currentHealth <= 0) Destroy(gameObject);
    }

    /// <summary>Applies flat damage from a non-turtle source (e.g. a Watchtower's SandBall projectile).</summary>
    public void ApplyDamage(int amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0) Destroy(gameObject);
    }

    /// <summary>Finds the closest currently-alive trash within maxDistance of position, or null if none.</summary>
    public static TrashHealth FindNearest(Vector2 position, float maxDistance)
    {
        TrashHealth nearest = null;
        float nearestSqrDistance = maxDistance * maxDistance;

        foreach (TrashHealth trash in allTrash)
        {
            if (trash == null) continue;

            float sqrDistance = ((Vector2)trash.transform.position - position).sqrMagnitude;
            if (sqrDistance <= nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = trash;
            }
        }

        return nearest;
    }
}
