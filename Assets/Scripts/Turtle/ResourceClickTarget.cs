using UnityEngine;

/// <summary>
/// Attach to a resource prefab (PalmTree, Rock, Coconut, Jellyfish) to let it
/// be clicked/hovered based on its actual rendered sprite bounds instead of
/// its gameplay Collider2D — which stays deliberately small (it drives real
/// physics: turtle bounce-and-harvest via TurtleHeadHitbox, pathfinding
/// obstacle avoidance) and is never resized just to make clicking easier. A
/// palm tree's canopy sprite, for example, is far bigger than the trunk
/// collider it shares a GameObject with.
///
/// Also owns the hover tint (mirrors TurtleAgent.ApplyTint/RevertTint
/// exactly), since it already caches the right set of SpriteRenderers.
/// </summary>
public class ResourceClickTarget : MonoBehaviour, IHoverTintable
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

    /// <summary>World-space bounds encapsulating every currently-visible (enabled renderer, active GameObject, non-null sprite) child sprite — e.g. a palm tree's canopy AND trunk together, not just the trunk collider. Recomputed live rather than cached, since depleting/respawning a ResourceNode toggles its variant-art child's active state at runtime (see ResourceNode.Deplete/Reactivate).</summary>
    public bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasAny = false;

        foreach (SpriteRenderer sr in spriteRenderers)
        {
            if (sr == null || !sr.enabled || sr.sprite == null || !sr.gameObject.activeInHierarchy) continue;

            if (!hasAny)
            {
                bounds = sr.bounds;
                hasAny = true;
            }
            else
            {
                bounds.Encapsulate(sr.bounds);
            }
        }

        return hasAny;
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

    /// <summary>Click-routing lookup: checks every current resource's bounds (via this component if present, else its Collider2D.bounds as a safety net so a prefab someone forgot to add this component to is never LESS clickable than it was before) for whichever contains worldPoint, nearest-by-position among multiple matches. Returns the resource's own root Transform — what TurtleAgent.MoveToResource/TryGetHarvestType expects — or null if nothing matched.</summary>
    public static Transform FindClickTargetAt(Vector3 worldPoint)
    {
        Transform nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null) continue;
            TryConsiderClickCandidate(node.transform, worldPoint, ref nearest, ref nearestSqrDistance);
        }

        foreach (Coconut coconut in Coconut.AllCoconuts)
        {
            if (coconut == null) continue;
            TryConsiderClickCandidate(coconut.transform, worldPoint, ref nearest, ref nearestSqrDistance);
        }

        foreach (JellyfishAgent jellyfish in JellyfishAgent.AllJellyfish)
        {
            if (jellyfish == null) continue;
            TryConsiderClickCandidate(jellyfish.transform, worldPoint, ref nearest, ref nearestSqrDistance);
        }

        return nearest;
    }

    /// <summary>Hover lookup: only resources that actually have this component, so ApplyTint/RevertTint is always possible on whatever's returned — a resource missing this component just falls back to the old silhouette outline instead (see HoverTargetOutline), not a Collider2D-bounds guess here.</summary>
    public static ResourceClickTarget FindHoverTargetAt(Vector3 worldPoint)
    {
        ResourceClickTarget nearest = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null) continue;
            TryConsiderHoverCandidate(node.GetComponent<ResourceClickTarget>(), worldPoint, ref nearest, ref nearestSqrDistance);
        }

        foreach (Coconut coconut in Coconut.AllCoconuts)
        {
            if (coconut == null) continue;
            TryConsiderHoverCandidate(coconut.GetComponent<ResourceClickTarget>(), worldPoint, ref nearest, ref nearestSqrDistance);
        }

        foreach (JellyfishAgent jellyfish in JellyfishAgent.AllJellyfish)
        {
            if (jellyfish == null) continue;
            TryConsiderHoverCandidate(jellyfish.GetComponent<ResourceClickTarget>(), worldPoint, ref nearest, ref nearestSqrDistance);
        }

        return nearest;
    }

    private static void TryConsiderClickCandidate(Transform candidate, Vector3 worldPoint, ref Transform nearest, ref float nearestSqrDistance)
    {
        if (!TryGetCandidateBounds(candidate, out Bounds bounds) || !Contains(bounds, worldPoint)) return;

        float sqrDistance = ((Vector2)candidate.position - (Vector2)worldPoint).sqrMagnitude;
        if (sqrDistance < nearestSqrDistance)
        {
            nearestSqrDistance = sqrDistance;
            nearest = candidate;
        }
    }

    private static void TryConsiderHoverCandidate(ResourceClickTarget candidate, Vector3 worldPoint, ref ResourceClickTarget nearest, ref float nearestSqrDistance)
    {
        if (candidate == null || !candidate.TryGetBounds(out Bounds bounds) || !Contains(bounds, worldPoint)) return;

        float sqrDistance = ((Vector2)candidate.transform.position - (Vector2)worldPoint).sqrMagnitude;
        if (sqrDistance < nearestSqrDistance)
        {
            nearestSqrDistance = sqrDistance;
            nearest = candidate;
        }
    }

    private static bool TryGetCandidateBounds(Transform candidate, out Bounds bounds)
    {
        ResourceClickTarget target = candidate.GetComponent<ResourceClickTarget>();
        if (target != null && target.TryGetBounds(out bounds)) return true;

        Collider2D collider = candidate.GetComponent<Collider2D>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>Bounds.Contains requires an exact z match — flat sprite bounds have size.z == 0, so a caller's point.z (always 0 here, see TurtleSelectionController/HoverTargetOutline) would never match a resource sitting at a different z otherwise. Snaps the query point to the bounds' own z before testing.</summary>
    private static bool Contains(Bounds bounds, Vector3 point)
    {
        point.z = bounds.center.z;
        return bounds.Contains(point);
    }
}
