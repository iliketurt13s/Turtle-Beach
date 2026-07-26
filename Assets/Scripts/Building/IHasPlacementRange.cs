/// <summary>
/// Opt-in interface for a buildable whose effect covers a radius around
/// itself (Fertilizer/Pet Rock's respawn boost, Campfire's speed buff,
/// Watchtower's fire radius, ...). Implementing it on a building script is
/// the only step needed to get a live range-preview circle drawn under
/// BuildModeController's placement ghost whenever that buildable is selected
/// — see BuildModeController.RefreshPlacementRangeCircle, which looks this
/// interface up via GetComponent on the selected BuildableDefinition's own
/// prefab, the same way it already reads that prefab's SpriteRenderer.
/// </summary>
public interface IHasPlacementRange
{
    /// <summary>Current effective radius in world units, including any upgrade-card bonus — read live every frame the ghost is shown, so a range upgrade picked mid-run is reflected immediately.</summary>
    float PlacementRange { get; }
}
