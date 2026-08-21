using UnityEngine;

/// <summary>
/// Barnacle-branch upgrade card: only offered once Barnacles has been picked
/// (see IRequiresUpgrade). The rough shells scrape harder, giving every
/// barnacled turtle a chance to take two units instead of one per hit.
///
/// Unlike the Wood/Rock double-drop cards, which each only affect their own
/// resource, this applies to every source — wood, rock, seaweed, coconuts and
/// jellyfish alike — because UpgradeManager.RollHarvestAmount adds it on top
/// of the per-type chance rather than as another arm of the switch. It never
/// affects how fast a source is used up, only how much comes off per hit, and
/// crabs (which never grow barnacles) don't get it.
/// </summary>
public class BarnacleDoubleHarvestUpgradeCard : UpgradeCardDefinition, IRequiresUpgrade
{
    [SerializeField, Range(0f, 1f)] private float doubleChanceAdded = 0.15f;

    public bool IsPrerequisiteMet => UpgradeManager.Instance != null && UpgradeManager.Instance.BarnaclesUnlocked;

    public override void Apply() => UpgradeManager.Instance?.AddBarnacleDoubleHarvestChance(doubleChanceAdded);
}
