using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Two visuals: a base (always visible) and a flame (shown only during the
/// day — hidden during storms). Any turtle within range gains a movement
/// speed buff; the buff is on only while the turtle is actually within range,
/// checked every frame by live distance (see UpdateProximityRange) rather
/// than a trigger collider — no physical collision/layer requirements,
/// entering applies it and leaving removes it immediately, no lingering
/// timer. Multiple overlapping campfires stack their bonuses linearly on a
/// turtle (see TurtleAgent.ApplyCampfireSpeedBuff), rather than the
/// strongest one winning.
/// </summary>
public class Campfire : MonoBehaviour
{
    [Tooltip("Hidden during storms, shown during the day.")]
    [SerializeField] private GameObject flameVisual;
    [Tooltip("Additive speed bonus applied to any turtle within range, e.g. 0.4 = +40% on its own.")]
    [SerializeField] private float speedBonus = 0.4f;
    [Tooltip("Radius within which a turtle gets the speed buff.")]
    [SerializeField] private float range = 2f;
    [Tooltip("Circle-shaped particle effect (see its Shape module) whose emission radius is kept in sync with Effective Range every frame, so a Campfire-branch range upgrade visibly grows it too. Leave unassigned to skip syncing.")]
    [SerializeField] public ParticleSystem rangeParticles;

    /// <summary>speedBonus plus any run-wide bonus from Campfire-branch upgrade cards (see UpgradeManager.CampfireSpeedBonus) — read live rather than cached, so a card picked mid-run applies immediately to this Campfire whether it was already placed or built afterward.</summary>
    private float EffectiveSpeedBonus => speedBonus + (UpgradeManager.Instance != null ? UpgradeManager.Instance.CampfireSpeedBonus : 0f);

    /// <summary>range plus any run-wide bonus from Campfire-branch upgrade cards (see UpgradeManager.CampfireRangeBonus) — read live, same rationale as EffectiveSpeedBonus.</summary>
    private float EffectiveRange => range + (UpgradeManager.Instance != null ? UpgradeManager.Instance.CampfireRangeBonus : 0f);

    // Records the exact bonus amount applied to each in-range turtle, not
    // just which turtles are in range — EffectiveSpeedBonus can change
    // mid-buff (an upgrade card picked while a turtle is still inside), and
    // removal must subtract exactly what was added or the turtle's
    // campfireBonusTotal would drift (see TurtleAgent.RemoveCampfireSpeedBuff).
    private readonly Dictionary<TurtleAgent, float> turtlesInRange = new Dictionary<TurtleAgent, float>();

    /// <summary>rangeParticles' originally-authored emission rate, captured once before SyncRangeParticles ever scales it — the baseline a range upgrade's area-ratio multiplier is applied to (see SyncRangeParticles).</summary>
    private float baseParticleEmissionRate;

    private void Awake()
    {
        if (rangeParticles != null) baseParticleEmissionRate = rangeParticles.emission.rateOverTime.constant;
    }

    private void Update()
    {
        bool storming = DayStormCycle.IsStorming;
        if (flameVisual != null) flameVisual.SetActive(!storming);
        SyncRangeParticles();

        // Daytime-only, matching the flame visual and the Campfire card's own
        // "*only works during the day" description — release anyone already
        // buffed the instant a storm starts (not just stop handing out new
        // buffs), rather than letting turtles that wandered in during the day
        // keep an un-earned buff for the whole night.
        if (storming)
        {
            ReleaseAllBuffs();
            return;
        }

        UpdateProximityRange();
    }

    /// <summary>Applies/removes the speed buff based on live distance to each turtle every frame, instead of trigger enter/exit events — a turtle just needs to be within range, regardless of collider/layer setup.</summary>
    private void UpdateProximityRange()
    {
        float rangeSqr = EffectiveRange * EffectiveRange;
        float bonus = EffectiveSpeedBonus;

        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle == null) continue;

            float sqrDistance = ((Vector2)turtle.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (sqrDistance <= rangeSqr)
            {
                if (!turtlesInRange.ContainsKey(turtle))
                {
                    turtlesInRange[turtle] = bonus;
                    turtle.ApplyCampfireSpeedBuff(bonus);
                }
            }
            else if (turtlesInRange.TryGetValue(turtle, out float appliedBonus))
            {
                turtlesInRange.Remove(turtle);
                turtle.RemoveCampfireSpeedBuff(appliedBonus);
            }
        }
    }

    /// <summary>Keeps rangeParticles' circle Shape module radius matching EffectiveRange every frame, so it grows/shrinks along with a Campfire-branch range upgrade exactly like the actual buff radius does — and scales emission rate by the same area ratio (EffectiveRange/range)^2, since a circle's area grows with the square of its radius, so particle count alone staying fixed would thin the density out as the circle grows (or overcrowd it as the circle shrinks). baseParticleEmissionRate is the untouched authored rate this scales from, not whatever rateOverTime currently is.</summary>
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

    private void OnDisable()
    {
        // Building destroyed while turtles were still inside — release them all.
        ReleaseAllBuffs();
    }

    private void ReleaseAllBuffs()
    {
        foreach (KeyValuePair<TurtleAgent, float> entry in turtlesInRange)
        {
            if (entry.Key != null) entry.Key.RemoveCampfireSpeedBuff(entry.Value);
        }

        turtlesInRange.Clear();
    }
}
