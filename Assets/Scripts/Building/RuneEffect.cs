using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for a rune building. A turtle sent here (see TurtleSelectionController
/// routing clicks on interactable buildings to TurtleAgent.MoveToBuilding)
/// bumps into it repeatedly — same bounce-and-collide mechanic as resource
/// harvesting, routed through TurtleHeadHitbox -> TurtleAgent.HandleHeadHit.
/// Each turtle's hits are counted independently; once one reaches
/// Hits Required, it receives this rune's buff (indefinite, applied once)
/// and goes idle again.
/// </summary>
public abstract class RuneEffect : MonoBehaviour
{
    [Tooltip("How many times a turtle must hit this rune before it receives the buff.")]
    [SerializeField] private int hitsRequired = 10;

    private readonly Dictionary<TurtleAgent, int> hitsByTurtle = new Dictionary<TurtleAgent, int>();

    /// <summary>Called by TurtleAgent.HandleHeadHit when a turtle's head touches this rune.</summary>
    public void RegisterHit(TurtleAgent turtle)
    {
        if (turtle == null || AlreadyHasBuff(turtle)) return;

        hitsByTurtle.TryGetValue(turtle, out int hits);
        hits++;

        if (hits >= hitsRequired)
        {
            hitsByTurtle.Remove(turtle);
            ApplyBuff(turtle);
            turtle.ClearTask();
        }
        else
        {
            hitsByTurtle[turtle] = hits;
        }
    }

    protected abstract bool AlreadyHasBuff(TurtleAgent turtle);
    protected abstract void ApplyBuff(TurtleAgent turtle);
}
