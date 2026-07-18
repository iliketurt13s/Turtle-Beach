using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared component behind Pet Rock (affects Rock) and Fertilizer (affects
/// Wood and Seaweed) — two prefabs, one script, configured with different
/// Affected Types. Speeds up respawn of any ResourceNode of a matching type
/// within Range, checked every frame by live distance (see
/// UpdateProximityRange, mirroring Campfire.UpdateProximityRange) rather than
/// a trigger collider — no physical collision/layer requirements, entering
/// applies the boost and leaving removes it immediately. Multiple overlapping
/// boosters stack linearly on a node (see ResourceNode.Update), not by the
/// node just taking whichever booster is strongest. Boost Visual (e.g. a
/// sparkle particle system, wired up the same way Campfire wires up its
/// flame) plays for as long as this booster is actively boosting at least
/// one node, giving the player a visible cue that it's doing something.
/// </summary>
public class ResourceRespawnBooster : MonoBehaviour
{
    /// <summary>Which building-branch upgrade track (see FertilizerRangeUpgradeCard/PetRockRangeUpgradeCard etc.) this instance reads its live bonuses from — Fertilizer and PetRock are the same script/component but must upgrade independently, and Affected Types alone isn't a reliable way to tell them apart (it's free-form and could be reconfigured), so this is set explicitly per prefab instead.</summary>
    private enum BoosterKind { Fertilizer, PetRock }

    [Tooltip("Which building this is for upgrade-card purposes (Fertilizer vs Pet Rock) — must match the prefab, since both share this script but upgrade independently.")]
    [SerializeField] private BoosterKind kind = BoosterKind.Fertilizer;
    [Tooltip("Resource types this building speeds up the respawn of.")]
    [SerializeField] private ResourceManager.ResourceType[] affectedTypes;
    [Tooltip("Additive bonus to respawn speed for every node in range, e.g. 1.0 = +100% (doubles) on its own. Stacks linearly with any other booster also in range of the same node.")]
    [SerializeField] private float respawnSpeedBonus = 1f;
    [Tooltip("Radius within which a matching ResourceNode's respawn is boosted.")]
    [SerializeField] public float range = 2f;
    [Tooltip("Particle effect (or any visual) toggled on for as long as this booster is actively boosting at least one node, off otherwise. Leave unassigned for no visual cue.")]
    [SerializeField] private GameObject boostVisual;

    /// <summary>respawnSpeedBonus plus any run-wide bonus from this booster's building-branch upgrade cards — read live rather than cached, so a card picked mid-run applies immediately to this booster whether it was already placed or built afterward (see Campfire.EffectiveSpeedBonus for the same pattern).</summary>
    public float RespawnSpeedBonus => respawnSpeedBonus + KindRespawnBonus;

    /// <summary>range plus any run-wide bonus from this booster's building-branch upgrade cards — read live, same rationale as RespawnSpeedBonus.</summary>
    private float EffectiveRange => range + KindRangeBonus;

    private float KindRangeBonus
    {
        get
        {
            if (UpgradeManager.Instance == null) return 0f;
            return kind == BoosterKind.Fertilizer ? UpgradeManager.Instance.FertilizerRangeBonus : UpgradeManager.Instance.PetRockRangeBonus;
        }
    }

    private float KindRespawnBonus
    {
        get
        {
            if (UpgradeManager.Instance == null) return 0f;
            return kind == BoosterKind.Fertilizer ? UpgradeManager.Instance.FertilizerRespawnBonus : UpgradeManager.Instance.PetRockRespawnBonus;
        }
    }

    private readonly HashSet<ResourceNode> boostedNodes = new HashSet<ResourceNode>();

    private void Update()
    {
        UpdateProximityRange();
        if (boostVisual != null) boostVisual.SetActive(boostedNodes.Count > 0);
    }

    /// <summary>Registers/unregisters with every currently in-range, type-matching ResourceNode based on live distance each frame, instead of trigger enter/exit events — a node just needs to be within range, regardless of collider/layer setup.</summary>
    private void UpdateProximityRange()
    {
        float rangeSqr = EffectiveRange * EffectiveRange;

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            if (node == null || !Affects(node.ResourceType)) continue;

            float sqrDistance = ((Vector2)node.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (sqrDistance <= rangeSqr)
            {
                if (boostedNodes.Add(node)) node.RegisterBooster(this);
            }
            else
            {
                if (boostedNodes.Remove(node)) node.UnregisterBooster(this);
            }
        }
    }

    private bool Affects(ResourceManager.ResourceType type)
    {
        if (affectedTypes == null) return false;

        foreach (ResourceManager.ResourceType affected in affectedTypes)
        {
            if (affected == type) return true;
        }

        return false;
    }

    private void OnDisable()
    {
        // Building destroyed/disabled while still boosting nodes — release them all.
        foreach (ResourceNode node in boostedNodes)
        {
            if (node != null) node.UnregisterBooster(this);
        }

        boostedNodes.Clear();
    }
}
