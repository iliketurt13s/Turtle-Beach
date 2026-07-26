using UnityEngine;

/// <summary>
/// Building-branch upgrade card: only offered once Sand Pile is unlocked
/// (see BuildingUpgradeCard/IRequiresBuilding). A one-time trade-off — set
/// Stackable = false on this card's prefab — that doubles the cost of every
/// Sand Pile placed from now on (see BuildableDefinition.MultiplyCost, which
/// only affects future placements since cost is only read at placement time)
/// and, in exchange, makes every Sand Pile start dealing damage-over-time to
/// whatever trash it has trapped — already-placed piles included, since
/// SandPile reads UpgradeManager.SandPileDotDamagePerTick/SandPileDotTickInterval
/// live rather than caching either.
/// </summary>
public class SandPileCostAndDamageUpgradeCard : BuildingUpgradeCard
{
    [SerializeField, Min(1f)] private float costMultiplier = 2f;
    [SerializeField, Min(1)] private int dotDamagePerTick = 1;
    [Tooltip("Seconds between each damage-over-time tick once this card is picked. Lower = faster ticking.")]
    [SerializeField, Min(0.05f)] private float dotTickInterval = 1f;

    public override void Apply()
    {
        RequiredBuilding?.MultiplyCost(costMultiplier);
        UpgradeManager.Instance?.AddSandPileDotDamagePerTick(dotDamagePerTick);
        UpgradeManager.Instance?.SetSandPileDotTickInterval(dotTickInterval);
    }
}
