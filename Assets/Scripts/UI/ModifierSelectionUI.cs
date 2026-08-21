using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu-side modifier picker: builds one ModifierTileView per entry in the
/// shared GameModifierCatalog into Tile Container, tracks which are ticked, and
/// previews what the player's whole setup will multiply their final score by.
/// The player may take any number of modifiers, none included — they aren't
/// mutually exclusive.
///
/// That readout used to combine an Easy/Medium/Hard difficulty pick with the
/// modifiers, which is why it's called a TOTAL rather than a modifier
/// multiplier. Difficulty is gone from the game, so modifiers are now the only
/// thing multiplying a score, and the per-frame poll that watched the
/// difficulty group for changes went with it — a tile toggle already refreshes
/// the readout directly.
///
/// The grid is icons only (four to a row via a Grid Layout Group on the
/// container); the name, description and multiplier bonus of whichever
/// modifier the pointer is over appear in one shared detail panel beside it,
/// the same arrangement UpgradeSelectionUI uses for hovered upgrade cards.
/// Pointing at a tile shows its details; moving off falls back to the last
/// tile actually CLICKED, so a modifier the player just took stays readable
/// while they consider the next one, and the panel only empties if nothing has
/// been clicked yet.
///
/// Tiles are instantiated at runtime rather than hand-placed in the scene so
/// the grid stays purely data-driven off the catalog (adding a modifier is one
/// catalog entry, no scene edit), following the same instantiate-a-prefab-into
/// -a-layout-container pattern as ResourceHUD. Tile Container is expected to be
/// the Content object of a ScrollRect, so the grid grows past the visible frame
/// and scrolls instead of overflowing it — there will eventually be far more
/// modifiers than fit on screen at once.
///
/// The previous run's picks are restored on load (see GameModifierSelection,
/// which deliberately doesn't consume the saved value), so bouncing between
/// the menu and a run doesn't silently clear the player's setup.
///
/// Modifiers are gated behind the player's level: the grid builds every catalog
/// entry, but any whose unlock level the player hasn't reached is bound locked
/// (see ProgressionCatalog and ModifierTileView) — greyed, overlaid, and
/// unclickable. Locked tiles occupy the grid rather than being filtered out, so
/// its shape doesn't shift as modifiers arrive and the player can see how much
/// is left to come; catalog order IS unlock order, so the locked ones naturally
/// trail with no sorting needed. What a locked modifier DOES, though, stays
/// hidden — hovering one shows only the level that unlocks it, never its name,
/// description or score bonus (see ShowDetails). The player learns there's
/// something there, not what it is.
///
/// A missing Progression reference fails OPEN (everything unlocked, with a
/// warning) — a mis-wired field should degrade to the old always-available
/// behavior rather than locking the player out of the game's content.
/// </summary>
public class ModifierSelectionUI : MonoBehaviour
{
    [Tooltip("The same GameModifierCatalog prefab asset the gameplay scene's GameModifierManager points at — the shared list of every modifier in the game.")]
    [SerializeField] private GameModifierCatalog catalog;
    [Tooltip("Icon tile prefab instantiated once per catalog entry.")]
    [SerializeField] private ModifierTileView tilePrefab;
    [Tooltip("Parent the tiles are instantiated under — the ScrollRect's Content object (Grid Layout Group, Fixed Column Count of 4), not the ScrollRect itself.")]
    [SerializeField] private Transform tileContainer;
    [Tooltip("The ProgressionCatalog on that same ModifiersCatalog prefab — decides which modifiers the player's level has unlocked. Leave unassigned to make every modifier available (with a warning).")]
    [SerializeField] private ProgressionCatalog progression;
    [Tooltip("Optional running readout of what every ticked modifier together multiplies the player's final score by. Refreshed on every tile toggle. This is the grand total, not the hovered modifier's own bonus — that's Detail Multiplier Text below.")]
    [SerializeField] private TMP_Text totalMultiplierText;
    [Tooltip("Format for Total Multiplier Text — {0} is the combined multiplier.")]
    [SerializeField] private string totalMultiplierFormat = "x{0:0.00}";

    [Header("Tutorial Gate")]
    [Tooltip("Panel covering the modifier grid until the player has finished the tutorial once (PlayerProgression.HasCompletedTutorial). Give it an opaque, Raycast Target image big enough to cover the whole grid — the tiles underneath stay live otherwise, and a click that lands on one would still tick it. Leave unassigned to skip the gate visually; selections are refused in code either way.")]
    [SerializeField] private GameObject tutorialLockPanel;

    [Header("Detail Panel")]
    [Tooltip("Optional root of the side panel, hidden whenever there's no modifier to describe. Leave unassigned to leave the panel always visible with blank text.")]
    [SerializeField] private GameObject detailRoot;
    [Tooltip("Optional larger icon of the modifier being described.")]
    [SerializeField] private Image detailIconImage;
    [SerializeField] private TMP_Text detailNameText;
    [SerializeField] private TMP_Text detailDescriptionText;
    [Tooltip("The hovered modifier's OWN score bonus, e.g. \"+25%\".")]
    [SerializeField] private TMP_Text detailMultiplierText;
    [Tooltip("Optional line shown only while describing a LOCKED modifier, telling the player what level unlocks it. Blanked for unlocked ones.")]
    [SerializeField] private TMP_Text detailLockText;
    [Tooltip("Format for Detail Lock Text — {0} is the level that unlocks the modifier.")]
    [SerializeField] private string lockedFormat = "Unlocks at Level {0}";

    private readonly List<ModifierTileView> tiles = new List<ModifierTileView>();

    /// <summary>The player's level as of when the grid was built. Cached rather than re-derived per hover — it can't change while this menu is up.</summary>
    private int currentLevel;

    /// <summary>Last tile the player actually clicked — what the detail panel falls back to once the pointer leaves the grid.</summary>
    private GameModifierDefinition pinnedModifier;

    /// <summary>True until the player has finished the tutorial once. While set, the grid is covered by Tutorial Lock Panel and no modifier counts as selected however the tiles underneath happen to be sitting.</summary>
    public bool ModifiersLocked => !PlayerProgression.HasCompletedTutorial;

    /// <summary>
    /// Every ticked modifier's definition, or nothing at all while the tutorial
    /// gate is up.
    ///
    /// Refused here rather than only hidden behind the panel because this one
    /// property is what every downstream consumer runs through — the total
    /// readout, SelectedIds, and so the run itself. A tile left ticked from a
    /// previous session (BuildTiles restores them from GameModifierSelection)
    /// or a click that slips past a panel with no raycast target would
    /// otherwise carry a modifier into a run the gate was meant to prevent.
    /// </summary>
    public List<GameModifierDefinition> SelectedModifiers
    {
        get
        {
            List<GameModifierDefinition> selected = new List<GameModifierDefinition>();
            if (ModifiersLocked) return selected;

            foreach (ModifierTileView tile in tiles)
            {
                if (tile.IsSelected && tile.Modifier != null) selected.Add(tile.Modifier);
            }
            return selected;
        }
    }

    /// <summary>What the ticked modifiers alone are worth: 1.0 with nothing ticked, compounding from there. Deliberately defers to GameModifierManager.CalculateMultiplier rather than reimplementing the formula, so this preview can't drift from what the run actually pays out.</summary>
    public float ModifierMultiplier => GameModifierManager.CalculateMultiplier(SelectedModifiers);

    /// <summary>The number the player actually cares about: everything their setup multiplies the final score by. Identical to ModifierMultiplier now that difficulty is gone from the game, and kept as its own name because it's what the readout means rather than what currently feeds it.</summary>
    public float TotalMultiplier => ModifierMultiplier;

    /// <summary>Ids of every ticked modifier, for MainMenuController.StartGame to hand across the scene load.</summary>
    public List<string> SelectedIds
    {
        get
        {
            List<string> ids = new List<string>();
            foreach (GameModifierDefinition modifier in SelectedModifiers)
            {
                ids.Add(modifier.Id);
            }
            return ids;
        }
    }

    private void Start()
    {
        BuildTiles();
        RefreshTotal();
        ShowDetails(null);

        // Last, so the cover ends up drawn over a grid that already exists. The
        // state can't change while this menu is up — the tutorial is completed
        // in the gameplay scene, and coming back from it reloads this one — so
        // this is set once rather than polled.
        if (tutorialLockPanel != null) tutorialLockPanel.SetActive(ModifiersLocked);

        if (ModifiersLocked)
        {
            Debug.Log("ModifierSelectionUI: modifiers are locked until the tutorial has been completed once. Tools > Turtle Beach > Mark Tutorial Completed unlocks them for testing.", this);

            if (tutorialLockPanel == null)
            {
                Debug.LogWarning("ModifierSelectionUI: no Tutorial Lock Panel assigned, so nothing visibly covers the grid — tiles still look tickable even though any selection is ignored. Assign the cover panel to that field.", this);
            }
        }
    }

    private void BuildTiles()
    {
        if (catalog == null || tilePrefab == null || tileContainer == null)
        {
            Debug.LogWarning("ModifierSelectionUI: Catalog, Tile Prefab or Tile Container is unassigned — the modifier grid will be empty.");
            return;
        }

        HashSet<string> previouslySelected = new HashSet<string>(GameModifierSelection.LoadIds());

        if (progression == null)
        {
            Debug.LogWarning("ModifierSelectionUI: no Progression assigned — every modifier will be available regardless of the player's level. Drag the ModifiersCatalog prefab onto this field.");
        }

        currentLevel = progression != null ? progression.LevelFromXp(PlayerProgression.TotalXp) : int.MaxValue;

        int lockedCount = 0;

        foreach (GameModifierDefinition modifier in catalog.Modifiers)
        {
            if (modifier == null) continue;

            bool locked = IsLocked(modifier);
            if (locked) lockedCount++;

            ModifierTileView tile = Instantiate(tilePrefab, tileContainer);
            tile.Bind(modifier, this, previouslySelected.Contains(modifier.Id), locked);
            tiles.Add(tile);
        }

        Debug.Log($"ModifierSelectionUI: built {tiles.Count} tile(s) at player level {(progression != null ? currentLevel.ToString() : "n/a")} — {lockedCount} locked, {tiles.Count - lockedCount} unlocked. A LOCKED tile deliberately shows no name, description or bonus when hovered, only its unlock level.");

        // A locked modifier blanks its name, description AND bonus by design, so
        // with no lock line to put in their place the detail panel comes up
        // completely empty — which reads as "descriptions are broken" rather
        // than "you haven't unlocked this yet".
        if (lockedCount > 0 && detailLockText == null)
        {
            Debug.LogWarning($"ModifierSelectionUI: {lockedCount} modifier(s) are locked at this level and no Detail Lock Text is assigned, so hovering any of them shows a blank detail panel with nothing explaining why. Assign that field, or raise the player's level (MenuProgressionUI has Grant 10,000 XP / Reset Progression context-menu items).", this);
        }
    }

    /// <summary>Whether the player's level puts this modifier out of reach. Fails open with no ProgressionCatalog — see the class doc comment.</summary>
    private bool IsLocked(GameModifierDefinition modifier)
    {
        return progression != null && !progression.IsUnlockedAtLevel(modifier, currentLevel);
    }

    /// <summary>Called by a hovered ModifierTileView to describe its modifier in the side panel. A LOCKED modifier is deliberately described as nothing but the level that unlocks it — what it actually does stays hidden until it's earned. Pass null to empty the panel.</summary>
    public void ShowDetails(GameModifierDefinition modifier)
    {
        bool locked = modifier != null && IsLocked(modifier);
        // Only an unlocked modifier gets described at all. A locked one keeps
        // its name, effect and score bonus secret — the whole point of the
        // lock — and shows just its unlock level instead.
        bool describe = modifier != null && !locked;

        if (detailRoot != null) detailRoot.SetActive(modifier != null);

        if (detailIconImage != null)
        {
            detailIconImage.sprite = describe ? modifier.Icon : null;
            detailIconImage.enabled = describe && modifier.Icon != null;
        }

        if (detailNameText != null) detailNameText.text = describe ? modifier.DisplayName : string.Empty;
        if (detailDescriptionText != null) detailDescriptionText.text = describe ? modifier.Description : string.Empty;
        if (detailMultiplierText != null)
        {
            detailMultiplierText.text = describe
                ? $"+{Mathf.RoundToInt(modifier.ScoreMultiplierBonus * 100f)}%"
                : string.Empty;
        }

        if (detailLockText != null)
        {
            detailLockText.text = locked
                ? string.Format(lockedFormat, progression.UnlockLevelOf(modifier))
                : string.Empty;
            detailLockText.gameObject.SetActive(locked);
        }
    }

    /// <summary>Called when the pointer leaves a tile — falls back to whichever modifier was last clicked rather than blanking the panel outright, so the pick the player just made stays described while they look at the others.</summary>
    public void EndHoverDetails()
    {
        ShowDetails(pinnedModifier);
    }

    /// <summary>Called by a ModifierTileView when it's clicked. Each tile owns its own selected state (selections here are independent rather than exclusive), so this only pins the clicked modifier for the detail panel and re-reads the running total.</summary>
    public void HandleTileToggled(ModifierTileView tile)
    {
        if (tile != null) pinnedModifier = tile.Modifier;
        RefreshTotal();
    }

    private void RefreshTotal()
    {
        if (totalMultiplierText != null) totalMultiplierText.text = string.Format(totalMultiplierFormat, TotalMultiplier);
    }
}
