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
/// deliberate pan-drag from also issuing a stray order.
/// </summary>
public class TurtleSelectionController : MonoBehaviour
{
    [Header("Click Detection")]
    [Tooltip("Max screen-pixel distance between press and release to count as a click rather than a drag.")]
    [SerializeField] private float clickDistanceThreshold = 6f;

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
            if (Vector2.Distance(pressPosition, releasePosition) <= clickDistanceThreshold)
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

        Collider2D hit = Physics2D.OverlapPoint(worldPoint);

        TurtleAgent clickedTurtle = hit != null ? hit.GetComponentInParent<TurtleAgent>() : null;
        if (clickedTurtle != null)
        {
            Select(clickedTurtle);
            return;
        }

        if (selectedTurtle == null) return;

        ResourceNode clickedResource = hit != null ? hit.GetComponentInParent<ResourceNode>() : null;
        Coconut clickedCoconut = hit != null ? hit.GetComponentInParent<Coconut>() : null;
        JellyfishAgent clickedJellyfish = hit != null ? hit.GetComponentInParent<JellyfishAgent>() : null;
        BuildingHealth clickedBuilding = hit != null ? hit.GetComponentInParent<BuildingHealth>() : null;

        if (clickedResource != null)
        {
            selectedTurtle.MoveToResource(clickedResource.transform);
        }
        else if (clickedCoconut != null)
        {
            // Coconut has no BuildingHealth, so the bouncy resource-approach
            // behavior is correct here, not MoveToBuilding's layer-swap logic.
            selectedTurtle.MoveToResource(clickedCoconut.transform);
        }
        else if (clickedJellyfish != null)
        {
            // Same reasoning as Coconut above — no BuildingHealth, so this is
            // a resource-approach target, not a MoveToBuilding one.
            selectedTurtle.MoveToResource(clickedJellyfish.transform);
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
