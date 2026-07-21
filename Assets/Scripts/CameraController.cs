using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    /// <summary>Scene-wide singleton (mirrors BuildModeController/PathfindingManager/etc.) so TurtleSelectionController/BuildModeController can check WasDragging without a dedicated Inspector-wired reference.</summary>
    public static CameraController Instance { get; private set; }

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 5f;
    [Tooltip("Closest the camera can zoom in (smallest orthographic size).")]
    [SerializeField] private float minZoom = 2f;
    [Tooltip("Farthest the camera can zoom out (largest orthographic size) — hard-capped further still by ClampToMapBounds so it can never exceed the painted map's own extent (core map + IslandGenerator's deep water outskirts) regardless of this value.")]
    [SerializeField] private float maxZoom = 50f;

    [Header("Drag")]
    [SerializeField] private float dragSpeed = 1f;
    [Tooltip("Cumulative screen-pixel distance the mouse must travel while the left button is held before a release counts as a camera drag rather than a click — see WasDragging. Distinct from TurtleSelectionController/BuildModeController's own click-distance threshold, which only measures net press-to-release displacement and would otherwise misread a drag that happens to end back near its start (e.g. a small correction while panning/zooming) as a click-to-order.")]
    [SerializeField] private float dragDistanceThreshold = 6f;

    [Header("Map Bounds")]
    [Tooltip("Generator whose map size caps how far the camera can zoom out or pan, so it can never see past the grid's edge into empty space.")]
    [SerializeField] private IslandGenerator islandGenerator;

    /// <summary>True if the mouse has moved at least DragDistanceThreshold (cumulative since the left button was last pressed, not just net displacement) — checked by TurtleSelectionController/BuildModeController so a pan/zoom gesture is never misread as a click-to-order or a building placement just because it happened to release near its own start point.</summary>
    public bool WasDragging { get; private set; }

    private Camera cam;
    private Vector2 lastMousePosition;
    private bool isDragging;
    private float cumulativeDragDistance;

    /// <summary>The orthographic size Drag Speed was tuned at, captured once so drag scaling (see HandleDrag) is relative rather than needing every scene to happen to start at some fixed absolute zoom.</summary>
    private float referenceOrthographicSize;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("CameraController: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        cam = GetComponent<Camera>();
        referenceOrthographicSize = cam.orthographicSize;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        HandleZoom();
        HandleDrag();
        ClampToMapBounds();
    }

    private void HandleZoom()
    {
        if (BuildModeController.IsActive || UpgradeSelectionUI.IsActive) return;

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
            cumulativeDragDistance = 0f;
            WasDragging = false;
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
        }

        if (!isDragging) return;

        Vector2 currentMousePosition = mouse.position.ReadValue();
        Vector2 delta = currentMousePosition - lastMousePosition;
        lastMousePosition = currentMousePosition;

        // Cumulative, not net press-to-release displacement — so a pan that
        // wanders and happens to end back near its start still counts as a
        // drag (see WasDragging) rather than snapping back to reading as a
        // plain click the instant the button comes up.
        cumulativeDragDistance += delta.magnitude;
        if (cumulativeDragDistance >= dragDistanceThreshold) WasDragging = true;

        // Scaled by how far zoomed out the camera currently is relative to
        // where Drag Speed was tuned (referenceOrthographicSize) — the same
        // mouse-pixel delta covers proportionally more world space the
        // further zoomed out you are, so without this a drag would feel
        // sluggish zoomed out and twitchy zoomed in instead of tracking the
        // cursor consistently at any zoom level.
        float zoomScale = referenceOrthographicSize > 0f ? cam.orthographicSize / referenceOrthographicSize : 1f;
        Vector3 move = new Vector3(-delta.x, -delta.y, 0f) * (dragSpeed * zoomScale * Time.unscaledDeltaTime);
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
