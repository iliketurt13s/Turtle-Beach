using UnityEngine;

/// <summary>
/// Upgrade card: unlocks one buildable in BuildModeController. Generic and
/// data-driven — every locked building (Wall, Campfire, Watchtower, Pet Rock,
/// Fertilizer, Sand Pile, ...) gets its own unlock card by making a new prefab
/// with this same script and pointing Buildable To Unlock at that building's
/// BuildableDefinition, no new code required.
/// </summary>
public class BuildingUnlockUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private BuildableDefinition buildableToUnlock;

    public override void Apply() => BuildModeController.Instance?.Unlock(buildableToUnlock);
}
