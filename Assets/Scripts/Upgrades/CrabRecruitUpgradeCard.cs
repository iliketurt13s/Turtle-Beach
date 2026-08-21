using UnityEngine;

/// <summary>
/// Upgrade card: hatches one crab recruit at the nest. A crab takes player
/// orders and gathers resources exactly like a turtle during the day, but sits
/// storms out at the nest instead of fighting — see CrabAgent for the full
/// list of what differs and which cards change it.
///
/// Registers itself with UpgradeManager.RegisterPerIslandRespawn for the same
/// reason SeaweedUpgradeCard does: IslandTransitionController destroys every
/// unit on the old island, so without this the crab would silently disappear
/// on the first island transition. One registration per pick, so picking this
/// three times gives three crabs on every future island too.
/// </summary>
public class CrabRecruitUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("The Crab prefab — a Turtle prefab variant with a CrabAgent component on it.")]
    [SerializeField] private GameObject crabPrefab;

    public override void Apply()
    {
        // Counted before spawning so the crab improvement branch (see
        // IRequiresUpgrade) is already open by the next upgrade offering.
        UpgradeManager.Instance?.AddCrab();
        SpawnOnCurrentIsland();
        UpgradeManager.Instance?.RegisterPerIslandRespawn(SpawnOnCurrentIsland);
    }

    /// <summary>Spawns through the nest rather than instantiating directly, so a crab shares the turtle spawn point, spawn parent and destroyed-nest guard. TurtleAgent.OnEnable then catches the new crab up on every upgrade picked so far, so nothing else is needed here.</summary>
    private void SpawnOnCurrentIsland() => TurtleNest.Instance?.SpawnUnit(crabPrefab);
}
