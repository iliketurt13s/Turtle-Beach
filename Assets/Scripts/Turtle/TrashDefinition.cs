using UnityEngine;

/// <summary>
/// Attach directly to a trash prefab (alongside TrashItem/TrashAgent/TrashHealth)
/// to identify which plastic type it is and how tough that type counts as for
/// spawning purposes. TrashSpawner spends a per-round rating budget on trash
/// picked from the pool, so new plastic types are just new prefabs with this
/// component attached (and their own TrashHealth/TrashAgent stat values tuned
/// to match), not new code.
/// </summary>
public class TrashDefinition : MonoBehaviour
{
    [SerializeField] private string displayName = "Plastic Bottle";
    [Tooltip("How much of a round's rating budget one instance of this plastic type costs to spawn. Tougher/harder types should have a higher rating.")]
    [SerializeField, Min(0f)] private float rating = 1f;
    [Tooltip("Damage multiplier applied specifically when this trash type hits a Watchtower (see BuildingHealth.OnCollisionEnter2D). 1 = no bonus.")]
    [SerializeField, Min(1f)] private float towerDamageMultiplier = 1f;

    [Header("Death Drops")]
    [Tooltip("Smaller trash prefabs this can release when destroyed, once unlocked (see UpgradeManager.TrashDeathDropsUnlocked, set by the Box/Pallet death-drop hazard card). Leave empty for trash types that shouldn't ever drop anything.")]
    [SerializeField] private GameObject[] deathDropPrefabs;
    [SerializeField] private int deathDropCount = 2;
    [SerializeField] private float deathDropSpawnRadius = 0.5f;

    public string DisplayName => displayName;
    public float Rating => rating;
    public float TowerDamageMultiplier => towerDamageMultiplier;

    /// <summary>Scatters Death Drop Count instances of a randomly-picked Death Drop Prefab around origin, each initialized with a path to nestTarget exactly like a normally round-spawned piece (mirrors ResourceNode.SpawnDrop's shape). No-op if Death Drop Prefabs is empty.</summary>
    public void SpawnDeathDrops(Vector3 origin, Transform nestTarget)
    {
        if (deathDropPrefabs == null || deathDropPrefabs.Length == 0)
        {
            // Reached only once death drops are actually unlocked, so this is
            // worth surfacing: the unlock looks broken from the player's side
            // when really no trash type has anything configured to drop.
            Debug.LogWarning($"TrashDefinition ({displayName}): death drops are unlocked but this trash type's Death Drop Prefabs array is empty, so it drops nothing.");
            return;
        }

        for (int i = 0; i < deathDropCount; i++)
        {
            GameObject prefab = deathDropPrefabs[Random.Range(0, deathDropPrefabs.Length)];
            if (prefab == null) continue;

            Vector2 offset = Random.insideUnitCircle * deathDropSpawnRadius;
            GameObject instance = Instantiate(prefab, origin + (Vector3)offset, Quaternion.identity);
            instance.GetComponent<TrashAgent>()?.Initialize(nestTarget);
            TrashSpawner.Instance?.RegisterExternalSpawn(instance);
        }
    }
}
