using UnityEngine;

/// <summary>
/// Implemented by anything HoverTargetOutline can tint on hover instead of
/// outlining (see ResourceClickTarget and BuildingHoverTint) — lets it track
/// and revert "whatever's currently tinted" through one field regardless of
/// which concrete type is behind it.
/// </summary>
public interface IHoverTintable
{
    void ApplyTint(Color tint);
    void RevertTint();
}
