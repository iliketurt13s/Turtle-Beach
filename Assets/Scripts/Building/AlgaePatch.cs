using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A little pile of algae the player places on land (build placement already
/// requires a sand tile, so "land only" needs no code here). Any turtle
/// standing on it moves faster — the same live-distance proximity model
/// Campfire uses (see UpdateProximityRange there), just with a radius small
/// enough that the buff really does mean "in contact with the tile", and with
/// no day/night gating: unlike a campfire's flame, algae is just as slippery
/// during a storm. Multiple overlapping piles stack their bonuses linearly on
/// a turtle (see TurtleAgent.ApplyAlgaeSpeedBuff), rather than the strongest
/// one winning.
///
/// Two upgrade cards change what a pile does, both read live by the systems
/// that care rather than pushed from here, so a card picked mid-run applies
/// immediately to already-placed piles as well as future ones:
/// AlgaeLingerUpgradeCard makes the buff outlast stepping off (handled
/// entirely inside TurtleAgent), and AlgaeFertilizerUpgradeCard turns on the
/// ResourceRespawnBooster that also sits on this prefab (handled entirely
/// inside that component — see its BoosterKind.Algae branch).
/// </summary>
public class AlgaePatch : MonoBehaviour, IHasPlacementRange
{
    [Tooltip("Additive speed bonus applied to any turtle within range, e.g. 0.3 = +30% on its own.")]
    [SerializeField] private float speedBonus = 0.3f;
    [Tooltip("Radius within which a turtle gets the speed buff. Deliberately about half a tile — the buff is meant to read as 'standing on the algae', not as an aura.")]
    [SerializeField] private float range = 0.6f;

    /// <summary>IHasPlacementRange implementation, so BuildModeController's ghost shows the buff radius while this is selected for placement.</summary>
    public float PlacementRange => range;

    // Records the exact bonus applied to each in-range turtle rather than just
    // which turtles are in range, for the same reason Campfire does: removal
    // must subtract exactly what was added, or the turtle's algaeBonusTotal
    // would drift if speedBonus were ever changed mid-buff.
    private readonly Dictionary<TurtleAgent, float> turtlesInRange = new Dictionary<TurtleAgent, float>();

    private void Update()
    {
        UpdateProximityRange();
    }

    /// <summary>Applies/removes the speed buff based on live distance to each turtle every frame, instead of trigger enter/exit events — a turtle just needs to be within range, regardless of collider/layer setup (which matters here, since turtles pass straight through buildings).</summary>
    private void UpdateProximityRange()
    {
        float rangeSqr = range * range;

        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle == null) continue;

            float sqrDistance = ((Vector2)turtle.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (sqrDistance <= rangeSqr)
            {
                if (!turtlesInRange.ContainsKey(turtle))
                {
                    turtlesInRange[turtle] = speedBonus;
                    turtle.ApplyAlgaeSpeedBuff(speedBonus);
                }
            }
            else if (turtlesInRange.TryGetValue(turtle, out float appliedBonus))
            {
                turtlesInRange.Remove(turtle);
                turtle.RemoveAlgaeSpeedBuff(appliedBonus);
            }
        }
    }

    private void OnDisable()
    {
        // Pile destroyed/removed while turtles were still standing on it —
        // release them all. Each release still honors the linger upgrade, so a
        // turtle keeps the buff for its few seconds rather than losing it the
        // instant the algae underneath it disappears.
        foreach (KeyValuePair<TurtleAgent, float> entry in turtlesInRange)
        {
            if (entry.Key != null) entry.Key.RemoveAlgaeSpeedBuff(entry.Value);
        }

        turtlesInRange.Clear();
    }
}
