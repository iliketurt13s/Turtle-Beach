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
/// The instant day begins, a turtle that's still stationed here (i.e. it
/// wasn't pulled away earlier that night) is dismissed to go do normal
/// daytime things (see DismissForDay) instead of sitting there idle all day
/// doing nothing — a watchtower has nothing to do outside a storm — but this
/// tower stays reserved for it, so the instant the next storm starts it's
/// automatically walked right back and re-stationed (see RecallForNight),
/// without the player needing to manually re-station it every single night.
/// The player can still pull it away at any time, day or night — before that
/// (see TurtleAgent.MoveToPoint/MoveToBuilding/MoveToResource, which unpark a
/// stationed turtle themselves and are what Update's own vacancy check
/// below notices), or after, since a fresh order simply overrides the
/// walk-back task the same way any other order overrides any other task.
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

    /// <summary>True from the moment a stationed turtle is dismissed for the day (see DismissForDay) until it's actually confirmed re-parked here at night (see TryStationTurtle, which clears it) — spans both "off doing daytime things" and "currently walking back after being recalled," so Update's vacancy check below doesn't mistake either sub-phase for the player having pulled it away for good and forget linkedTurtle.</summary>
    private bool expectingReturn;

    /// <summary>Called by TurtleAgent.HandleHeadHit on every physical head-bump against this tower — including incidental ones from a turtle just passing by on some other task. Only actually stations the turtle if nobody's already stationed and the tower is genuinely this turtle's current order (CurrentTaskTarget), not just something it happened to collide with — stationing itself works any time (day or storm), so a turtle sent here always gets snapped to dock center on first contact rather than left jammed against the tower's solid collider; only the rotate-and-fire behavior in Update() is storm-gated.</summary>
    public void TryStationTurtle(TurtleAgent turtle)
    {
        if (linkedTurtle != null || turtle == null) return;
        if (turtle.CurrentTaskTarget != transform) return;

        linkedTurtle = turtle;
        expectingReturn = false;
        Vector3 dockPosition = turtleDockPoint != null ? turtleDockPoint.position : transform.position;
        turtle.Park(dockPosition);
    }

    private void Update()
    {
        // The player can pull a stationed turtle away with a direct order at
        // any time, including mid-storm (see TurtleAgent.MoveToPoint/
        // MoveToBuilding/MoveToResource, which unpark it themselves) — notice
        // that here and forget it, so a new turtle can be stationed instead
        // of this tower thinking its post is still filled. Skipped while
        // expectingReturn, since a dismissed-for-the-day (or currently
        // walking back) turtle is deliberately not parked right now without
        // having actually been pulled away for good.
        if (linkedTurtle != null && !linkedTurtle.IsParked && !expectingReturn) linkedTurtle = null;

        bool storming = DayStormCycle.IsStorming;
        if (wasStorming && !storming) DismissForDay();
        else if (!wasStorming && storming) RecallForNight();
        wasStorming = storming;

        // linkedTurtle.IsParked (not just linkedTurtle != null) guards the aim/fire
        // loop below — while a recalled turtle is still walking back (see
        // RecallForNight), it hasn't been re-parked yet, and SetLookTarget's
        // steering.SetTarget call would otherwise fight with the walk-back's
        // own steering target every frame until it arrives.
        if (!storming || linkedTurtle == null || !linkedTurtle.IsParked) return;

        TrashHealth target = FindTarget();
        linkedTurtle.SetLookTarget(target != null ? target.transform : null);

        fireTimer += Time.deltaTime;
        if (fireTimer < EffectiveFireInterval) return;
        fireTimer = 0f;

        if (target != null) FireAt(target);
    }

    /// <summary>Called the instant day begins while a turtle is still stationed here — Update's own vacancy check above already covers a mid-storm pull-away separately, so reaching this method at all means the turtle stuck out the whole night, exactly the case this tower should keep reserved for. Unparks it to go do normal daytime things (harvest, wander, etc.) rather than sitting idle here all day — a watchtower has nothing for it to do outside a storm — while expectingReturn keeps the reservation alive for RecallForNight to act on once night falls again.</summary>
    private void DismissForDay()
    {
        if (linkedTurtle == null) return;

        expectingReturn = true;
        linkedTurtle.Unpark();
    }

    /// <summary>Called the instant night falls, for a turtle dismissed that morning (see DismissForDay/expectingReturn) — walks it back to this same tower; TryStationTurtle re-parks it (and clears expectingReturn) once it physically arrives, exactly like a fresh player order would. A genuine player order given anytime before or after this overrides the walk-back the same way any order overrides any task.</summary>
    private void RecallForNight()
    {
        if (linkedTurtle == null || !expectingReturn) return;

        linkedTurtle.MoveToBuilding(transform);
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
