using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The settings screen — one panel of sliders (camera drag speed, camera zoom
/// speed, music volume, sound effects volume) dropped into BOTH scenes, which
/// behaves correctly in each without being configured differently.
/// GameSettings owns the values themselves; this owns only the screen.
///
/// Scene-wide singleton (see CLAUDE.md) so SettingsButton and GameOverUI can
/// reach it without a serialized reference, plus a static IsOpen the gameplay
/// scene's input systems poll to stand down while it is up — the same shape as
/// UpgradeSelectionUI.IsActive and GameOverUI.IsPaused, and for the same
/// reason: TurtleSelectionController and BuildModeController read clicks
/// straight off Physics2D rather than exclusively through Unity's UI event
/// system, so without a flag the player would order turtles and place
/// buildings through this panel. IsOpen is folded into
/// BuildModeController.IsBlockedByPhase, so everything already standing down
/// for a blocking phase (the build drawer, its hammer tab, the tutorial mentor
/// and arrow) covers this one for free.
///
/// The slider RANGES are set from GameSettings' own constants in Awake rather
/// than authored per slider, so the two can't disagree and adding this panel to
/// a second scene is drag-and-drop rather than four numbers to retype.
///
/// One detection drives the two things that genuinely differ between scenes:
/// whether a GameOverUI exists (IsGameplayScene).
/// - Escape. GameOverUI is the gameplay scene's single reader of that key (see
///   its HandleEscape for why one reader matters), so there this panel doesn't
///   touch Escape at all — GameOverUI closes it, ahead of pausing. The Menu
///   scene has no such reader, so this panel owns Escape there itself.
/// - Pausing. In gameplay, opening this freezes the game; the previous
///   timeScale is restored on close rather than assuming 1, so it nests
///   correctly both over TimeControlUI's 2x and over an already-paused game
///   (opened from the pause menu it stores 0, and closing leaves it paused).
///   In the Menu there is nothing to pause, and freezing it would stop the
///   menu's own ambience animating.
///
/// It closes the same way from every route — its own close button, Escape, or
/// the pause menu's Back button via GameOverUI.GoBack — because all three call
/// Close(), which hands the panel to UISlidePanel.PlayOut and only deactivates
/// it once it has actually slid off. Nothing needs to sit inside anything else
/// for that to work; sharing the pause menu's Back button is a wiring choice,
/// not a hierarchy one.
///
/// The one thing that arrangement does require is that the Back button stays
/// CLICKABLE while this is up, so a full-screen backdrop here wants Raycast
/// Target OFF (see TurtleSelectionController.IsPointerOverUI for why that is
/// normally the dangerous setting — here it is the safe one). Nothing leaks
/// through: IsOpen already stands down turtle selection, build mode and the
/// camera, and TimeControlUI's three speed buttons — the only other UI that
/// acts on a click without checking a phase flag — gate on it too.
/// </summary>
public class SettingsMenuUI : MonoBehaviour
{
    public static SettingsMenuUI Instance { get; private set; }

    /// <summary>True while the settings screen is up. Polled by CameraController, TurtleSelectionController and BuildModeController.IsBlockedByPhase so gameplay input stands down — see the class doc comment.</summary>
    public static bool IsOpen { get; private set; }

    [Tooltip("Root object for the whole settings screen, including its modal backdrop. Hidden until Open() is called.")]
    [SerializeField] private GameObject panel;
    [Tooltip("Panel's own UISlidePanel, so it slides in and out instead of popping — it is only deactivated once it is actually off-screen. Turn that component's Play In On Enable OFF and leave the driving to this script. Leave this field unassigned to fall back to an instant show/hide.")]
    [SerializeField] private UISlidePanel panelSlide;

    [Header("Sliders")]
    [Tooltip("Camera drag speed, as a multiplier over CameraController's authored Drag Speed. Min/Max are set from GameSettings in Awake — there is no need to configure the range on the Slider itself.")]
    [SerializeField] private Slider cameraDragSpeedSlider;
    [Tooltip("Camera zoom speed, as a multiplier over CameraController's authored Zoom Speed.")]
    [SerializeField] private Slider cameraZoomSpeedSlider;
    [Tooltip("Music volume — scales MusicPlayer's loop.")]
    [SerializeField] private Slider musicVolumeSlider;
    [Tooltip("Sound effects volume — scales every pooled one-shot plus the beach/rain ambience beds.")]
    [SerializeField] private Slider sfxVolumeSlider;

    [Header("Hidden While Open (optional)")]
    [Tooltip("Optional, and normally left empty. Objects switched off while the settings screen is up and switched back on when it closes — for anything that would otherwise show around this panel rather than behind it (e.g. the pause menu's Paused label, if this opens over the top of it). Do NOT drag in the pause menu's Back button, or the object it is parented under, if that button is what closes this screen.")]
    [SerializeField] private GameObject[] hideWhileOpen;

    [Header("Value Readouts (optional)")]
    [SerializeField] private TMP_Text cameraDragSpeedValue;
    [SerializeField] private TMP_Text cameraZoomSpeedValue;
    [SerializeField] private TMP_Text musicVolumeValue;
    [SerializeField] private TMP_Text sfxVolumeValue;
    [Tooltip("Format for the two camera readouts — {0} is the multiplier.")]
    [SerializeField] private string multiplierFormat = "{0:0.00}x";
    [Tooltip("Format for the two volume readouts — {0} is the volume as a percentage.")]
    [SerializeField] private string percentFormat = "{0:0}%";

    /// <summary>True when this scene has a GameOverUI in it — i.e. it is the gameplay scene, not the Menu. Drives Escape ownership and whether opening this pauses; read by SettingsButton so it only hides itself for blocking phases in the scene those phases exist in. See the class doc comment.</summary>
    public bool IsGameplayScene { get; private set; }

    /// <summary>Whatever Time.timeScale was when this opened — TimeControlUI's 2x, or 0 if it was opened from an already-paused game. Restored on close rather than assuming 1.</summary>
    private float timeScaleBeforeOpen = 1f;
    private bool pausedByThis;

    /// <summary>Set while pushing stored values INTO the sliders, so their own onValueChanged doesn't write those same values straight back out — harmless in itself, but it would fire on every sync and make a genuine change indistinguishable from a refresh.</summary>
    private bool suppressSliderCallbacks;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("SettingsMenuUI: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Static, so force it rather than trusting its default — it would
        // otherwise survive a scene load (Restart, or Menu after a run) and an
        // Editor Play session with Domain Reload disabled, leaving every input
        // system in the new scene gated as if a settings screen were up.
        IsOpen = false;

        // Include inactive: GameOverUI may sit on an object switched off at
        // edit time, and this must not depend on whose Awake ran first (the
        // component exists either way).
        IsGameplayScene = FindAnyObjectByType<GameOverUI>(FindObjectsInactive.Include) != null;

        ConfigureSlider(cameraDragSpeedSlider, GameSettings.MinCameraSpeedMultiplier, GameSettings.MaxCameraSpeedMultiplier, HandleCameraDragSpeedChanged);
        ConfigureSlider(cameraZoomSpeedSlider, GameSettings.MinCameraSpeedMultiplier, GameSettings.MaxCameraSpeedMultiplier, HandleCameraZoomSpeedChanged);
        ConfigureSlider(musicVolumeSlider, 0f, 1f, HandleMusicVolumeChanged);
        ConfigureSlider(sfxVolumeSlider, 0f, 1f, HandleSfxVolumeChanged);

        SyncFromSettings();

        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        Instance = null;

        // Cleared here as well as in Awake: a scene change while the screen is
        // open (it can't normally happen, but the Restart/Menu buttons are one
        // Escape away) would otherwise strand the flag on and leave the next
        // scene's input suspended for good.
        IsOpen = false;
    }

    private void ConfigureSlider(Slider slider, float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
    {
        if (slider == null) return;

        // Range comes from GameSettings rather than the Inspector — see the
        // class doc comment.
        slider.wholeNumbers = false;
        slider.minValue = min;
        slider.maxValue = max;

        // AddListener rather than Inspector wiring, matching
        // BuildMenuToggleButton: the hookup can't be lost by someone rebuilding
        // the slider, and there is no second place for it to go stale.
        slider.onValueChanged.AddListener(onChanged);
    }

    private void HandleCameraDragSpeedChanged(float value)
    {
        if (suppressSliderCallbacks) return;

        GameSettings.CameraDragSpeed = value;
        RefreshReadouts();
    }

    private void HandleCameraZoomSpeedChanged(float value)
    {
        if (suppressSliderCallbacks) return;

        GameSettings.CameraZoomSpeed = value;
        RefreshReadouts();
    }

    private void HandleMusicVolumeChanged(float value)
    {
        if (suppressSliderCallbacks) return;

        GameSettings.MusicVolume = value;
        RefreshReadouts();
    }

    private void HandleSfxVolumeChanged(float value)
    {
        if (suppressSliderCallbacks) return;

        GameSettings.SfxVolume = value;
        RefreshReadouts();
    }

    /// <summary>Pushes the stored values into the sliders. Done on every open as well as in Awake, since the settings are shared with the other scene's copy of this panel and with anything else that writes them.</summary>
    private void SyncFromSettings()
    {
        suppressSliderCallbacks = true;

        if (cameraDragSpeedSlider != null) cameraDragSpeedSlider.value = GameSettings.CameraDragSpeed;
        if (cameraZoomSpeedSlider != null) cameraZoomSpeedSlider.value = GameSettings.CameraZoomSpeed;
        if (musicVolumeSlider != null) musicVolumeSlider.value = GameSettings.MusicVolume;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = GameSettings.SfxVolume;

        suppressSliderCallbacks = false;

        RefreshReadouts();
    }

    /// <summary>Reads back out of GameSettings rather than off the slider it was just handed, so the number on screen is the CLAMPED value actually stored — the two only differ at the ends of the range, which is exactly where a readout disagreeing with the setting would be noticed.</summary>
    private void RefreshReadouts()
    {
        if (cameraDragSpeedValue != null) cameraDragSpeedValue.text = string.Format(multiplierFormat, GameSettings.CameraDragSpeed);
        if (cameraZoomSpeedValue != null) cameraZoomSpeedValue.text = string.Format(multiplierFormat, GameSettings.CameraZoomSpeed);
        if (musicVolumeValue != null) musicVolumeValue.text = string.Format(percentFormat, GameSettings.MusicVolume * 100f);
        if (sfxVolumeValue != null) sfxVolumeValue.text = string.Format(percentFormat, GameSettings.SfxVolume * 100f);
    }

    private void Update()
    {
        if (!IsOpen) return;

        // Gameplay's Escape belongs to GameOverUI alone (it closes this ahead
        // of pausing); the Menu scene has no such reader, so this panel takes
        // the key there itself. Two readers on one key would race on script
        // execution order — see GameOverUI.HandleEscape.
        if (!IsGameplayScene && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        // A run can end underneath this — the nest is destroyed on a frame the
        // player happens to be in here — and GameOverUI puts its results up
        // regardless. Stand aside for that rather than leaving the settings
        // screen floating over a finished run.
        if (IsGameplayScene && GameOverUI.IsRunOver)
        {
            // ShowResult has already set the time scale itself on its way up.
            // Dropping the claim before closing leaves that alone, instead of
            // overwriting it with the value captured back when this screen
            // opened — which is now stale by a whole run ending.
            pausedByThis = false;
            Close();
        }
    }

    /// <summary>Wire this up to a settings button's OnClick() in the Inspector, or let SettingsButton call it. Opening is what re-reads the stored values, so the panel is always current.</summary>
    public void Open()
    {
        if (IsOpen) return;

        IsOpen = true;
        SyncFromSettings();

        if (IsGameplayScene)
        {
            // Stored rather than assumed: this can be opened over
            // TimeControlUI's 2x, or from the pause menu where it is already 0.
            timeScaleBeforeOpen = Time.timeScale;
            Time.timeScale = 0f;
            pausedByThis = true;
        }

        SetHiddenObjectsVisible(false);

        if (panel != null) panel.SetActive(true);
        if (panelSlide != null) panelSlide.PlayIn();
    }

    /// <summary>Wire this up to the settings screen's Back/Close button OnClick(). Also what Escape routes to — from here in the Menu, from GameOverUI in gameplay — so the two can't drift apart.</summary>
    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;

        // One disk write per visit to the screen, rather than one per frame of
        // a dragged slider — see GameSettings.
        GameSettings.Flush();

        if (pausedByThis)
        {
            pausedByThis = false;

            // Deliberately not 1f: restores the pause menu's 0 if this was
            // opened from there, and TimeControlUI's speed otherwise.
            Time.timeScale = timeScaleBeforeOpen;
        }

        SetHiddenObjectsVisible(true);

        if (panelSlide != null)
        {
            panelSlide.PlayOut(() => { if (panel != null) panel.SetActive(false); });
        }
        else if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    /// <summary>
    /// Switches the Hide While Open list off on the way in and back on on the
    /// way out — the pause menu's own furniture, in the arrangement where this
    /// screen is a page inside that panel.
    ///
    /// Showing is skipped once the run is over, and that asymmetry is the whole
    /// point of it being a method rather than two SetActive loops: GameOverUI
    /// has just decided what belongs on screen for the results (see
    /// ShowResult, which hides exactly this furniture), and this closing a
    /// beat later would otherwise put "Game Paused" straight back up over a
    /// finished run.
    /// </summary>
    private void SetHiddenObjectsVisible(bool visible)
    {
        if (hideWhileOpen == null) return;
        if (visible && IsGameplayScene && GameOverUI.IsRunOver) return;

        foreach (GameObject target in hideWhileOpen)
        {
            if (target != null) target.SetActive(visible);
        }
    }

    /// <summary>What SettingsButton clicks through to, so a button the panel doesn't cover closes it again rather than doing nothing.</summary>
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>Wire this up to an optional "Defaults" button on the settings screen. Puts every value back to the tuned default and re-syncs the sliders to match.</summary>
    public void ResetToDefaults()
    {
        GameSettings.ResetToDefaults();
        SyncFromSettings();
    }
}
