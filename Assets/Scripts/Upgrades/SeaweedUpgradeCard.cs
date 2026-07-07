using UnityEngine;

/// <summary>
/// Upgrade card: grows a small patch of Seaweed resource nodes somewhere in
/// shallow water. This card is a prefab asset (not a scene object), so it
/// can't hold a serialized scene reference to IslandGenerator — it looks the
/// scene's instance up directly when applied.
/// </summary>
public class SeaweedUpgradeCard : UpgradeCardDefinition, IGrantsFoodItem
{
    [SerializeField] private GameObject seaweedNodePrefab;
    [SerializeField] private int nodeCount = 4;
    [SerializeField] private float patchRadius = 2f;

    public override void Apply()
    {
        IslandGenerator islandGenerator = Object.FindFirstObjectByType<IslandGenerator>();
        SeaweedPatchSpawner.SpawnPatch(islandGenerator, seaweedNodePrefab, nodeCount, patchRadius);
    }
}
