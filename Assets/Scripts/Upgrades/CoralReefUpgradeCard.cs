using UnityEngine;

/// <summary>
/// Upgrade card: grows a clump of coral in the shallows as a sea wall. Trash
/// routes around a reef the way it routes around a palm tree, and bounces off
/// it physically if it blunders in; turtles pass straight through, since to
/// them coral behaves like any other building. See CoralReef for how those
/// two halves are wired.
///
/// Same prefab-asset constraints as SeaweedUpgradeCard (no serialized scene
/// references — IslandGenerator is looked up when applied) and the same
/// per-island respawn registration, since IslandTransitionController clears
/// the old island's reef on every transition.
/// </summary>
public class CoralReefUpgradeCard : UpgradeCardDefinition
{
    [SerializeField] private GameObject coralPrefab;
    [Tooltip("How many coral cells this pick grows. One CoralReef component per cell — the reef's width is this number, not one object's collider size.")]
    [SerializeField, Min(1)] private int cellCount = 6;
    [Tooltip("How tightly the coral clumps, in world units around a randomly chosen shallow-water cell.")]
    [SerializeField] private float patchRadius = 3f;

    public override void Apply()
    {
        SpawnOnCurrentIsland();
        UpgradeManager.Instance?.RegisterPerIslandRespawn(SpawnOnCurrentIsland);
    }

    private void SpawnOnCurrentIsland()
    {
        IslandGenerator islandGenerator = Object.FindAnyObjectByType<IslandGenerator>();
        CoralReefSpawner.SpawnPatch(islandGenerator, coralPrefab, cellCount, patchRadius);
    }
}
