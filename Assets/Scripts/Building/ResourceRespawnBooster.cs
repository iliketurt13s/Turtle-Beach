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
public class ResourceRespawnBooster : MonoBehaviour, IHasPlacementRange
{
    /// <summary>Which building-branch upgrade track (see FertilizerRangeUpgradeCard/PetRockRangeUpgradeCard etc.) this instance reads its live bonuses from — Fertilizer, PetRock and Algae are the same script/component but must upgrade independently, and Affected Types alone isn't a reliable way to tell them apart (it's free-form and could be reconfigured), so this is set explicitly per prefab instead.</summary>
    private enum BoosterKind { Fertilizer, PetRock, Algae, PlanterPot }

    [Tooltip("Which building this is for upgrade-card purposes (Fertilizer vs Pet Rock vs Algae vs Planter Pot) — must match the prefab, since they all share this script but upgrade independently. Algae and Planter Pot additionally do nothing at all until their own branch card (Algae Bloom / Root Network) is picked.")]
    [SerializeField] private BoosterKind kind = BoosterKind.Fertilizer;
    [Tooltip("Resource types this building speeds up the respawn of.")]
    [SerializeField] private ResourceManager.ResourceType[] affectedTypes;
    [Tooltip("Additive bonus to respawn speed for every node in range, e.g. 1.0 = +100% (doubles) on its own. Stacks linearly with any other booster also in range of the same node.")]
    [SerializeField] private float respawnSpeedBonus = 1f;
    [Tooltip("Radius within which a matching ResourceNode's respawn is boosted.")]
    [SerializeField] public float range = 2f;
    [Tooltip("Particle effect (or any visual) toggled on for as long as this booster is actively boosting at least one node, off otherwise. Leave unassigned for no visual cue.")]
    [SerializeField] private GameObject boostVisual;
    [Tooltip("Circle-shaped particle effect (see its Shape module) whose emission radius is kept in sync with Effective Range every frame, so a Fertilizer/Pet Rock-branch range upgrade visibly grows it too. Leave unassigned to skip syncing.")]
    [SerializeField] private ParticleSystem rangeParticles;

    /// <summary>respawnSpeedBonus plus any run-wide bonus from this booster's building-branch upgrade cards — read live rather than cached, so a card picked mid-run applies immediately to this booster whether it was already placed or built afterward (see Campfire.EffectiveSpeedBonus for the same pattern).</summary>
    public float RespawnSpeedBonus => respawnSpeedBonus + KindRespawnBonus;

    /// <summary>range plus any run-wide bonus from this booster's building-branch upgrade cards — read live, same rationale as RespawnSpeedBonus.</summary>
    private float EffectiveRange => range + KindRangeBonus;

    /// <summary>IHasPlacementRange implementation, so BuildModeController's ghost shows this booster's real (upgrade-inclusive) range while it's selected for placement.</summary>
    public float PlacementRange => EffectiveRange;

    private float KindRangeBonus
    {
        get
        {
            if (UpgradeManager.Instance == null) return 0f;
            return kind switch
            {
                BoosterKind.Fertilizer => UpgradeManager.Instance.FertilizerRangeBonus,
                BoosterKind.PetRock => UpgradeManager.Instance.PetRockRangeBonus,
                _ => 0f, // Algae and Planter Pot have no range/respawn branch cards of their own — only the on/off unlock below
            };
        }
    }

    private float KindRespawnBonus
    {
        get
        {
            if (UpgradeManager.Instance == null) return 0f;
            return kind switch
            {
                BoosterKind.Fertilizer => UpgradeManager.Instance.FertilizerRespawnBonus,
                BoosterKind.PetRock => UpgradeManager.Instance.PetRockRespawnBonus,
                _ => 0f,
            };
        }
    }

    /// <summary>
    /// False for the two kinds that are gated behind a card of their own — an
    /// Algae pile before Algae Bloom, a Planter Pot before Root Network. Both
    /// prefabs carry this component from the moment they are placed, and this
    /// is what keeps them completely inert (no boosting, no visual) until that
    /// card turns the branch on, so already-placed ones start fertilizing the
    /// instant it is picked with nothing to retrofit.
    /// </summary>
    private bool IsBoostingEnabled => kind switch
    {
        BoosterKind.Algae => UpgradeManager.Instance != null && UpgradeManager.Instance.AlgaeFertilizerUnlocked,
        BoosterKind.PlanterPot => UpgradeManager.Instance != null && UpgradeManager.Instance.PlanterPotFertilizerUnlocked,
        _ => true,
    };

    private readonly HashSet<ResourceNode> boostedNodes = new HashSet<ResourceNode>();

    /// <summary>rangeParticles' originally-authored emission rate, captured once before SyncRangeParticles ever scales it — the baseline a range upgrade's area-ratio multiplier is applied to (see SyncRangeParticles).</summary>
    private float baseParticleEmissionRate;

    private void Awake()
    {
        if (rangeParticles != null) baseParticleEmissionRate = rangeParticles.emission.rateOverTime.constant;
    }

    private void Update()
    {
        if (!IsBoostingEnabled)
        {
            ReleaseAllBoosts();
            if (boostVisual != null) boostVisual.SetActive(false);
            return;
        }

        UpdateProximityRange();
        if (boostVisual != null) boostVisual.SetActive(boostedNodes.Count > 0);
        SyncRangeParticles();
    }

    /// <summary>Keeps rangeParticles' circle Shape module radius matching EffectiveRange every frame, so it grows/shrinks along with a Fertilizer/Pet Rock-branch range upgrade exactly like the actual boost radius does — and scales emission rate by the same area ratio (EffectiveRange/range)^2, since a circle's area grows with the square of its radius, so particle count alone staying fixed would thin the density out as the circle grows (or overcrowd it as the circle shrinks). baseParticleEmissionRate is the untouched authored rate this scales from, not whatever rateOverTime currently is.</summary>
    private void SyncRangeParticles()
    {
        if (rangeParticles == null) return;

        float effectiveRange = EffectiveRange;

        ParticleSystem.ShapeModule shape = rangeParticles.shape;
        shape.radius = effectiveRange;

        if (range > 0f)
        {
            ParticleSystem.EmissionModule emission = rangeParticles.emission;
            float areaRatio = (effectiveRange * effectiveRange) / (range * range);
            emission.rateOverTime = baseParticleEmissionRate * areaRatio;
        }
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
        ReleaseAllBoosts();
    }

    /// <summary>Unregisters from every node currently being boosted. Shared by OnDisable and by the Algae-not-yet-unlocked early-out in Update, so switching off mid-boost can never leave a node holding a stale reference that keeps speeding its respawn up forever.</summary>
    private void ReleaseAllBoosts()
    {
        if (boostedNodes.Count == 0) return;

        foreach (ResourceNode node in boostedNodes)
        {
            if (node != null) node.UnregisterBooster(this);
        }

        boostedNodes.Clear();
    }
}
