using UnityEngine;

/// <summary>
/// One authored sound event — a small bundle of clip variations plus how to
/// play them — serialized inline on whatever component owns the event (see
/// ResourceNode's harvest hit, TurtleLocomotion's sand push). NOT a
/// MonoBehaviour or an asset: it's a plain [System.Serializable] class, so
/// adding a sound to something is one field on that component rather than a
/// new object to wire up, and every sound's tuning sits next to the thing that
/// makes it.
///
/// Playing is delegated to AudioManager, which owns the AudioSource pool and
/// the throttling — nothing here creates or holds an AudioSource, so a sound
/// can be authored on a prefab that gets destroyed mid-playback (a depleting
/// node, a dying turtle) without cutting itself off. If no AudioManager exists
/// in the scene at all, plays silently no-op, which is what keeps the Menu
/// scene's ambience turtles from needing one.
///
/// Three things stop a repeated event from turning into noise, all tunable per
/// event because a harvest impact and a fin stroke want very different values:
/// random clip choice, random pitch, and (in AudioManager) a shared throttle —
/// see Min Interval and Max Voices, whose budget is shared by every object
/// drawing from the same clips, not per object.
/// </summary>
[System.Serializable]
public class SoundEffect
{
    [Tooltip("Clip variations. One is picked at random per play, so a repeated event (chopping a tree, stroke after stroke of a fin) doesn't replay the identical waveform every time. A single clip is fine — it just leans on the pitch jitter below instead. Empty = this sound is silent, which is a valid way to switch one off.")]
    [SerializeField] private AudioClip[] clips;

    [Tooltip("Playback volume, before AudioManager's own Master Volume.")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("Random pitch range per play (x = min, y = max). 1 is the clip's authored pitch. A little spread is what keeps a rapidly-repeating sound from reading as a machine gun; set both to 1 to disable.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.94f, 1.06f);

    [Header("Throttling")]
    [Tooltip("Minimum real seconds between two plays of this sound ANYWHERE in the scene — the budget is shared by every object using the same clips, so twelve turtles harvesting at once still can't play it faster than this. Real seconds, not scaled: at 2x game speed events fire twice as often, and this is what stops that from doubling the noise too. 0 disables.")]
    [SerializeField, Min(0f)] private float minInterval = 0.06f;

    [Tooltip("How many copies of this sound may overlap at once, again shared scene-wide. Past this, new plays are dropped rather than queued — a 13th simultaneous impact adds nothing but mud. Keep low for busy events.")]
    [SerializeField, Min(1)] private int maxVoices = 4;

    [Tooltip("Skip plays further than this from the camera, in world units — so a turtle harvesting off-screen on the far side of the island stays silent. 0 = always audible regardless of distance. Only meaningful for plays that pass a position.")]
    [SerializeField, Min(0f)] private float maxAudibleDistance;

    /// <summary>Cached first non-null clip, this sound's identity for throttling — see GroupKey.</summary>
    private AudioClip groupKey;
    private bool groupKeyResolved;

    public float Volume => volume;
    public Vector2 PitchRange => pitchRange;
    public float MinInterval => minInterval;
    public int MaxVoices => maxVoices;
    public float MaxAudibleDistance => maxAudibleDistance;

    /// <summary>True if there's anything to play at all.</summary>
    public bool HasClips => PickClip() != null;

    /// <summary>
    /// What AudioManager throttles this sound BY: the first clip in the list.
    ///
    /// Every prefab clone carries its own copy of this SoundEffect, so the
    /// object itself can't be the identity — throttling by that would give each
    /// of twenty trees its own private budget and defeat the point. The clips,
    /// though, are shared project assets: every tree references the same
    /// AudioClip, so keying on one gives exactly the intended "all trees share
    /// one budget, trees and rocks are budgeted separately" grouping, with no
    /// extra id field to author and keep consistent.
    /// </summary>
    public AudioClip GroupKey
    {
        get
        {
            if (groupKeyResolved) return groupKey;

            groupKeyResolved = true;
            if (clips != null)
            {
                foreach (AudioClip clip in clips)
                {
                    if (clip == null) continue;

                    groupKey = clip;
                    break;
                }
            }

            return groupKey;
        }
    }

    /// <summary>A random one of the assigned clips, skipping empty slots (an array with a hole in it is a very easy Inspector mistake, and silently playing nothing is hard to diagnose). Null if none are assigned.</summary>
    public AudioClip PickClip()
    {
        if (clips == null || clips.Length == 0) return null;

        // Fast path for the overwhelmingly common fully-populated case: one
        // random index, no scanning.
        AudioClip candidate = clips[Random.Range(0, clips.Length)];
        if (candidate != null) return candidate;

        // Something in the array is empty — fall back to picking among only the
        // clips that actually exist, so a hole doesn't cause silent plays.
        int populated = 0;
        foreach (AudioClip clip in clips)
        {
            if (clip != null) populated++;
        }

        if (populated == 0) return null;

        int target = Random.Range(0, populated);
        foreach (AudioClip clip in clips)
        {
            if (clip == null) continue;
            if (target-- == 0) return clip;
        }

        return null;
    }

    /// <summary>Plays this sound at a world position — used for distance culling only (playback itself is 2D, see AudioManager). No-op if there's no AudioManager, no clips, or the throttle rejects it.</summary>
    public void Play(Vector3 worldPosition) => AudioManager.Play(this, worldPosition);

    /// <summary>Plays this sound with no position, so Max Audible Distance never culls it — for UI and other non-diegetic sounds.</summary>
    public void Play() => AudioManager.Play(this, null);
}
