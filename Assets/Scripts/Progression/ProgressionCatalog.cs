using UnityEngine;

/// <summary>
/// Turns a raw lifetime XP total (see PlayerProgression) into a level, and a
/// level into which modifiers the player has unlocked.
///
/// Goes on the SAME prefab GameObject as GameModifierCatalog. That's not
/// incidental: unlock ORDER is the catalog's own array order — level 1 unlocks
/// Modifiers[0], level 2 unlocks Modifiers[1], and so on — so the two are the
/// same authored decision and reordering the catalog reorders the unlocks with
/// no second list to keep in sync. By default max level is simply how many
/// modifiers exist, so adding a seventh catalog entry raises the ceiling on its
/// own; Level Cap overrides that when the two should differ.
///
/// Kept a separate component rather than more fields on GameModifierCatalog so
/// that class stays purely "the authored list of modifiers", and so the sibling
/// is resolved with an on-demand GetComponent rather than a serialized
/// reference — this is only ever touched as a prefab ASSET, never instantiated,
/// so Awake never runs (the same constraint, and the same workaround, as
/// GameModifierDefinition.Effects). That also means zero Inspector wiring
/// between the two halves.
///
/// The XP curve is authored level by level rather than computed, so any single
/// level can be hand-tuned. It escalates because score-per-run grows steeply
/// with difficulty and stacked modifiers — a flat cost would make late levels
/// arrive faster than early ones. The array needn't cover every level: past its
/// end, levels cost the LARGEST value in it, so the curve can be authored only
/// as far as it's interesting and then plateau at its most expensive step.
///
/// Because nothing stores a level (see PlayerProgression — only the raw XP
/// total is saved), retuning the curve re-levels every existing save to match
/// rather than leaving a stored number disagreeing with it. Raising a cost can
/// therefore move a player DOWN a level; that's the intended trade for never
/// having a save that contradicts its own curve.
/// </summary>
public class ProgressionCatalog : MonoBehaviour
{
    [Header("XP Curve")]
    [Tooltip("XP needed to advance FROM each level: element 0 is level 0 -> 1, element 1 is level 1 -> 2, and so on. It doesn't have to reach Level Cap — every level past the end of this array costs whatever the LARGEST entry here is. Leave it empty to stop progression entirely.")]
    [SerializeField] private int[] xpPerLevel = { 2000, 3000, 4500, 6500, 9000, 12000 };

    [Header("Cap")]
    [Tooltip("Highest level a player can reach. 0 means 'one level per modifier in the catalog' — the ceiling then rises on its own whenever a modifier is added, and no level is ever left unlocking nothing. Any positive value caps it there instead; setting it BELOW the number of modifiers makes the ones past it permanently unreachable, which is warned about on edit.")]
    [SerializeField, Min(0)] private int levelCap;

    private GameModifierCatalog cachedCatalog;

    /// <summary>The modifier list this progression unlocks through. Resolved on demand, not in Awake — see class doc comment.</summary>
    private GameModifierCatalog Catalog
    {
        get
        {
            if (cachedCatalog == null) cachedCatalog = GetComponent<GameModifierCatalog>();
            return cachedCatalog;
        }
    }

    /// <summary>
    /// The highest level reachable. Level Cap when it's set; otherwise one per
    /// modifier in the catalog, since each level unlocks exactly one and a
    /// level with nothing behind it isn't worth reaching. Past this, XP stops
    /// buying anything.
    /// </summary>
    public int MaxLevel
    {
        get
        {
            // An explicit cap is authoritative and needs no catalog at all —
            // levels can outnumber modifiers deliberately, and the warning for
            // the reverse (modifiers stranded above the cap) is in OnValidate,
            // not here, so this stays free to call every frame.
            if (levelCap > 0) return levelCap;

            GameModifierCatalog catalog = Catalog;
            if (catalog == null || catalog.Modifiers == null)
            {
                Debug.LogWarning("ProgressionCatalog: no GameModifierCatalog on this GameObject and no Level Cap set — put both components on the same prefab, or set a cap. Max level is 0, so nothing will ever unlock.");
                return 0;
            }

            return catalog.Modifiers.Length;
        }
    }

    /// <summary>
    /// XP needed to get from fromLevel to fromLevel + 1, straight out of the
    /// authored array. Levels past its end cost the LARGEST entry in it, so a
    /// curve that doesn't reach Level Cap plateaus at its most expensive step
    /// rather than becoming free.
    ///
    /// Deliberately the largest rather than the last entry: they're the same
    /// thing for the ascending curve this is normally authored as, but if a
    /// dip is ever tuned into the tail, "last" would make every level beyond
    /// the array cheaper than the peak the player already paid — an
    /// accelerating end-game, which is the one shape this curve exists to
    /// avoid. Largest can only ever plateau.
    /// </summary>
    public int XpToAdvance(int fromLevel)
    {
        if (xpPerLevel == null || xpPerLevel.Length == 0) return 0;

        if (fromLevel >= 0 && fromLevel < xpPerLevel.Length) return Mathf.Max(0, xpPerLevel[fromLevel]);

        return LargestAuthoredCost();
    }

    /// <summary>The most expensive authored level, which every level past the end of the curve costs. Rescanned per call rather than cached: the array is a handful of entries, and a cache would need invalidating whenever it's edited in the Inspector — including mid-play, which is exactly when this gets retuned.</summary>
    private int LargestAuthoredCost()
    {
        int largest = 0;
        for (int i = 0; i < xpPerLevel.Length; i++)
        {
            if (xpPerLevel[i] > largest) largest = xpPerLevel[i];
        }

        return largest;
    }

    /// <summary>Editor-only sanity check for the one Level Cap mistake that silently costs the player content: capping below the catalog's length strands every modifier past the cap, unreachable forever. The opposite (a cap above the catalog) is deliberate-able — those levels just unlock nothing — so it isn't flagged.</summary>
    private void OnValidate()
    {
        if (levelCap <= 0) return;

        GameModifierCatalog catalog = GetComponent<GameModifierCatalog>();
        int modifierCount = catalog != null && catalog.Modifiers != null ? catalog.Modifiers.Length : 0;
        if (modifierCount <= levelCap) return;

        Debug.LogWarning($"ProgressionCatalog: Level Cap is {levelCap} but the catalog holds {modifierCount} modifiers, so the last {modifierCount - levelCap} can never be unlocked. Raise the cap, or set it to 0 to track the catalog automatically.", this);
    }

    /// <summary>The level a given lifetime XP total works out to, capped at MaxLevel. Walks the curve rather than storing a level anywhere, so retuning the curve re-levels an existing save instead of leaving the two disagreeing.</summary>
    public int LevelFromXp(int totalXp)
    {
        int level = 0;
        int remaining = Mathf.Max(0, totalXp);
        int max = MaxLevel;

        while (level < max)
        {
            int needed = XpToAdvance(level);
            // A zero-cost level would loop forever; treat an unauthored curve
            // as "no further levels" rather than instantly maxing the player.
            if (needed <= 0 || remaining < needed) break;

            remaining -= needed;
            level++;
        }

        return level;
    }

    /// <summary>How much of the current level's requirement a given XP total has already covered — the numerator of the menu/end-of-run progress bar. Always 0 at max level, where the bar is pinned full instead.</summary>
    public int XpIntoLevel(int totalXp)
    {
        int remaining = Mathf.Max(0, totalXp);
        int max = MaxLevel;

        for (int level = 0; level < max; level++)
        {
            int needed = XpToAdvance(level);
            if (needed <= 0 || remaining < needed) return remaining;

            remaining -= needed;
        }

        return 0;
    }

    /// <summary>The modifier a given level unlocks, or null if that level is out of range (0, or past the end of the catalog). Level N unlocks Modifiers[N - 1], so level 0 — a brand-new player — unlocks nothing.</summary>
    public GameModifierDefinition UnlockedAt(int level)
    {
        GameModifierCatalog catalog = Catalog;
        if (catalog == null || catalog.Modifiers == null) return null;
        if (level < 1 || level > catalog.Modifiers.Length) return null;

        return catalog.Modifiers[level - 1];
    }

    /// <summary>Whether a modifier is available to a player at the given level. A modifier the catalog doesn't contain reads as locked — the safe answer, and the same outcome GameModifierManager already gives an unrecognized saved id.</summary>
    public bool IsUnlockedAtLevel(GameModifierDefinition modifier, int level)
    {
        GameModifierCatalog catalog = Catalog;
        if (modifier == null || catalog == null || catalog.Modifiers == null) return false;

        for (int i = 0; i < catalog.Modifiers.Length; i++)
        {
            if (catalog.Modifiers[i] == modifier) return i < level;
        }

        return false;
    }

    /// <summary>The level a modifier becomes available at, or 0 if it isn't in the catalog. For the menu's "Unlocks at Level N" hint on a locked tile.</summary>
    public int UnlockLevelOf(GameModifierDefinition modifier)
    {
        GameModifierCatalog catalog = Catalog;
        if (modifier == null || catalog == null || catalog.Modifiers == null) return 0;

        for (int i = 0; i < catalog.Modifiers.Length; i++)
        {
            if (catalog.Modifiers[i] == modifier) return i + 1;
        }

        return 0;
    }
}
