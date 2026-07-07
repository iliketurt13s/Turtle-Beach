using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the resource counter list: listens to ResourceManager.ResourceChanged
/// and lazily instantiates one ResourceCounterRow per resource type the first
/// time it's collected. Rows never disappear once shown, matching
/// ResourceManager's counts-only-ever-increase behavior. Adding a new
/// ResourceManager.ResourceType only needs one new entry in Resource Icons here.
/// </summary>
public class ResourceHUD : MonoBehaviour
{
    [Serializable]
    private struct ResourceIcon
    {
        public ResourceManager.ResourceType type;
        public Sprite icon;
    }

    [SerializeField] private ResourceCounterRow rowPrefab;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private List<ResourceIcon> resourceIcons;

    private readonly Dictionary<ResourceManager.ResourceType, ResourceCounterRow> rows
        = new Dictionary<ResourceManager.ResourceType, ResourceCounterRow>();

    private void Start()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ResourceChanged += HandleResourceChanged;
        }
        else
        {
            Debug.LogWarning("ResourceHUD: no ResourceManager in scene, counters won't update.");
        }
    }

    private void OnDestroy()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ResourceChanged -= HandleResourceChanged;
        }
    }

    private void HandleResourceChanged(ResourceManager.ResourceType type, int newTotal)
    {
        if (!rows.TryGetValue(type, out ResourceCounterRow row))
        {
            row = Instantiate(rowPrefab, rowContainer);
            row.Initialize(GetIcon(type));
            rows[type] = row;
        }

        row.SetCount(newTotal);
    }

    private Sprite GetIcon(ResourceManager.ResourceType type)
    {
        foreach (ResourceIcon entry in resourceIcons)
        {
            if (entry.type == type) return entry.icon;
        }

        return null;
    }
}
