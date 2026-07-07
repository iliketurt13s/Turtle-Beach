using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to any building prefab to give it hit points that trash chips away
/// at on contact. Destroys the building once health reaches zero (if this is
/// a wall, WallAutoTile's own OnDestroy already unregisters it from WallGrid
/// and refreshes neighbor sprites). All currently-alive buildings can be
/// healed back to full at once via HealAll, e.g. when a storm ends.
/// </summary>
public class BuildingHealth : MonoBehaviour
{
    private static readonly List<BuildingHealth> allBuildings = new List<BuildingHealth>();

    /// <summary>Every currently-enabled building, so e.g. Iron Ingot can pick one at random.</summary>
    public static IReadOnlyList<BuildingHealth> AllBuildings => allBuildings;

    [SerializeField] private int maxHealth = 10;
    [SerializeField] private int damagePerHit = 1;
    [SerializeField] private BuildingHealthBar healthBar;
    [Tooltip("Whether turtles can be sent to interact with this building (e.g. buff stations). Non-interactable buildings (like walls) are just obstacles.")]
    [SerializeField] private bool isInteractable = false;

    public bool IsInteractable => isInteractable;

    private int currentHealth;
    private readonly List<Action> pendingBonusRevokers = new List<Action>();

    private void Awake()
    {
        currentHealth = maxHealth;
        if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);
    }

    private void OnEnable()
    {
        allBuildings.Add(this);
    }

    private void OnDisable()
    {
        allBuildings.Remove(this);

        // Don't leave a dangling static-event subscription if this building is
        // destroyed while still holding an active temporary bonus.
        foreach (Action revoke in pendingBonusRevokers) DayStormCycle.StormEnded -= revoke;
        pendingBonusRevokers.Clear();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.GetComponentInParent<TrashItem>() == null) return;

        currentHealth -= damagePerHit;
        if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);
        if (currentHealth <= 0) Destroy(gameObject);
    }

    private void Heal()
    {
        currentHealth = maxHealth;
        if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);
    }

    /// <summary>
    /// Temporarily raises this building's max (and current) health by amount,
    /// automatically reverting the instant the next storm ends (e.g. for Iron
    /// Ingot's pickup buff). Heal()/HealAll() need no changes — they already
    /// heal to whatever maxHealth currently is, boosted or not.
    /// </summary>
    public void ApplyTemporaryMaxHealthBonus(int amount)
    {
        if (amount <= 0) return;

        maxHealth += amount;
        currentHealth += amount;
        if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);

        Action revert = null;
        revert = () =>
        {
            DayStormCycle.StormEnded -= revert;
            pendingBonusRevokers.Remove(revert);

            maxHealth = Mathf.Max(1, maxHealth - amount);
            currentHealth = Mathf.Min(currentHealth, maxHealth);
            if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);
        };

        pendingBonusRevokers.Add(revert);
        DayStormCycle.StormEnded += revert;
    }

    /// <summary>Heals every currently-registered building to full. Call once when a storm ends.</summary>
    public static void HealAll()
    {
        foreach (BuildingHealth building in allBuildings)
        {
            building.Heal();
        }
    }
}
