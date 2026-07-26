using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reusable colored screen-wipe transition: slides in from the right to fully
/// cover the screen, then — once whatever happens while covered finishes —
/// retreats back the way it came, off-screen to the right again, revealing
/// whatever's now on screen. This project has no DontDestroyOnLoad/persistent
/// objects anywhere (every system is scene-instantiated, see GameOverUI's own
/// doc comment), so one instance lives in each scene's own Canvas rather than
/// surviving a scene load — the RectTransform is stretched to exactly fill
/// its Canvas, so the same script/prefab works unmodified whether that
/// Canvas is Screen Space - Overlay (GameScene) or World Space (Menu).
///
/// Every scene holding one of these always reveals itself shortly after it
/// loads (see Start/revealDelay) — regardless of whether it was actually
/// reached via PlayCoverAndLoadScene or opened directly in the Editor, so no
/// extra "was this a real transition" state is needed anywhere (including a
/// cold app launch on the Menu scene, which just plays a harmless reveal wipe
/// too). The short real-time delay before it starts sliding lets the freshly
/// loaded scene's own Awake/Start/procedural setup finish first, so the slide
/// itself doesn't stutter through that hitch.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SceneTransitionPanel : MonoBehaviour
{
    [SerializeField] private float slideDuration = 0.5f;
    [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Width (in this RectTransform's own units — typically pixels) of the source graphic's repeating tile, e.g. 111 for a 111x32 pattern. The off-screen resting position is rounded up to the nearest multiple of this so the tiled pattern always lands on the exact same phase — no visible seam between the cover and reveal positions, or between one transition and the next. Set to 0 to disable and just travel exactly one screen width.")]
    [SerializeField] private float graphicTileWidth = 111f;

    [Tooltip("Real-time seconds this scene sits fully covered and motionless before Reveal starts sliding on load. A freshly loaded scene's own Awake/Start/procedural setup (island generation, spawning, etc.) tends to hitch the frame — waiting here lets that settle first so the slide itself always plays smoothly instead of stuttering through it.")]
    [SerializeField] private float revealDelay = 0.25f;

    private RectTransform rectTransform;
    private Coroutine running;

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        SetNormalizedPosition(0f); // start fully covering, hiding any first-frame setup pop-in
    }

    private void Start()
    {
        Restart(RevealAfterDelay());
    }

    private IEnumerator RevealAfterDelay()
    {
        if (revealDelay > 0f) yield return new WaitForSecondsRealtime(revealDelay);
        yield return SlideTo(1f);
    }

    /// <summary>Slides in to cover the screen, then loads sceneName.</summary>
    public void PlayCoverAndLoadScene(string sceneName)
    {
        Restart(CoverThenLoad(sceneName));
    }

    /// <summary>Slides in to cover the screen, then invokes onCovered instead of loading a scene — for an in-place transition (e.g. a new island) that never actually changes scene. Call Reveal() yourself once whatever happens while covered finishes; nothing else will.</summary>
    public void PlayCoverThenInvoke(Action onCovered)
    {
        Restart(CoverThen(onCovered));
    }

    /// <summary>Slides back off-screen to the right, uncovering the screen — the same edge it slid in from, ready to cover again next time with no extra repositioning needed.</summary>
    public void Reveal()
    {
        Restart(SlideTo(1f));
    }

    private void Restart(IEnumerator routine)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(routine);
    }

    private IEnumerator CoverThenLoad(string sceneName)
    {
        yield return SlideTo(0f);
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator CoverThen(Action onCovered)
    {
        yield return SlideTo(0f);
        onCovered?.Invoke();
    }

    /// <summary>normalized: 0 = covering, 1 = fully off-screen right — the panel only ever sits at one of these two positions, sliding directly between them for both the cover and the reveal.</summary>
    private IEnumerator SlideTo(float normalized)
    {
        float startX = rectTransform.anchoredPosition.x;
        float targetX = normalized * OffScreenDistance();

        float t = 0f;
        while (t < slideDuration)
        {
            // Unscaled so the wipe plays at the same real-world speed
            // regardless of TimeControlUI's pause/double-speed Time.timeScale.
            t += Time.unscaledDeltaTime;
            float p = easeCurve.Evaluate(Mathf.Clamp01(t / slideDuration));
            SetPositionX(Mathf.Lerp(startX, targetX, p));
            yield return null;
        }

        SetPositionX(targetX);
        running = null;
    }

    private void SetNormalizedPosition(float normalized) => SetPositionX(normalized * OffScreenDistance());

    /// <summary>Smallest distance that both fully clears the screen (at least rect.width) AND lands exactly on a multiple of graphicTileWidth, so the tiled graphic always rests at the same phase off-screen.</summary>
    private float OffScreenDistance()
    {
        float width = rectTransform.rect.width;
        if (graphicTileWidth <= 0f) return width;

        return Mathf.Ceil(width / graphicTileWidth) * graphicTileWidth;
    }

    private void SetPositionX(float x)
    {
        Vector2 pos = rectTransform.anchoredPosition;
        pos.x = x;
        rectTransform.anchoredPosition = pos;
    }
}
