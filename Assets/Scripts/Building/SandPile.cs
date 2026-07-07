using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slows trash down while it's on top of this building: raises the trash's
/// Rigidbody2D.linearDamping (Unity 6's renamed .drag) for as long as it's
/// inside the trigger area, restoring its exact original value on exit.
/// Caches each rigidbody's own pre-existing damping rather than assuming a
/// shared baseline, since different trash prefabs already ship with different
/// baked-in values.
/// </summary>
public class SandPile : MonoBehaviour
{
    [SerializeField] private float dampingIncrease = 5f;

    private readonly Dictionary<Rigidbody2D, float> originalDamping = new Dictionary<Rigidbody2D, float>();

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<TrashItem>() == null) return;

        Rigidbody2D rb = other.GetComponentInParent<Rigidbody2D>();
        if (rb == null || originalDamping.ContainsKey(rb)) return;

        originalDamping[rb] = rb.linearDamping;
        rb.linearDamping += dampingIncrease;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Rigidbody2D rb = other.GetComponentInParent<Rigidbody2D>();
        if (rb == null || !originalDamping.TryGetValue(rb, out float original)) return;

        rb.linearDamping = original;
        originalDamping.Remove(rb);
    }

    private void OnDisable()
    {
        foreach (KeyValuePair<Rigidbody2D, float> entry in originalDamping)
        {
            if (entry.Key != null) entry.Key.linearDamping = entry.Value;
        }

        originalDamping.Clear();
    }
}
