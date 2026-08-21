/// <summary>
/// Implemented by an improvement card that must not enter the draw pool until
/// the upgrade it improves has already been picked — the non-building
/// equivalent of IRequiresBuilding, for branches whose parent is a plain
/// upgrade rather than a placeable building (Crabs, Barnacles). Checked by
/// UpgradeSelectionUI.Show alongside the building gate.
///
/// Implementations answer from UpgradeManager's own run state (e.g. CrabCount
/// > 0, BarnaclesUnlocked) rather than from a serialized reference to the
/// parent card, so no prefab-to-prefab wiring is needed and a modifier-applied
/// parent (see GameModifierDefinition, which bypasses UpgradeSelectionUI
/// entirely) still opens its branch correctly.
/// </summary>
public interface IRequiresUpgrade
{
    /// <summary>True once this card's parent upgrade has been picked this run. False keeps it out of the draw pool entirely.</summary>
    bool IsPrerequisiteMet { get; }
}
