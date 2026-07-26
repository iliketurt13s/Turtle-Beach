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
/// Resources and interactable buildings tint like a selected turtle does
/// (see ResourceClickTarget/BuildingHoverTint, tracked here via the shared
/// IHoverTintable interface) — resources are looked up by their actual
/// rendered sprite bounds (ResourceClickTarget.FindHoverTargetAt), not the
/// small gameplay collider TurtleSelectionController's click routing no
/// longer relies on either. Everything else (trash, or a resource/building
/// that hasn't had its tint component added yet) falls back to the original
/// same-sprite silhouette outline — flattened to one solid color via
/// SpriteSilhouette.shader (keeps the sprite's alpha/shape but discards its
/// RGB, unlike a tint which would still show through the art's own shading)
/// and scaled up slightly behind the target's own renderer so it peeks out
/// on every edge.
///
/// Which sprite gets outlined defaults to the target's first
/// GetComponentInChildren&lt;SpriteRenderer&gt; — fine for most objects. If a
/// specific object needs a different one (see HoverOutlineSpriteSource's own
/// doc comment for when that's necessary), just add that component to it and
/// drag in the right SpriteRenderer; nothing here needs to change, and every
/// object without it keeps working exactly as before.
/// </summary>
public class HoverTargetOutline : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TurtleSelectionController turtleSelectionController;

    [Header("Outline")]
    [SerializeField] private Color outlineColor = Color.white;
    [Tooltip("How much bigger than the target's own sprite the outline silhouette is drawn, e.g. 1.15 = 15% larger on every side.")]
    [SerializeField] private float outlineScale = 1.15f;

    [Header("Tint")]
    [Tooltip("Tint applied to a hovered resource/interactable building instead of the outline. Same gold as TurtleAgent's own selectedTint by default — there's no shared constant between the two, so if you retune one, retune the other to match.")]
    [SerializeField] private Color hoverTintColor = new Color(1f, 0.85f, 0.35f);

    private const string SilhouetteShaderName = "Custom/SpriteSilhouette";

    private Camera cam;
    private GameObject outlineObject;
    private SpriteRenderer outlineRenderer;
    private Material silhouetteMaterial;

    private IHoverTintable hoveredTintTarget;

    private void Awake()
    {
        cam = Camera.main;
        BuildOutline();
        SetVisible(false);
    }

    private void OnDisable()
    {
        SetHoverTintTarget(null);
    }

    private void OnDestroy()
    {
        SetHoverTintTarget(null);
        if (outlineObject != null) Destroy(outlineObject);
        if (silhouetteMaterial != null) Destroy(silhouetteMaterial);
    }

    private void BuildOutline()
    {
        outlineObject = new GameObject("HoverTargetOutline");
        outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
        outlineRenderer.color = outlineColor;

        Shader silhouetteShader = Shader.Find(SilhouetteShaderName);
        if (silhouetteShader != null)
        {
            silhouetteMaterial = new Material(silhouetteShader);
            outlineRenderer.material = silhouetteMaterial;
        }
        else
        {
            // Shader missing (e.g. stripped from a build) — falls back to the
            // default sprite material, which still shows a tinted (not flat
            // solid-color) silhouette rather than nothing at all.
            Debug.LogWarning($"HoverTargetOutline: shader '{SilhouetteShaderName}' not found, falling back to a tinted outline.");
        }
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
            SetVisible(false);
            SetHoverTintTarget(null);
            return;
        }

        UpdateHoverTarget();
    }

    private void SetVisible(bool visible)
    {
        if (outlineObject != null) outlineObject.SetActive(visible);
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
            SetVisible(false);
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
            SetVisible(false);
            return;
        }

        Transform target = ResolveTarget(Physics2D.OverlapPoint(world));

        // Only an interactable building can carry this — see BuildingHoverTint's
        // doc comment for why it doesn't need its own bounds-based lookup.
        BuildingHoverTint buildingTint = target != null ? target.GetComponent<BuildingHoverTint>() : null;
        if (buildingTint != null)
        {
            SetHoverTintTarget(buildingTint);
            SetVisible(false);
            return;
        }

        // Neither tint case matched (trash, or a resource/building whose tint
        // component hasn't been added yet) — fall back to the original outline.
        SetHoverTintTarget(null);

        SpriteRenderer targetRenderer = ResolveSpriteRenderer(target);

        if (targetRenderer == null || targetRenderer.sprite == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        outlineRenderer.sprite = targetRenderer.sprite;
        outlineRenderer.flipX = targetRenderer.flipX;
        outlineRenderer.flipY = targetRenderer.flipY;
        outlineRenderer.sortingLayerID = targetRenderer.sortingLayerID;
        outlineRenderer.sortingOrder = targetRenderer.sortingOrder - 1;

        Transform rendererTransform = targetRenderer.transform;
        outlineObject.transform.SetPositionAndRotation(rendererTransform.position, rendererTransform.rotation);
        outlineObject.transform.localScale = rendererTransform.lossyScale * outlineScale;
    }

    /// <summary>Uses a HoverOutlineSpriteSource's assigned SpriteRenderer if the target has one, otherwise falls back to the default GetComponentInChildren lookup — so most objects need nothing added at all, and only ones whose representative sprite isn't the first one found need the override component.</summary>
    private static SpriteRenderer ResolveSpriteRenderer(Transform target)
    {
        if (target == null) return null;

        HoverOutlineSpriteSource source = target.GetComponent<HoverOutlineSpriteSource>();
        if (source != null && source.SpriteRenderer != null) return source.SpriteRenderer;

        return target.GetComponentInChildren<SpriteRenderer>();
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
