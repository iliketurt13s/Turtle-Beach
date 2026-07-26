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

    /// <summary>Raised right before a building is destroyed by reaching zero health, while its GameObject/components are still valid — lets other systems react to exactly which building was lost.</summary>
    public static event Action<BuildingHealth> Destroyed;

    [SerializeField] private int maxHealth = 10;
    [SerializeField] private int damagePerHit = 1;
    [Tooltip("Minimum seconds between separate damage instances on this building — trash bouncing repeatedly against it (or several pieces colliding in the same instant) can't chip away more than one hit's worth inside this window.")]
    [SerializeField] private float hitCooldown = 1f;
    [SerializeField] private BuildingHealthBar healthBar;
    [Tooltip("Whether turtles can be sent to interact with this building (e.g. buff stations). Non-interactable buildings (like walls) are just obstacles.")]
    [SerializeField] private bool isInteractable = false;

    public bool IsInteractable => isInteractable;

    /// <summary>Overrides the Inspector-authored value at runtime — e.g. a proximity-buff building can force itself non-interactable in code (never an explicit player order), so that stays true regardless of what a prefab's checkbox happens to be set to.</summary>
    public void SetInteractable(bool value) => isInteractable = value;

    private int currentHealth;
    private float lastHitTime = float.NegativeInfinity;
    private readonly List<Action> pendingBonusRevokers = new List<Action>();

    private BuildableDefinition definition;
    private Watchtower watchtower;
    private int appliedHealthBonus;
    private SquashAndStretch squashAndStretch;

    private void Awake()
    {
        definition = GetComponent<BuildableDefinition>();
        watchtower = GetComponent<Watchtower>();
        squashAndStretch = GetComponent<SquashAndStretch>();
        currentHealth = maxHealth;
        if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);
    }

    private void Update()
    {
        // Live-applies HealthBonus from a building-branch upgrade card (e.g.
        // WallHealthUpgradeCard) so it retroactively toughens up already-placed
        // buildings too, not just future ones — maxHealth is otherwise copied
        // once per instance at Instantiate time, unlike Campfire's per-frame
        // live-read stats, so this polls for the delta instead.
        if (definition == null) return;

        int currentBonus = definition.HealthBonus;
        if (currentBonus == appliedHealthBonus) return;

        int delta = currentBonus - appliedHealthBonus;
        appliedHealthBonus = currentBonus;
        maxHealth += delta;
        currentHealth += delta;
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

        int damage = damagePerHit + (UpgradeManager.Instance != null ? UpgradeManager.Instance.TrashDamageBonus : 0);

        TrashDefinition trashDefinition = collision.collider.GetComponentInParent<TrashDefinition>();
        if (watchtower != null && trashDefinition != null && trashDefinition.TowerDamageMultiplier > 1f)
        {
            damage = Mathf.RoundToInt(damage * trashDefinition.TowerDamageMultiplier);
        }

        ApplyDamage(damage);
    }

    /// <summary>Applies damage from any source, collision or not (e.g. Battery's acid AoE via BatteryAcidOnDeath). Shared by OnCollisionEnter2D so both paths destroy exactly the same way. No-ops entirely (no squash, no health change) if Hit Cooldown hasn't elapsed since the last damage instance, so a trash piece bouncing repeatedly against this building can't melt it in one physical contact flurry.</summary>
    public void ApplyDamage(int amount)
    {
        if (Time.time - lastHitTime < hitCooldown) return;
        lastHitTime = Time.time;

        currentHealth -= amount;
        if (healthBar != null) healthBar.SetHealth(currentHealth, maxHealth);
        squashAndStretch?.Play();
        if (currentHealth <= 0)
        {
            Destroyed?.Invoke(this);
            Destroy(gameObject);
        }
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
