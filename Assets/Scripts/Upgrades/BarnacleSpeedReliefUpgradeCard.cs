using UnityEngine;

/// <summary>
/// Barnacle-branch upgrade card: only offered once Barnacles has been picked
/// (see IRequiresUpgrade). Softens the barnacle movement penalty without
/// touching the damage bonus that paid for it. Stackable, but clamped at no
/// penalty at all — repeat picks can cancel the slowdown out entirely and
/// never turn it into a speed boost.
/// </summary>
public class BarnacleSpeedReliefUpgradeCard : UpgradeCardDefinition, IRequiresUpgrade
{
    [Tooltip("How much of the speed penalty this removes, in multiplier terms, e.g. 0.08 turns a x0.75 penalty into x0.83.")]
    [SerializeField, Range(0f, 1f)] private float penaltyRemoved = 0.08f;

    public bool IsPrerequisiteMet => UpgradeManager.Instance != null && UpgradeManager.Instance.BarnaclesUnlocked;

    public override void Apply() => UpgradeManager.Instance?.EaseBarnacleSpeedPenalty(penaltyRemoved);
}
