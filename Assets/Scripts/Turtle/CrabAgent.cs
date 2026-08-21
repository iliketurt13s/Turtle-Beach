using UnityEngine;

/// <summary>
/// Marker component that turns an otherwise ordinary TurtleAgent into a crab
/// recruit (see CrabRecruitUpgradeCard). Deliberately holds no behavior of its
/// own — the Crab prefab is a Turtle prefab variant, so selection, player
/// orders, pathfinding, harvesting, delivery, the head hitbox and separation
/// all come from TurtleAgent unchanged, and only the handful of places where a
/// crab genuinely differs check TurtleAgent.IsCrab:
///
/// - it never acquires an aggro target, and its head deals no damage to trash
///   (TrashHealth reads TurtleAgent.CanAttackTrash), so by default it sits out
///   the fight entirely;
/// - during a storm it walks home and holds still at the nest rather than
///   guard-roaming around it (see TurtleAgent.UpdateIdle);
/// - run-wide turtle upgrades (speed, crit, carry capacity, Barnacles) and the
///   nest's night food flights both skip it — crabs are their own unit, with
///   their own carry-capacity and combat cards instead (see
///   UpgradeManager.ApplyTo and TurtleNest.GetEligibleTurtles).
///
/// The first two of those revert to ordinary turtle behavior once the Crab
/// Warriors card is picked (UpgradeManager.CrabsFightAtNight); the third never
/// does. Faster crab attack cadence is a prefab setting, not code here — see
/// TurtleHeadHitbox's Max Reload Interval.
/// </summary>
[RequireComponent(typeof(TurtleAgent))]
public class CrabAgent : MonoBehaviour
{
}
