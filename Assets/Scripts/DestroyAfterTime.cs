using UnityEngine;

/// <summary>
/// Drop onto any GameObject to destroy it after a fixed delay, set in the
/// Inspector.
/// </summary>
public class DestroyAfterTime : MonoBehaviour
{
    [Tooltip("Seconds to wait before this GameObject is destroyed.")]
    [SerializeField] private float lifetime = 5f;

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
