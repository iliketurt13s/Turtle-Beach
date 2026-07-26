using UnityEngine;

/// <summary>
/// Attach to the Glue Bottle trash prefab: applies a temporary slow debuff to
/// whichever turtle's head lands the hit that damages this trash. Hooked in
/// from TrashHealth.OnTriggerEnter2D — the same turtle-hits-trash contact
/// point the coconut-buff knockback already reads attacker state from, just
/// applying an effect in the opposite direction (trash affecting the turtle).
/// </summary>
[RequireComponent(typeof(TrashHealth))]
public class GlueSlowOnHit : MonoBehaviour
{
    [Tooltip("Speed multiplier applied to the attacking turtle while slowed. 0.5 = half speed.")]
    [SerializeField, Range(0f, 1f)] private float slowMultiplier = 0.5f;
    [Tooltip("Seconds the slow lasts. Hitting another Glue Bottle before it expires restarts the timer rather than stacking.")]
    [SerializeField] private float slowDuration = 3f;

    public void ApplySlow(TurtleAgent attacker)
    {
        if (attacker != null) attacker.ApplyGlueSlow(slowMultiplier, slowDuration);
    }
}
