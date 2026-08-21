using System;
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

    /// <summary>Every currently-alive piece of trash, so e.g. TurtleNest can check whether any has reached the island/shallows yet before it starts dispensing food.</summary>
    public static IReadOnlyList<TrashHealth> AllTrash => allTrash;

    /// <summary>Raised right before a piece of trash is destroyed by reaching zero health, while its GameObject/components are still valid — mirrors BuildingHealth.Destroyed. Fires for every piece of trash in the scene; subscribers (e.g. BatteryAcidOnDeath) filter to themselves.</summary>
    public static event Action<TrashHealth> Died;

    [SerializeField] private int maxHealth = 3;
    [SerializeField] private int damagePerHit = 1;

    private int currentHealth;
    private TrashDefinition definition;
    private Rigidbody2D rb;
    private GlueSlowOnHit glueSlowOnHit;
    private SquashAndStretch squashAndStretch;

    private void Awake()
    {
        currentHealth = maxHealth;
        definition = GetComponent<TrashDefinition>();
        rb = GetComponent<Rigidbody2D>();
        glueSlowOnHit = GetComponent<GlueSlowOnHit>();
        squashAndStretch = GetComponent<SquashAndStretch>();
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

        // A turtle the player is actively mouse-steering (selected) shouldn't
        // attack anything it's dragged into.
        if (attacker.IsSelected) return;

        // A crab recruit deals no damage until the Crab Warriors card is
        // picked — see CrabAgent. Same flag keeps it from ever aggroing, so a
        // pre-Warriors crab only ever reaches trash by physically bumping into
        // it, and that bump does nothing.
        if (!attacker.CanAttackTrash) return;

        int baseDamage = damagePerHit + attacker.BonusDamageToTrash;
        bool isCrit = UnityEngine.Random.value < attacker.CritChance;
        int totalDamage = isCrit ? baseDamage * 2 : baseDamage;
        if (isCrit) Debug.Log($"TrashHealth: critical hit! {totalDamage} damage (base {baseDamage})");

        currentHealth -= totalDamage;
        squashAndStretch?.Play();

        // The attacker's own hit sound, so a turtle sounds the same striking
        // trash as it does striking anything else. Played from this side
        // because this is where a turtle-vs-trash hit is actually adjudicated:
        // TurtleAgent.HandleHeadHit never sees trash at all.
        attacker.PlayHeadHitSound();

        if (glueSlowOnHit != null) glueSlowOnHit.ApplySlow(attacker);

        if (attacker.HasCoconutKnockbackBuff && rb != null)
        {
            Vector2 pushDir = ((Vector2)transform.position - (Vector2)other.transform.position).normalized;
            rb.AddForce(pushDir * attacker.CoconutKnockbackForce, ForceMode2D.Impulse);
        }

        if (currentHealth <= 0) Die();
    }

    /// <summary>Applies flat damage from a non-turtle source (e.g. a Watchtower's SandBall projectile).</summary>
    public void ApplyDamage(int amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0) Die();
    }

    /// <summary>Shared by both death paths above so score is awarded exactly once regardless of what actually landed the killing blow (a turtle's own bite, a Watchtower's SandBall, Sand Pile damage-over-time, ...). Amount is this trash type's own TrashDefinition.Rating (the same "cost index" TrashSpawner spends its round budget on) doubled, so tougher plastic is worth proportionally more.</summary>
    private void Die()
    {
        if (definition != null)
        {
            ScoreManager.Instance?.AddTrashScore(Mathf.RoundToInt(definition.Rating * 2f));

            if (UpgradeManager.Instance != null && UpgradeManager.Instance.TrashDeathDropsUnlocked)
            {
                definition.SpawnDeathDrops(transform.position, GetComponent<TrashAgent>()?.NestTarget);
            }
        }

        Died?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>Finds the closest currently-alive trash within maxDistance of position, or null if none.</summary>
    public static TrashHealth FindNearest(Vector2 position, float maxDistance, Func<TrashHealth, bool> filter = null)
    {
        TrashHealth nearest = null;
        float nearestSqrDistance = maxDistance * maxDistance;

        foreach (TrashHealth trash in allTrash)
        {
            if (trash == null) continue;
            // Applied inside the scan rather than to its result, so a rejected
            // piece of trash sitting closest can't hide an acceptable one just
            // behind it (see TurtleAgent's leash, the only caller today).
            if (filter != null && !filter(trash)) continue;

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
