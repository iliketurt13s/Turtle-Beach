using UnityEngine;

/// <summary>
/// Crab-branch upgrade card: only offered once at least one crab has been
/// recruited (see IRequiresUpgrade), and raises how much every crab can carry
/// before it has to run its load back to the nest. Entirely separate from the
/// turtle carry-capacity card — crabs are their own unit (see CrabAgent), so
/// neither card touches the other's units.
/// </summary>
public class CrabCarryCapacityUpgradeCard : UpgradeCardDefinition, IRequiresUpgrade
{
    [SerializeField, Min(1)] private int capacityAdded = 2;

    public bool IsPrerequisiteMet => UpgradeManager.Instance != null && UpgradeManager.Instance.CrabCount > 0;

    public override void Apply() => UpgradeManager.Instance?.AddCrabCarryCapacity(capacityAdded);
}
