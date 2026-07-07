using UnityEngine;

/// <summary>Rune granting a turtle a permanent bonus to damage dealt to trash.</summary>
public class HardHatRune : RuneEffect
{
    [Tooltip("Extra damage a buffed turtle deals to trash per hit.")]
    [SerializeField] private int bonusDamage = 1;

    protected override bool AlreadyHasBuff(TurtleAgent turtle) => turtle.HasHardHatBuff;

    protected override void ApplyBuff(TurtleAgent turtle)
    {
        turtle.ApplyHardHatBuff(bonusDamage);
    }
}
