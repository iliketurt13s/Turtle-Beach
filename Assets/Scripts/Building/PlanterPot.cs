using UnityEngine;

/// <summary>
/// A building the player places that is, mechanically, a tree: it carries a
/// ResourceNode of its own, so turtles find it, path to it and harvest it
/// through exactly the same code that serves a naturally-generated palm — no
/// branch anywhere else in the project knows the difference.
///
/// That is why this component is so small. Everything that makes a planted
/// tree WORSE than a wild one is authored on the prefab rather than written
/// here: fewer Hits To Deplete (it yields less per cycle) and a longer Respawn
/// Duration (it comes back slower) on its ResourceNode, both plain Inspector
/// values that can be retuned without touching code. The building side is the
/// same story — BuildableDefinition for the cost, and the placement itself is
/// BuildModeController's generic path.
///
/// All this class does is bridge the one thing the prefab can't express: the
/// run-wide Planter Pot yield upgrade, which has to reach every pot already
/// standing as well as every future one. It polls for that total and pushes it
/// into its own node, the same shape BuildingHealth uses to live-apply a
/// building-branch health bonus.
///
/// The fertilizer half of the branch needs nothing here at all: the prefab
/// carries a ResourceRespawnBooster set to BoosterKind.PlanterPot, which sits
/// inert until Root Network is picked — the identical arrangement Algae uses.
/// </summary>
[RequireComponent(typeof(ResourceNode))]
public class PlanterPot : MonoBehaviour
{
    private ResourceNode node;

    /// <summary>The bonus last pushed, so a frame that changed nothing does no work. Starts at -1 rather than 0 so the very first Update always pushes, establishing the baseline even when the run-wide bonus is still zero.</summary>
    private int appliedHitsBonus = -1;

    private void Awake()
    {
        node = GetComponent<ResourceNode>();
    }

    private void Update()
    {
        // Polled rather than pushed from the card, for the same reason
        // BuildingHealth polls its own health bonus: pots are placed and
        // destroyed throughout a run, so a card that fanned out to whatever
        // existed at the moment it was picked would miss every pot built
        // afterward, and would need its own registry to find them at all.
        int bonus = UpgradeManager.Instance != null ? UpgradeManager.Instance.PlanterPotHitsBonus : 0;
        if (bonus == appliedHitsBonus) return;

        appliedHitsBonus = bonus;
        node.SetBonusHitsToDeplete(bonus);
    }
}
