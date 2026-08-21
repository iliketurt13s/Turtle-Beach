using UnityEngine;

/// <summary>
/// The game's two looping beds: beach ambience that runs the whole time, and
/// rain that fades in for the storm and back out when it passes. One of these
/// per scene that wants them (the Menu can carry one for the beach loop alone).
///
/// Deliberately does NOT go through AudioManager's pool. That pool exists for
/// one-shots — it hands out a voice, waits for the clip to finish, and takes
/// the voice back (see ReleaseFinishedVoices). A loop never finishes, so it
/// would hold a pooled voice forever, and two of them would permanently shrink
/// the budget every other sound in the game draws from. These own two dedicated
/// AudioSources instead, created here rather than authored, so there is nothing
/// to wire and no way to leave one accidentally set to Play On Awake.
///
/// It still reads AudioManager.MasterVolume every frame so the loops sit under
/// the same master mix as everything else, and falls back to full volume when
/// there is no AudioManager in the scene at all — matching how SoundEffect
/// degrades to silence rather than erroring.
///
/// The storm is polled from DayStormCycle rather than driven by an event,
/// matching how the rest of this project follows the phase flags. It is gated
/// on DayStormCycle.Instance existing, not just IsStorming being true, because
/// IsStorming is a static that nothing resets when a scene without a
/// DayStormCycle loads — returning to the Menu mid-storm would otherwise leave
/// it reading true and the menu would sit there raining.
/// </summary>
public class AmbienceController : MonoBehaviour
{
    /// <summary>Scene-wide singleton (mirrors AudioManager's) so GarbagePatchCutsceneController can end the weather at the exact frame its animation finishes without holding a serialized reference to this.</summary>
    public static AmbienceController Instance { get; private set; }

    [Header("Beach Ambience")]
    [Tooltip("Looped for the entire scene. Leave unassigned for no beach bed.")]
    [SerializeField] private AudioClip beachClip;
    [Tooltip("Volume of the beach loop during the day, before AudioManager's Master Volume.")]
    [SerializeField, Range(0f, 1f)] private float beachVolume = 0.5f;
    [Tooltip("Volume the beach loop drops to while it is storming, so the rain has room without the beach having to be cut entirely. Set equal to Beach Volume to leave it unducked.")]
    [SerializeField, Range(0f, 1f)] private float beachVolumeDuringStorm = 0.25f;

    [Header("Rain")]
    [Tooltip("Looped only while a storm is running. Leave unassigned for no rain.")]
    [SerializeField] private AudioClip rainClip;
    [Tooltip("Volume the rain reaches once the storm is fully underway.")]
    [SerializeField, Range(0f, 1f)] private float rainVolume = 0.6f;

    [Tooltip("Seconds each loop takes to fade to its new level when the storm starts or ends. Real seconds, so a paused or sped-up game still crossfades at a natural rate — the weather changing is not a gameplay event the player can fast-forward through.")]
    [SerializeField, Min(0f)] private float fadeDuration = 2.5f;

    [Tooltip("Seconds the rain takes to go when EndStormWeather cuts it (the garbage-patch cutscene finishing). Much shorter than Fade Duration on purpose: that one is weather drifting in and out on its own, this is a beat in the game landing, and the rain should be gone with it rather than trailing into the new day. Not zero — a full-volume loop stopped on a single frame clicks.")]
    [SerializeField, Min(0f)] private float stormCutFadeDuration = 0.6f;

    private AudioSource beachSource;
    private AudioSource rainSource;

    /// <summary>Set by EndStormWeather to take the rain down before DayStormCycle.IsStorming has actually cleared. One-shot: rearmed by the next storm's rising edge in Update, so it can never mute a storm that hasn't happened yet.</summary>
    private bool stormWeatherCut;
    private bool wasStorming;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("AmbienceController: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        beachSource = CreateLoopSource(beachClip);
        rainSource = CreateLoopSource(rainClip);

        // Rain starts silent even if the scene somehow loads mid-storm; the
        // first Update fades it up rather than slamming it on at full volume.
        if (rainSource != null) rainSource.volume = 0f;
        if (beachSource != null) beachSource.volume = 0f;
    }

    /// <summary>Builds one looping 2D source for clip, or null if there's no clip to play. 2D for the same reason AudioManager's pool is (see there): in a zoomable top-down game real falloff would swell and fade with the zoom rather than with anything in the world — and a weather bed has no position in the world to begin with.</summary>
    private AudioSource CreateLoopSource(AudioClip clip)
    {
        if (clip == null) return null;

        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.Play();

        return source;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Ends the weather NOW, without waiting for DayStormCycle.IsStorming to
    /// clear: the rain fades out over Storm Cut Fade Duration and the beach
    /// comes back up out of its storm duck.
    ///
    /// Called by GarbagePatchCutsceneController the moment its camera has
    /// finished coming home. IsStorming deliberately stays true from the end of
    /// the fighting right through the upgrade pick AND that cutscene (see
    /// DayStormCycle.HandleUpgradeChoiceComplete — it gates every daytime turtle
    /// behavior, which must not resume while the player is still watching), so
    /// polling it alone left the rain running under all of it and only starting
    /// its slow fade once the next day had already begun.
    ///
    /// It also covers the run's last storm, where the fix is not cosmetic:
    /// the winning cutscene deliberately never invokes its onComplete, so
    /// IsStorming is never cleared at all and the rain would otherwise keep
    /// pouring underneath the win screen for as long as the player sat on it.
    /// </summary>
    public void EndStormWeather() => stormWeatherCut = true;

    private void Update()
    {
        bool storming = DayStormCycle.Instance != null && DayStormCycle.IsStorming;

        // Rearmed on the rising edge rather than cleared by whoever cut it: a
        // cut belongs to the storm it ended, and the next one starting is the
        // one moment that is unambiguously a different storm.
        if (storming && !wasStorming) stormWeatherCut = false;
        wasStorming = storming;

        bool raining = storming && !stormWeatherCut;
        float master = AudioManager.MasterVolume;
        float duration = stormWeatherCut ? stormCutFadeDuration : fadeDuration;

        FadeToward(beachSource, (raining ? beachVolumeDuringStorm : beachVolume) * master, duration);
        FadeToward(rainSource, (raining ? rainVolume : 0f) * master, duration);
    }

    /// <summary>Walks one source's volume toward target at a rate set by duration (Fade Duration normally, Storm Cut Fade Duration while a cut is in force). MoveTowards rather than a coroutine so a storm that starts while a previous fade is still running simply reverses from wherever it got to, with no routine to cancel and no chance of two fades fighting over one volume.</summary>
    private void FadeToward(AudioSource source, float target, float duration)
    {
        if (source == null) return;

        if (duration <= 0f)
        {
            source.volume = target;
            return;
        }

        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime / duration);
    }
}
