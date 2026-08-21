using UnityEngine;

/// <summary>
/// Run-modifier effect (Inflation): every buildable's price climbs with each
/// one placed, the way Turtle Bed's already does — so a defense can't just be
/// spammed out of a big pile of wood, and each additional wall is worth less
/// than the one before it.
///
/// Applies to every entry in BuildModeController's Buildables array rather than
/// to a hand-listed set, so a building type added to the game later is covered
/// by this modifier automatically with nothing to remember to wire up. That
/// deliberately includes buildables that already grow (their rate simply gets
/// steeper) and the Demolish entry, whose cost list is empty and so has nothing
/// for a percentage to act on.
///
/// The increase is stored separately from the prefab-authored percentage and
/// cleared by BuildableDefinition.ResetPriceScaling at the start of each game
/// — these components live on prefab ASSETS, so a modifier that wrote to the
/// serialized field would quietly re-author the prefab for every future run.
/// </summary>
public class BuildingPriceGrowthUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Percentage points added to every buildable's price-increase-per-placement. 25 means each one placed makes the next cost 25% more, compounding — the fourth wall costs roughly double the first.")]
    [SerializeField, Min(0f)] private float priceIncreasePercentPerPlacement = 25f;

    public override void Apply()
    {
        if (BuildModeController.Instance == null)
        {
            Debug.LogError($"BuildingPriceGrowthUpgradeCard ({DisplayName}): no BuildModeController in the scene — building prices will stay flat.");
            return;
        }

        int affected = 0;
        foreach (BuildableDefinition buildable in BuildModeController.Instance.Buildables)
        {
            if (buildable == null) continue;

            buildable.AddPriceIncreasePercent(priceIncreasePercentPerPlacement);
            affected++;
        }

        Debug.Log($"BuildingPriceGrowthUpgradeCard ({DisplayName}): {affected} buildable(s) now cost +{priceIncreasePercentPerPlacement}% more per placement.");
    }
}
