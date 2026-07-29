using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Highlights whichever specific resource, interactable building, food
/// pickup (Coconut/Jellyfish), or trash the mouse is directly over — so when
/// several of these share a tile, the player can tell exactly which one a
/// click will actually send the selected turtle to (CellIndicator only tints
/// the whole cell, it can't distinguish between overlapping objects within
/// it). Hidden under the same conditions as CellIndicator: no turtle
/// selected, build mode active, or the upgrade selection UI is up.
///
/// Everything hoverable tints like a selected turtle does (see
/// ResourceClickTarget/BuildingHoverTint/TrashHoverTint, tracked here via the
/// shared IHoverTintable interface). Resources are looked up by their actual
/// rendered sprite bounds (ResourceClickTarget.FindHoverTargetAt), not the
/// small gameplay collider TurtleSelectionController's click routing no
/// longer relies on either; trash and interactable buildings are looked up
/// via their Collider2D through ResolveTarget.
/// </summary>
public class HoverTargetOutline : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TurtleSelectionController turtleSelectionController;

    [Header("Tint")]
    [Tooltip("Tint applied to a hovered resource/interactable building/trash. Same gold as TurtleAgent's own selectedTint by default — there's no shared constant between the two, so if you retune one, retune the other to match.")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 0.85f, 0.35f);

    private Camera cam;

    private IHoverTintable hoveredTintTarget;

    private void Awake()
    {
        cam = Camera.main;
    }

    private void OnDisable()
    {
        SetHoverTintTarget(null);
    }

    private void OnDestroy()
    {
        SetHoverTintTarget(null);
    }

    private void Update()
    {
        bool shouldShow = !BuildModeController.IsActive
            && !UpgradeSelectionUI.IsActive
            && !GarbagePatchCutsceneController.IsActive
            && turtleSelectionController != null
            && turtleSelectionController.HasSelection;

        if (!shouldShow)
        {
            SetHoverTintTarget(null);
            return;
        }

        UpdateHoverTarget();
    }

    /// <summary>Switches which IHoverTintable is currently tinted, reverting the previous one first — pass null to just revert whatever's currently tinted. No-ops if newTarget is already the current one, so re-hovering the same object every frame doesn't reapply the tint pointlessly.</summary>
    private void SetHoverTintTarget(IHoverTintable newTarget)
    {
        if (hoveredTintTarget == newTarget) return;

        hoveredTintTarget?.RevertTint();
        hoveredTintTarget = newTarget;
        hoveredTintTarget?.ApplyTint(hoverTintColor);
    }

    private void UpdateHoverTarget()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null)
        {
            SetHoverTintTarget(null);
            return;
        }

        Vector3 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        world.z = 0f;

        // Resources first, via their actual rendered sprite bounds rather than
        // the small gameplay collider ResolveTarget below still uses — see
        // ResourceClickTarget's own doc comment.
        ResourceClickTarget hoveredResource = ResourceClickTarget.FindHoverTargetAt(world);
        if (hoveredResource != null)
        {
            SetHoverTintTarget(hoveredResource);
            return;
        }

        Transform target = ResolveTarget(Physics2D.OverlapPoint(world));
        if (target == null)
        {
            SetHoverTintTarget(null);
            return;
        }

        IHoverTintable tintable = target.GetComponent<IHoverTintable>();
        SetHoverTintTarget(tintable);
    }

    /// <summary>Mirrors TurtleSelectionController.HandleClick's own (collider-based) target resolution priority, plus trash slotted in ahead of buildings — see the class doc comment for why trash is included despite not being a manual click target. Resources are matched here too, purely as a safety net for a resource prefab that hasn't had ResourceClickTarget added yet — see UpdateHoverTarget, which always tries the bounds-based resource lookup first.</summary>
    private Transform ResolveTarget(Collider2D hit)
    {
        if (hit == null) return null;

        ResourceNode resource = hit.GetComponentInParent<ResourceNode>();
        if (resource != null) return resource.transform;

        Coconut coconut = hit.GetComponentInParent<Coconut>();
        if (coconut != null) return coconut.transform;

        JellyfishAgent jellyfish = hit.GetComponentInParent<JellyfishAgent>();
        if (jellyfish != null) return jellyfish.transform;

        TrashHealth trash = hit.GetComponentInParent<TrashHealth>();
        if (trash != null) return trash.transform;

        BuildingHealth building = hit.GetComponentInParent<BuildingHealth>();
        if (building != null && building.IsInteractable) return building.transform;

        return null;
    }
}
