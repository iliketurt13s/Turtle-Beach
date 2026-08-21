/// <summary>
/// Crab-branch upgrade card: only offered once at least one crab has been
/// recruited (see IRequiresUpgrade). Crabs stop sitting storms out at the nest
/// and start chasing and attacking trash exactly like turtles do — see
/// TurtleAgent.CanAttackTrash, the one flag gating both their aggro and their
/// damage, so before this card a crab neither seeks trash out nor hurts what
/// it bumps into.
///
/// Their much faster attack cadence is not set here: it's the Crab prefab's
/// own TurtleHeadHitbox Max Reload Interval, which reloads the hitbox on a
/// timer rather than once per swim stroke.
/// </summary>
public class CrabCombatUpgradeCard : UpgradeCardDefinition, IRequiresUpgrade
{
    public bool IsPrerequisiteMet => UpgradeManager.Instance != null && UpgradeManager.Instance.CrabCount > 0;

    public override void Apply() => UpgradeManager.Instance?.UnlockCrabCombat();
}
