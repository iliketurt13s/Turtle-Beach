using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single on-screen arrow that hovers beside whatever the current tutorial
/// step wants the player to interact with, and points at it. One of these lives
/// in the scene and is re-aimed step by step by TutorialManager; there is never
/// more than one, so the player is never asked to look at two things at once.
///
/// It tracks a PICKER rather than a fixed Transform, which is what lets a step
/// say "the nearest tree" instead of "that tree" — but it asks that picker only
/// until it gets an answer, and then never again. Whatever it first latches
/// onto is what it points at for the rest of the step, full stop: a target is
/// chosen once and does not change, even if that tree is chopped out or a
/// closer one appears.
///
/// That permanence is deliberate. "Nearest" is measured from a turtle that is
/// itself walking toward the target, so anything re-evaluated per frame
/// flickers between two similarly-distant candidates as the turtle moves, and
/// an arrow that keeps changing its mind is worse than no arrow at all. The
/// player is sent to one specific thing and it stays that thing.
///
/// The picker IS re-asked while it is still returning null, since that only
/// means the thing to point at doesn't exist yet — a build-drawer row that is
/// instantiated a moment after the drawer opens, an upgrade card that hasn't
/// been dealt. Once it returns something, that answer is final. A target that
/// is later destroyed simply fades the arrow out; nothing replaces it.
///
/// World targets and UI targets both work through that one provider, and the
/// difference is detected rather than declared: a RectTransform IS a Transform,
/// so the caller passes either and this resolves each to a screen point the
/// right way. That matters because the tutorial has to point at turtles and
/// rocks (world) as well as the hammer tab and an upgrade card (UI).
///
/// The screen-point maths is the first of its kind in this project, so the two
/// traps are worth naming. The gameplay Canvas is Screen Space OVERLAY, which
/// means (a) a UI element's transform.position is ALREADY in screen pixels — it
/// must not be run through Camera.WorldToScreenPoint the way a world object is
/// — and (b) every RectTransformUtility call here takes null for its camera
/// argument, not Camera.main. Passing the camera to an Overlay canvas is the
/// classic version of this bug and puts everything in roughly the right place
/// while being subtly wrong at the edges.
///
/// Like TutorialMentor, this clears Raycast Target on itself from code (an
/// arrow parked over a card the player is being told to click would otherwise
/// silently eat that click — see TurtleSelectionController's and
/// BuildModeController's EventSystem guards) and hides itself while the pause
/// menu or game-over panel is up.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TutorialArrow : MonoBehaviour
{
    [Tooltip("The rect the arrow is positioned inside — normally the full-screen Tutorial object this sits under. MUST be stretched to cover the whole screen with zero offsets, since off-screen targets are clamped against its edges. Leave unassigned to use this object's own parent.")]
    [SerializeField] private RectTransform positioningSpace;

    [Header("Placement")]
    [Tooltip("How far from the target the arrow sits, along the approach direction the step asks for. Tune to comfortably clear the arrow's own length so the tip doesn't overlap what it's pointing at.")]
    [SerializeField] private float offsetDistance = 90f;
    [Tooltip("Which way the arrow's own sprite points when its rotation is zero, in degrees counter-clockwise from screen-right. 90 = the sprite is drawn pointing UP (the usual), 0 = right, 180 = left, 270 = down.")]
    [SerializeField] private float spriteAngleOffset = 90f;
    [Tooltip("Keeps the arrow this far inside the screen edge when its target is off-screen — the arrow pins to the border and points outward toward it, rather than sliding off with it and leaving the player with no hint at all.")]
    [SerializeField] private float edgeMargin = 60f;

    [Header("Motion")]
    [Tooltip("How far the arrow nudges toward its target and back, to draw the eye. 0 to hold still.")]
    [SerializeField] private float bobAmplitude = 14f;
    [Tooltip("Seconds for one full nudge in and out.")]
    [SerializeField] private float bobPeriod = 0.9f;
    [Tooltip("Seconds the arrow takes to fade in when it gets a target and out when it loses one. Unscaled, so it still plays while the game is paused.")]
    [SerializeField] private float fadeDuration = 0.2f;

    private RectTransform rect;
    private CanvasGroup group;
    private Func<Transform> picker;
    private Transform held;
    private Vector2 approachDirection = Vector2.up;
    private float bobTime;

    private void Awake()
    {
        rect = (RectTransform)transform;
        if (positioningSpace == null) positioningSpace = rect.parent as RectTransform;

        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // See the class doc comment — this is the failure with no visible cause.
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    /// <summary>
    /// Starts pointing at whatever picker first chooses, and then keeps
    /// pointing at that same target for good — see the class doc comment for
    /// why the choice is permanent. Calling Track again is the only thing that
    /// makes a new choice, which is what gives each step a fresh target.
    ///
    /// approachDirection is the side the arrow sits on, in screen terms:
    /// Vector2.up parks it above the target pointing down (the default read for
    /// a world object), Vector2.right parks it to the target's right pointing
    /// left, which is what a step aimed at something already hard against a
    /// screen edge — like the build drawer's hammer tab — needs.
    /// </summary>
    public void Track(Func<Transform> picker, Vector2 approachDirection)
    {
        this.picker = picker;

        // Dropped so a new step always makes a fresh choice rather than
        // inheriting the last step's target.
        held = null;

        // Guarded so a caller passing Vector2.zero by accident doesn't leave the
        // arrow sitting exactly on top of its target with an undefined rotation.
        this.approachDirection = approachDirection.sqrMagnitude > 0.0001f ? approachDirection.normalized : Vector2.up;
    }

    /// <summary>Stops pointing at anything and fades the arrow away.</summary>
    public void Clear()
    {
        picker = null;
        held = null;
    }

    /// <summary>The committed target. The picker is consulted only while nothing has been chosen yet — once it hands back a target, that choice is final for this step.</summary>
    private Transform ResolveTarget()
    {
        if (held != null) return held;

        held = picker?.Invoke();
        return held;
    }

    private void LateUpdate()
    {
        // LateUpdate so the arrow reads positions everything else has already
        // finished writing this frame — a turtle that moved in Update, a build
        // drawer row mid-slide — instead of trailing them by a frame.
        Vector2 targetLocal = Vector2.zero;
        bool visible = positioningSpace != null && TryResolve(out targetLocal);

        float targetAlpha = visible && !GameOverUI.IsPaused && !GameOverUI.IsRunOver && !SettingsMenuUI.IsOpen ? 1f : 0f;
        group.alpha = fadeDuration > 0f
            ? Mathf.MoveTowards(group.alpha, targetAlpha, Time.unscaledDeltaTime / fadeDuration)
            : targetAlpha;

        if (!visible) return;

        Rect area = positioningSpace.rect;
        Rect inside = Rect.MinMaxRect(
            area.xMin + edgeMargin, area.yMin + edgeMargin,
            area.xMax - edgeMargin, area.yMax - edgeMargin);

        Vector2 anchor;
        Vector2 pointDirection;

        if (inside.width > 0f && inside.height > 0f && !inside.Contains(targetLocal))
        {
            // Target is off-screen (the player panned away from the nearest
            // tree, say). Pin to the border and point outward at it, so the
            // arrow still says which way to look instead of vanishing.
            anchor = new Vector2(
                Mathf.Clamp(targetLocal.x, inside.xMin, inside.xMax),
                Mathf.Clamp(targetLocal.y, inside.yMin, inside.yMax));
            Vector2 outward = targetLocal - anchor;
            pointDirection = outward.sqrMagnitude > 0.0001f ? outward.normalized : -approachDirection;
        }
        else
        {
            anchor = targetLocal + approachDirection * offsetDistance;
            pointDirection = -approachDirection;
        }

        bobTime += Time.unscaledDeltaTime;
        float bob = bobPeriod > 0f ? Mathf.Sin(bobTime / bobPeriod * Mathf.PI * 2f) * bobAmplitude : 0f;

        // Bobs along the direction it points, so the nudge reads as the arrow
        // jabbing at its target rather than drifting sideways past it.
        rect.anchoredPosition = anchor + pointDirection * bob;

        float angle = Mathf.Atan2(pointDirection.y, pointDirection.x) * Mathf.Rad2Deg;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle - spriteAngleOffset);
    }

    /// <summary>Resolves the current target to a point in Positioning Space's local coordinates, or false if there is nothing to point at right now.</summary>
    private bool TryResolve(out Vector2 localPoint)
    {
        localPoint = Vector2.zero;

        Transform target = ResolveTarget();
        if (target == null) return false;

        Vector2 screenPoint;
        if (target is RectTransform uiTarget)
        {
            // Already screen-space on an Overlay canvas — see the class doc
            // comment. Running this through Camera.WorldToScreenPoint would be
            // squaring a conversion that has already happened.
            screenPoint = RectTransformUtility.WorldToScreenPoint(null, uiTarget.position);
        }
        else
        {
            Camera cam = Camera.main;
            if (cam == null) return false;

            screenPoint = cam.WorldToScreenPoint(target.position);
        }

        // null camera, not Camera.main: this canvas is Screen Space Overlay.
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(positioningSpace, screenPoint, null, out localPoint);
    }
}
