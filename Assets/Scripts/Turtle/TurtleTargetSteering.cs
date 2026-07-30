using UnityEngine;

/// <summary>
/// Continuously rotates the turtle's Rigidbody2D to face an assignable target
/// transform. Combined with TurtleLocomotion's ongoing fin-stroke impulses, this
/// makes the turtle always paddle toward wherever the target currently is.
/// The target can be swapped at runtime (e.g. the camera today, a resource or
/// waypoint later) via the Inspector field or SetTarget.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class TurtleTargetSteering : MonoBehaviour
{
    [Header("Steering")]
    [Tooltip("The transform the turtle continuously turns to face and paddles toward.")]
    [SerializeField] private Transform target;
    [Tooltip("Degrees per second the turtle can turn to face the target.")]
    [SerializeField] private float turnSpeed = 180f;

    // Combined product of every currently-active speed buff (permanent upgrade
    // x campfire x temporary food buff), pushed here by TurtleLocomotion so
    // turning snaps around quicker right along with the faster stroke rate,
    // instead of a buffed turtle paddling fast but still turning at its normal
    // rate. 1 = no buff active.
    private float turnSpeedMultiplier = 1f;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        if (target == null) return;

        Vector2 direction = (Vector2)target.position - rb.position;
        if (direction.sqrMagnitude < 0.0001f) return;

        // Assumes the turtle's art faces along local +X (rotation 0 = facing right),
        // matching the forward axis TurtleLocomotion uses for its impulse.
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float newAngle = Mathf.MoveTowardsAngle(rb.rotation, targetAngle, turnSpeed * turnSpeedMultiplier * Time.fixedDeltaTime);
        rb.MoveRotation(newAngle);
    }

    /// <summary>Reassign what the turtle steers toward at runtime.</summary>
    public void SetTarget(Transform newTarget) => target = newTarget;

    /// <summary>Called by TurtleLocomotion with the combined product of every currently-active speed buff, so turn rate scales along with stroke rate. Overwrites, not compounds — TurtleLocomotion always passes the already-combined total.</summary>
    public void SetTurnSpeedMultiplier(float multiplier) => turnSpeedMultiplier = multiplier;
}
