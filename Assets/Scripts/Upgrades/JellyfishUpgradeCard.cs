using UnityEngine;

/// <summary>Upgrade card: raises the chance a Jellyfish spawns in the shallows each round (see JellyfishSpawner.TryRollSpawn). Stacks.</summary>
public class JellyfishUpgradeCard : UpgradeCardDefinition, IGrantsFoodItem
{
    [SerializeField, Range(0f, 1f)] private float spawnChanceAdded = 0.2f;

    public override void Apply() => UpgradeManager.Instance?.AddJellyfishSpawnChance(spawnChanceAdded);
}
