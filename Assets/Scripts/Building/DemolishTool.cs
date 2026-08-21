using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The build drawer's demolish entry: a marker component plus its refund
/// tuning, attached to a prefab that also carries a BuildableDefinition and a
/// SpriteRenderer but is never actually placed in the world.
///
/// Riding on BuildableDefinition is what makes this a drawer row for free —
/// BuildSelectionUI already draws one row per unlocked entry of
/// BuildModeController.Buildables and asks each for its Icon/DisplayName, and
/// BuildModeController already shows that same sprite as the cursor ghost. All
/// this component adds is the handful of branches in those two scripts that
/// swap click-to-place for click-to-destroy, in the spirit of the rest of the
/// building system: a new prefab, not a new manager. Its cost list should be
/// left empty — the drawer blanks the cost readout for this entry rather than
/// showing a price of zero.
///
/// Like an UpgradeCardDefinition, this component runs ON THE PREFAB ASSET, not
/// on a scene instance — BuildModeController holds the array entry directly and
/// calls Demolish on it. So: no Awake, no coroutines, no serialized scene
/// references, and no per-run mutable state; reach the scene through singletons
/// (ResourceManager.Instance, TurtleNest.Instance) the way the upgrade cards do.
/// That constraint is also why the refund flies out as one burst of effects
/// spawned in a single frame rather than a staggered stream like
/// TurtleAgent.DeliverCarriedResources — a prefab asset can't run the coroutine
/// a stagger would need.
/// </summary>
public class DemolishTool : MonoBehaviour
{
    [Serializable]
    private struct ResourceIcon
    {
        public ResourceManager.ResourceType type;
        public Sprite icon;
    }

    [Header("Refund")]
    [Tooltip("Fraction of what a building ACTUALLY cost to place (see BuildableDefinition.PaidCost, not the scaled-up price of the next one) that comes back when it's demolished. Rounded down per resource type, so a place-then-demolish loop is always a net loss — placing also bumps that buildable's price, and demolishing rolls the bump back, so the two can never be cycled for profit.")]
    [SerializeField, Range(0f, 1f)] private float refundFraction = 0.5f;

    [Tooltip("The same delivery pop-effect prefab turtles fly resources to the nest with — Assets/Prefabs/Animals/CarryingResources/PopToEffect. Leave unassigned and the refund still lands correctly, just instantly with no animation.")]
    [SerializeField] private GameObject refundPopEffectPrefab;

    [Tooltip("Sprite flown to the nest for each refunded resource type. A type with no icon here is still refunded, just instantly (same fallback as leaving the effect prefab unassigned) rather than flying an invisible sprite. Only Wood/Rock are needed unless a building is given a food cost.")]
    [SerializeField] private ResourceIcon[] refundIcons;

    [Header("Refund Effects")]
    [Tooltip("Most effects spawned for one refunded resource type. A refund bigger than this rides fewer effects carrying more each, so demolishing something expensive doesn't spray dozens of sprites across the island.")]
    [SerializeField, Min(1)] private int maxEffectsPerResource = 5;
    [Tooltip("Radius around the demolished building that the effects start from, so several units leaving at once fan out instead of stacking into what looks like a single sprite.")]
    [SerializeField, Min(0f)] private float effectSpread = 0.3f;

    /// <summary>True if definition is the drawer's demolish entry rather than a real building. The single place that knowledge lives — BuildModeController asks to decide what a click does, BuildSelectionUI to decide where the row goes and whether to show a price.</summary>
    public static bool IsDemolishEntry(BuildableDefinition definition) =>
        definition != null && definition.GetComponent<DemolishTool>() != null;

    /// <summary>
    /// Destroys a placed building and flies its refund back to the nest.
    ///
    /// Destroying it is all that's needed to undo the placement everywhere else:
    /// BuildableDefinition.OnDestroy already rolls this buildable's price back
    /// down (so the next one costs what this one did), WallAutoTile.OnDestroy
    /// already unregisters from WallGrid and refreshes its neighbours' sprites,
    /// and BuildModeController.UpdateTurtleBedAvailability polls the bed count
    /// rather than listening for a specific cause of death, so demolishing a bed
    /// at the cap puts Turtle Bed back in the drawer on its own.
    ///
    /// BuildingHealth.Destroyed is deliberately NOT raised — that event means
    /// "a building was lost to damage", and a demolition the player asked for
    /// isn't that.
    /// </summary>
    public void Demolish(BuildableDefinition building)
    {
        if (building == null) return;

        // Captured before the Destroy call: Destroy is deferred to the end of
        // the frame so the transform would still read fine here, but the refund
        // is computed off the instance's recorded cost and there is no reason
        // for any of it to depend on that timing.
        Vector3 origin = building.transform.position;
        List<ResourceManager.ResourceCost> refund = CalculateRefund(building);

        Destroy(building.gameObject);

        DeliverRefund(refund, origin);
    }

    /// <summary>Half (see Refund Fraction) of what this instance actually paid, dropping any type that rounds down to nothing.</summary>
    private List<ResourceManager.ResourceCost> CalculateRefund(BuildableDefinition building)
    {
        List<ResourceManager.ResourceCost> refund = new List<ResourceManager.ResourceCost>();

        foreach (ResourceManager.ResourceCost paid in building.PaidCost)
        {
            int amount = Mathf.FloorToInt(paid.amount * refundFraction);
            if (amount <= 0) continue;

            refund.Add(new ResourceManager.ResourceCost { type = paid.type, amount = amount });
        }

        return refund;
    }

    /// <summary>
    /// Flies each refunded resource type to the nest on the turtles' own
    /// delivery pop effect, crediting ResourceManager only as each one lands —
    /// exactly how TurtleAgent.DeliverCarriedResources does it, so a refund
    /// reads as a delivery rather than as a number quietly changing in the HUD.
    ///
    /// No ScoreManager.AddResourceScore here, unlike a turtle's delivery:
    /// resources coming back off a building were already scored when they were
    /// harvested, and paying score for them again would make build-and-demolish
    /// a way to farm it.
    /// </summary>
    private void DeliverRefund(List<ResourceManager.ResourceCost> refund, Vector3 origin)
    {
        if (refund.Count == 0 || ResourceManager.Instance == null) return;

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;

        foreach (ResourceManager.ResourceCost cost in refund)
        {
            Sprite icon = IconFor(cost.type);
            if (nest == null || refundPopEffectPrefab == null || icon == null)
            {
                // Nothing to fly it with (or nowhere to fly it to, the nest
                // being gone meaning the run is already over) — still refund
                // correctly, just instantly. Same fallback TurtleAgent takes
                // when its own effect prefab isn't wired.
                ResourceManager.Instance.Add(cost.type, cost.amount);
                continue;
            }

            int effectCount = Mathf.Min(cost.amount, maxEffectsPerResource);
            int perEffect = cost.amount / effectCount;
            int remainder = cost.amount % effectCount;

            for (int i = 0; i < effectCount; i++)
            {
                // Locals, not the loop variables: a `for` variable is captured
                // by reference, so the closures below would every one of them
                // see the final iteration's values.
                ResourceManager.ResourceType type = cost.type;
                int payload = perEffect + (i < remainder ? 1 : 0);

                Vector2 offset = UnityEngine.Random.insideUnitCircle * effectSpread;
                Vector3 from = origin + new Vector3(offset.x, offset.y, 0f);

                GameObject instance = Instantiate(refundPopEffectPrefab, from, Quaternion.identity);
                instance.GetComponent<ResourcePopEffect>()?.Initialize(
                    icon, from, nest.position,
                    () =>
                    {
                        ResourceManager.Instance?.Add(type, payload);
                        TurtleNest.Instance?.PlaySquash();
                    });
            }
        }
    }

    private Sprite IconFor(ResourceManager.ResourceType type)
    {
        if (refundIcons == null) return null;

        foreach (ResourceIcon entry in refundIcons)
        {
            if (entry.type == type) return entry.icon;
        }

        return null;
    }
}
