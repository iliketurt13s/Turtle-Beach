using UnityEngine;

/// <summary>
/// Slowly orbits this transform around the world origin (where the nest
/// always sits — see IslandGenerator.SpawnTurtleNest) at a fixed radius, plus
/// an independent slow spin applied only to Debris Spin Root (the debris
/// pile), never to this object's own transform — so anything else parented to
/// the patch stays upright while the pile tumbles. Keeping the spin on a child
/// also means GarbagePatch can measure a debris piece's distance from the
/// pile's center in plain local space, whatever this is currently doing (see
/// GarbagePatch.RemoveOneDebrisPiece). The only parametric continuous-motion object in the project —
/// everything else (turtles, trash, jellyfish) moves via physics or
/// pathfinding — so this is deliberately self-contained and never touches
/// PathfindingManager.
/// </summary>
public class GarbagePatchOrbit : MonoBehaviour
{
    [Tooltip("Tangential speed (world units/second) the patch travels along its orbit circle. Kept as a linear speed rather than a fixed angular one, since GarbagePatchSpawner scales the orbit radius with the active game mode's island size (see IslandGenerator's Game Mode Size Presets) — a fixed degrees/second would make the patch visibly sweep faster on a larger island's much bigger circle instead of taking proportionally longer to come back around. Default (2.2) reproduces the pre-game-mode fixed 2 degrees/second at the original 64x64 map's orbit radius.")]
    [SerializeField] private float orbitLinearSpeed = 2.2f;
    [Tooltip("Transform spun for the tumbling-debris look — should be GarbagePatch's Debris Root, not this object's own transform, so the health bar (a separate, non-spinning child) stays upright.")]
    [SerializeField] private Transform debrisSpinRoot;
    [SerializeField] private float selfSpinDegreesPerSecond = 15f;

    private float orbitRadius;
    private float currentAngleDegrees;
    private float orbitSpeedDegreesPerSecond;

    /// <summary>Called once by GarbagePatchSpawner right after Instantiate — a scene-computed placement radius/starting angle can't be baked into the prefab itself. Derives angular speed from Orbit Linear Speed and this radius so tangential speed stays consistent across game modes' very different radii, rather than the old fixed angular speed that would sweep faster at a larger radius.</summary>
    public void Initialize(float radius, float startAngleDegrees)
    {
        orbitRadius = radius;
        currentAngleDegrees = startAngleDegrees;
        orbitSpeedDegreesPerSecond = orbitRadius > 0f ? (orbitLinearSpeed / orbitRadius) * Mathf.Rad2Deg : 0f;
        UpdatePosition();
    }

    private void Update()
    {
        currentAngleDegrees += orbitSpeedDegreesPerSecond * Time.deltaTime;
        UpdatePosition();
        if (debrisSpinRoot != null) debrisSpinRoot.Rotate(0f, 0f, selfSpinDegreesPerSecond * Time.deltaTime);
    }

    private void UpdatePosition()
    {
        float rad = currentAngleDegrees * Mathf.Deg2Rad;
        transform.position = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * orbitRadius;
    }
}
