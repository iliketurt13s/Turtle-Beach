using UnityEngine;

/// <summary>
/// Shakes a UI RectTransform with a small random offset while Play() is in
/// effect, and snaps it exactly back to rest on Stop(). Used for the tutorial
/// mentor's "talking" wobble, driven off TypewriterText.IsTyping so the shake
/// and the speech are literally the same state (see TutorialMentor).
///
/// Deliberately not UIBobAnimator: that one is a smooth, continuous sine bob
/// with no start/stop, which reads as floating rather than speaking. This
/// re-rolls a fresh offset Frequency times a second and holds it in between, so
/// it stutters like a voice instead of gliding.
///
/// IMPORTANT: this writes anchoredPosition, and so does UISlidePanel. Two
/// components writing one anchoredPosition fight, and the loser is whichever
/// ran first (see BuildMenuHudShift's doc comment, which is the canonical
/// write-up of that hazard in this project). Put this on a CHILD of the sliding
/// object, never on the sliding object itself.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TutorialJitter : MonoBehaviour
{
    [Tooltip("How far the shake displaces this element from rest, in this RectTransform's own units (typically pixels). A few px is plenty — this is a speech wobble, not an earthquake.")]
    [SerializeField] private float amplitude = 6f;
    [Tooltip("How many times a second a fresh random offset is rolled. The offset is HELD between rolls, which is what makes this stutter rather than glide; higher is buzzier, lower is choppier.")]
    [SerializeField] private float frequency = 18f;

    private RectTransform rect;
    private Vector2 restPosition;
    private bool restCaptured;
    private float rollTimer;

    /// <summary>True while the shake is running. An owner can drive this straight from another component's state (e.g. TypewriterText.IsTyping) without tracking a duplicate flag.</summary>
    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        CaptureRest();
    }

    private void CaptureRest()
    {
        // Captured on demand rather than only in Awake, for the same reason
        // UISlidePanel.CaptureHome is: Play() is reachable from another
        // component's Awake, which may run first.
        if (rect == null) rect = (RectTransform)transform;
        if (restCaptured) return;

        restCaptured = true;
        restPosition = rect.anchoredPosition;
    }

    /// <summary>Starts shaking. Safe to call repeatedly while already running — it just keeps going rather than re-capturing rest from an already-displaced position, which would make the element drift away over successive calls.</summary>
    public void Play()
    {
        CaptureRest();
        if (IsPlaying) return;

        IsPlaying = true;
        // Zero so the first Update rolls immediately rather than holding rest
        // for up to a full interval — the shake has to start on the same frame
        // the first character appears.
        rollTimer = 0f;
    }

    /// <summary>Stops shaking and puts the element back exactly where it started, so a shake can never leave a permanent offset behind.</summary>
    public void Stop()
    {
        if (!IsPlaying) return;

        IsPlaying = false;
        CaptureRest();
        rect.anchoredPosition = restPosition;
    }

    private void OnDisable()
    {
        // A shake interrupted by deactivation would otherwise leave the element
        // parked at whatever random offset it was on when it went away.
        Stop();
    }

    private void Update()
    {
        if (!IsPlaying) return;

        // Unscaled throughout, matching the rest of this project's UI juice —
        // the tutorial can be up while TimeControlUI has the game paused.
        rollTimer -= Time.unscaledDeltaTime;
        if (rollTimer > 0f) return;

        rollTimer += frequency > 0f ? 1f / frequency : float.MaxValue;
        rect.anchoredPosition = restPosition + Random.insideUnitCircle * amplitude;
    }
}
