using UnityEngine;

/// <summary>
/// Attach to the Battery trash prefab alongside TrashHealth: when this
/// specific instance dies, spawns a lingering BatteryAcidPuddle at the death
/// point that ticks once a second against every building within Acid Radius
/// (Turtle Beds, Walls and Watchtowers alike), for Acid Damage seconds. Each
/// tick deals 1 damage plus whatever
/// UpgradeManager.TrashDamageBonus a run modifier has added, so the total is
/// Acid Damage x (1 + bonus) — duration and total damage were the same number
/// before that bonus existed, and no longer are.
/// Subscribes to TrashHealth.Died (a static event every trash instance raises
/// on death) and filters to itself via the trash parameter, since that event
/// fires for every piece of trash in the scene.
/// </summary>
[RequireComponent(typeof(TrashHealth))]
public class BatteryAcidOnDeath : MonoBehaviour
{
    [Tooltip("Radius the acid damages buildings within — every building type, not just Watchtowers.")]
    [SerializeField] private float acidRadius = 2.5f;
    [Tooltip("How many seconds the acid puddle lingers, ticking once per second. Each tick deals 1 damage plus UpgradeManager's Trash Damage Bonus, so with no modifiers this is also the total damage (8 = 8 seconds, 8 damage) but a +1 damage modifier makes the same 8 seconds deal 16.")]
    [SerializeField] private int acidDamage = 8;
    [Tooltip("Optional acid-splash particle effect played at the point of death. Its emission shape's radius is set to match Acid Radius at spawn time, so the visual splash always matches the actual damage range regardless of how the prefab's own shape module is authored.")]
    [SerializeField] private ParticleSystem acidParticlePrefab;

    private TrashHealth trashHealth;

    private void Awake()
    {
        trashHealth = GetComponent<TrashHealth>();
    }

    private void OnEnable()
    {
        TrashHealth.Died += HandleDied;
    }

    private void OnDisable()
    {
        TrashHealth.Died -= HandleDied;
    }

    private void HandleDied(TrashHealth trash)
    {
        if (trash != trashHealth) return;

        if (acidParticlePrefab != null)
        {
            ParticleSystem instance = Instantiate(acidParticlePrefab, transform.position, Quaternion.identity);
            ParticleSystem.ShapeModule shape = instance.shape;
            shape.radius = acidRadius;
        }

        GameObject puddle = new GameObject("BatteryAcidPuddle");
        puddle.transform.position = transform.position;
        puddle.AddComponent<BatteryAcidPuddle>().Initialize(acidRadius, acidDamage);

        // Without this, DayStormCycle.AnyTrashAlive() has no idea the puddle
        // exists — if the Battery happened to be the last live trash, the
        // storm (and BuildingHealth.HealAll(), which fully heals every
        // Watchtower) would end on the very next frame, wiping out whatever
        // the acid had ticked off so far and making the DoT look like it just
        // stops working partway through instead of running its full course.
        TrashSpawner.Instance?.RegisterExternalSpawn(puddle);
    }
}
