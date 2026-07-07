using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Two visuals: a base (always visible) and a flame (shown only during the
/// day — hidden during storms). While it's day, any turtle physically inside
/// this campfire's trigger radius gains a movement speed buff; the buff is on
/// only while the turtle is actually inside (trigger enter applies it, exit
/// removes it immediately — no lingering timer). Multiple overlapping
/// campfires stack their bonuses linearly on a turtle (see
/// TurtleAgent.ApplyCampfireSpeedBuff), rather than the strongest one winning.
/// </summary>
public class Campfire : MonoBehaviour
{
    [Tooltip("Hidden during storms, shown during the day.")]
    [SerializeField] private GameObject flameVisual;
    [Tooltip("Additive speed bonus applied to any turtle inside the radius, e.g. 0.4 = +40% on its own.")]
    [SerializeField] private float speedBonus = 0.4f;

    private readonly HashSet<TurtleAgent> turtlesInRange = new HashSet<TurtleAgent>();

    private void Update()
    {
        if (flameVisual != null) flameVisual.SetActive(!DayStormCycle.IsStorming);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TurtleAgent turtle = other.GetComponentInParent<TurtleAgent>();
        if (turtle == null || !turtlesInRange.Add(turtle)) return;

        turtle.ApplyCampfireSpeedBuff(speedBonus);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        TurtleAgent turtle = other.GetComponentInParent<TurtleAgent>();
        if (turtle == null || !turtlesInRange.Remove(turtle)) return;

        turtle.RemoveCampfireSpeedBuff(speedBonus);
    }

    private void OnDisable()
    {
        // Building destroyed while turtles were still inside — release them all.
        foreach (TurtleAgent turtle in turtlesInRange)
        {
            if (turtle != null) turtle.RemoveCampfireSpeedBuff(speedBonus);
        }

        turtlesInRange.Clear();
    }
}
