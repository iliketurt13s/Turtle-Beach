/// <summary>
/// Marker for an upgrade card that should only be offered once a specific
/// building has been unlocked — e.g. a card that improves the Campfire
/// shouldn't show up before the player can even place a Campfire yet.
/// UpgradeSelectionUI.Show filters these out of the draw pool until
/// BuildModeController reports RequiredBuilding as unlocked (see
/// BuildModeController.IsUnlocked). "Unlocked" means placeable, not
/// necessarily placed yet — the building's own BuildingUnlockUpgradeCard is
/// usually what "branches into" a card like this once picked.
/// </summary>
public interface IRequiresBuilding
{
    BuildableDefinition RequiredBuilding { get; }
}
