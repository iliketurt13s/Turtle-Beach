using UnityEngine;

/// <summary>
/// Optional override for HoverTargetOutline. Add this alongside a
/// ResourceNode/Coconut/JellyfishAgent/TrashHealth/interactable BuildingHealth
/// only when the sprite it should outline isn't the one
/// GetComponentInChildren&lt;SpriteRenderer&gt;() would find on its own — e.g. a
/// decorative base layer sits earlier in the hierarchy than the actual
/// variant art, or the right one starts out disabled and would otherwise be
/// skipped. Drag in whichever SpriteRenderer should be outlined instead. Left
/// off entirely (the common case), HoverTargetOutline just falls back to its
/// normal lookup, so most objects need nothing added at all.
/// </summary>
public class HoverOutlineSpriteSource : MonoBehaviour
{
    [Tooltip("The SpriteRenderer HoverTargetOutline should outline for this object. Leave unassigned to have HoverTargetOutline fall back to its default GetComponentInChildren<SpriteRenderer>() lookup.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    public SpriteRenderer SpriteRenderer => spriteRenderer;
}
