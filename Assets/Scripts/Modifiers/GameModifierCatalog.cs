using UnityEngine;

/// <summary>
/// The single authored list of every run modifier in the game. Lives on one
/// prefab asset that BOTH scenes reference — ModifierSelectionUI in the menu
/// (to build the scrollable pick list) and GameModifierManager in the gameplay
/// scene (to resolve the player's saved ids back into real definitions).
///
/// Deliberately one shared asset rather than a serialized array on each of
/// those two components: those live in different scenes, so two arrays would
/// have to be kept in sync by hand forever, and a modifier present in the menu
/// but missing from the gameplay scene's copy would be silently pickable and
/// then silently do nothing. Registering a new modifier is one edit here.
/// </summary>
public class GameModifierCatalog : MonoBehaviour
{
    [Tooltip("Every modifier the player can pick, in the order they appear in the menu's scroll list. Add new modifier prefabs here — this is the only place they need registering.")]
    [SerializeField] private GameModifierDefinition[] modifiers;

    public GameModifierDefinition[] Modifiers => modifiers;

    /// <summary>Looks up a modifier by its stable Id, or null if this catalog has no such entry — which is the expected outcome for an id saved by an older build whose modifier has since been removed, so callers should skip rather than treat it as an error.</summary>
    public GameModifierDefinition FindById(string id)
    {
        if (modifiers == null || string.IsNullOrEmpty(id)) return null;

        foreach (GameModifierDefinition modifier in modifiers)
        {
            if (modifier != null && modifier.Id == id) return modifier;
        }

        return null;
    }
}
