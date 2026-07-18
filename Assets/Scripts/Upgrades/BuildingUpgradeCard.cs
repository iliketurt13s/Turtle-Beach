using UnityEngine;

/// <summary>
/// Base for an upgrade card that only becomes eligible once a specific
/// building has been unlocked (see IRequiresBuilding, checked by
/// UpgradeSelectionUI.Show) — i.e. it "branches off" from that building's own
/// BuildingUnlockUpgradeCard rather than being offered from the very start.
/// Concrete subclasses just implement Apply(), same as any other
/// UpgradeCardDefinition; new building-branch upgrades are new prefabs with a
/// concrete subclass of this, not new picker code.
/// </summary>
public abstract class BuildingUpgradeCard : UpgradeCardDefinition, IRequiresBuilding
{
    [Tooltip("This card is only offered once this building has been unlocked.")]
    [SerializeField] private BuildableDefinition requiredBuilding;

    public BuildableDefinition RequiredBuilding => requiredBuilding;
}
