using UnityEngine;

/// <summary>
/// Drop onto any UI RectTransform (e.g. the menu's title/logo) for a slow,
/// continuous vertical bob — a sine wave added on top of whatever anchored
/// position it starts at, so it works regardless of layout/anchoring and
/// needs no Animator/timeline setup. Purely decorative menu juice; uses
/// unscaled time like this project's other UI juice (SquashAndStretch,
/// SceneTransitionPanel) so it keeps animating even if something sets
/// Time.timeScale to 0.
/// </summary>
public class UIBobAnimator : MonoBehaviour
{
    [Tooltip("How far up/down (in this RectTransform's own units, typically pixels) the bob travels from its starting position.")]
    [SerializeField] private float amplitude = 12f;
    [Tooltip("Seconds for one full up-and-down cycle.")]
    [SerializeField] private float period = 3f;

    private RectTransform rectTransform;
    private Vector2 basePosition;
    private float time;

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        basePosition = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        time = 0f;
    }

    private void Update()
    {
        time += Time.unscaledDeltaTime;
        float offset = Mathf.Sin(time / period * Mathf.PI * 2f) * amplitude;
        rectTransform.anchoredPosition = basePosition + new Vector2(0f, offset);
    }
}
