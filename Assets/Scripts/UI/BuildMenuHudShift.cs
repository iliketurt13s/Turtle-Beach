using UnityEngine;

/// <summary>
/// Moves a HUD element out of the build drawer's way by mirroring the drawer's
/// own movement exactly: however far the drawer has slid out at this instant,
/// this element is offset by the same amount, and it comes back the same way.
/// Drop one on each element that needs to get clear (the resource counters, the
/// time controls) — it's per-element rather than something the drawer pushes,
/// so adding another HUD piece later is one component, not an edit to the
/// drawer.
///
/// Mirroring beats running a parallel animation. There's no distance to keep in
/// step with the drawer's width, no duration or curve that can drift out of
/// sync with its slide, and resizing or re-siting the drawer moves the HUD by
/// the new amount automatically. It also handles direction for free: the offset
/// is whatever vector the drawer actually travels, so docking it on the other
/// edge shifts the HUD the other way with nothing to change here.
///
/// It writes NOTHING while the drawer is fully closed, which is what makes it
/// safe to sit alongside this element's own UISlidePanel (TimeControlUI has
/// one, to slide the controls in at scene start). Two components both writing
/// anchoredPosition otherwise fight, and the loser is whichever ran first: this
/// component used to capture its resting position in Awake, straight after a
/// sibling UISlidePanel had snapped the element off-screen, so "go back home"
/// meant "go back off-screen" and the HUD vanished with the drawer. Now the
/// resting position is read from that panel's authored home rather than from a
/// live transform value another component may already have overwritten, and
/// while the drawer is shut this component keeps its hands off entirely.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BuildMenuHudShift : MonoBehaviour
{
    [Tooltip("The build drawer's own UISlidePanel — the thing whose movement this element copies. Required; without it there's nothing to mirror and this element won't move at all.")]
    [SerializeField] private UISlidePanel drawerPanel;

    private RectTransform rect;
    /// <summary>This element's own slide panel, if it has one — its authored home is the resting position to offset from, since it owns this transform whenever the drawer isn't out.</summary>
    private UISlidePanel ownSlidePanel;
    private Vector2 capturedHome;
    private bool wasDisplaced;

    /// <summary>Where this element sits with the drawer shut. Taken from a sibling UISlidePanel when there is one, because that component overwrites anchoredPosition in its own Awake and the value left behind is off-screen, not the authored layout position.</summary>
    private Vector2 RestPosition => ownSlidePanel != null ? ownSlidePanel.HomePosition : capturedHome;

    private void Awake()
    {
        rect = (RectTransform)transform;
        ownSlidePanel = GetComponent<UISlidePanel>();
        capturedHome = rect.anchoredPosition;

        if (drawerPanel == null)
        {
            Debug.LogWarning($"{name}: BuildMenuHudShift has no Drawer Panel assigned, so it has nothing to mirror and this element will never move out of the build drawer's way. Drag the build panel's UISlidePanel into that field.", this);
        }
    }

    /// <summary>
    /// LateUpdate, not Update: UISlidePanel animates from a coroutine, which
    /// Unity runs after every Update and before LateUpdate. Reading the
    /// drawer's position here gets the value it holds THIS frame rather than
    /// last frame's, so the HUD tracks the slide with no lag — and this
    /// element's own slide panel, if it has one, has already written its
    /// position by the time this adds the offset on top.
    /// </summary>
    private void LateUpdate()
    {
        if (drawerPanel == null) return;

        Vector2 displacement = drawerPanel.CurrentDisplacement;
        bool isDisplaced = displacement.sqrMagnitude > 0.000001f;

        // Hands off once the drawer is shut AND this has already put the
        // element back — that final write lands it exactly on the rest position
        // rather than a hair off it, and from then on the transform belongs to
        // whatever else wants it (the element's own intro slide, a layout, the
        // Inspector).
        if (!isDisplaced && !wasDisplaced) return;

        rect.anchoredPosition = RestPosition + displacement;
        wasDisplaced = isDisplaced;
    }
}
