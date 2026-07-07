using System;
using UnityEngine;

/// <summary>
/// One flexible, hand-rolled animation (no Animator/tweening — matches
/// TrashAgent's fade-timer-in-Update idiom) servicing both resource-carry
/// pop effects via the same script, tuned differently per prefab: with no
/// target, it hops in place and fades out (harvest-pop); with a target, it
/// arcs there and stays opaque, invoking a completion callback on arrival
/// (delivery-fly), used to defer the actual ResourceManager.Add until the
/// animation completes.
/// </summary>
public class ResourcePopEffect : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float duration = 0.4f;
    [SerializeField] private float hopHeight = 0.5f;

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private bool hasTarget;
    private float elapsed;
    private Action onComplete;

    /// <summary>Call immediately after Instantiate. Pass to as null for an in-place pop-and-fade, or a world position for an arcing fly-to animation with onComplete invoked on arrival.</summary>
    public void Initialize(Sprite icon, Vector3 from, Vector3? to, Action onComplete = null)
    {
        if (spriteRenderer != null) spriteRenderer.sprite = icon;

        startPosition = from;
        transform.position = from;
        hasTarget = to.HasValue;
        targetPosition = to ?? from;
        this.onComplete = onComplete;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        if (hasTarget)
        {
            Vector3 pos = Vector3.Lerp(startPosition, targetPosition, t);
            pos.y += hopHeight * Mathf.Sin(t * Mathf.PI);
            transform.position = pos;
        }
        else
        {
            Vector3 pos = startPosition;
            pos.y += hopHeight * 0.4f * Mathf.Sin(t * Mathf.PI);
            transform.position = pos;

            if (spriteRenderer != null)
            {
                Color color = spriteRenderer.color;
                color.a = Mathf.Lerp(1f, 0f, t);
                spriteRenderer.color = color;
            }
        }

        if (t >= 1f)
        {
            if (hasTarget) onComplete?.Invoke();
            Destroy(gameObject);
        }
    }
}
