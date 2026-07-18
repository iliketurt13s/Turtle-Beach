using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach directly to a building prefab (alongside its SpriteRenderer/Collider2D)
/// to describe its display name and resource cost. BuildModeController
/// references this component and instantiates its GameObject when placed, so
/// new building types are just new prefabs with this component attached, not
/// new code.
/// </summary>
public class BuildableDefinition : MonoBehaviour
{
    [SerializeField] private string displayName = "Wall";
    [SerializeField] private List<ResourceManager.ResourceCost> cost;
    [Tooltip("Percent this buildable's cost increases by, compounding, every time one is placed (see RegisterPlacement). 0 = cost never changes.")]
    [SerializeField, Min(0f)] public float priceIncreasePercentPerPlacement = 0f;

    private int timesPlaced;
    private float costMultiplier = 1f;
    private int healthBonus = 0;

    public string DisplayName => displayName;

    /// <summary>The Inspector-authored base cost scaled up by Price Increase Percent Per Placement (compounded once per previous placement, see RegisterPlacement) and by Cost Multiplier (see MultiplyCost). Recomputed on every access rather than cached, since both change over the session.</summary>
    public IReadOnlyList<ResourceManager.ResourceCost> Cost => ScaledCost();

    /// <summary>Cumulative max-health bonus granted to every instance of this buildable, from a building-branch upgrade card (see WallHealthUpgradeCard). Read live by BuildingHealth.</summary>
    public int HealthBonus => healthBonus;

    /// <summary>Bumps the price for this buildable's next placement. Called by BuildModeController right after a placement of this buildable succeeds.</summary>
    public void RegisterPlacement() => timesPlaced++;

    /// <summary>Multiplies this buildable's cost from now on, e.g. 0.85 = 15% cheaper, 2f = double — from a building-branch upgrade card (see WallCostReductionUpgradeCard/SandPileCostAndDamageUpgradeCard). Stacks multiplicatively with repeated picks. Only affects placements made after it's called — cost is only read at placement time, so already-placed buildings are unaffected.</summary>
    public void MultiplyCost(float multiplier) => costMultiplier *= multiplier;

    /// <summary>Adds to this buildable's max-health bonus (see HealthBonus) — from a building-branch upgrade card (see WallHealthUpgradeCard).</summary>
    public void AddHealthBonus(int amount) => healthBonus += amount;

    /// <summary>Resets this buildable back to its Inspector-authored base price/health, undoing any upgrade-card multipliers/bonuses too. This component lives on the buildable's prefab asset, not a scene instance (BuildModeController references it directly to read/instantiate), so this state otherwise persists across separate play sessions instead of resetting like normal scene state — see BuildModeController.Awake, which calls this for every configured buildable at the start of each game.</summary>
    public void ResetPriceScaling()
    {
        timesPlaced = 0;
        costMultiplier = 1f;
        healthBonus = 0;
    }

    private List<ResourceManager.ResourceCost> ScaledCost()
    {
        List<ResourceManager.ResourceCost> scaled = new List<ResourceManager.ResourceCost>(cost.Count);

        float placementMultiplier = timesPlaced == 0 || priceIncreasePercentPerPlacement <= 0f
            ? 1f
            : Mathf.Pow(1f + priceIncreasePercentPerPlacement / 100f, timesPlaced);
        float multiplier = placementMultiplier * costMultiplier;

        if (Mathf.Approximately(multiplier, 1f))
        {
            scaled.AddRange(cost);
            return scaled;
        }

        foreach (ResourceManager.ResourceCost c in cost)
        {
            scaled.Add(new ResourceManager.ResourceCost { type = c.type, amount = Mathf.CeilToInt(c.amount * multiplier) });
        }

        return scaled;
    }
}
