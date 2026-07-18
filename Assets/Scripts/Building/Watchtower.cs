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
/// The player can also pull it away early with a direct order at any time,
/// day or night (see TurtleAgent.MoveToPoint/MoveToBuilding/MoveToResource,
/// which unpark a stationed turtle themselves) — Update notices the vacancy
/// on its own and forgets the turtle so a new one can be stationed.
/// </summary>
public class Watchtower : MonoBehaviour
{
    [SerializeField] private float targetRadius = 6f;
    [SerializeField] private float fireInterval = 1.5f;

    /// <summary>fireInterval shortened by any run-wide bonus from Watchtower-branch upgrade cards (see UpgradeManager.WatchtowerFireRateBonus), e.g. a 0.2 bonus fires every fireInterval / 1.2 seconds — read live, same pattern as Campfire.EffectiveSpeedBonus.</summary>
    private float EffectiveFireInterval => fireInterval / (1f + (UpgradeManager.Instance != null ? UpgradeManager.Instance.WatchtowerFireRateBonus : 0f));
    [Tooltip("Where a stationed turtle is snapped to and held — typically the tower's own center. Defaults to this transform if left unassigned.")]
    [SerializeField] private Transform turtleDockPoint;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject sandBallPrefab;

    private TurtleAgent linkedTurtle;
    private bool wasStorming;
    private float fireTimer;

    /// <summary>Called by TurtleAgent.HandleHeadHit on every physical head-bump against this tower — including incidental ones from a turtle just passing by on some other task. Only actually stations the turtle if nobody's already stationed and the tower is genuinely this turtle's current order (CurrentTaskTarget), not just something it happened to collide with — stationing itself works any time (day or storm), so a turtle sent here always gets snapped to dock center on first contact rather than left jammed against the tower's solid collider; only the rotate-and-fire behavior in Update() is storm-gated.</summary>
    public void TryStationTurtle(TurtleAgent turtle)
    {
        if (linkedTurtle != null || turtle == null) return;
        if (turtle.CurrentTaskTarget != transform) return;

        linkedTurtle = turtle;
        Vector3 dockPosition = turtleDockPoint != null ? turtleDockPoint.position : transform.position;
        turtle.Park(dockPosition);
    }

    private void Update()
    {
        // The player can pull a stationed turtle away with a direct order at
        // any time, including mid-storm (see TurtleAgent.MoveToPoint/
        // MoveToBuilding/MoveToResource, which unpark it themselves) — notice
        // that here and forget it, so a new turtle can be stationed instead
        // of this tower thinking its post is still filled.
        if (linkedTurtle != null && !linkedTurtle.IsParked) linkedTurtle = null;

        bool storming = DayStormCycle.IsStorming;
        if (wasStorming && !storming) ReleaseTurtle();
        wasStorming = storming;

        if (!storming || linkedTurtle == null) return;

        TrashHealth target = FindTarget();
        linkedTurtle.SetLookTarget(target != null ? target.transform : null);

        fireTimer += Time.deltaTime;
        if (fireTimer < EffectiveFireInterval) return;
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
        int bonusDamage = UpgradeManager.Instance != null ? UpgradeManager.Instance.WatchtowerDamageBonus : 0;
        instance.GetComponent<SandBall>()?.Launch(target.transform.position, bonusDamage);
    }

    private void OnDestroy()
    {
        // Free the occupant if the tower itself is destroyed while staffed.
        if (linkedTurtle != null) linkedTurtle.Unpark();
    }
}
