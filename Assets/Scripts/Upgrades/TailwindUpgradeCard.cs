using UnityEngine;

/// <summary>
/// Upgrade card ("Tailwind"): turtles swim faster the more company they have
/// nearby, so moving the population as a group beats scattering it across the
/// island — and a nest-guard picket during a storm is fast as well as safe.
///
/// Each neighbour contributes on a linear falloff (full weight touching,
/// nothing at Tailwind Radius) and the total is capped, so a swarm doesn't
/// multiply into something absurd; see TurtleAgent.UpdateTailwind, which owns
/// the scan and runs it on a timer rather than per physics step.
///
/// Stackable, but only the BONUS accumulates — repeat picks make company worth
/// more without also widening the radius or raising the stack cap, so the card
/// stays a strength upgrade rather than quietly becoming a range one too.
/// </summary>
public class TailwindUpgradeCard : UpgradeCardDefinition
{
    [Tooltip("Speed bonus at a full stack of company. 0.3 = 30% faster when surrounded. Accumulates across repeat picks.")]
    [SerializeField, Min(0f)] private float speedBonus = 0.3f;

    [Tooltip("How close another turtle has to be to contribute anything, in world units. SET rather than accumulated, so repeat picks don't silently widen it.")]
    [SerializeField, Min(0.1f)] private float radius = 3f;

    [Tooltip("Ceiling on total company, in units of \"one turtle right on top of this one\". 3 means a crowd of eight is worth no more than three close companions, so the bonus tops out instead of running away in a swarm. Also SET rather than accumulated.")]
    [SerializeField, Min(0.1f)] private float maxStack = 3f;

    public override void Apply() => UpgradeManager.Instance?.AddTailwind(speedBonus, radius, maxStack);
}
