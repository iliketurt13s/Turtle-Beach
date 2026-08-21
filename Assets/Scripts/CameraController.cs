using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    /// <summary>Scene-wide singleton (mirrors BuildModeController/PathfindingManager/etc.) so TurtleSelectionController/BuildModeController can check WasDragging without a dedicated Inspector-wired reference.</summary>
    public static CameraController Instance { get; private set; }

    [Header("Zoom")]
    [Tooltip("Scroll-wheel zoom speed the game is tuned at. The player's own Camera Zoom Speed setting multiplies this (see GameSettings), so retuning here moves everyone's camera whatever they have that slider set to — which is the point of the setting being a multiplier rather than an absolute speed.")]
    [SerializeField] private float zoomSpeed = 5f;
    [Tooltip("Closest the camera can zoom in (smallest orthographic size).")]
    [SerializeField] private float minZoom = 2f;
    [Tooltip("Farthest the camera can zoom out (largest orthographic size) — hard-capped further still by ClampToMapBounds so it can never exceed the painted map's own extent (core map + IslandGenerator's deep water outskirts) regardless of this value.")]
    [SerializeField] private float maxZoom = 50f;

    [Header("Drag")]
    [Tooltip("Drag-to-pan speed the game is tuned at, at the camera's starting zoom (Drag Zoom Scaling below decides whether HandleDrag scales it by the current zoom on top). The player's own Camera Drag Speed setting multiplies this — see Zoom Speed above.")]
    [SerializeField] private float dragSpeed = 1f;
    [Tooltip("Cumulative screen-pixel distance the mouse must travel while the left button is held before a release counts as a camera drag rather than a click — see WasDragging. Distinct from TurtleSelectionController/BuildModeController's own click-distance threshold, which only measures net press-to-release displacement and would otherwise misread a drag that happens to end back near its start (e.g. a small correction while panning/zooming) as a click-to-order.")]
    [SerializeField] private float dragDistanceThreshold = 6f;
    [Tooltip("How far the drag scales with zoom. 0 = screen-space: a pixel of mouse movement pans the camera the same world distance however far out you are, so the pan can never run away from you when zoomed out. 1 = world-space (the old behaviour): a pixel covers proportionally more world the further out you are, which keeps the grabbed point under the cursor but is hard to control from far out. In between blends the two. Either end feels identical at the camera's starting zoom, so Drag Speed stays tuned whatever this is set to.")]
    [Range(0f, 1f)]
    [SerializeField] private float dragZoomScaling = 0f;

    [Header("Map Bounds")]
    [Tooltip("Generator whose map size caps how far the camera can zoom out or pan, so it can never see past the grid's edge into empty space.")]
    [SerializeField] private IslandGenerator islandGenerator;

    [Header("WebGL Input Scale")]
    [Tooltip("The browser's native mouse-wheel/pointer events report deltas in a very different magnitude than the Editor's own input backend does (scroll deltaY especially can be off by 10-100x depending on browser) — Zoom Speed/Drag Speed above are tuned against the Editor, so these compensate only at runtime in an actual WebGL player (checked via Application.platform, never touching Editor/standalone behavior). These fields stay unconditionally serialized on every platform — an #if UNITY_EDITOR-style exclusion here would make the Editor and a WebGL Player build disagree about this type's field layout, which Unity warns about (and can break serialization) on domain reload. Start here and adjust after testing a real build; there's no single universally-correct value across every browser/display.")]
    [SerializeField] private float webGLScrollScale = 0.1f;
    [SerializeField] private float webGLDragScale = 0.3f;

    /// <summary>True if the mouse has moved at least DragDistanceThreshold (cumulative since the left button was last pressed, not just net displacement) — checked by TurtleSelectionController/BuildModeController so a pan/zoom gesture is never misread as a click-to-order or a building placement just because it happened to release near its own start point.</summary>
    public bool WasDragging { get; private set; }

    private Camera cam;
    private Vector2 lastMousePosition;
    private bool isDragging;
    private float cumulativeDragDistance;
    private Coroutine panCoroutine;
    private Coroutine zoomCoroutine;
    private Transform followTarget;

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

        if (followTarget != null)
        {
            Vector3 position = transform.position;
            position.x = followTarget.position.x;
            position.y = followTarget.position.y;
            transform.position = position;
        }

        ClampToMapBounds();
    }

    private void HandleZoom()
    {
        if (BuildModeController.IsActive || UpgradeSelectionUI.IsActive || GarbagePatchCutsceneController.IsActive || SettingsMenuUI.IsOpen) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Application.platform == RuntimePlatform.WebGLPlayer) scroll *= webGLScrollScale;
        if (Mathf.Approximately(scroll, 0f)) return;

        // The player's setting is read live rather than cached in Awake, so a
        // slider moved on the in-game settings screen is felt the moment that
        // screen closes rather than on the next scene load.
        cam.orthographicSize = Mathf.Clamp(
            cam.orthographicSize - scroll * zoomSpeed * GameSettings.CameraZoomSpeed * Time.unscaledDeltaTime,
            minZoom,
            maxZoom);
    }

    private void HandleDrag()
    {
        if (UpgradeSelectionUI.IsActive || GarbagePatchCutsceneController.IsActive || SettingsMenuUI.IsOpen) return;

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

        if (Application.platform == RuntimePlatform.WebGLPlayer) delta *= webGLDragScale;

        // Cumulative, not net press-to-release displacement — so a pan that
        // wanders and happens to end back near its start still counts as a
        // drag (see WasDragging) rather than snapping back to reading as a
        // plain click the instant the button comes up.
        cumulativeDragDistance += delta.magnitude;
        if (cumulativeDragDistance >= dragDistanceThreshold) WasDragging = true;

        // Drag Zoom Scaling blends between judging the drag by screen distance
        // (the delta is already in mouse pixels, so leaving it at that means a
        // pixel is worth the same world distance at every zoom) and by world
        // distance (the same pixel covers proportionally more world the
        // further out you are, which keeps the grabbed point under the cursor
        // but makes a zoomed-out pan accelerate away from the player).
        // referenceOrthographicSize is the zoom Drag Speed was tuned at, so
        // both ends of the blend agree there and only diverge away from it.
        float zoomRatio = referenceOrthographicSize > 0f ? cam.orthographicSize / referenceOrthographicSize : 1f;
        float zoomScale = Mathf.Lerp(1f, zoomRatio, dragZoomScaling);
        Vector3 move = new Vector3(-delta.x, -delta.y, 0f) * (dragSpeed * GameSettings.CameraDragSpeed * zoomScale * Time.unscaledDeltaTime);
        transform.position += move;
    }

    /// <summary>Scripted move to a world position over duration, used by GarbagePatchCutsceneController to pan out to the garbage patch and back. Not gated on this class's own IsActive-style flags — the caller (GarbagePatchCutsceneController) is responsible for suspending player input for the duration via its own IsActive flag, checked by HandleZoom/HandleDrag above.</summary>
    public void PanTo(Vector3 targetWorldPosition, float duration, Action onComplete)
    {
        if (panCoroutine != null) StopCoroutine(panCoroutine);
        panCoroutine = StartCoroutine(PanRoutine(targetWorldPosition, duration, onComplete));
    }

    private IEnumerator PanRoutine(Vector3 target, float duration, Action onComplete)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        float safeDuration = Mathf.Max(duration, 0.01f);

        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration));
            transform.position = Vector3.Lerp(start, target, t);
            yield return null;
        }

        transform.position = target;
        panCoroutine = null;
        onComplete?.Invoke();
    }

    /// <summary>This camera's current orthographic size, so a scripted sequence can capture what to zoom back to rather than assuming a starting value (see GarbagePatchCutsceneController, which restores the player's own zoom on the way home).</summary>
    public float OrthographicSize => cam != null ? cam.orthographicSize : 0f;

    /// <summary>
    /// The orthographic size at which a circle of worldRadius, centered on
    /// screen, just fits — in BOTH axes. Vertically that is the radius itself;
    /// horizontally the view is aspect times wider, so a portrait or
    /// narrow-window aspect (below 1) is the case that needs the extra room,
    /// and taking the max of the two is what stops the subject being cropped
    /// left and right on those. Lives here because the aspect belongs to this
    /// camera, not to whoever is asking to frame something.
    /// </summary>
    public float FramingSizeForRadius(float worldRadius)
    {
        if (cam == null || cam.aspect <= 0f) return worldRadius;

        return Mathf.Max(worldRadius, worldRadius / cam.aspect);
    }

    /// <summary>
    /// Scripted zoom to an orthographic size, with the same easing shape and
    /// unscaled clock as PanTo — and deliberately its own coroutine rather than
    /// a parameter on the pans, so a sequence can run one alongside the other
    /// (pan out and zoom in over the same duration) without the two having to
    /// be the same call. Clamped to this camera's own Min/Max Zoom, and
    /// ClampToMapBounds still applies on top every frame, so a scripted zoom
    /// can no more see off the edge of the map than a player's can.
    ///
    /// Like PanTo, this does NOT suspend player input on its own — the caller
    /// owns that (GarbagePatchCutsceneController via its IsActive flag, which
    /// HandleZoom already stands down for).
    /// </summary>
    public void ZoomTo(float targetOrthographicSize, float duration, Action onComplete = null)
    {
        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
        zoomCoroutine = StartCoroutine(ZoomRoutine(targetOrthographicSize, duration, onComplete));
    }

    private IEnumerator ZoomRoutine(float targetOrthographicSize, float duration, Action onComplete)
    {
        float start = cam.orthographicSize;
        float target = Mathf.Clamp(targetOrthographicSize, minZoom, maxZoom);
        float elapsed = 0f;
        float safeDuration = Mathf.Max(duration, 0.01f);

        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration));
            cam.orthographicSize = Mathf.Lerp(start, target, t);
            yield return null;
        }

        cam.orthographicSize = target;
        zoomCoroutine = null;
        onComplete?.Invoke();
    }

    /// <summary>Same easing/duration shape as PanTo, but eases toward target's LIVE position every frame instead of a fixed snapshot — for panning toward something that keeps moving (e.g. the orbiting GarbagePatch), so the tween converges on wherever the target actually ends up rather than a stale point captured back when the pan started. Doesn't keep tracking once duration elapses — call BeginFollowing right after if the camera should stay locked on afterward.</summary>
    public void PanToFollowing(Transform target, float duration, Action onComplete)
    {
        if (panCoroutine != null) StopCoroutine(panCoroutine);
        panCoroutine = StartCoroutine(PanFollowingRoutine(target, duration, onComplete));
    }

    private IEnumerator PanFollowingRoutine(Transform target, float duration, Action onComplete)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        float safeDuration = Mathf.Max(duration, 0.01f);

        while (elapsed < safeDuration && target != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration));
            Vector3 liveTarget = new Vector3(target.position.x, target.position.y, start.z);
            transform.position = Vector3.Lerp(start, liveTarget, t);
            yield return null;
        }

        panCoroutine = null;
        onComplete?.Invoke();
    }

    /// <summary>Continuously snaps this camera's X/Y onto target's position every frame (preserving this camera's own Z) until StopFollowing is called — used to keep the orbiting GarbagePatch centered for the whole cutscene hold period, since a one-shot pan would otherwise leave it drifting off-screen by the time the hold ends.</summary>
    public void BeginFollowing(Transform target) => followTarget = target;

    /// <summary>Stops BeginFollowing's per-frame tracking.</summary>
    public void StopFollowing() => followTarget = null;

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
