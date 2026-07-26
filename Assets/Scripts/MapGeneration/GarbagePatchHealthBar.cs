using UnityEngine;

/// <summary>
/// Discrete "pip" health bar for GarbagePatch — a fixed, pre-authored number
/// of pip sprites regardless of Max Segments, unlike BuildingHealthBar's
/// continuous scaled fill. Purely visual: just toggles pre-authored child
/// sprites on/off, owns no health data itself. Shows current/max as a ratio
/// of the pip count rather than one pip per point, so it stays meaningful
/// even when Max Segments (see GarbagePatch's game-mode presets, e.g. Cove's
/// 10 or Big Island's effectively-infinite cap) exceeds how many pips exist —
/// losing 2 of 10 with 5 pips authored drops exactly 1 pip, not nothing.
/// </summary>
public class GarbagePatchHealthBar : MonoBehaviour
{
    [Tooltip("Pip sprites, in order. A lost segment is hidden entirely rather than recolored/shrunk.")]
    [SerializeField] private SpriteRenderer[] segments;

    public void SetSegments(int current, int max)
    {
        if (segments == null) return;

        int visibleCount = max > 0
            ? Mathf.Clamp(Mathf.RoundToInt((float)current / max * segments.Length), 0, segments.Length)
            : 0;

        // A low ratio against a high Max Segments (e.g. Big Island's
        // near-infinite cap, or Cove's) can round down to 0 visible pips even
        // with real health still remaining — never show fully depleted while
        // the patch is actually still alive.
        if (current > 0) visibleCount = Mathf.Max(visibleCount, 1);

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] != null) segments[i].gameObject.SetActive(i < visibleCount);
        }
    }
}
