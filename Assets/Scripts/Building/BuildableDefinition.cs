using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach directly to a building prefab (alongside its SpriteRenderer/Collider2D)
/// to describe its display name and resource cost. BuildModeController
/// references this component and instantiates its GameObject when placed, so
/// new building types are just new prefabs with this component attached, not
/// new code.
/// </summary>
public class BuildableDefinition : MonoBehaviour
{
    [SerializeField] private string displayName = "Wall";
    [SerializeField] private List<ResourceManager.ResourceCost> cost;

    public string DisplayName => displayName;
    public IReadOnlyList<ResourceManager.ResourceCost> Cost => cost;
}
