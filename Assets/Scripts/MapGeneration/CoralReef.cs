using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One cell of the Coral Reef sea wall grown in the shallows by
/// CoralReefUpgradeCard. Deliberately not a ResourceNode despite doing the
/// same job in PathfindingManager's obstacle set: a ResourceNode would also
/// make coral harvestable, targetable by a player order, and eligible for
/// IslandPropSpawner's guaranteed-minimum-per-type pass. So this is its own
/// bare registry instead (mirroring ResourceNode.AllNodes / TrashHealth.allTrash),
/// consulted only where coral is genuinely meant to matter.
///
/// The reef blocks trash and nothing else, via two independent mechanisms
/// that have to agree:
///
/// - Pathing: PathfindingManager.BuildBlockedCells folds AllReefs into the
///   blocked set only when its caller asks for it (avoidCoral, the default).
///   Trash takes that default and routes around a reef exactly as it routes
///   around a palm tree; TurtleAgent passes false at every call site, so
///   turtles never detour around something they can swim straight through.
/// - Physics: the prefab sits on the Building layer, where the existing
///   Turtle/TurtleInteracting-vs-Building collision exclusions (see
///   TurtleBuildingCollisionSetup) already let turtles pass through, while
///   trash on the Default layer still collides solidly with it.
///
/// It carries no BuildingHealth, which is what makes it both indestructible
/// and non-interactable — trash that does blunder into a reef bounces off
/// rather than chewing through it. That also keeps it out of
/// BuildingHealth.AllBuildings, so IslandTransitionController clears it via
/// this registry instead (see its own AllReefs pass).
///
/// One CoralReef per cell: the obstacle set keys off a single
/// transform.position per entry, so a wider reef is many of these, not one
/// object with a bigger collider.
/// </summary>
public class CoralReef : MonoBehaviour
{
    private static readonly List<CoralReef> allReefs = new List<CoralReef>();

    /// <summary>Every currently-live piece of coral, so PathfindingManager can treat them as trash-only obstacles and IslandTransitionController can clear them on a new island.</summary>
    public static IReadOnlyList<CoralReef> AllReefs => allReefs;

    private void OnEnable() => allReefs.Add(this);
    private void OnDisable() => allReefs.Remove(this);
}
