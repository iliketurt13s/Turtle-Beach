using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hammer tab that opens and closes the build drawer — the only way into
/// build mode. Wires its Button to BuildModeController.ToggleBuildMode and
/// otherwise stays out of the way: the controller owns whether build mode is
/// on, and BuildSelectionUI owns the panel's slide, so this script never
/// touches either.
///
/// Deliberately has no open/closed visual of its own — the icon is a fixed
/// hammer, and the drawer sliding out from behind it is the feedback. Click to
/// open, click again to close; Escape also closes it (handled in GameOverUI,
/// which owns that key — see there for why the priority lives in one place).
///
/// Meant to live as a child of the sliding panel, poking out past its inner
/// edge so it rides along like a real drawer pull and stays on screen when the
/// panel is parked off it. Nothing here assumes that, though — a tab pinned to
/// the screen edge as a sibling works exactly the same, it just doesn't move.
///
/// Hides itself (and stops taking clicks) whenever a phase owns the screen, off
/// BuildModeController.IsBlockedByPhase — the same condition that forces build
/// mode off — so it isn't left floating over the upgrade pick, the
/// garbage-patch cutscene, the pause menu or the game-over panel.
/// </summary>
public class BuildMenuToggleButton : MonoBehaviour
{
    [Tooltip("The Button that toggles the drawer. Leave unassigned to use one on this same GameObject.")]
    [SerializeField] private Button toggleButton;

    [Header("Phase Hiding")]
    [Tooltip("Optional CanvasGroup faded out (and made non-interactive) while a phase owns the screen. Leave unassigned to stay visible always — but then the hammer floats over the upgrade pick and the game-over panel.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Seconds the fade in/out takes. Runs on unscaled time, so it still plays while the game is paused at Time.timeScale 0.")]
    [SerializeField] private float fadeDuration = 0.15f;

    private bool wasBlocked;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (toggleButton == null) toggleButton = GetComponent<Button>();

        if (toggleButton == null)
        {
            Debug.LogWarning($"{name}: BuildMenuToggleButton has no Button to listen to, so there is now NO way to enter build mode at all. Assign one (or put this component on the button itself).", this);
            return;
        }

        // AddListener rather than Inspector wiring so the hookup can't be lost
        // by someone rebuilding the button, and so this component is
        // self-contained: drop it on, and the button works.
        toggleButton.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (toggleButton != null) toggleButton.onClick.RemoveListener(HandleClick);
    }

    private void Start()
    {
        // Snap (no fade) to whatever state the scene actually starts in, so the
        // first frame doesn't animate from a default that was never on screen.
        wasBlocked = BuildModeController.IsBlockedByPhase;
        ApplyBlocked(wasBlocked, animate: false);
    }

    private void HandleClick()
    {
        if (BuildModeController.Instance == null) return;

        BuildModeController.Instance.ToggleBuildMode();
    }

    private void Update()
    {
        // Polled rather than event-driven, matching how everything else in this
        // project follows the phase flags — a phase can start from several
        // unrelated places, and polling covers all of them without any needing
        // to know this tab exists.
        bool blocked = BuildModeController.IsBlockedByPhase;
        if (blocked == wasBlocked) return;

        wasBlocked = blocked;
        ApplyBlocked(blocked, animate: true);
    }

    private void ApplyBlocked(bool blocked, bool animate)
    {
        if (canvasGroup == null) return;

        // Raycast blocking goes off immediately either way — a tab that's fading
        // out must stop eating clicks on frame one, not once the fade finishes.
        canvasGroup.interactable = !blocked;
        canvasGroup.blocksRaycasts = !blocked;

        float target = blocked ? 0f : 1f;

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);

        if (!animate || fadeDuration <= 0f)
        {
            canvasGroup.alpha = target;
            return;
        }

        fadeRoutine = StartCoroutine(FadeRoutine(target));
    }

    private IEnumerator FadeRoutine(float target)
    {
        float from = canvasGroup.alpha;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        canvasGroup.alpha = target;
        fadeRoutine = null;
    }
}
