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
/// Deliberately has no BuildingHealth (it doesn't take contact damage from
/// trash the way a wall/Watchtower does — it wears out on its own timer
/// instead), so it isn't swept up by IslandTransitionController's
/// BuildingHealth.AllBuildings cleanup; AllSandPiles below is its own
/// registry (same Awake/OnDestroy pattern as TurtleBed.AllBeds) so that
/// transition can destroy leftover sand piles too instead of leaving them
/// behind on the old island's cleared ground.
/// </summary>
public class SandPile : MonoBehaviour
{
    private static readonly List<SandPile> allSandPiles = new List<SandPile>();

    /// <summary>Every currently-alive sand pile, so IslandTransitionController can destroy leftovers when moving to a new island (mirrors BuildingHealth.AllBuildings/TurtleBed.AllBeds).</summary>
    public static IReadOnlyList<SandPile> AllSandPiles => allSandPiles;

    [SerializeField] private float dampingIncrease = 5f;
    [Tooltip("Seconds of trash contact this sand pile can withstand before it's worn away and destroyed. Only counts down while trash is actually touching it (see class doc comment) — sitting empty doesn't wear it out.")]
    [SerializeField] private float durationBeforeDestroyed = 20f;
    [Tooltip("Fallback seconds between each damage-over-time tick, only used if UpgradeManager isn't available. Once SandPileCostAndDamageUpgradeCard has been picked, EffectiveDotTickInterval reads its live-set UpgradeManager.SandPileDotTickInterval instead, same pattern as EffectiveDampingIncrease.")]
    [SerializeField, Min(0.05f)] private float dotTickInterval = 1f;

    /// <summary>dampingIncrease plus any run-wide bonus from Sand Pile-branch upgrade cards (see UpgradeManager.SandPileDampingBonus) — read live, same pattern as Campfire.EffectiveSpeedBonus.</summary>
    private float EffectiveDampingIncrease => dampingIncrease + (UpgradeManager.Instance != null ? UpgradeManager.Instance.SandPileDampingBonus : 0f);

    /// <summary>UpgradeManager.SandPileDotTickInterval (set by SandPileCostAndDamageUpgradeCard, see class doc comment) if available, else this instance's own Inspector-authored fallback — read live every tick rather than cached, so a mid-run tick-speed pick applies to already-placed piles too.</summary>
    private float EffectiveDotTickInterval => UpgradeManager.Instance != null ? UpgradeManager.Instance.SandPileDotTickInterval : dotTickInterval;

    private readonly Dictionary<Rigidbody2D, float> originalDamping = new Dictionary<Rigidbody2D, float>();
    private readonly Dictionary<Rigidbody2D, float> dotTimers = new Dictionary<Rigidbody2D, float>();
    private float remainingDuration;

    private void Awake()
    {
        remainingDuration = durationBeforeDestroyed;
    }

    private void OnEnable()
    {
        allSandPiles.Add(this);
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

        // Snapshot first: ApplyDamage below can kill the trash, and Destroy()
        // fires OnTriggerExit2D synchronously the instant a destroyed
        // collider's trigger contact is cleaned up (unlike the rest of
        // destruction, which is deferred) -- that reenters OnTriggerExit2D and
        // removes from originalDamping/dotTimers mid-loop, so enumerating the
        // live dictionary here would throw "Collection was modified".
        List<Rigidbody2D> rbs = new List<Rigidbody2D>(originalDamping.Keys);
        float tickInterval = EffectiveDotTickInterval;

        foreach (Rigidbody2D rb in rbs)
        {
            if (rb == null)
            {
                (stale ??= new List<Rigidbody2D>()).Add(rb);
                continue;
            }

            // May have been removed by a synchronous OnTriggerExit2D triggered
            // from a previous iteration's ApplyDamage call (see above).
            if (!originalDamping.ContainsKey(rb)) continue;

            float timer = (dotTimers.TryGetValue(rb, out float t) ? t : 0f) + Time.deltaTime;
            if (timer >= tickInterval)
            {
                timer -= tickInterval;
                rb.GetComponentInParent<TrashHealth>()?.ApplyDamage(damagePerTick);
            }

            // Don't resurrect an entry OnTriggerExit2D just removed.
            if (originalDamping.ContainsKey(rb))
            {
                dotTimers[rb] = timer;
            }
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
        allSandPiles.Remove(this);

        foreach (KeyValuePair<Rigidbody2D, float> entry in originalDamping)
        {
            if (entry.Key != null) entry.Key.linearDamping = entry.Value;
        }

        originalDamping.Clear();
        dotTimers.Clear();
    }
}
