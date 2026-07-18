using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the Turtle Bed building prefab. Each placed bed spawns exactly one
/// turtle (via the scene's TurtleNest) after a short delay, sends it to visit
/// the bed, and stays linked to it for its whole life: destroying the bed (e.g.
/// trashed down to 0 HP by BuildingHealth) makes its linked turtle leave —
/// waiting out any ongoing storm first, via TurtleDeparture, so it's never
/// pulled away mid-fight. TurtleNest no longer spawns on any internal timer, so
/// placing zero beds means the turtle population never grows on its own.
/// </summary>
public class TurtleBed : MonoBehaviour
{
    /// <summary>Every currently-placed bed, so BuildModeController can enforce Max Turtle Beds (mirrors TurtleAgent.AllTurtles/Coconut.AllCoconuts).</summary>
    private static readonly List<TurtleBed> allBeds = new List<TurtleBed>();
    public static IReadOnlyList<TurtleBed> AllBeds => allBeds;

    [Tooltip("Seconds after placement before this bed's turtle spawns.")]
    [SerializeField] private float spawnDelayAfterPlacement = 5f;

    private IslandGenerator islandGenerator;
    private float spawnTimer;
    private bool hasSpawned;
    private GameObject linkedTurtle;

    private void OnEnable() => allBeds.Add(this);
    private void OnDisable() => allBeds.Remove(this);

    /// <summary>Called by whatever placed this bed (see BuildModeController.TryPlace) with the scene's IslandGenerator, since this can't be pre-wired on the prefab asset itself.</summary>
    public void Initialize(IslandGenerator generator)
    {
        islandGenerator = generator;
    }

    private void Update()
    {
        if (hasSpawned) return;

        spawnTimer += Time.deltaTime;
        if (spawnTimer < spawnDelayAfterPlacement) return;

        hasSpawned = true;
        SpawnLinkedTurtle();
    }

    private void SpawnLinkedTurtle()
    {
        TurtleNest nest = islandGenerator != null ? islandGenerator.TurtleNestInstance : null;
        if (nest == null) return;

        linkedTurtle = nest.SpawnTurtle();
        if (linkedTurtle == null) return;

        TurtleAgent agent = linkedTurtle.GetComponent<TurtleAgent>();
        if (agent != null) agent.MoveToPoint(transform.position);
    }

    private void OnDestroy()
    {
        if (linkedTurtle == null) return;

        // Don't yank the turtle away mid-storm — TurtleDeparture waits for
        // DayStormCycle.IsStorming to clear before actually destroying it.
        TurtleDeparture departure = linkedTurtle.AddComponent<TurtleDeparture>();
        departure.BeginDeparture();
    }
}
