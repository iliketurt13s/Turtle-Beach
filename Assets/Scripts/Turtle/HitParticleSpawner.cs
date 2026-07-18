using UnityEngine;

/// <summary>
/// Drop onto any GameObject that has a trigger Collider2D to spawn a particle
/// effect at the contact point every time something tagged Food Tag or Trash
/// Tag below enters that trigger — e.g. added directly onto a turtle's Head
/// child, alongside TurtleHeadHitbox (which shares that same trigger
/// collider), so a head-hit against food or trash gets a hit-flash while
/// other things it bumps (a rune, a Watchtower...) don't. No coupling to
/// TurtleAgent/TurtleHeadHitbox at all — Unity calls OnTriggerEnter2D on every
/// component on a GameObject independently, so this reacts on its own.
///
/// The particle prefab is expected to be greyscale/white so it can be freely
/// recolored per instance via Particle Color below, rather than needing a
/// separate colored prefab variant per GameObject this is attached to. The
/// prefab's own Particle System should have Stop Action set to Destroy (Main
/// module) so spawned instances clean themselves up once finished, without
/// this script needing to track or destroy them itself.
/// </summary>
public class HitParticleSpawner : MonoBehaviour
{
    [Tooltip("Particle prefab to spawn on every matching trigger hit. Expected to be greyscale/white so Particle Color below can freely recolor it.")]
    [SerializeField] private ParticleSystem particlePrefab;
    [Tooltip("Tint applied to every spawned particle instance's start color.")]
    [SerializeField] private Color particleColor = Color.white;
    [Tooltip("Only spawn when the colliding object (or its parent) has this tag.")]
    [SerializeField] private string foodTag = "food";
    [Tooltip("Only spawn when the colliding object (or its parent) has this tag.")]
    [SerializeField] private string trashTag = "trash";

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (particlePrefab == null) return;
        if (!other.CompareTag(foodTag) && !other.CompareTag(trashTag)) return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        ParticleSystem instance = Instantiate(particlePrefab, hitPoint, Quaternion.identity);

        ParticleSystem.MainModule main = instance.main;
        main.startColor = particleColor;
    }
}
