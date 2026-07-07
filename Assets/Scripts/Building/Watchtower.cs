using UnityEngine;

/// <summary>
/// A turtle sent here (via the normal interactable-building click routing —
/// see TurtleAgent.MoveToBuilding/TurtleSelectionController) is snapped to
/// Turtle Dock Point and parked there (fully immobile, see TurtleAgent.Park)
/// once it physically arrives (see HandleHeadHit's Watchtower branch, which
/// calls TryStationTurtle). Only one turtle may be stationed at a time. While
/// storming and occupied, continuously rotates to face the nearest trash
/// within Target Radius (see TurtleAgent.SetLookTarget) and fires a SandBall
/// at it on an interval — no line-of-sight check yet (deliberately isolated in
/// FindTarget so that can be added later without touching the firing loop).
/// The instant a storm ends, the stationed turtle is automatically released.
/// </summary>
public class Watchtower : MonoBehaviour
{
    [SerializeField] private float targetRadius = 6f;
    [SerializeField] private float fireInterval = 1.5f;
    [Tooltip("Where a stationed turtle is snapped to and held — typically the tower's own center. Defaults to this transform if left unassigned.")]
    [SerializeField] private Transform turtleDockPoint;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject sandBallPrefab;

    private TurtleAgent linkedTurtle;
    private bool wasStorming;
    private float fireTimer;

    /// <summary>Called by TurtleAgent.HandleHeadHit on every physical head-bump against this tower — including incidental ones from a turtle just passing by on some other task. Only actually stations the turtle if: it's currently storming (turtles can't mount the tower during the day), nobody's already stationed, and the tower is genuinely this turtle's current order (CurrentTaskTarget), not just something it happened to collide with.</summary>
    public void TryStationTurtle(TurtleAgent turtle)
    {
        if (!DayStormCycle.IsStorming) return;
        if (linkedTurtle != null || turtle == null) return;
        if (turtle.CurrentTaskTarget != transform) return;

        linkedTurtle = turtle;
        Vector3 dockPosition = turtleDockPoint != null ? turtleDockPoint.position : transform.position;
        turtle.Park(dockPosition);
    }

    private void Update()
    {
        bool storming = DayStormCycle.IsStorming;
        if (wasStorming && !storming) ReleaseTurtle();
        wasStorming = storming;

        if (!storming || linkedTurtle == null) return;

        TrashHealth target = FindTarget();
        linkedTurtle.SetLookTarget(target != null ? target.transform : null);

        fireTimer += Time.deltaTime;
        if (fireTimer < fireInterval) return;
        fireTimer = 0f;

        if (target != null) FireAt(target);
    }

    private void ReleaseTurtle()
    {
        if (linkedTurtle != null) linkedTurtle.Unpark();
        linkedTurtle = null;
    }

    /// <summary>Today: nearest trash in radius only. Swap just this method's body later to add line-of-sight without touching the firing loop above.</summary>
    private TrashHealth FindTarget()
    {
        Vector2 origin = firePoint != null ? (Vector2)firePoint.position : (Vector2)transform.position;
        return TrashHealth.FindNearest(origin, targetRadius);
    }

    private void FireAt(TrashHealth target)
    {
        if (sandBallPrefab == null) return;

        Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;
        GameObject instance = Instantiate(sandBallPrefab, spawnPosition, Quaternion.identity);
        instance.GetComponent<SandBall>()?.Launch(target.transform.position);
    }

    private void OnDestroy()
    {
        // Free the occupant if the tower itself is destroyed while staffed.
        if (linkedTurtle != null) linkedTurtle.Unpark();
    }
}
