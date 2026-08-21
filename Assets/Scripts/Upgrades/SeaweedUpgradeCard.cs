using UnityEngine;

/// <summary>
/// Upgrade card: grows a small patch of Seaweed resource nodes somewhere in
/// shallow water. This card is a prefab asset (not a scene object), so it
/// can't hold a serialized scene reference to IslandGenerator — it looks the
/// scene's instance up directly when applied. Also registers itself with
/// UpgradeManager.RegisterPerIslandRespawn so the patch keeps regrowing on
/// every future island too, not just the one it was picked on — otherwise
/// IslandTransitionController's wipe-and-regenerate would take this island's
/// patch with it and the upgrade would effectively vanish after one transition.
/// </summary>
public class SeaweedUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private GameObject seaweedNodePrefab;
    [SerializeField] private int nodeCount = 4;
    [SerializeField] private float patchRadius = 2f;

    public override void Apply()
    {
        SpawnOnCurrentIsland();
        // Also recorded as a run-state flag, which is what the food-branch
        // cards' IRequiresUpgrade gates read (see
        // UpgradeManager.UnlockedFoodTypeCount) — unlike Coconut and
        // Jellyfish, this card sets no number they could infer it from.
        UpgradeManager.Instance?.UnlockSeaweed();
        UpgradeManager.Instance?.RegisterPerIslandRespawn(SpawnOnCurrentIsland);
    }

    private void SpawnOnCurrentIsland()
    {
        IslandGenerator islandGenerator = Object.FindAnyObjectByType<IslandGenerator>();
        SeaweedPatchSpawner.SpawnPatch(islandGenerator, seaweedNodePrefab, nodeCount, patchRadius);
    }
}
