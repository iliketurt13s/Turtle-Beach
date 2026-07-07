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

    public string DisplayName => displayName;
    public float Rating => rating;
}
