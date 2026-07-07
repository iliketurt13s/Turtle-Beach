using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the small carried-resource sprites shown on a turtle's shell while
/// it's ferrying a load back to the nest — one per carried unit, appearing as
/// harvest hits land and clearing (in the same order) as each is delivered.
/// Slots are plain child transforms the user positions on the shell in the
/// Editor; each is expected to carry its own SpriteRenderer (on itself or a
/// child), resolved once here rather than requiring a second parallel list
/// the user would have to keep in sync. Each resource type can have several
/// sprite variants (same idea as RandomSpriteRenderer elsewhere in this
/// project) — GetRandomIcon rolls one once per newly-carried unit, and
/// TurtleAgent reuses that same result for the shell slot, the harvest-pop
/// effect, and the delivery-fly effect, so a given unit looks consistent
/// across its whole trip rather than re-rolling at each step.
/// </summary>
public class CarriedResourceVisuals : MonoBehaviour
{
    [Serializable]
    private struct ResourceIcon
    {
        public ResourceManager.ResourceType type;
        [Tooltip("Sprite variants for this resource type — one is picked at random per carried unit.")]
        public Sprite[] icons;
    }

    [Tooltip("Sprite variants shown for each resource type, both on the shell slots and on the pop effects.")]
    [SerializeField] private List<ResourceIcon> resourceIcons;
    [Tooltip("Empty child transforms positioned on the shell, in fill/delivery order. Each should have a SpriteRenderer on itself or a child.")]
    [SerializeField] private List<Transform> slots;

    private SpriteRenderer[] slotRenderers;
    private Queue<int> freeSlotIndices;

    private void Awake()
    {
        slotRenderers = new SpriteRenderer[slots.Count];
        freeSlotIndices = new Queue<int>();
        for (int i = 0; i < slots.Count; i++)
        {
            slotRenderers[i] = slots[i] != null ? slots[i].GetComponentInChildren<SpriteRenderer>() : null;
            if (slotRenderers[i] != null) slotRenderers[i].enabled = false;
            freeSlotIndices.Enqueue(i);
        }
    }

    /// <summary>Lights up the next free shell slot with icon (already resolved via GetRandomIcon, so the same variant shows here and on the effects for this unit) and returns the slot index used, so the caller (TurtleAgent) can clear the exact right slot later regardless of which of its two carry lists (nest-bound vs food-bound) this unit belongs to — both draw from this same pool. Returns -1 and shows nothing if every authored slot is already occupied (a carry-capacity-overflow edge case — the unit is still tracked/delivered, just not visually represented).</summary>
    public int ShowNext(Sprite icon)
    {
        if (freeSlotIndices.Count == 0) return -1;

        int index = freeSlotIndices.Dequeue();
        SpriteRenderer sr = slotRenderers[index];
        if (sr != null)
        {
            sr.sprite = icon;
            sr.enabled = true;
        }

        return index;
    }

    /// <summary>Hides the slot at index and returns it to the free pool. Called as each carried unit pops off, in whichever order its own carry list delivers.</summary>
    public void ClearSlot(int index)
    {
        if (index < 0 || index >= slotRenderers.Length) return;
        if (slotRenderers[index] != null) slotRenderers[index].enabled = false;
        freeSlotIndices.Enqueue(index);
    }

    /// <summary>Hides every slot and rebuilds the full free pool. Only correct when every carried unit everywhere (both carry lists) is gone — a partial delivery must clear its own slots individually via ClearSlot instead.</summary>
    public void ClearAll()
    {
        freeSlotIndices.Clear();
        for (int i = 0; i < slotRenderers.Length; i++)
        {
            if (slotRenderers[i] != null) slotRenderers[i].enabled = false;
            freeSlotIndices.Enqueue(i);
        }
    }

    /// <summary>World position a delivery pop-effect for the unit at index should launch from.</summary>
    public Vector3 GetSlotWorldPosition(int index)
    {
        if (index < 0 || index >= slots.Count || slots[index] == null) return transform.position;
        return slots[index].position;
    }

    /// <summary>Picks one random sprite variant for type. Call once per newly-carried unit and reuse the result for that unit's whole trip (shell slot, harvest-pop, delivery-fly) rather than calling this again later.</summary>
    public Sprite GetRandomIcon(ResourceManager.ResourceType type)
    {
        foreach (ResourceIcon entry in resourceIcons)
        {
            if (entry.type != type) continue;
            if (entry.icons == null || entry.icons.Length == 0) return null;

            return entry.icons[UnityEngine.Random.Range(0, entry.icons.Length)];
        }

        return null;
    }
}
