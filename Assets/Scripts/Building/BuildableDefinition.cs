using System;
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

    /// <summary>
    /// Runtime-only addition to Price Increase Percent Per Placement, from the
    /// Inflation run modifier (see AddPriceIncreasePercent) — so a buildable
    /// authored with a flat price starts climbing with every one placed, the
    /// way Turtle Bed already does.
    ///
    /// A separate field rather than writing to the serialized one, for the
    /// same reason costMultiplier is: this component lives on a PREFAB ASSET,
    /// so a runtime write to a serialized field survives the play session and
    /// silently re-authors the prefab. This one is reset by ResetPriceScaling
    /// along with everything else at the start of each game.
    /// </summary>
    private float extraPriceIncreasePercentPerPlacement = 0f;

    /// <summary>What was actually spent to place THIS instance, recorded by BuildModeController right after it was paid for (see RecordPaidCost) — null on the array entry itself and on any instance nobody bought.</summary>
    private List<ResourceManager.ResourceCost> paidCost;

    /// <summary>Set by BuildModeController right after instantiating this buildable (see LinkToMaster) — Instantiate always gives a placed building its own separate BuildableDefinition component clone, distinct from this array entry that BuildModeController/upgrade cards actually read Cost from and mutate, so a placed instance needs a way back to it.</summary>
    private BuildableDefinition master;

    public string DisplayName => displayName;

    /// <summary>This buildable's representative sprite, e.g. for BuildSelectionUI's icons — same GetComponentInChildren&lt;SpriteRenderer&gt; lookup BuildModeController.RefreshGhostSprite already uses for the ghost itself. Null if this buildable has no SpriteRenderer anywhere in its hierarchy.</summary>
    public Sprite Icon => GetComponentInChildren<SpriteRenderer>()?.sprite;

    /// <summary>The Inspector-authored base cost scaled up by Price Increase Percent Per Placement (compounded once per previous placement, see RegisterPlacement) and by Cost Multiplier (see MultiplyCost). Recomputed on every access rather than cached, since both change over the session.</summary>
    public IReadOnlyList<ResourceManager.ResourceCost> Cost => ScaledCost();

    /// <summary>Cumulative max-health bonus granted to every instance of this buildable, from a building-branch upgrade card (see WallHealthUpgradeCard). Read live by BuildingHealth.</summary>
    public int HealthBonus => healthBonus;

    /// <summary>
    /// What this placed instance cost when it went down, for DemolishTool to
    /// refund a fraction of. Cost can't answer that: it's the live, scaled
    /// price of the NEXT placement, which climbs with every one made, so
    /// reading it at demolition time would refund against a building the
    /// player never bought.
    ///
    /// Falls back to the Inspector-authored base cost for an instance nobody
    /// paid for — StarterTurtleBedSpawner's freebie — so demolishing that one
    /// refunds what a bed is worth rather than nothing. It can't be farmed:
    /// there is only ever the one, and rebuilding it costs full price.
    /// </summary>
    public IReadOnlyList<ResourceManager.ResourceCost> PaidCost => paidCost ?? cost;

    /// <summary>Records what was actually spent on this instance. Called once by BuildModeController right after Instantiate, alongside LinkToMaster, with the same snapshot of the price that was handed to ResourceManager.TrySpend — snapshot rather than a re-read, since Cost recomputes (and by then has already been bumped by RegisterPlacement).</summary>
    public void RecordPaidCost(IReadOnlyList<ResourceManager.ResourceCost> paid)
    {
        paidCost = paid != null ? new List<ResourceManager.ResourceCost>(paid) : null;
    }

    /// <summary>Bumps the price for this buildable's next placement. Called by BuildModeController right after a placement of this buildable succeeds.</summary>
    public void RegisterPlacement() => timesPlaced++;

    /// <summary>Links a placed instance's own component clone back to this array entry, so its OnDestroy can find its way back to roll the price back down. Called once by BuildModeController right after Instantiate.</summary>
    public void LinkToMaster(BuildableDefinition definitionMaster) => master = definitionMaster;

    /// <summary>Raised on the master array entry (not the destroyed instance) right after RegisterDestruction, so BuildModeController can refresh the ghost's displayed cost if this buildable happens to be selected. See OnDestroy.</summary>
    public static event Action<BuildableDefinition> PriceRolledBack;

    /// <summary>Undoes one RegisterPlacement bump, so this buildable's next placement costs exactly what its last-placed instance cost. Called on this array entry (see OnDestroy) whenever any placed instance of it is destroyed — by trash, wearing out, or any other means — so losing a building always gets a discount back on rebuilding one, never stacking below what was actually paid.</summary>
    public void RegisterDestruction()
    {
        timesPlaced = Mathf.Max(0, timesPlaced - 1);
        PriceRolledBack?.Invoke(this);
    }

    /// <summary>Routes this placed instance's destruction back to the master array entry's RegisterDestruction (see LinkToMaster) — a no-op for the master entry itself, or for a stray instance BuildModeController never linked (e.g. StarterTurtleBedSpawner's freebie, which was never paid for either).</summary>
    private void OnDestroy()
    {
        if (master != null) master.RegisterDestruction();
    }

    /// <summary>Multiplies this buildable's cost from now on, e.g. 0.85 = 15% cheaper, 2f = double — from a building-branch upgrade card (see WallCostReductionUpgradeCard/SandPileCostAndDamageUpgradeCard). Stacks multiplicatively with repeated picks. Only affects placements made after it's called — cost is only read at placement time, so already-placed buildings are unaffected.</summary>
    public void MultiplyCost(float multiplier) => costMultiplier *= multiplier;

    /// <summary>Adds to this buildable's max-health bonus (see HealthBonus) — from a building-branch upgrade card (see WallHealthUpgradeCard).</summary>
    public void AddHealthBonus(int amount) => healthBonus += amount;

    /// <summary>Adds percentage points to how much this buildable's price climbs per placement, on top of whatever its prefab authored (see extraPriceIncreasePercentPerPlacement). Stacks additively with repeat calls. Only affects placements made from now on, exactly like MultiplyCost.</summary>
    public void AddPriceIncreasePercent(float percentPerPlacement) => extraPriceIncreasePercentPerPlacement += Mathf.Max(0f, percentPerPlacement);

    /// <summary>Resets this buildable back to its Inspector-authored base price/health, undoing any upgrade-card multipliers/bonuses too. This component lives on the buildable's prefab asset, not a scene instance (BuildModeController references it directly to read/instantiate), so this state otherwise persists across separate play sessions instead of resetting like normal scene state — see BuildModeController.Awake, which calls this for every configured buildable at the start of each game.</summary>
    public void ResetPriceScaling()
    {
        timesPlaced = 0;
        costMultiplier = 1f;
        healthBonus = 0;
        extraPriceIncreasePercentPerPlacement = 0f;
    }

    private List<ResourceManager.ResourceCost> ScaledCost()
    {
        List<ResourceManager.ResourceCost> scaled = new List<ResourceManager.ResourceCost>(cost.Count);

        float increasePercent = priceIncreasePercentPerPlacement + extraPriceIncreasePercentPerPlacement;
        float placementMultiplier = timesPlaced == 0 || increasePercent <= 0f
            ? 1f
            : Mathf.Pow(1f + increasePercent / 100f, timesPlaced);
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
