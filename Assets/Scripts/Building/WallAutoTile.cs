using UnityEngine;

/// <summary>
/// Attach to a building prefab that should visually connect to same-type
/// neighbors (e.g. walls forming a fence). Swaps its own sprite based on a
/// 4-bit bitmask of which cardinal neighbors also have a wall, and refreshes
/// those neighbors too whenever it's placed or removed.
/// </summary>
public class WallAutoTile : MonoBehaviour
{
    private const int North = 1;
    private const int East = 2;
    private const int South = 4;
    private const int West = 8;

    [Tooltip("One sprite per 4-bit neighbor bitmask: North=1, East=2, South=4, West=8 (summed). " +
        "Index 0 = isolated post, index 15 = connected on all 4 sides. Leave a slot empty to fall back to index 0.")]
    [SerializeField] private Sprite[] wallSprites = new Sprite[16];

    [Tooltip("SpriteRenderer to update. Defaults to one on this same GameObject if left unassigned — set this to a child visuals SpriteRenderer instead if the graphics live on a separate child from this script/the collider (e.g. so SquashAndStretch's Target Override can punch the visuals without resizing the collider).")]
    [SerializeField] private SpriteRenderer spriteRendererOverride;

    private SpriteRenderer spriteRenderer;
    private Vector3Int cell;

    private void Awake()
    {
        spriteRenderer = spriteRendererOverride != null ? spriteRendererOverride : GetComponent<SpriteRenderer>();
    }

    /// <summary>Called by whatever placed this wall (see BuildModeController.TryPlace) with its grid cell.</summary>
    public void Initialize(Vector3Int placedCell)
    {
        cell = placedCell;
        WallGrid.Register(cell, this);
        RefreshSelfAndNeighbors();
    }

    private void OnDestroy()
    {
        WallGrid.Unregister(cell, this);
        RefreshNeighbors();
    }

    private void RefreshSelfAndNeighbors()
    {
        UpdateSprite();
        RefreshNeighbors();
    }

    private void RefreshNeighbors()
    {
        RefreshNeighbor(cell + Vector3Int.up);
        RefreshNeighbor(cell + Vector3Int.right);
        RefreshNeighbor(cell + Vector3Int.down);
        RefreshNeighbor(cell + Vector3Int.left);
    }

    private void RefreshNeighbor(Vector3Int neighborCell)
    {
        if (WallGrid.TryGetWall(neighborCell, out WallAutoTile neighbor)) neighbor.UpdateSprite();
    }

    private void UpdateSprite()
    {
        int mask = 0;
        if (WallGrid.HasWallAt(cell + Vector3Int.up)) mask |= North;
        if (WallGrid.HasWallAt(cell + Vector3Int.right)) mask |= East;
        if (WallGrid.HasWallAt(cell + Vector3Int.down)) mask |= South;
        if (WallGrid.HasWallAt(cell + Vector3Int.left)) mask |= West;

        Sprite sprite = mask < wallSprites.Length ? wallSprites[mask] : null;
        spriteRenderer.sprite = sprite != null ? sprite : wallSprites[0];
    }
}
