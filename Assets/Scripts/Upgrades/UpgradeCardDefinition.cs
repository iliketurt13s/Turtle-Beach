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

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public string Description => description;
    public bool Stackable => stackable;

    /// <summary>Applies this card's effect. Called once, the moment the player selects it.</summary>
    public abstract void Apply();
}
