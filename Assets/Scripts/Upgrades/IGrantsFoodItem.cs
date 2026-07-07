/// <summary>
/// Marker for an upgrade card that introduces a food item (Seaweed, Coconut,
/// future ones). Picking one of these for the first time, before a Food
/// Building exists, forces the player to place one immediately — see
/// UpgradeSelectionUI.Select and BuildModeController.EnsureFoodBuildingPlaced.
/// </summary>
public interface IGrantsFoodItem
{
}
