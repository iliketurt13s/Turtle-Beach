using UnityEngine;

/// <summary>
/// Attach to a trash prefab (alongside TrashHealth) to have it tint on hover
/// like ResourceClickTarget/BuildingHoverTint/TurtleAgent's own selected-tint,
/// instead of the old silhouette outline. Tint-only — trash click routing
/// already works fine off the existing Collider2D via HoverTargetOutline's
/// ResolveTarget, so like BuildingHoverTint this has no bounds/hit-testing
/// responsibility at all.
/// </summary>
public class TrashHoverTint : MonoBehaviour, IHoverTintable
{
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;

    private void Awake()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            originalColors[i] = spriteRenderers[i].color;
        }
    }

    public void ApplyTint(Color tint)
    {
        foreach (SpriteRenderer sr in spriteRenderers)
        {
            if (sr != null) sr.color = tint;
        }
    }

    public void RevertTint()
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null) spriteRenderers[i].color = originalColors[i];
        }
    }
}
