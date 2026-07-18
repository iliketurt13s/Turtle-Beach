using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Two visuals: a base (always visible) and a flame (shown only during the
/// day — hidden during storms). Any turtle within range gains a movement
/// speed buff; the buff is on only while the turtle is actually within range,
/// checked every frame by live distance (see UpdateProximityRange, mirroring
/// FoodBuilding.UpdateProximityEating) rather than a trigger collider — no
/// physical collision/layer requirements, entering applies it and leaving
/// removes it immediately, no lingering timer. Multiple overlapping campfires
/// stack their bonuses linearly on a turtle (see
/// TurtleAgent.ApplyCampfireSpeedBuff), rather than the strongest one winning.
/// </summary>
public class Campfire : MonoBehaviour
{
    [Tooltip("Hidden during storms, shown during the day.")]
    [SerializeField] private GameObject flameVisual;
    [Tooltip("Additive speed bonus applied to any turtle within range, e.g. 0.4 = +40% on its own.")]
    [SerializeField] private float speedBonus = 0.4f;
    [Tooltip("Radius within which a turtle gets the speed buff.")]
    [SerializeField] private float range = 2f;

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

    private void Update()
    {
        if (flameVisual != null) flameVisual.SetActive(!DayStormCycle.IsStorming);
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

    private void OnDisable()
    {
        // Building destroyed while turtles were still inside — release them all.
        foreach (KeyValuePair<TurtleAgent, float> entry in turtlesInRange)
        {
            if (entry.Key != null) entry.Key.RemoveCampfireSpeedBuff(entry.Value);
        }

        turtlesInRange.Clear();
    }
}
