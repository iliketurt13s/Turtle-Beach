using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Drop onto any clickable UI GameObject alongside a SquashAndStretch to get
/// click-feedback juice with zero changes to whatever script already owns
/// that button's click logic. Deliberately hooks IPointerClickHandler rather
/// than Button.onClick — UpgradeCardView.Bind() calls
/// selectButton.onClick.RemoveAllListeners() on every card rebind, which
/// would silently strip a listener added that way. The EventSystem invokes
/// every IPointerClickHandler on a GameObject independently, so this sits
/// alongside existing click handling without any risk of interference.
/// </summary>
[RequireComponent(typeof(SquashAndStretch))]
public class UIClickSquash : MonoBehaviour, IPointerClickHandler
{
    private SquashAndStretch squashAndStretch;

    private void Awake()
    {
        squashAndStretch = GetComponent<SquashAndStretch>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        squashAndStretch.Play();
    }
}
