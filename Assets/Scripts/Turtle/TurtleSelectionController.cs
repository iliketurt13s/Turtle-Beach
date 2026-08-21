using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Single scene-wide listener translating mouse input into turtle
/// selection/orders as a strict two-step gesture: with nothing selected, a
/// left-click picks a turtle and does nothing else; with a turtle selected,
/// the next left-click is ALWAYS an order to it (harvest if a resource was
/// clicked, station if an interactable building was, otherwise walk there),
/// and issuing it releases the selection so the click after that is free to
/// pick a turtle again. Clicking a second turtle mid-gesture deliberately
/// does not switch the selection — it orders the selected turtle to walk to
/// that spot. Turtles wander constantly, so a selection that could be
/// silently reassigned meant an order click that landed near a passing turtle
/// grabbed that turtle instead of moving the one you meant, with no feedback
/// that anything had changed. Right-click deselects without issuing an order.
/// Runs independently of
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
/// only behavior. The two magnets can no longer contend for the same click
/// (which used to need a nearest-wins tiebreak): turtle magnetism only
/// applies while nothing is selected, and resource magnetism only while
/// something is, so exactly one of them is ever live for a given click.
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

    [Tooltip("Played on every click that lands in the WORLD - selecting a turtle, giving it an order, or clicking bare ground. Clicks that land on the HUD never reach here (see the EventSystem guard in Update), so this and the UI button sound can never both fire for one click. Sits beside Click Particle Prefab on purpose: same event, same place to tune it.")]
    [SerializeField] private SoundEffect clickSound = new SoundEffect();

    /// <summary>True while a turtle is currently selected, e.g. for CellIndicator to know whether to show.</summary>
    public bool HasSelection => selectedTurtle != null;

    private Vector2 pressPosition;
    /// <summary>Whether the current left-button press began over UI, so the release can be ignored even if the pointer has since moved off it.</summary>
    private bool pressStartedOverUI;
    private TurtleAgent selectedTurtle;
    private Camera cam;

    /// <summary>
    /// True if the mouse is over any raycast-taking UI graphic — clicking the
    /// build-menu button, or anything else on the HUD, must not also order the
    /// selected turtle to walk to whatever is behind it. This controller stands
    /// down entirely while build mode is up, so the drawer itself is already
    /// covered; what this adds is the click that OPENS it. Deliberately a local
    /// copy of BuildModeController's identical helper, matching how the two
    /// already keep separate copies of the click-vs-drag threshold — keep them
    /// in sync.
    ///
    /// CAUTION for anyone adding a full-screen UI graphic: the EventSystem
    /// answers this for ANY graphic flagged Raycast Target, decorative or not,
    /// so an overlay left on that flag silently swallows every gameplay click
    /// for as long as it's up, with no error and nothing on screen to suggest
    /// input is the problem. That is exactly what a storm-time darkening did
    /// once — see UpgradeSelectionUI.SetBackdropBlocking. A graphic that is
    /// only there to be LOOKED at wants Raycast Target off.
    /// </summary>
    private static bool IsPointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void Awake()
    {
        cam = Camera.main;
    }

    private void Update()
    {
        if (BuildModeController.IsActive || UpgradeSelectionUI.IsActive || GarbagePatchCutsceneController.IsActive || GameOverUI.IsPaused || SettingsMenuUI.IsOpen) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = mouse.position.ReadValue();
            pressStartedOverUI = IsPointerOverUI();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            Vector2 releasePosition = mouse.position.ReadValue();
            bool overUI = pressStartedOverUI || IsPointerOverUI();
            pressStartedOverUI = false;

            bool wasCameraDrag = CameraController.Instance != null && CameraController.Instance.WasDragging;
            if (!overUI && !wasCameraDrag && Vector2.Distance(pressPosition, releasePosition) <= clickDistanceThreshold)
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
        clickSound.Play(worldPoint);

        Collider2D hit = Physics2D.OverlapPoint(worldPoint);

        // With nothing selected, a click can only ever pick a turtle — there's
        // no one to give an order to yet.
        if (selectedTurtle == null)
        {
            TurtleAgent clickedTurtle = ResolveClickedTurtle(hit, worldPoint);
            if (clickedTurtle != null) Select(clickedTurtle);
            return;
        }

        // With a turtle selected, EVERY click is an order to that turtle —
        // clicking another turtle deliberately does not switch the selection,
        // it just orders the selected one to walk over there. Selection is a
        // two-step "pick, then command" gesture, and a click that silently
        // reassigned it mid-gesture meant a slightly-off order click near a
        // wandering turtle grabbed that turtle instead of moving the one you
        // meant. The order below releases the selection, so the next click is
        // free to pick a new turtle again; right-click bails out without
        // ordering anything.
        //
        // Resources are hit-tested against their actual rendered sprite bounds
        // (see ResourceClickTarget), not the small gameplay collider a click
        // would otherwise need to land on — e.g. a palm tree's canopy is much
        // bigger than its trunk collider. At night (storming), only resource
        // *harvest* targeting is disabled — clicking a resource just moves the
        // turtle there like any other ground point, matching TurtleAgent
        // treating harvesting as a day-only activity and cancelling resource
        // tasks the instant a storm starts.
        bool night = DayStormCycle.IsStorming;
        Transform clickedResource = night
            ? null
            : ResourceClickTarget.FindClickTargetAt(worldPoint, resourceClickMagnetRadius);

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

    /// <summary>Resolves whichever turtle a click hits — an exact collider hit first, else the nearest live turtle within Turtle Click Magnet Radius (see FindNearestTurtleWithinRadius). Only ever consulted while nothing is selected, since a click with a turtle already selected is always an order rather than a selection.</summary>
    private TurtleAgent ResolveClickedTurtle(Collider2D hit, Vector3 worldPoint)
    {
        TurtleAgent exact = hit != null ? hit.GetComponentInParent<TurtleAgent>() : null;
        if (exact != null) return exact;

        return FindNearestTurtleWithinRadius(worldPoint);
    }

    /// <summary>Fallback when no turtle collider is exactly under the click: picks whichever live turtle is nearest the click point, if within Turtle Click Magnet Radius — forgives a slightly missed click rather than requiring pixel-perfect precision on a turtle's own (fairly small) collider. Returns null if the radius is 0 or nothing qualifies, so the old exact-hit-only behavior is preserved at that setting.</summary>
    private TurtleAgent FindNearestTurtleWithinRadius(Vector3 worldPoint)
    {
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
