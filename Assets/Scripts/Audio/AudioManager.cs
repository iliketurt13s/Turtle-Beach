using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-wide sound-effect player: one pool of AudioSources shared by
/// everything that makes noise, plus the throttling that keeps a busy island
/// from turning into mush. Follows the same static-Instance pattern as
/// ResourceManager/PathfindingManager (see CLAUDE.md) — sounds are triggered by
/// runtime-spawned objects (nodes, turtles, trash) that can't hold a
/// scene-authored reference.
///
/// Pooled rather than an AudioSource per object, for two reasons beyond the
/// obvious component count: a sound outlives its emitter (a tree keeps ringing
/// after it depletes and hides its visual, a turtle's last stroke finishes
/// after it dies), and a shared pool is what makes a global voice cap possible
/// at all. Sources are plain 2D (spatialBlend 0) — this is a zoomable top-down
/// game, so true 3D falloff would have sounds swell and fade as the player
/// zooms rather than as anything in the world changed. Distance is handled
/// instead by SoundEffect's own Max Audible Distance cull.
///
/// Throttling is per SoundEffect.GroupKey (its first clip — see there for why),
/// so every tree in the scene shares one budget and rocks get their own:
/// Min Interval rate-limits, Max Voices caps overlap. Both are enforced here
/// rather than on the emitter, since the whole point is that they're shared
/// across all of them.
///
/// If no AudioManager is in the scene, every Play is a silent no-op — sounds
/// are never a hard dependency, which is what lets the Menu scene run its
/// ambience turtles without one.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Tooltip("How many sounds can play simultaneously across the whole game. Once every source is busy, further plays are dropped — per-sound Max Voices should normally run out first, so this is the backstop that keeps a pathological frame (a storm ending on top of a harvest rush) from spawning unbounded audio.")]
    [SerializeField, Min(1)] private int voicePoolSize = 24;

    [Tooltip("Scales every sound played through here, on top of each SoundEffect's own volume. The single knob to pull for an overall SFX mix pass — the AUTHORING half of the mix. The player's own Sound Effects slider (GameSettings.SfxVolume) multiplies on top of it, so this stays the level the game is mixed at and that stays the level the player asked for.")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

    [Header("Shared Sounds")]
    [Tooltip("The click every UI button makes. Authored ONCE here rather than per button, because it is one sound for the whole game and assigning the same clip to forty buttons is forty chances to miss one. A button opts in by carrying a UIButtonSound component, which needs nothing configured on it. Note that a scene without an AudioManager hears none of this - Play is a silent no-op there - so the Menu scene needs one of its own.")]
    [SerializeField] private SoundEffect buttonClickSound = new SoundEffect();

    /// <summary>Per-group throttle bookkeeping — see the class doc comment. Allocated once per distinct sound on its first play, then reused.</summary>
    private class GroupState
    {
        public float LastPlayTime = float.NegativeInfinity;
        public int ActiveVoices;
    }

    private readonly Dictionary<AudioClip, GroupState> groups = new Dictionary<AudioClip, GroupState>();

    private AudioSource[] pool;
    /// <summary>Which group each pooled source is currently playing for (null = free), so ReleaseFinishedVoices knows whose voice count to give back.</summary>
    private AudioClip[] poolGroups;

    private Camera cam;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("AudioManager: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        cam = Camera.main;

        pool = new AudioSource[voicePoolSize];
        poolGroups = new AudioClip[voicePoolSize];
        for (int i = 0; i < voicePoolSize; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            // 2D: see the class doc comment. Also ignores the listener's
            // position entirely, so nothing depends on where the camera rig
            // happens to put the AudioListener.
            source.spatialBlend = 0f;
            pool[i] = source;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        ReleaseFinishedVoices();
    }

    /// <summary>
    /// Hands voices back to their group once their clip has finished. Polled
    /// over the pool rather than tracked with a coroutine or a scheduled
    /// callback per play: the pool is small and fixed, so this is a couple
    /// dozen bool reads a frame with no per-play allocation — and it can't
    /// leak a voice the way a coroutine cancelled by a disable would.
    /// </summary>
    private void ReleaseFinishedVoices()
    {
        for (int i = 0; i < pool.Length; i++)
        {
            if (poolGroups[i] == null || pool[i].isPlaying) continue;

            if (groups.TryGetValue(poolGroups[i], out GroupState state)) state.ActiveVoices--;
            poolGroups[i] = null;
        }
    }

    /// <summary>Scales every sound played through here: the authored mix (Master Volume above) times the player's own Sound Effects setting. Exposed so a sound that owns its own AudioSource rather than borrowing one from the pool - a LOOP, which would otherwise occupy a pooled voice forever - can still sit under the same master mix, and so the ambience beds follow the effects slider without knowing about it. Read live rather than cached, so dragging that slider is audible while it is being dragged. Falls back to the authored mix being 1 when no AudioManager exists, matching how Play degrades to a silent no-op — the player's setting still applies.</summary>
    public static float MasterVolume => (Instance != null ? Instance.masterVolume : 1f) * GameSettings.SfxVolume;

    /// <summary>Plays the shared UI button click. Called by UIButtonSound so no individual button has to hold a clip of its own.</summary>
    public static void PlayButtonClick()
    {
        if (Instance == null) return;

        // No position: a UI click is non-diegetic, so Max Audible Distance must
        // never cull it based on where the camera happens to be looking.
        Instance.PlayEffect(Instance.buttonClickSound, null);
    }

    /// <summary>Plays a sound effect, if one is configured and the throttle allows it. worldPosition is used only for SoundEffect's distance cull — pass null for a sound that should always be audible. Safe to call with no AudioManager in the scene (silent no-op), so callers never need to null-check.</summary>
    public static void Play(SoundEffect effect, Vector3? worldPosition)
    {
        if (Instance == null || effect == null) return;

        Instance.PlayEffect(effect, worldPosition);
    }

    private void PlayEffect(SoundEffect effect, Vector3? worldPosition)
    {
        AudioClip clip = effect.PickClip();
        if (clip == null) return;

        if (IsOutOfEarshot(effect, worldPosition)) return;

        AudioClip key = effect.GroupKey;
        if (!groups.TryGetValue(key, out GroupState state))
        {
            state = new GroupState();
            groups[key] = state;
        }

        // Unscaled: the throttle is about how fast sounds arrive at the
        // player's ear, which doesn't change just because the game is running
        // at 2x — if anything that's exactly when it needs to hold hardest.
        if (Time.unscaledTime - state.LastPlayTime < effect.MinInterval) return;
        if (state.ActiveVoices >= effect.MaxVoices) return;

        int index = FindFreeSource();
        if (index < 0) return;

        AudioSource source = pool[index];
        source.clip = clip;
        // The static, not the field: it is the one that folds in the player's
        // Sound Effects setting, and reading it here means every pooled
        // one-shot picks that up without a second path to keep in step.
        source.volume = effect.Volume * MasterVolume;
        source.pitch = Random.Range(effect.PitchRange.x, effect.PitchRange.y);
        source.Play();

        poolGroups[index] = key;
        state.LastPlayTime = Time.unscaledTime;
        state.ActiveVoices++;
    }

    /// <summary>True if this play is far enough from the camera to skip — how a turtle harvesting on the far side of the island stays silent while the player is looking elsewhere. Always false when the effect doesn't set a distance, when no position was given, or when there's no camera to measure from.</summary>
    private bool IsOutOfEarshot(SoundEffect effect, Vector3? worldPosition)
    {
        if (effect.MaxAudibleDistance <= 0f || !worldPosition.HasValue) return false;

        // Re-fetched if the cached camera has gone (scene reload between runs
        // leaves the old reference destroyed, and Camera.main is a lookup worth
        // avoiding every single play).
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Vector2 delta = (Vector2)worldPosition.Value - (Vector2)cam.transform.position;
        return delta.sqrMagnitude > effect.MaxAudibleDistance * effect.MaxAudibleDistance;
    }

    private int FindFreeSource()
    {
        for (int i = 0; i < pool.Length; i++)
        {
            if (poolGroups[i] == null) return i;
        }

        return -1;
    }
}
