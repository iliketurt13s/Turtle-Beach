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
///
/// Sand Boulder Roller (see SandBoulderRoller) is a subclass rather than a
/// second copy of all this: everything above — stationing, dismissal, recall,
/// eviction, the aim-and-fire loop — is identical for it, and only three
/// things differ (which trash counts as a target, which way the projectile
/// actually goes, and which upgrade track the range/fire rate come from).
/// Those three are the protected virtuals below; the serialized fields are
/// protected for the same reason. Nothing else here should need overriding,
/// and a fourth tower type should extend this the same way.
/// </summary>
public class Watchtower : MonoBehaviour, IHasPlacementRange
{
    [SerializeField] protected float targetRadius = 6f;
    [SerializeField] protected float fireInterval = 1.5f;

    /// <summary>IHasPlacementRange implementation, so BuildModeController's ghost shows this tower's real (upgrade-inclusive) fire radius while it's selected for placement.</summary>
    public float PlacementRange => EffectiveTargetRadius;

    /// <summary>How far this tower can actually see, base radius plus whatever its own branch has added. Virtual because each tower type reads a different upgrade track — a plain Watchtower currently has no range card, so this is just the authored value.</summary>
    protected virtual float EffectiveTargetRadius => targetRadius;

    /// <summary>fireInterval shortened by any run-wide bonus from Watchtower-branch upgrade cards (see UpgradeManager.WatchtowerFireRateBonus), e.g. a 0.2 bonus fires every fireInterval / 1.2 seconds — read live, same pattern as Campfire.EffectiveSpeedBonus. Virtual so a different tower type isn't silently sped up by cards gated on the Watchtower being unlocked.</summary>
    protected virtual float EffectiveFireInterval => fireInterval / (1f + (UpgradeManager.Instance != null ? UpgradeManager.Instance.WatchtowerFireRateBonus : 0f));
    [Tooltip("Where a stationed turtle is snapped to and held — typically the tower's own center. Defaults to this transform if left unassigned.")]
    [SerializeField] private Transform turtleDockPoint;
    [SerializeField] protected Transform firePoint;
    [SerializeField] protected GameObject sandBallPrefab;

    /// <summary>Where projectiles leave from — the assigned Fire Point, or this tower's own center if none was wired. Shared by targeting and firing so the two can never measure from different places.</summary>
    protected Vector2 FireOrigin => firePoint != null ? (Vector2)firePoint.position : (Vector2)transform.position;

    private TurtleAgent linkedTurtle;
    private bool wasStorming;
    private float fireTimer;

    /// <summary>True from the moment a stationed turtle is dismissed for the day (see DismissForDay) until it's actually confirmed re-parked here at night (see TryStationTurtle, which clears it) — spans both "off doing daytime things" and "currently walking back after being recalled," so Update's vacancy check below doesn't mistake either sub-phase for the player having pulled it away for good and forget linkedTurtle.</summary>
    private bool expectingReturn;

    /// <summary>Called by TurtleAgent.HandleHeadHit on every physical head-bump against this tower — including incidental ones from a turtle just passing by on some other task. Only actually stations the turtle if the tower is genuinely this turtle's current order (CurrentTaskTarget), not just something it happened to collide with — stationing itself works any time (day or storm), so a turtle sent here always gets snapped to dock center on first contact rather than left jammed against the tower's solid collider; only the rotate-and-fire behavior in Update() is storm-gated. A different turtle deliberately sent here (the CurrentTaskTarget check already rules out an incidental bump) always takes over rather than being blocked — see EvictCurrentOccupant.</summary>
    public void TryStationTurtle(TurtleAgent turtle)
    {
        if (turtle == null) return;
        if (turtle.CurrentTaskTarget != transform) return;

        if (linkedTurtle != null && linkedTurtle != turtle) EvictCurrentOccupant();

        linkedTurtle = turtle;
        expectingReturn = false;
        Vector3 dockPosition = turtleDockPoint != null ? turtleDockPoint.position : transform.position;
        turtle.Park(dockPosition);
    }

    /// <summary>Frees whichever turtle currently holds this tower so a different one can take over instead of being refused. Unpark() covers an already-stationed occupant — it also clears its task and restores normal physics. ClearTask() covers one still walking back from a RecallForNight order that hasn't arrived yet (Unpark() would no-op on it, since it was never actually parked): without clearing its task too, it would still be genuinely ordered here (CurrentTaskTarget still this tower) and, on finally arriving, would pass TryStationTurtle's own CurrentTaskTarget check and wrongly evict the new occupant that preempted it.</summary>
    private void EvictCurrentOccupant()
    {
        if (linkedTurtle.IsParked) linkedTurtle.Unpark();
        else linkedTurtle.ClearTask();
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

    /// <summary>Today: nearest trash in radius only. Swap just this method's body later to add line-of-sight without touching the firing loop above. Virtual because a tower that can only shoot along fixed lanes has to reject targets it physically cannot hit (see SandBoulderRoller).</summary>
    protected virtual TrashHealth FindTarget() => TrashHealth.FindNearest(FireOrigin, EffectiveTargetRadius);

    /// <summary>Spawns and launches one projectile at target. Virtual so a tower type can aim it somewhere other than straight at what it picked, or hand it different projectile stats.</summary>
    protected virtual void FireAt(TrashHealth target)
    {
        if (sandBallPrefab == null) return;

        GameObject instance = Instantiate(sandBallPrefab, FireOrigin, Quaternion.identity);
        int bonusDamage = UpgradeManager.Instance != null ? UpgradeManager.Instance.WatchtowerDamageBonus : 0;
        instance.GetComponent<SandBall>()?.Launch(target.transform.position, bonusDamage);
    }

    private void OnDestroy()
    {
        // Free the occupant if the tower itself is destroyed while staffed.
        if (linkedTurtle != null) linkedTurtle.Unpark();
    }
}
