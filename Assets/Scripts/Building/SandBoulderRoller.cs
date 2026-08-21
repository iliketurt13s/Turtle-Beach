using UnityEngine;

/// <summary>
/// A Watchtower variant: a stationed turtle rolls heavy sand boulders, but
/// only ever straight up, down, left or right — never on a free diagonal — and
/// each boulder ploughs on through everything in that lane instead of breaking
/// on the first piece of trash it touches.
///
/// The trade is deliberate and is the whole design: a Watchtower covers a full
/// circle but stops one piece of trash per shot, while this covers four narrow
/// lanes and can clear an entire column of a clump in one go. That makes WHERE
/// it is placed the interesting decision — lined up with an approach it can
/// rake, rather than simply near the fighting.
///
/// A subclass rather than a parallel script (see Watchtower's own doc comment):
/// stationing, dismissal, recall, eviction and the aim-and-fire loop are all
/// identical and inherited untouched. Only three things are overridden — which
/// trash counts as a target, which way the boulder actually goes, and which
/// upgrade tracks the range and fire rate read from.
///
/// Both upgrade branches (range and pierce) read their own totals off
/// UpgradeManager rather than a Watchtower one, so a Watchtower card never
/// silently buffs a roller and vice versa, even though the two share this code.
/// </summary>
public class SandBoulderRoller : Watchtower
{
    /// <summary>The four lanes a boulder can travel. Authored as a constant rather than derived from the roller's own rotation: these are world axes, matching how the player reads the grid the building sits on, and a prefab dropped in with a stray rotation shouldn't quietly skew its firing lines.</summary>
    private static readonly Vector2[] Lanes = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

    [Header("Boulder Roller")]
    [Tooltip("How far off a perfect lane (in degrees) a piece of trash may sit and still be shot at. Small values make the roller precise but often idle; large values let it fire at trash the boulder will visibly pass beside, since the boulder always travels the exact lane regardless of where the target actually is. Roughly half the angle between lanes (45) would mean it always has a target somewhere and misses constantly.")]
    [SerializeField, Range(1f, 45f)] private float laneTolerance = 18f;

    [Tooltip("Seconds between shots. Deliberately its own field rather than inheriting the Watchtower's fire-rate upgrades — a roller shot is worth far more than a sand ball when it lines up, so the two are balanced separately.")]
    [SerializeField, Min(0.05f)] private float boulderFireInterval = 2.5f;

    /// <summary>Base radius plus this building's own range branch — never the Watchtower's, which is a different building's upgrade track.</summary>
    protected override float EffectiveTargetRadius =>
        targetRadius + (UpgradeManager.Instance != null ? UpgradeManager.Instance.BoulderRollerRangeBonus : 0f);

    /// <summary>This roller's own interval, with no Watchtower fire-rate bonus applied — see boulderFireInterval.</summary>
    protected override float EffectiveFireInterval => boulderFireInterval;

    /// <summary>
    /// The nearest trash in range that actually sits in one of the four lanes.
    ///
    /// Filtered inside the search rather than after it (see
    /// TrashHealth.FindNearest's filter parameter), so a piece of trash sitting
    /// closer but off-lane can't mask one further out that this roller could
    /// genuinely hit — which would otherwise leave it standing idle in the
    /// middle of a fight for no visible reason.
    /// </summary>
    protected override TrashHealth FindTarget()
    {
        Vector2 origin = FireOrigin;
        return TrashHealth.FindNearest(origin, EffectiveTargetRadius,
            trash => IsInLane((Vector2)trash.transform.position - origin));
    }

    /// <summary>Fires along the exact lane the target was found in, not at the target itself — everything else in that lane gets hit on the way through, which is the point of a boulder.</summary>
    protected override void FireAt(TrashHealth target)
    {
        if (sandBallPrefab == null) return;

        Vector2 origin = FireOrigin;
        Vector2 lane = NearestLane((Vector2)target.transform.position - origin);

        GameObject instance = Instantiate(sandBallPrefab, origin, Quaternion.identity);
        int bonusPierce = UpgradeManager.Instance != null ? UpgradeManager.Instance.BoulderRollerPierceBonus : 0;

        // No Watchtower damage bonus here for the same reason the fire rate is
        // separate: that branch belongs to a different building.
        instance.GetComponent<SandBall>()?.LaunchInDirection(lane, bonusDamage: 0, bonusPierce: bonusPierce);
    }

    /// <summary>Whether an offset from this roller falls within Lane Tolerance of any of the four lanes.</summary>
    private bool IsInLane(Vector2 offset)
    {
        if (offset.sqrMagnitude < 0.0001f) return true;

        return Vector2.Angle(offset, NearestLane(offset)) <= laneTolerance;
    }

    /// <summary>Whichever of the four lanes an offset points most nearly along. Compared by dot product rather than by angle, since for unit lanes the largest dot is the smallest angle and it avoids four trig calls per candidate in a scan that runs over every live piece of trash.</summary>
    private static Vector2 NearestLane(Vector2 offset)
    {
        Vector2 best = Lanes[0];
        float bestDot = float.MinValue;

        foreach (Vector2 lane in Lanes)
        {
            float dot = Vector2.Dot(offset, lane);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = lane;
            }
        }

        return best;
    }

    /// <summary>Debug-only Scene view visualization: draws the four firing lanes out to the roller's live range, plus the tolerance wedge around each, so placement can be lined up against an approach by eye instead of guessed at.</summary>
    private void OnDrawGizmosSelected()
    {
        float range = EffectiveTargetRadius;
        Vector3 origin = firePoint != null ? firePoint.position : transform.position;

        Gizmos.color = new Color(1f, 0.85f, 0.35f, 0.9f);
        foreach (Vector2 lane in Lanes)
        {
            Gizmos.DrawLine(origin, origin + (Vector3)(lane * range));

            Vector3 edgeA = Quaternion.Euler(0f, 0f, laneTolerance) * (Vector3)(lane * range);
            Vector3 edgeB = Quaternion.Euler(0f, 0f, -laneTolerance) * (Vector3)(lane * range);
            Gizmos.DrawLine(origin, origin + edgeA);
            Gizmos.DrawLine(origin, origin + edgeB);
        }
    }
}
