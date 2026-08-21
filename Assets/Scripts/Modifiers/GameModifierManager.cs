using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scene half of the run modifier system: resolves whichever
/// modifiers the player ticked on the menu (see GameModifierSelection) back
/// into real GameModifierDefinitions via the shared catalog, applies their
/// effects once at run start, and exposes the score multiplier they earned for
/// ScoreManager to apply at the end of the run.
///
/// Same static-Instance shape as ScoreManager/UpgradeManager and for the same
/// reason — ScoreManager has no scene-authored reference to this and just
/// reaches it through the accessor.
///
/// Effects are applied in Start, not Awake: they're UpgradeCardDefinition
/// prefabs that push into UpgradeManager.Instance and TrashSpawner.Instance,
/// and those singletons are only assigned in their own components' Awake, so
/// applying any earlier would silently no-op through their null-conditional
/// calls. Start still runs long before the first storm, which is the earliest
/// any modifier's effect can actually matter.
/// </summary>
public class GameModifierManager : MonoBehaviour
{
    public static GameModifierManager Instance { get; private set; }

    [Tooltip("The same GameModifierCatalog prefab asset the menu scene's ModifierSelectionUI points at — the shared list of every modifier in the game.")]
    [SerializeField] private GameModifierCatalog catalog;

    private readonly List<GameModifierDefinition> active = new List<GameModifierDefinition>();

    /// <summary>The modifiers actually in force this run, resolved from the menu's selection. Read by ScoreBreakdownUI to name them and by ScoreMultiplier below.</summary>
    public IReadOnlyList<GameModifierDefinition> Active => active;

    /// <summary>End-of-run score multiplier earned by this run's modifiers. Applied by ScoreManager.FinalizeRun on top of (and after) the difficulty multiplier.</summary>
    public float ScoreMultiplier => CalculateMultiplier(active);

    /// <summary>
    /// The one definition of how selected modifiers combine, shared by this
    /// manager and the menu's ModifierSelectionUI so the multiplier previewed
    /// before a run can never disagree with the one actually paid out.
    ///
    /// Modifiers COMPOUND rather than add: each one multiplies the running
    /// total by its own bonus instead of every bonus being summed onto a flat
    /// 1.0. So four +25% modifiers are worth x2.44, not x2.00 — stacking gets
    /// disproportionately more valuable the deeper you go, which is the point.
    /// Taking on a fourth hardship when the run is already brutal should be
    /// worth more than the first one was, not the same.
    /// </summary>
    public static float CalculateMultiplier(IEnumerable<GameModifierDefinition> modifiers)
    {
        float multiplier = 1f;
        if (modifiers == null) return multiplier;

        foreach (GameModifierDefinition modifier in modifiers)
        {
            if (modifier != null) multiplier *= 1f + modifier.ScoreMultiplierBonus;
        }

        return multiplier;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GameModifierManager: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveSelection();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (active.Count == 0)
        {
            Debug.Log("GameModifierManager: no modifiers active this run — score multiplier x1.00.");
            return;
        }

        foreach (GameModifierDefinition modifier in active)
        {
            Debug.Log($"GameModifierManager: applying \"{modifier.Id}\" (+{modifier.ScoreMultiplierBonus:0.##} bonus).");
            modifier.Apply();
        }

        Debug.Log($"GameModifierManager: applied {active.Count} modifier(s), score multiplier x{ScoreMultiplier:0.00}.");
    }

    /// <summary>Turns the menu's saved id list into definitions. Runs in Awake so ScoreMultiplier is already correct for anything that reads it before Start.</summary>
    private void ResolveSelection()
    {
        active.Clear();

        if (catalog == null)
        {
            Debug.LogWarning("GameModifierManager: no Catalog assigned — this run will have no modifiers and no score multiplier.");
            return;
        }

        // Same prefab, so no extra Inspector wiring — and null (no progression
        // in the project) simply means nothing is gated, which is the old
        // behavior. See ProgressionCatalog.
        ProgressionCatalog progression = catalog.GetComponent<ProgressionCatalog>();
        int level = progression != null ? progression.LevelFromXp(PlayerProgression.TotalXp) : 0;

        foreach (string id in GameModifierSelection.LoadIds())
        {
            GameModifierDefinition modifier = catalog.FindById(id);
            if (modifier == null)
            {
                // Expected whenever a modifier is removed from the catalog
                // while a player still has it saved from an earlier build —
                // drop it and carry on rather than failing the whole run.
                Debug.LogWarning($"GameModifierManager: saved modifier id \"{id}\" isn't in the catalog anymore, skipping it.");
                continue;
            }

            // The menu already refuses to tick a locked modifier, so this only
            // catches a saved list that outlived the level that earned it — a
            // progress reset, or a hand-edited PlayerPrefs. Belt and braces,
            // because the failure it prevents is silent: a modifier the player
            // hasn't unlocked quietly multiplying their score.
            if (progression != null && !progression.IsUnlockedAtLevel(modifier, level))
            {
                Debug.LogWarning($"GameModifierManager: saved modifier \"{id}\" isn't unlocked at level {level}, skipping it.");
                continue;
            }

            if (!active.Contains(modifier)) active.Add(modifier);
        }

        Debug.Log($"GameModifierManager: resolved {active.Count} modifier(s) from the catalog's {(catalog.Modifiers != null ? catalog.Modifiers.Length : 0)} entries.");
    }
}
