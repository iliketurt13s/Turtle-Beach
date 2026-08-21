using UnityEngine;

/// <summary>
/// Upgrade card: every turtle can carry more before it has to break off and
/// walk its load back to the nest. Applies to already-hatched turtles and
/// future ones alike (see UpgradeManager.ApplyTo). Crabs are unaffected —
/// they have their own capacity card, see CrabCarryCapacityUpgradeCard.
///
/// Note the shell only draws as many carried icons as the Turtle prefab has
/// CarriedResourceVisuals slots — units past that count and deliver normally,
/// they just don't show a sprite on the shell.
/// </summary>
public class CarryCapacityUpgradeCard : UpgradeCardDefinition
{
    [SerializeField, Min(1)] private int capacityAdded = 2;

    public override void Apply() => UpgradeManager.Instance?.AddCarryCapacity(capacityAdded);
}
