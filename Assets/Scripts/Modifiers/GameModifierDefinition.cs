using UnityEngine;

/// <summary>
/// One opt-in run modifier, offered on the menu's options screen before a run
/// starts (see ModifierSelectionUI) — the player takes on some extra hardship
/// in exchange for a score multiplier at the end, rather than having hardship
/// forced on them mid-run the way the old hazard upgrade picks did.
///
/// Same "definitions live on prefabs, not new code" shape as
/// UpgradeCardDefinition/BuildableDefinition/TrashDefinition: a new modifier is
/// a new prefab with this component plus an entry in GameModifierCatalog, no
/// script changes. Unlike UpgradeCardDefinition this class is deliberately
/// concrete rather than abstract — it has no Apply() of its own to override,
/// because the actual gameplay effect comes from whichever
/// UpgradeCardDefinition components sit alongside it on this same GameObject
/// (TrashSpeedUpgradeCard, UnlockTrashUpgradeCard, ...). Those card scripts are
/// reused verbatim, so every modifier's behavior stays in one already-tested
/// place; this type only adds the run-start timing, the menu presentation, and
/// the score multiplier.
///
/// The cards are read off this GameObject rather than referenced as separate
/// card prefabs because nothing draws them as cards anymore — the harmful pool
/// they used to be picked from is gone, so a standalone card prefab per effect
/// would be an empty layer of indirection. One prefab per modifier, carrying
/// both this component and its effect script(s), is the whole thing.
///
/// Presentation falls back to the first effect card's own name/description/icon
/// whenever this component's matching field is left blank, so a modifier whose
/// card already spells all that out doesn't have to have it typed twice into
/// two components on the same object.
///
/// Id is what survives the Menu -> GameScene boundary: the menu writes the
/// selected ids into PlayerPrefs as a comma-separated list and
/// GameModifierManager resolves them back through the catalog on the other
/// side (PlayerPrefs is the only cross-scene channel this project has — there
/// are no DontDestroyOnLoad objects anywhere). It must be unique within the
/// catalog and must never be renamed once players have runs saved against it,
/// or their selection silently drops on the next launch. Display Name is free
/// to change at any time; Id is not.
/// </summary>
public class GameModifierDefinition : MonoBehaviour
{
    [Tooltip("Stable unique key used to remember this modifier across the scene load (PlayerPrefs) — must be unique in the catalog, must not contain a comma, and must never be renamed once released. Use a short lowercase slug, e.g. \"sharp_edges\". Falls back to the prefab's object name if left blank.")]
    [SerializeField] private string id;

    [Header("Presentation")]
    [Tooltip("Leave blank to use the effect card's own Display Name.")]
    [SerializeField] private string displayName;
    [Tooltip("Leave blank to use the effect card's own Description.")]
    [SerializeField, TextArea] private string description;
    [Tooltip("Optional standalone icon for the side detail panel only — the grid tile itself uses Unpressed/Pressed Sprite below, which already include their own background. Leave unassigned to fall back to the effect card's icon, then to Unpressed Sprite.")]
    [SerializeField] private Sprite icon;

    [Header("Tile Art")]
    [Tooltip("This modifier's tile in the menu grid while it is NOT selected — the full image, artwork and background together. Leave unassigned to use whatever generic face the tile prefab itself was authored with; every tile is cloned from that one prefab, so this is how a modifier gets art of its own.")]
    [SerializeField] private Sprite unpressedSprite;
    [Tooltip("This modifier's tile while it IS selected. Leave unassigned to fall back to the tile prefab's own pressed sprite.")]
    [SerializeField] private Sprite pressedSprite;

    [Header("Effect")]
    [Tooltip("How much this modifier multiplies the end-of-run score by, e.g. 0.25 = x1.25. Selected modifiers COMPOUND rather than add (see GameModifierManager.CalculateMultiplier), so three 0.25 modifiers give 1.25^3 = x1.95, not x1.75. Set this proportional to how much the modifier actually hurts.")]
    [SerializeField, Min(0f)] private float scoreMultiplierBonus = 0.25f;

    /// <summary>Stable cross-scene key — see class doc comment. Falls back to the GameObject's name so a prefab with a blank Id still resolves rather than silently matching every other blank one.</summary>
    public string Id => string.IsNullOrWhiteSpace(id) ? name : id;
    /// <summary>This component's own Display Name, or the effect card's if blank, or the Id as a last resort — never null, so it's always safe to assign straight to a TMP_Text.</summary>
    public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName : (FirstEffect?.DisplayName ?? Id);
    /// <summary>This component's own Description, or the effect card's if blank. Empty rather than null when neither is set (a null would go straight into a TMP_Text).</summary>
    public string Description => !string.IsNullOrEmpty(description) ? description : (FirstEffect?.Description ?? string.Empty);
    /// <summary>Icon for the side detail panel: this component's own, else the effect card's, else the tile's unpressed art — so the panel still has something to show without a third sprite being authored. Only used there; the grid tile draws Unpressed/Pressed Sprite directly.</summary>
    public Sprite Icon => icon != null ? icon : (FirstEffect?.Icon != null ? FirstEffect.Icon : unpressedSprite);
    /// <summary>This modifier's whole unselected tile — artwork and background in one sprite. Null keeps the tile prefab's generic one (see MenuButtonVisuals.SetSprites).</summary>
    public Sprite UnpressedSprite => unpressedSprite;
    /// <summary>This modifier's whole selected tile. Null keeps the tile prefab's generic one.</summary>
    public Sprite PressedSprite => pressedSprite;
    public float ScoreMultiplierBonus => scoreMultiplierBonus;

    /// <summary>The effect card this modifier borrows its presentation from when a field above is left blank, or null if no card is attached yet.</summary>
    private UpgradeCardDefinition FirstEffect
    {
        get
        {
            UpgradeCardDefinition[] effects = Effects;
            return effects.Length > 0 ? effects[0] : null;
        }
    }

    /// <summary>
    /// The upgrade card components sitting on this same GameObject — the
    /// modifier's actual gameplay effect. Usually one; several is fine when a
    /// modifier bundles more than one change (e.g. unlock a trash type AND
    /// buff its damage).
    ///
    /// Fetched on demand rather than cached in Awake, because this component
    /// is only ever touched as a prefab ASSET (the catalog holds asset
    /// references, and both the menu and GameModifierManager read straight off
    /// those) — Awake never runs on an asset that's never instantiated, so a
    /// cache populated there would be permanently empty.
    /// </summary>
    public UpgradeCardDefinition[] Effects => GetComponents<UpgradeCardDefinition>();

    /// <summary>Applies every effect card on this GameObject. Called once by GameModifierManager in Start (not Awake — the effect cards poke UpgradeManager.Instance/TrashSpawner.Instance, which are only set in those components' own Awake).</summary>
    public void Apply()
    {
        UpgradeCardDefinition[] effects = Effects;
        if (effects.Length == 0)
        {
            Debug.LogWarning($"GameModifierDefinition: \"{Id}\" has no UpgradeCardDefinition component on it, so selecting it does nothing but raise the score multiplier.");
            return;
        }

        foreach (UpgradeCardDefinition effect in effects)
        {
            // Applied straight on the prefab-asset component, never on an
            // instance — exactly how UpgradeSelectionUI.Select already calls
            // its pool entries. These cards hold no per-instance state, they
            // only push values into the scene's manager singletons.
            Debug.Log($"GameModifierDefinition: \"{Id}\" applying effect {effect.GetType().Name}.");
            effect.Apply();
        }
    }
}
