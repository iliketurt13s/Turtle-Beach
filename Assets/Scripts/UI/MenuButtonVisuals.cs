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
/// </summary>
[RequireComponent(typeof(Image))]
public class MenuButtonVisuals : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Sprite unpressedSprite;
    [SerializeField] private Sprite pressedSprite;
    [Tooltip("Multiplies the button's sprite color while the pointer is over it, and independently whenever IsActive is true (pressed-and-held for a plain button; persistently selected for ExclusiveToggleButton, staying tinted even after the pointer leaves, until deselected). The two sources work side by side rather than one overriding the other. White = no visible change.")]
    [SerializeField] private Color hoverTint = new Color(0.85f, 0.85f, 0.85f);

    private Image image;
    protected bool isPointerOver;
    private bool isPressed;

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
        isPressed = true;
        Refresh();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        Refresh();
    }

    protected void Refresh()
    {
        if (image == null) return;

        Sprite sprite = IsActive ? pressedSprite : unpressedSprite;
        if (sprite != null) image.sprite = sprite;

        image.color = (isPointerOver || IsActive) ? hoverTint : Color.white;
    }
}
