using UnityEngine;

/// <summary>
/// The music bed: one looping soundtrack per scene, kept separate from every
/// other sound in the game so the settings screen's Music Volume slider has
/// something of its own to control.
///
/// Deliberately does NOT go through AudioManager's pool, for the same reason
/// AmbienceController doesn't (see there): that pool hands out a voice and
/// takes it back when the clip finishes, so a track that runs for minutes would
/// hold one hostage. This owns a single dedicated AudioSource, created here
/// rather than authored, so there is nothing to wire and no way to leave one
/// accidentally set to Play On Awake.
///
/// The volume split is the point of the class: everything else in the game
/// (pooled one-shots, and the beach/rain beds that read
/// AudioManager.MasterVolume) answers to Sfx Volume, while this alone answers
/// to Music Volume. Both are read live, every frame, so dragging a slider is
/// audible while it is being dragged rather than on the next track.
///
/// Several tracks can be listed; they play one after another with a gap
/// between, shuffled by default. A single clip is simply looped instead, with
/// no gap — the common case needs no configuring.
///
/// Everything here runs on unscaled time. Music that stopped between tracks
/// because the player paused, or hurried along at TimeControlUI's 2x, would be
/// following the wrong clock — audio playback itself already ignores
/// Time.timeScale, and the gap timer should match it.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    [Tooltip("Tracks to play, in order (or shuffled — see Shuffle). One clip is simply looped. Leave empty for no music at all, which is a silent no-op rather than an error.")]
    [SerializeField] private AudioClip[] tracks;

    [Tooltip("Pick the next track at random instead of walking the list in order. Never repeats the track that just played, as long as there are at least two.")]
    [SerializeField] private bool shuffle = true;

    [Tooltip("Volume of the music before the player's Music Volume setting is applied — the authoring knob for how the soundtrack sits against the rest of the mix, the way AudioManager's Master Volume is for effects.")]
    [SerializeField, Range(0f, 1f)] private float trackVolume = 1f;

    [Tooltip("Seconds of silence between tracks. Ignored when there is only one track, which loops seamlessly instead.")]
    [SerializeField, Min(0f)] private float gapBetweenTracks = 2f;

    [Tooltip("Seconds the volume takes to travel the full 0-1 range. Covers the fade in at scene start AND how quickly the music follows the settings slider, so it glides to a new level rather than jumping.")]
    [SerializeField, Min(0f)] private float fadeDuration = 1.5f;

    private AudioSource source;
    private int trackIndex = -1;
    private float gapRemaining;

    private void Awake()
    {
        if (tracks == null || tracks.Length == 0) return;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        // 2D for the same reason AudioManager's pool is (see there): in a
        // zoomable top-down game real falloff would swell and fade with the
        // zoom rather than with anything in the world — and music has no
        // position in the world to begin with.
        source.spatialBlend = 0f;
        // Starts silent whatever the setting says, so the first track fades up
        // rather than slamming in on the frame the scene loads.
        source.volume = 0f;
        // A lone track loops rather than being restarted by Update's gap
        // handling, so it has no seam in it.
        source.loop = tracks.Length == 1;

        PlayNextTrack();
    }

    private void Update()
    {
        if (source == null) return;

        FadeToward(trackVolume * GameSettings.MusicVolume);

        // loop is on for a single track, so this branch only ever runs for a
        // real playlist.
        if (source.isPlaying || source.loop) return;

        gapRemaining -= Time.unscaledDeltaTime;
        if (gapRemaining <= 0f) PlayNextTrack();
    }

    /// <summary>Walks the volume toward target at a rate set by Fade Duration. MoveTowards rather than a coroutine, matching AmbienceController: a setting changed mid-fade simply reverses from wherever the fade got to, with no routine to cancel and no chance of two fades fighting over one volume.</summary>
    private void FadeToward(float target)
    {
        if (fadeDuration <= 0f)
        {
            source.volume = target;
            return;
        }

        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime / fadeDuration);
    }

    private void PlayNextTrack()
    {
        AudioClip clip = PickNextClip();
        if (clip == null)
        {
            // Every slot in the array is empty. Stop rather than retrying after
            // every gap forever — there is nothing here that a later frame
            // could make playable.
            Debug.LogWarning($"{name}: MusicPlayer has tracks assigned but every slot is empty, so there is no music to play.", this);
            enabled = false;
            return;
        }

        source.clip = clip;
        source.Play();

        // Set at the START of a track rather than when it ends: Update leaves
        // this alone for as long as the source is playing, so by the time it
        // starts counting down it is already loaded with the full gap.
        gapRemaining = gapBetweenTracks;
    }

    /// <summary>Next clip in the list, or a random one that isn't the clip just played. Steps past empty slots, so a hole left in the array costs nothing rather than playing a gap's worth of silence.</summary>
    private AudioClip PickNextClip()
    {
        if (tracks.Length == 1) return tracks[0];

        int next = shuffle ? PickShuffledIndex() : (trackIndex + 1) % tracks.Length;

        for (int attempt = 0; attempt < tracks.Length; attempt++)
        {
            if (tracks[next] != null)
            {
                trackIndex = next;
                return tracks[next];
            }

            // Walks on from the pick rather than drawing again, so a list that
            // is mostly holes still finds its one real track in a single pass.
            next = (next + 1) % tracks.Length;
        }

        return null;
    }

    private int PickShuffledIndex()
    {
        // Nothing has played yet — every track is equally fair game.
        if (trackIndex < 0) return Random.Range(0, tracks.Length);

        // Draws from the OTHER tracks and steps past the current one, so no
        // track can immediately repeat while every other one stays equally
        // likely — a draw-and-retry loop would be neither.
        int offset = Random.Range(0, tracks.Length - 1);
        return (trackIndex + 1 + offset) % tracks.Length;
    }
}
