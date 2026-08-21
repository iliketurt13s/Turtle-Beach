using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Algae is unlocked (see
/// BuildingUpgradeCard/IRequiresBuilding), and makes the algae speed buff
/// outlast standing on the pile — a turtle that steps off keeps it for a few
/// more seconds instead of losing it instantly. Applies to already-placed
/// piles and future ones alike, since TurtleAgent reads
/// UpgradeManager.AlgaeLingerDuration live at the moment it would drop the
/// buff rather than the pile caching anything.
/// </summary>
public class AlgaeLingerUpgradeCard : BuildingUpgradeCard
{
    [Tooltip("Seconds added to how long the algae buff lingers after a turtle steps off. Stacks with itself.")]
    [SerializeField, Min(0f)] private float lingerSecondsAdded = 2f;

    public override void Apply() => UpgradeManager.Instance?.AddAlgaeLingerDuration(lingerSecondsAdded);
}
