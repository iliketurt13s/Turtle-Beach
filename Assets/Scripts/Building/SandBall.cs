using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Projectile fired by a Watchtower or a Sand Boulder Roller. Given an initial
/// velocity at spawn time (see Launch/LaunchInDirection); destroys itself after
/// MaxLifetime elapses (in case it misses), or on hitting a TrashHealth once it
/// has run out of pierce.
///
/// Pierce is what separates a roller's boulder from a tower's sand ball: a
/// boulder ploughs through a lane of trash instead of stopping at the first
/// thing it touches. Each piece of trash is remembered as it's hit, because a
/// projectile that keeps travelling can re-enter the same collider (physics
/// jostling, an irregular shape) and would otherwise spend its whole pierce
/// budget on one target.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class SandBall : MonoBehaviour
{
    [SerializeField] private float speed = 6f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float maxLifetime = 3f;
    [Tooltip("How many EXTRA pieces of trash this projectile carries on through after the first. 0 (the default, and every sand ball) stops at the first thing it hits; 2 means it damages three pieces in total before breaking up. Raised at fire time by the Sand Boulder Roller's pierce upgrades.")]
    [SerializeField, Min(0)] private int pierceCount = 0;

    private Rigidbody2D rb;
    private float lifeTimer;

    /// <summary>Trash already damaged by this projectile, so re-entering a collider it has passed through can't consume another point of pierce. Allocated lazily — a non-piercing sand ball is destroyed on its first hit and never needs one.</summary>
    private HashSet<TrashHealth> alreadyHit;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>Called immediately after Instantiate — sets this ball flying toward targetPosition. bonusDamage adds to this ball's damage once (from Watchtower-branch upgrade cards, see UpgradeManager.WatchtowerDamageBonus), read live by Watchtower at fire time; bonusPierce likewise for the roller's own branch.</summary>
    public void Launch(Vector2 targetPosition, int bonusDamage = 0, int bonusPierce = 0)
    {
        Vector2 direction = targetPosition - rb.position;
        LaunchInDirection(direction, bonusDamage, bonusPierce);
    }

    /// <summary>
    /// The direction-first form, for a launcher that aims somewhere other than
    /// straight at what it picked — the Sand Boulder Roller, whose boulders
    /// only ever travel along one of four fixed lanes regardless of exactly
    /// where in that lane the target is standing.
    ///
    /// A degenerate direction falls back to +X rather than leaving a
    /// projectile sitting motionless on top of the tower until its lifetime
    /// runs out.
    /// </summary>
    public void LaunchInDirection(Vector2 direction, int bonusDamage = 0, int bonusPierce = 0)
    {
        damage += bonusDamage;
        pierceCount += Mathf.Max(0, bonusPierce);

        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        direction.Normalize();

        rb.linearVelocity = direction * speed;
        rb.rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
    }

    private void Update()
    {
        lifeTimer += Time.deltaTime;
        if (lifeTimer >= maxLifetime) Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TrashHealth trash = other.GetComponentInParent<TrashHealth>();
        if (trash == null) return;

        // The second half of the condition matters on the LAST target a
        // boulder can hit: pierce is already spent by then, but the projectile
        // is still travelling through colliders it has passed, and without
        // this it would damage one of them a second time on re-entry.
        if (pierceCount > 0 || alreadyHit != null)
        {
            alreadyHit ??= new HashSet<TrashHealth>();
            if (!alreadyHit.Add(trash)) return;
        }

        // Damage before the pierce test, so the piece that uses up the last
        // point of pierce is still damaged by the hit that stops the boulder.
        trash.ApplyDamage(damage);

        if (pierceCount <= 0)
        {
            Destroy(gameObject);
            return;
        }

        pierceCount--;
    }
}
