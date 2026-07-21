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
/// resource/building targeting is disabled entirely — every click just issues
/// a ground-move order, even one landing on a resource or interactable building.
/// </summary>
public class TurtleSelectionController : MonoBehaviour
{
    [Header("Click Detection")]
    [Tooltip("Max screen-pixel distance between press and release to count as a click rather than a drag.")]
    [SerializeField] private float clickDistanceThreshold = 6f;

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
        if (BuildModeController.IsActive || UpgradeSelectionUI.IsActive) return;

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

        TurtleAgent clickedTurtle = hit != null ? hit.GetComponentInParent<TurtleAgent>() : null;
        if (clickedTurtle != null)
        {
            Select(clickedTurtle);
            return;
        }

        if (selectedTurtle == null) return;

        // Resources are hit-tested against their actual rendered sprite bounds
        // (see ResourceClickTarget), not the small gameplay collider a click
        // would otherwise need to land on — e.g. a palm tree's canopy is much
        // bigger than its trunk collider. Falls back to the collider-based
        // BuildingHealth/ground-move resolution below only if no resource matched.
        // At night (storming), a turtle's target can't be (re)assigned at all —
        // clicking a resource or interactable building just moves it there like
        // any other ground point, matching TurtleAgent treating harvesting as
        // day-only and cancelling resource tasks the instant a storm starts.
        bool night = DayStormCycle.IsStorming;
        Transform clickedResource = night ? null : ResourceClickTarget.FindClickTargetAt(worldPoint);
        BuildingHealth clickedBuilding = (!night && hit != null) ? hit.GetComponentInParent<BuildingHealth>() : null;

        if (clickedResource != null)
        {
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
