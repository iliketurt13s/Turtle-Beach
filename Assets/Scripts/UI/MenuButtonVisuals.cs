using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop onto any clickable UI GameObject alongside its own Button (for click
/// wiring) to swap between two button-face sprites — Unpressed while idle or
/// merely hovered, Pressed for as long as the pointer is both held down and
/// still over the button — and tint the graphic while the pointer is over
/// it, hovered or pressed alike. Unity's built-in Button Transition only
/// supports Sprite Swap OR Color Tint, never both at once, so this hooks the
/// pointer interfaces directly instead. Touches only this component's own
/// Image; like UIClickSquash, it sits alongside whatever already owns the
/// button's onClick without touching it.
///
/// IsActive/Refresh are protected/virtual so ExclusiveToggleButton can
/// subclass this instead of duplicating the sprite pair/hover tint fields
/// and hover-tracking logic — it just overrides IsActive to key the
/// "Pressed" sprite off a persistent selected flag instead of momentary
/// pointer-down.
///
/// The sprite pair is normally authored per-button in the Inspector, but
/// SetSprites can replace it at runtime for buttons cloned from one shared
/// prefab that each need their own face — see ModifierTileView, where every
/// tile comes from the same prefab but wears its own modifier's art.
///
/// SetInteractable adds a fourth visual state on top of idle/hovered/pressed:
/// a flat Disabled Tint that overrides the other two, for a button that's
/// present but unavailable — currently a modifier the player hasn't unlocked
/// yet. Deliberately not Unity's own Button.interactable, which is a separate
/// mechanism that would only grey the Button's own target graphic through the
/// transition this class exists to bypass. Making this the base class's job
/// means any button in the project can be disabled, not just tiles.
/// </summary>
[RequireComponent(typeof(Image))]
public class MenuButtonVisuals : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Sprite unpressedSprite;
    [SerializeField] private Sprite pressedSprite;
    [Tooltip("Multiplies the button's sprite color while the pointer is over it, and independently whenever IsActive is true (pressed-and-held for a plain button; persistently selected for ExclusiveToggleButton, staying tinted even after the pointer leaves, until deselected). The two sources work side by side rather than one overriding the other. White = no visible change.")]
    [SerializeField] private Color hoverTint = new Color(0.85f, 0.85f, 0.85f);
    [Tooltip("Flat color multiplied over the sprite while the button is non-interactable (see SetInteractable), overriding Hover Tint entirely. Set this to white if the disabled look is already baked into an overlay graphic and you don't want the art dimmed twice.")]
    [SerializeField] private Color disabledTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    private Image image;
    protected bool isPointerOver;
    private bool isPressed;
    private bool interactable = true;

    /// <summary>False while this button is present but unavailable — it shows Disabled Tint and never takes the pressed sprite.</summary>
    public bool Interactable => interactable;

    /// <summary>True while Pressed Sprite should show instead of Unpressed Sprite. Base behavior is "pointer currently held down over this button"; ExclusiveToggleButton overrides this to mean "currently selected" instead.</summary>
    protected virtual bool IsActive => isPressed && isPointerOver;

    protected virtual void Awake()
    {
        image = GetComponent<Image>();
    }

    protected virtual void OnEnable()
    {
        isPointerOver = false;
        isPressed = false;
        Refresh();
    }

    public virtual void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;
        Refresh();
    }

    public virtual void OnPointerExit(PointerEventData eventData)
    {
        // Dragging off while held shouldn't leave the button stuck showing
        // its pressed sprite/tint with no pointer over it to ever clear them.
        isPointerOver = false;
        isPressed = false;
        Refresh();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Enter/Exit deliberately still run while disabled — a locked modifier
        // tile should still describe itself in the side panel on hover. Only
        // the press look is suppressed, since nothing will come of the click.
        if (!interactable) return;

        isPressed = true;
        Refresh();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!interactable) return;

        isPressed = false;
        Refresh();
    }

    /// <summary>Turns the disabled look on or off. Survives OnEnable (which only clears the momentary pointer state), so a caller sets this once at bind time rather than reapplying it.</summary>
    public void SetInteractable(bool value)
    {
        interactable = value;
        Refresh();
    }

    /// <summary>Replaces this button's sprite pair at runtime, for buttons instantiated from a shared prefab that each need their own face. A null argument leaves that side of the pair on whatever the prefab authored, so a caller can override just one of the two — or supply neither and keep the prefab's generic look.</summary>
    public void SetSprites(Sprite unpressed, Sprite pressed)
    {
        if (unpressed != null) unpressedSprite = unpressed;
        if (pressed != null) pressedSprite = pressed;
        Refresh();
    }

    protected void Refresh()
    {
        // Resolved here as well as in Awake so a Refresh triggered by
        // SetSprites still lands if it somehow arrives before Awake has run.
        if (image == null) image = GetComponent<Image>();
        if (image == null) return;

        Sprite sprite = IsActive ? pressedSprite : unpressedSprite;
        if (sprite != null) image.sprite = sprite;

        // Checked before the hover/active tint rather than blended with it: a
        // disabled button reads the same however the pointer is behaving,
        // which is the whole point of the state.
        if (!interactable)
        {
            image.color = disabledTint;
            return;
        }

        image.color = (isPointerOver || IsActive) ? hoverTint : Color.white;
    }
}
