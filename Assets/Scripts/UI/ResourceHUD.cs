using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
    [Tooltip("Uniform scale applied to every instantiated row (icon + count text together), so the whole HUD can be sized up/down from this one field instead of editing the row prefab directly. If Row Container has a Vertical/Horizontal Layout Group, its spacing is widened to compensate, since a Layout Group reserves space for a child's original unscaled size regardless of its actual rendered scale.")]
    [SerializeField] private float rowScale = 1f;
    [SerializeField] private List<ResourceIcon> resourceIcons;

    private readonly Dictionary<ResourceManager.ResourceType, ResourceCounterRow> rows
        = new Dictionary<ResourceManager.ResourceType, ResourceCounterRow>();

    private void Awake()
    {
        ExpandLayoutSpacingForRowScale();
    }

    /// <summary>Widens Row Container's layout spacing by however much extra height/width each row's scale-up adds beyond its own unscaled size, so scaled rows don't overlap the next one down — a Layout Group only ever reserves a child's original RectTransform size, never its rendered (scaled) size. No-op if Row Scale is 1 or Row Container isn't under a Vertical/Horizontal Layout Group.</summary>
    private void ExpandLayoutSpacingForRowScale()
    {
        if (Mathf.Approximately(rowScale, 1f) || rowPrefab == null || rowContainer == null) return;

        HorizontalOrVerticalLayoutGroup layoutGroup = rowContainer.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (layoutGroup == null) return;

        RectTransform prefabRect = rowPrefab.transform as RectTransform;
        if (prefabRect == null) return;

        float unscaledExtent = layoutGroup is VerticalLayoutGroup ? prefabRect.rect.height : prefabRect.rect.width;
        layoutGroup.spacing += unscaledExtent * (rowScale - 1f);
    }

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
            row.transform.localScale = Vector3.one * rowScale;
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
