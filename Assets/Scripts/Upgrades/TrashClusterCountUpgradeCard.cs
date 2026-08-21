using UnityEngine;

/// <summary>
/// Run-modifier effect (Scattered Tide): every storm's trash is split across
/// far more clumps than usual, so the same wave arrives from all around the
/// island at once instead of as one or two dense fronts — a wall line and a
/// couple of watchtowers covering the obvious approach stop being enough.
///
/// Deliberately raises the clump COUNT only and leaves the round's rating
/// budget alone (see TrashSpawner.AddClusterCountBonus): the hardship is the
/// spread, not the volume, and stacking both would be two modifiers in one
/// prefab wearing a single score bonus.
/// </summary>
public class TrashClusterCountUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Extra clumps added to both ends of TrashSpawner's per-round Min/Max cluster roll, so every round is scattered wider rather than merely more variable. Tune against the spawner's own Min/Max Cluster Count — 4 on a 2-4 spawner means rounds now split across 6-8 clumps.")]
    [SerializeField, Min(1)] private int extraClusters = 4;

    public override void Apply()
    {
        if (TrashSpawner.Instance == null)
        {
            Debug.LogError($"TrashClusterCountUpgradeCard ({DisplayName}): no TrashSpawner in the scene — trash will keep clumping normally.");
            return;
        }

        TrashSpawner.Instance.AddClusterCountBonus(extraClusters);
    }
}
