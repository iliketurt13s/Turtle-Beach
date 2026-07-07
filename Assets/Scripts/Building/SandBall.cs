using UnityEngine;

/// <summary>
/// Projectile fired by a Watchtower. Given an initial velocity toward its
/// target at spawn time (see Launch); destroys itself on hitting a TrashHealth
/// or after MaxLifetime elapses (in case it misses).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class SandBall : MonoBehaviour
{
    [SerializeField] private float speed = 6f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float maxLifetime = 3f;

    private Rigidbody2D rb;
    private float lifeTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>Called immediately after Instantiate — sets this ball flying toward targetPosition.</summary>
    public void Launch(Vector2 targetPosition)
    {
        Vector2 direction = (targetPosition - rb.position);
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

        trash.ApplyDamage(damage);
        Destroy(gameObject);
    }
}
