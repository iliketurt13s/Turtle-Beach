using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single scene-wide listener translating mouse input into turtle
/// selection/orders: left-click a turtle to select it, left-click elsewhere to
/// send the selected turtle there (or to harvest, if a resource was clicked) —
/// which also deselects it, so a fresh click is needed to redirect it again.
/// Right-click deselects without issuing an order. Runs independently of
/// CameraController's left-click-drag camera pan (both simply poll
/// Mouse.current each frame); a press/release distance threshold keeps a
/// deliberate pan-drag from also issuing a stray order — but that threshold
/// alone only measures net displacement, so a pan/zoom gesture that wanders
/// and happens to release back near where it started (easy to do while
/// scrolling to zoom mid-drag) would otherwise still slip through and read as
/// a click, sending the selected turtle wherever the camera ended up panning
/// to. CameraController.WasDragging (cumulative movement while held, not just
/// net displacement) is checked too, so any real drag suppresses the order
/// regardless of where it happened to end. At night (DayStormCycle.IsStorming),
/// clicking a resource just issues a ground-move order instead of a harvest
/// order — harvesting is a day-only activity — but buildings (a Watchtower
/// above all) stay fully clickable at night, since that's exactly when
/// stationing a turtle at one matters. Both turtle selection and resource
/// clicks are magnetic within a tunable radius (TurtleClickMagnetRadius/
/// ResourceClickMagnetRadius) — a click that misses every exact hit test
/// still resolves to the nearest candidate within range, rather than
/// requiring pixel-perfect precision. Set either to 0 for the old exact-hit-
/// only behavior. When a turtle and a resource are both within range of the
/// same click (their magnet radii/bounds overlapping), HandleClick defers to
/// whichever one is actually closer to the click point rather than always
/// favoring turtle selection.
/// </summary>
public class TurtleSelectionController : MonoBehaviour
{
    [Header("Click Detection")]
    [Tooltip("Max screen-pixel distance between press and release to count as a click rather than a drag.")]
    [SerializeField] private float clickDistanceThreshold = 6f;

    [Header("Click Tolerance")]
    [Tooltip("Extra world-space radius, beyond a turtle's own (fairly small) collider, that still selects it — forgives a slightly missed click instead of requiring a pixel-perfect hit. 0 = exact collider hit only.")]
    [SerializeField] private float turtleClickMagnetRadius = 0.3f;
    [Tooltip("Extra world-space radius, beyond a resource's own rendered sprite bounds, that still counts as clicking it (see ResourceClickTarget.FindClickTargetAt). 0 = exact sprite-bounds hit only.")]
    [SerializeField] private float resourceClickMagnetRadius = 0.2f;

    [Header("Click Feedback")]
    [Tooltip("Spawned at the clicked world point on every genuine click this controller handles (turtle select, resource/building order, ground move). Left unassigned = no effect. Never fires for a build-mode placement click, since those are handled entirely by BuildModeController instead.")]
    [SerializeField] private ParticleSystem clickParticlePrefab;
    [Tooltip("Tint applied to every spawned click particle instance's start color.")]
    [SerializeField] private Color clickParticleColor = Color.white;

    /// <summary>True while a turtle is currently selected, e.g. for CellIndicator to know whether to show.</summary>
    public bool HasSelection => selectedTurtle != null;

    private Vector2 pressPosition;
    private TurtleAgent selectedTurtle;
    private Camera cam;

    private void Awake()
    {
        cam = Camera.main;
    }

    private void Update()
    {
        if (BuildModeController.IsActive || UpgradeSelectionUI.IsActive || GarbagePatchCutsceneController.IsActive || GameOverUI.IsPaused) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = mouse.position.ReadValue();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            Vector2 releasePosition = mouse.position.ReadValue();
            bool wasCameraDrag = CameraController.Instance != null && CameraController.Instance.WasDragging;
            if (!wasCameraDrag && Vector2.Distance(pressPosition, releasePosition) <= clickDistanceThreshold)
            {
                HandleClick(releasePosition);
            }
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            DeselectCurrent();
        }
    }

    private void HandleClick(Vector2 screenPosition)
    {
        if (cam == null) return;

        Vector3 worldPoint = cam.ScreenToWorldPoint(screenPosition);
        worldPoint.z = 0f;

        SpawnClickParticle(worldPoint);

        Collider2D hit = Physics2D.OverlapPoint(worldPoint);

        TurtleAgent clickedTurtle = ResolveClickedTurtle(hit, worldPoint, out float turtleSqrDistance);

        // Resources are hit-tested against their actual rendered sprite bounds
        // (see ResourceClickTarget), not the small gameplay collider a click
        // would otherwise need to land on — e.g. a palm tree's canopy is much
        // bigger than its trunk collider. At night (storming), only resource
        // *harvest* targeting is disabled — clicking a resource just moves the
        // turtle there like any other ground point, matching TurtleAgent
        // treating harvesting as a day-only activity and cancelling resource
        // tasks the instant a storm starts. A resource is only actionable at
        // all while a turtle is already selected (nothing to give the order
        // to otherwise) — clickedResource simply stays null in that case, so
        // the distance comparison below always falls through to turtle
        // selection with no special-casing needed.
        bool night = DayStormCycle.IsStorming;
        Transform clickedResource = null;
        float resourceSqrDistance = float.MaxValue;
        if (selectedTurtle != null && !night)
        {
            clickedResource = ResourceClickTarget.FindClickTargetAt(worldPoint, resourceClickMagnetRadius);
            if (clickedResource != null)
            {
                resourceSqrDistance = ((Vector2)clickedResource.position - (Vector2)worldPoint).sqrMagnitude;
            }
        }

        // Both a turtle and a resource can be within range of the same click
        // (their magnet radii/bounds overlapping) — rather than always
        // favoring turtle selection, defer to whichever one the click
        // actually landed nearer to.
        if (clickedTurtle != null && clickedResource != null && resourceSqrDistance < turtleSqrDistance)
        {
            clickedTurtle = null;
        }

        if (clickedTurtle != null)
        {
            Select(clickedTurtle);
            return;
        }

        if (selectedTurtle == null) return;

        // Falls back to the collider-based BuildingHealth/ground-move
        // resolution below only if no resource matched. Buildings stay fully
        // clickable at night — a Watchtower in particular is exactly what you
        // want to station a turtle at once a storm hits, so that must never
        // be blocked here.
        BuildingHealth clickedBuilding = hit != null ? hit.GetComponentInParent<BuildingHealth>() : null;

        if (clickedResource != null)
        {
            clickedResource.GetComponent<SquashAndStretch>()?.Play();
            selectedTurtle.MoveToResource(clickedResource);
        }
        else if (clickedBuilding != null && clickedBuilding.IsInteractable)
        {
            selectedTurtle.MoveToBuilding(clickedBuilding.transform);
        }
        else
        {
            selectedTurtle.MoveToPoint(worldPoint);
        }

        DeselectCurrent();
    }

    /// <summary>Resolves whichever turtle a click hits — an exact collider hit first, else the nearest live turtle within Turtle Click Magnet Radius (see FindNearestTurtleWithinRadius) — along with its squared distance to worldPoint, so HandleClick can compare it against a candidate resource and defer to whichever is actually closer to the click.</summary>
    private TurtleAgent ResolveClickedTurtle(Collider2D hit, Vector3 worldPoint, out float sqrDistance)
    {
        TurtleAgent exact = hit != null ? hit.GetComponentInParent<TurtleAgent>() : null;
        if (exact != null)
        {
            sqrDistance = ((Vector2)exact.transform.position - (Vector2)worldPoint).sqrMagnitude;
            return exact;
        }

        return FindNearestTurtleWithinRadius(worldPoint, out sqrDistance);
    }

    /// <summary>Fallback when no turtle collider is exactly under the click: picks whichever live turtle is nearest the click point, if within Turtle Click Magnet Radius — forgives a slightly missed click rather than requiring pixel-perfect precision on a turtle's own (fairly small) collider. Returns null (and sqrDistance = float.MaxValue) if the radius is 0 or nothing qualifies, so the old exact-hit-only behavior is preserved at that setting.</summary>
    private TurtleAgent FindNearestTurtleWithinRadius(Vector3 worldPoint, out float sqrDistance)
    {
        sqrDistance = float.MaxValue;
        if (turtleClickMagnetRadius <= 0f) return null;

        TurtleAgent nearest = null;
        float nearestSqrDistance = turtleClickMagnetRadius * turtleClickMagnetRadius;

        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle == null) continue;

            float candidateSqrDistance = ((Vector2)turtle.transform.position - (Vector2)worldPoint).sqrMagnitude;
            if (candidateSqrDistance <= nearestSqrDistance)
            {
                nearestSqrDistance = candidateSqrDistance;
                nearest = turtle;
            }
        }

        if (nearest != null) sqrDistance = nearestSqrDistance;
        return nearest;
    }

    private void SpawnClickParticle(Vector3 worldPoint)
    {
        if (clickParticlePrefab == null) return;

        ParticleSystem instance = Instantiate(clickParticlePrefab, worldPoint, Quaternion.identity);
        ParticleSystem.MainModule main = instance.main;
        main.startColor = clickParticleColor;
    }

    private void Select(TurtleAgent turtle)
    {
        if (selectedTurtle == turtle) return;

        DeselectCurrent();
        selectedTurtle = turtle;
        selectedTurtle.Select();
    }

    private void DeselectCurrent()
    {
        if (selectedTurtle == null) return;

        selectedTurtle.Deselect();
        selectedTurtle = null;
    }
}
