using UnityEngine;

/// <summary>Rune granting a turtle a permanent boost to every fin's propulsion frequency (more frequent bursts, not stronger ones).</summary>
public class FlipperRune : RuneEffect
{
    [Tooltip("Multiplier applied once to every propelling fin's oscillation frequency.")]
    [SerializeField] private float frequencyMultiplier = 1.5f;

    protected override bool AlreadyHasBuff(TurtleAgent turtle) => turtle.HasFlipperBuff;

    protected override void ApplyBuff(TurtleAgent turtle)
    {
        turtle.ApplyFlipperBuff(frequencyMultiplier);
    }
}
