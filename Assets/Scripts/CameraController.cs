using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoom = 2f;
    [SerializeField] private float maxZoom = 20f;

    [Header("Drag")]
    [SerializeField] private float dragSpeed = 1f;

    [Header("Map Bounds")]
    [Tooltip("Generator whose map size caps how far the camera can zoom out or pan, so it can never see past the grid's edge into empty space.")]
    [SerializeField] private IslandGenerator islandGenerator;

    private Camera cam;
    private Vector2 lastMousePosition;
    private bool isDragging;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Update()
    {
        HandleZoom();
        HandleDrag();
        ClampToMapBounds();
    }

    private void HandleZoom()
    {
        // While forced into an uninterruptible placement (see
        // BuildModeController.EnsureFoodBuildingPlaced), Shift can't cancel
        // build mode to get zoom back — so zoom stays available specifically
        // during that phase, even though IsActive is also true.
        if ((BuildModeController.IsActive && !BuildModeController.IsForced) || UpgradeSelectionUI.IsActive) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        cam.orthographicSize = Mathf.Clamp(
            cam.orthographicSize - scroll * zoomSpeed * Time.unscaledDeltaTime,
            minZoom,
            maxZoom);
    }

    private void HandleDrag()
    {
        if (UpgradeSelectionUI.IsActive) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            isDragging = true;
            lastMousePosition = mouse.position.ReadValue();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
        }

        if (!isDragging) return;

        Vector2 currentMousePosition = mouse.position.ReadValue();
        Vector2 delta = currentMousePosition - lastMousePosition;
        lastMousePosition = currentMousePosition;

        Vector3 move = new Vector3(-delta.x, -delta.y, 0f) * (dragSpeed * Time.unscaledDeltaTime);
        transform.position += move;
    }

    /// <summary>
    /// Hard constraint (on top of Handle Zoom's own min/max Zoom preference):
    /// caps orthographic size so the camera's full view can never be wider or
    /// taller than the map, then clamps position so that view stays entirely
    /// within the map's bounds — between them, the camera can never see
    /// anything off the grid, however it's zoomed or dragged.
    /// </summary>
    private void ClampToMapBounds()
    {
        if (islandGenerator == null) return;

        float halfWidth = islandGenerator.HalfWidth;
        float halfHeight = islandGenerator.HalfHeight;

        float maxOrthoSizeForBounds = Mathf.Min(halfHeight, halfWidth / cam.aspect);
        cam.orthographicSize = Mathf.Min(cam.orthographicSize, maxOrthoSizeForBounds);

        float visibleHalfHeight = cam.orthographicSize;
        float visibleHalfWidth = cam.orthographicSize * cam.aspect;

        Vector3 position = transform.position;
        position.x = Mathf.Clamp(position.x, -halfWidth + visibleHalfWidth, halfWidth - visibleHalfWidth);
        position.y = Mathf.Clamp(position.y, -halfHeight + visibleHalfHeight, halfHeight - visibleHalfHeight);
        transform.position = position;
    }
}
