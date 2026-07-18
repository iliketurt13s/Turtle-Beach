using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slows trash down while it's on top of this building: raises the trash's
/// Rigidbody2D.linearDamping (Unity 6's renamed .drag) for as long as it's
/// inside the trigger area, restoring its exact original value on exit.
/// Caches each rigidbody's own pre-existing damping rather than assuming a
/// shared baseline, since different trash prefabs already ship with different
/// baked-in values. Wears out from use: durationBeforeDestroyed only counts
/// down while at least one piece of trash is currently touching it (see
/// originalDamping.Count in Update) — sitting empty doesn't erode it, so the
/// countdown pauses (not resets) the moment the last piece of trash leaves.
/// </summary>
public class SandPile : MonoBehaviour
{
    [SerializeField] private float dampingIncrease = 5f;
    [Tooltip("Seconds of trash contact this sand pile can withstand before it's worn away and destroyed. Only counts down while trash is actually touching it (see class doc comment) — sitting empty doesn't wear it out.")]
    [SerializeField] private float durationBeforeDestroyed = 20f;
    [Tooltip("Seconds between each damage-over-time tick on trapped trash, once SandPileCostAndDamageUpgradeCard has been picked (see UpgradeManager.SandPileDotDamagePerTick).")]
    [SerializeField] private float dotTickInterval = 1f;

    /// <summary>dampingIncrease plus any run-wide bonus from Sand Pile-branch upgrade cards (see UpgradeManager.SandPileDampingBonus) — read live, same pattern as Campfire.EffectiveSpeedBonus.</summary>
    private float EffectiveDampingIncrease => dampingIncrease + (UpgradeManager.Instance != null ? UpgradeManager.Instance.SandPileDampingBonus : 0f);

    private readonly Dictionary<Rigidbody2D, float> originalDamping = new Dictionary<Rigidbody2D, float>();
    private readonly Dictionary<Rigidbody2D, float> dotTimers = new Dictionary<Rigidbody2D, float>();
    private float remainingDuration;

    private void Awake()
    {
        remainingDuration = durationBeforeDestroyed;
    }

    private void Update()
    {
        // Only wears out while actively in contact with trash — originalDamping
        // holds exactly the rigidbodies currently inside the trigger.
        if (originalDamping.Count == 0) return;

        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        ApplyDotDamage();
    }

    /// <summary>No-ops until SandPileCostAndDamageUpgradeCard has been picked (UpgradeManager.SandPileDotDamagePerTick > 0) — read live, so it applies to already-placed sand piles the instant the card is picked, not just future ones.</summary>
    private void ApplyDotDamage()
    {
        int damagePerTick = UpgradeManager.Instance != null ? UpgradeManager.Instance.SandPileDotDamagePerTick : 0;
        if (damagePerTick <= 0) return;

        List<Rigidbody2D> stale = null;

        foreach (Rigidbody2D rb in originalDamping.Keys)
        {
            if (rb == null)
            {
                (stale ??= new List<Rigidbody2D>()).Add(rb);
                continue;
            }

            float timer = (dotTimers.TryGetValue(rb, out float t) ? t : 0f) + Time.deltaTime;
            if (timer >= dotTickInterval)
            {
                timer -= dotTickInterval;
                rb.GetComponentInParent<TrashHealth>()?.ApplyDamage(damagePerTick);
            }

            dotTimers[rb] = timer;
        }

        if (stale == null) return;
        foreach (Rigidbody2D rb in stale)
        {
            originalDamping.Remove(rb);
            dotTimers.Remove(rb);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<TrashItem>() == null) return;

        Rigidbody2D rb = other.GetComponentInParent<Rigidbody2D>();
        if (rb == null || originalDamping.ContainsKey(rb)) return;

        originalDamping[rb] = rb.linearDamping;
        rb.linearDamping += EffectiveDampingIncrease;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Rigidbody2D rb = other.GetComponentInParent<Rigidbody2D>();
        if (rb == null || !originalDamping.TryGetValue(rb, out float original)) return;

        rb.linearDamping = original;
        originalDamping.Remove(rb);
        dotTimers.Remove(rb);
    }

    private void OnDisable()
    {
        foreach (KeyValuePair<Rigidbody2D, float> entry in originalDamping)
        {
            if (entry.Key != null) entry.Key.linearDamping = entry.Value;
        }

        originalDamping.Clear();
        dotTimers.Clear();
    }
}
