using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The cog that opens the settings screen. Drop it on a Button in either scene
/// and it works: it finds SettingsMenuUI through that class's static Instance
/// rather than a serialized reference, so the same prefab can be dropped into
/// the Menu and the gameplay scene without rewiring.
///
/// Modelled on BuildMenuToggleButton, including the CanvasGroup fade — this is
/// the second on-screen tab that has to get out of the way when a phase owns
/// the screen, and matching it means one behaviour to learn rather than two.
///
/// The phase hiding is deliberately conditional on being IN the gameplay scene
/// (SettingsMenuUI.IsGameplayScene). BuildModeController.IsBlockedByPhase reads
/// statics — GameOverUI.IsRunOver among them — that only the gameplay scene
/// ever resets, so a player who finishes a run and returns to the Menu arrives
/// with IsRunOver still true. Hiding off that flag unconditionally would leave
/// the Menu's settings button permanently invisible, with nothing on screen to
/// suggest why.
/// </summary>
public class SettingsButton : MonoBehaviour
{
    [Tooltip("The Button that opens the settings screen. Leave unassigned to use one on this same GameObject.")]
    [SerializeField] private Button button;

    [Header("Phase Hiding (gameplay scene only)")]
    [Tooltip("Optional CanvasGroup faded out (and made non-interactive) while a phase owns the screen — the upgrade pick, the garbage-patch cutscene, the pause menu, a finished run, or the settings screen itself. Leave unassigned to stay visible always.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Seconds the fade in/out takes. Runs on unscaled time, so it still plays while the game is paused at Time.timeScale 0.")]
    [SerializeField] private float fadeDuration = 0.15f;

    private bool hidesForPhases;
    private bool wasBlocked;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();

        if (button == null)
        {
            Debug.LogWarning($"{name}: SettingsButton has no Button to listen to, so there is now no way to open the settings screen. Assign one (or put this component on the button itself).", this);
            return;
        }

        // AddListener rather than Inspector wiring, matching
        // BuildMenuToggleButton: drop the component on, and the button works.
        button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    private void Start()
    {
        // Start, not Awake: SettingsMenuUI resolves IsGameplayScene in its own
        // Awake, which may run after this one.
        hidesForPhases = SettingsMenuUI.Instance != null && SettingsMenuUI.Instance.IsGameplayScene;

        if (SettingsMenuUI.Instance == null)
        {
            Debug.LogWarning($"{name}: no SettingsMenuUI in this scene, so this button opens nothing. Add the settings panel to the scene.", this);
        }

        // Snap (no fade) to whatever state the scene actually starts in, so the
        // first frame doesn't animate from a state that was never on screen.
        wasBlocked = IsBlocked;
        ApplyBlocked(wasBlocked, animate: false);
    }

    /// <summary>Blocked whenever a phase owns the screen — reusing BuildModeController's expression rather than re-listing those phases, so a new one only has to be added once — plus while the settings screen is up, which that expression also covers but which is the reason this button in particular has to go.</summary>
    private bool IsBlocked => hidesForPhases && BuildModeController.IsBlockedByPhase;

    private void HandleClick()
    {
        if (SettingsMenuUI.Instance == null) return;

        SettingsMenuUI.Instance.Toggle();
    }

    private void Update()
    {
        // Polled rather than event-driven, matching how everything else in this
        // project follows the phase flags — a phase can begin in several
        // unrelated places, and polling covers all of them without any of them
        // needing to know this button exists.
        bool blocked = IsBlocked;
        if (blocked == wasBlocked) return;

        wasBlocked = blocked;
        ApplyBlocked(blocked, animate: true);
    }

    private void ApplyBlocked(bool blocked, bool animate)
    {
        if (canvasGroup == null) return;

        // Raycast blocking goes off immediately either way — a button that is
        // fading out must stop eating clicks on frame one, not once the fade
        // has finished.
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
