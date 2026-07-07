using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// Shows which cell the mouse is over while a turtle is selected, so the
/// player can see what clicking there will do before committing to it. Tints
/// a greyscale indicator sprite based on what's in the cell: water, a
/// building/resource (anything occupying the tile), or plain sand. Hidden
/// whenever no turtle is selected or build mode is active (the build ghost
/// already covers that case).
/// </summary>
public class CellIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private TurtleSelectionController turtleSelectionController;
    [Tooltip("Greyscale sprite tinted by the colors below.")]
    [SerializeField] private Sprite indicatorSprite;

    [Header("Colors")]
    [SerializeField] private Color sandColor = Color.white;
    [SerializeField] private Color waterColor = new Color(0.3f, 0.6f, 1f);
    [Tooltip("Shown over anything occupying the tile: buildings, resources, the nest, ...")]
    [SerializeField] private Color occupiedColor = new Color(1f, 0.8f, 0.2f);

    private Camera cam;
    private GameObject indicatorObject;
    private SpriteRenderer indicatorRenderer;
    private int turtleLayer;

    private void Awake()
    {
        cam = Camera.main;
        turtleLayer = LayerMask.NameToLayer("Turtle");
        BuildIndicator();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (indicatorObject != null) Destroy(indicatorObject);
    }

    private void BuildIndicator()
    {
        indicatorObject = new GameObject("CellIndicator");
        indicatorRenderer = indicatorObject.AddComponent<SpriteRenderer>();
        indicatorRenderer.sprite = indicatorSprite;
    }

    private void Update()
    {
        bool shouldShow = !BuildModeController.IsActive
            && turtleSelectionController != null
            && turtleSelectionController.HasSelection;

        SetVisible(shouldShow);
        if (!shouldShow) return;

        UpdateIndicator();
    }

    private void SetVisible(bool visible)
    {
        if (indicatorObject != null) indicatorObject.SetActive(visible);
    }

    private void UpdateIndicator()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null || islandGenerator == null) return;

        Tilemap sand = islandGenerator.SandTilemap;
        if (sand == null) return;

        Vector3 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        world.z = 0f;

        Vector3Int cell = sand.WorldToCell(world);
        Vector3 cellCenter = sand.GetCellCenterWorld(cell);
        indicatorObject.transform.position = cellCenter;

        indicatorRenderer.color = GetColorForCell(cell, cellCenter, sand);
    }

    private Color GetColorForCell(Vector3Int cell, Vector3 cellCenter, Tilemap sand)
    {
        if (!sand.HasTile(cell)) return waterColor;

        int mask = turtleLayer >= 0 ? ~(1 << turtleLayer) : ~0;
        Collider2D hit = Physics2D.OverlapBox(cellCenter, Vector2.one * 0.9f, 0f, mask);
        if (hit == null) return sandColor;

        // A non-interactable building (e.g. a wall) reads as plain sand — only
        // interactable buildings (and resources, which have no such toggle)
        // show the occupied/resource color.
        BuildingHealth building = hit.GetComponentInParent<BuildingHealth>();
        if (building != null && !building.IsInteractable) return sandColor;

        return occupiedColor;
    }
}
