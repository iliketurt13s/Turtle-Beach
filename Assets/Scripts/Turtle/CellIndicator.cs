using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// Shows which cell the mouse is over while a turtle is selected, so the
/// player can see what clicking there will do before committing to it. Always
/// plain white — hidden whenever no turtle is selected or build mode is
/// active (the build ghost already covers that case).
/// </summary>
public class CellIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private TurtleSelectionController turtleSelectionController;
    [SerializeField] private Sprite indicatorSprite;

    private Camera cam;
    private GameObject indicatorObject;
    private SpriteRenderer indicatorRenderer;

    private void Awake()
    {
        cam = Camera.main;
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
        indicatorRenderer.color = Color.white;
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
        indicatorObject.transform.position = sand.GetCellCenterWorld(cell);
    }
}
