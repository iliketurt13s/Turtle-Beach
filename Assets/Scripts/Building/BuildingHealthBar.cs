using UnityEngine;

/// <summary>
/// Generic world-space health bar for any building. Attach as a child of a
/// building prefab with a fill SpriteRenderer whose pivot is set to its left
/// edge, so scaling its local X shrinks it from the right. Hidden at full
/// health so undamaged buildings aren't cluttered with a bar.
/// </summary>
public class BuildingHealthBar : MonoBehaviour
{
    [SerializeField] private SpriteRenderer fillRenderer;
    [SerializeField] private Color fullColor = Color.green;
    [SerializeField] private Color emptyColor = Color.red;

    public void SetHealth(int current, int max)
    {
        float t = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        if (fillRenderer != null)
        {
            Vector3 scale = fillRenderer.transform.localScale;
            scale.x = t;
            fillRenderer.transform.localScale = scale;
            fillRenderer.color = Color.Lerp(emptyColor, fullColor, t);
        }

        gameObject.SetActive(current < max);
    }
}
