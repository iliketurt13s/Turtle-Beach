using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the turtle's Head child, alongside its own trigger Collider2D.
/// Relays contact to the parent TurtleAgent so only head contact — not the
/// shell — counts as a harvest/attack hit. The shell's own solid collider
/// still drives the physical bounce-and-collide movement; this is purely a
/// "did the head specifically touch it" detector riding along on top of that.
/// On every TurtleLocomotion.ImpulseApplied (one per fin stroke — see that
/// class), briefly disables and re-enables its own collider (see
/// ReloadCollisionRoutine) to force Unity to treat the next overlap as a
/// fresh contact — without this, a turtle mashed continuously against
/// something (trash in particular, which unlike a harvestable resource has
/// no bouncy Physics Material 2D guaranteeing a genuine separate-and-reapproach
/// cycle) can get its trigger "stuck": still physically touching, but
/// OnTriggerEnter2D never re-fires because Unity never sees the contact
/// actually end, so no further hits land despite the turtle still pushing
/// against it every physics step. Tied to impulses rather than reloading
/// again immediately after every hit — the latter would let a stuck contact
/// re-fire every physics step (up to the fixed-timestep rate, e.g. 50/sec),
/// far faster than the turtle is actually swimming — so this instead
/// guarantees at most (and dependably) one extra hit opportunity per stroke,
/// matching the turtle's own movement cadence and scaling with it exactly
/// the way speed buffs already do.
/// </summary>
public class TurtleHeadHitbox : MonoBehaviour
{
    [Tooltip("Longest this hitbox will go without a reload, in seconds, regardless of stroke cadence. 0 (the default, and what the Turtle prefab uses) keeps hits tied purely to fin strokes as described above. Above 0 adds a timer on top, so a unit can land hits faster than it swims — this is what gives the Crab prefab its much shorter attack interval.")]
    [SerializeField, Min(0f)] private float maxReloadInterval = 0f;

    private TurtleAgent agent;
    private TurtleLocomotion locomotion;
    private Collider2D headCollider;
    private Coroutine reloadRoutine;
    private float sinceLastReload;

    private void Awake()
    {
        agent = GetComponentInParent<TurtleAgent>();
        locomotion = GetComponentInParent<TurtleLocomotion>();
        headCollider = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        if (locomotion != null) locomotion.ImpulseApplied += ReloadCollision;
    }

    private void OnDisable()
    {
        if (locomotion != null) locomotion.ImpulseApplied -= ReloadCollision;

        reloadRoutine = null;
        if (headCollider != null) headCollider.enabled = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (agent != null) agent.HandleHeadHit(other);
    }

    /// <summary>Only does anything when Max Reload Interval is above zero: forces a reload whenever that long has passed since the last one, so a unit configured with a short interval keeps landing hits between (and faster than) its own fin strokes. Left entirely inert on the Turtle prefab, where the stroke-driven reload above is the only one.</summary>
    private void Update()
    {
        if (maxReloadInterval <= 0f) return;

        sinceLastReload += Time.deltaTime;
        if (sinceLastReload < maxReloadInterval) return;

        ReloadCollision();
    }

    private void ReloadCollision()
    {
        if (headCollider == null || reloadRoutine != null) return;

        sinceLastReload = 0f;
        reloadRoutine = StartCoroutine(ReloadCollisionRoutine());
    }

    /// <summary>Disables the collider for exactly one physics step, then re-enables it — long enough for Unity's physics engine to register the contact as ended (toggling within the same step never actually would), so the very next overlap check generates a brand new OnTriggerEnter2D even if the two colliders in fact never separated.</summary>
    private IEnumerator ReloadCollisionRoutine()
    {
        headCollider.enabled = false;
        yield return new WaitForFixedUpdate();
        headCollider.enabled = true;
        reloadRoutine = null;
    }
}
