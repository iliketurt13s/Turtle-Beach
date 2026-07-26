using UnityEngine;

/// <summary>
/// Attach to the Battery trash prefab alongside TrashHealth: when this
/// specific instance dies, spawns a lingering BatteryAcidPuddle at the death
/// point that deals 1 damage/second to every Watchtower within Acid Radius,
/// for Acid Damage seconds (so duration always equals total damage dealt —
/// e.g. Acid Damage 8 means the puddle lasts 8 seconds and deals 8 total).
/// Subscribes to TrashHealth.Died (a static event every trash instance raises
/// on death) and filters to itself via the trash parameter, since that event
/// fires for every piece of trash in the scene.
/// </summary>
[RequireComponent(typeof(TrashHealth))]
public class BatteryAcidOnDeath : MonoBehaviour
{
    [SerializeField] private float acidRadius = 2.5f;
    [Tooltip("Total damage the acid puddle deals over its lifetime, 1 per second — this is also how many seconds the puddle lingers for (8 damage = 8 seconds).")]
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
