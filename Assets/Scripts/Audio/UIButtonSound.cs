using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Drop onto any clickable UI element to give it the game's shared button
/// click. Carries no clip and no settings of its own — the sound is authored
/// once on AudioManager (see its Button Click Sound) and every button borrows
/// it, so re-tuning the click is one edit rather than one per button, and a
/// newly-added button can't end up silent because someone forgot to drag a
/// clip onto it.
///
/// Implemented as IPointerClickHandler rather than by subscribing to
/// Button.onClick, so it also works on things that take clicks without being a
/// Button (an Image acting as a hit target, an upgrade card's icon — see
/// UIClickSquash, which hooks the same event for the same reason and is this
/// component's visual counterpart).
///
/// Fires on click regardless of what the click then does. That includes a
/// button that turns out to be non-interactable: the EventSystem doesn't route
/// clicks to a disabled Selectable at all, so those never reach here in the
/// first place and there's nothing to guard against.
/// </summary>
public class UIButtonSound : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        AudioManager.PlayButtonClick();
    }
}
