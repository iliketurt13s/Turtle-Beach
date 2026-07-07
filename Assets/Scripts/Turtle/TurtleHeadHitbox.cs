using UnityEngine;

/// <summary>
/// Attach to the turtle's Head child, alongside its own trigger Collider2D.
/// Relays contact to the parent TurtleAgent so only head contact — not the
/// shell — counts as a harvest/attack hit. The shell's own solid collider
/// still drives the physical bounce-and-collide movement; this is purely a
/// "did the head specifically touch it" detector riding along on top of that.
/// </summary>
public class TurtleHeadHitbox : MonoBehaviour
{
    private TurtleAgent agent;

    private void Awake()
    {
        agent = GetComponentInParent<TurtleAgent>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (agent != null) agent.HandleHeadHit(other);
    }
}
