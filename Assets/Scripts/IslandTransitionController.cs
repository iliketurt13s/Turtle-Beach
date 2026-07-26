using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the "move to a new island" sequence once GarbagePatchCutsceneController
/// determines the current patch has just been depleted: wipes every placed
/// building (plus any leftover SandPile — see its own registry/doc comment
/// for why it needs a separate pass) and every turtle but one (the new
/// island starts the same way a brand-new game does, via
/// StarterTurtleBedSpawner), clears leftover trash/jellyfish/resource nodes
/// from the old island, resets the resource stockpile, then regenerates the
/// island in place (no scene reload).
///
/// Score (ScoreManager) and every upgrade (UpgradeManager, and
/// BuildModeController's unlocked-buildables set) are deliberately left
/// untouched — this is a mid-run milestone, not a fresh game.
/// </summary>
public class IslandTransitionController : MonoBehaviour
{
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private DayStormCycle dayStormCycle;

    public void RunTransition(Action onComplete)
    {
        StartCoroutine(RunTransitionRoutine(onComplete));
    }

    private IEnumerator RunTransitionRoutine(Action onComplete)
    {
        Debug.Log("IslandTransitionController: island transition beginning...");

        foreach (BuildingHealth building in new List<BuildingHealth>(BuildingHealth.AllBuildings))
        {
            if (building != null) Destroy(building.gameObject);
        }

        // SandPile has no BuildingHealth (see its class doc comment), so it's
        // never in the list above — without this it would be left behind on
        // the old island's cleared ground.
        foreach (SandPile sandPile in new List<SandPile>(SandPile.AllSandPiles))
        {
            if (sandPile != null) Destroy(sandPile.gameObject);
        }

        foreach (TurtleAgent turtle in new List<TurtleAgent>(TurtleAgent.AllTurtles))
        {
            if (turtle != null) Destroy(turtle.gameObject);
        }

        foreach (TrashHealth trash in new List<TrashHealth>(TrashHealth.AllTrash))
        {
            if (trash != null) Destroy(trash.gameObject);
        }

        foreach (JellyfishAgent jellyfish in new List<JellyfishAgent>(JellyfishAgent.AllJellyfish))
        {
            if (jellyfish != null) Destroy(jellyfish.gameObject);
        }

        foreach (ResourceNode node in new List<ResourceNode>(ResourceNode.AllNodes))
        {
            if (node != null) Destroy(node.gameObject);
        }

        // Destroy() defers actual removal (and each object's OnDisable) to the
        // end of this frame, so the colliders above are still physically live
        // right now. Wait a frame before regenerating so StarterTurtleBedSpawner's
        // placement check (run moments from now, only ~5 units from the nest,
        // which is always at world origin on every island) doesn't see a
        // still-live collider from a building we just told Unity to destroy.
        yield return null;

        ResourceManager.Instance?.ResetAll();
        if (dayStormCycle != null) dayStormCycle.AdvanceToNextIsland();

        // ScoreManager, UpgradeManager, and BuildModeController's unlocked set
        // are deliberately not touched anywhere in this method — score and
        // every earned upgrade/unlock carry over to the new island untouched.

        if (dayStormCycle != null) dayStormCycle.enabled = false;

        if (islandGenerator != null) islandGenerator.GenerateIsland();

        if (dayStormCycle != null) dayStormCycle.enabled = true;

        Debug.Log("IslandTransitionController: transition complete.");
        onComplete?.Invoke();
    }
}
