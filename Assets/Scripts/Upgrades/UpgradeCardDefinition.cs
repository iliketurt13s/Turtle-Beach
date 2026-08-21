using UnityEngine;

/// <summary>
/// Attach directly to an upgrade card prefab to describe its display name,
/// icon, description, and whether it can be picked more than once. Attached
/// to UpgradeSelectionUI's Upgrade Pool array, so new upgrades are just new
/// prefabs with a concrete subclass of this component, not new picker code.
/// </summary>
public abstract class UpgradeCardDefinition : MonoBehaviour
{
    [SerializeField] private string displayName = "Upgrade";
    [SerializeField] private Sprite icon;
    [SerializeField, TextArea] private string description = "Description";
    [Tooltip("If false, this card is removed from the draw pool for the rest of the run once picked. If true, it can be picked repeatedly and stacks.")]
    [SerializeField] private bool stackable = true;
    [Tooltip("Tick for a plain numeric boost to an existing stat (turtle speed, crit chance, wall health...) as opposed to a card that unlocks or changes something. Off by default. Ticked cards are drawn HALF as often as everything else — see UpgradeSelectionUI's Base Stat Draw Weight.")]
    [SerializeField] private bool baseStatUpgrade;

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public string Description => description;
    public bool Stackable => stackable;

    /// <summary>
    /// True for cards that only move a number the player already has. Purely a
    /// draw-rate hint — nothing about the card's effect changes, it's just
    /// weighted down in UpgradeSelectionUI's draw so the pool doesn't fill up
    /// with stat bumps at the expense of the cards that actually unlock things.
    /// Deliberately defaults to false so every existing prefab keeps its current
    /// odds until the flag is ticked by hand.
    /// </summary>
    public bool IsBaseStatUpgrade => baseStatUpgrade;

    /// <summary>Applies this card's effect. Called once, the moment the player selects it.</summary>
    public abstract void Apply();
}
