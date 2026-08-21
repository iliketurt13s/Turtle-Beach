using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu's half of the progression system: shows the player's level on
/// the start screen, and announces any modifiers they've unlocked since they
/// were last here.
///
/// The level readout is a progress bar flanked by two numbers — the level the
/// player is on to its left, the one they're working toward to its right — so
/// the gap between them is what the bar is measuring. It fills across the
/// CURRENT level only, emptying and refilling on each level up, rather than
/// tracking lifetime XP toward some far-off maximum. This is the same
/// XpIntoLevel/XpToAdvance pair the end-of-run bar animates (see
/// LevelUpPanelUI); the difference is that this one is a still frame of where
/// the player stands, set once in Start, while that one animates the jump the
/// run just earned. Nothing here re-reads XP later because nothing can change
/// it without leaving this scene first.
///
/// The announcement lives HERE rather than on the end-of-run level page
/// (LevelUpPanelUI, which shows the bar filling but never says what it bought)
/// because a newly unlocked modifier is only actionable on this screen — it's
/// two clicks from the popup to the grid the modifier now appears in. Telling
/// the player about it on a screen they're about to leave would just make them
/// remember it.
///
/// The popup shows the modifier's tile art and nothing else — no name, no
/// description, no score bonus. That's deliberate: the art is the same picture
/// the tile wears in the grid, which is all the player needs to go find it, and
/// the options screen's detail panel (see ModifierSelectionUI) already explains
/// every modifier properly. Repeating that here would turn a one-beat
/// celebration into a wall of text, and would do it several times in a row
/// after a big run.
///
/// Which unlocks are still owed is worked out from two numbers, never from an
/// event: the level their lifetime XP currently buys, and
/// PlayerProgression.AnnouncedLevel, the highest level already announced. Every
/// level in between gets a popup, shown one at a time — a huge run that jumps
/// three levels announces all three rather than losing two of them. That's
/// also why AnnouncedLevel stores a level rather than being a bool flag like
/// TutorialEnabled.
///
/// AnnouncedLevel is advanced immediately when the queue is built rather than
/// per dismissal, so quitting mid-popup costs the player one notification
/// instead of re-showing it on every launch forever.
///
/// The popup slides in and out like every other panel in this scene (see
/// UISlidePanel), but it can't lean on Play In On Enable alone the way
/// MainMenuController's options screen does, because a queue of unlocks is
/// shown through ONE GameObject that's activated once and then repopulated in
/// place — the second and third announcements never re-enable anything, so
/// there's no OnEnable for that setting to fire on. Hence Popup Slide: the
/// first unlock rides Play In On Enable, and every one after it gets PlayIn
/// called on it explicitly, so they all get the same entrance. Dismissal always
/// goes out through PlayOut, with whatever comes next — the following unlock,
/// or deactivating the popup for good — deferred to its completion callback so
/// the slide is actually seen.
///
/// Level and unlock state are read straight from PlayerPrefs via
/// PlayerProgression, since ScoreManager only exists in the gameplay scene —
/// exactly how MainMenuController already reads the high score.
/// </summary>
public class MenuProgressionUI : MonoBehaviour
{
    [Tooltip("The ProgressionCatalog on the shared ModifiersCatalog prefab — the XP curve and the unlock order both come from it.")]
    [SerializeField] private ProgressionCatalog progression;

    [Header("Level Readout")]
    [Tooltip("The progress bar itself: the sand-hole track with the rock handle. Driven 0-1 across the CURRENT level only — it refills from empty on every level up, rather than tracking lifetime XP. Forced display-only at runtime (see RefreshLevelReadout), so there's no need to uncheck Interactable yourself.")]
    [SerializeField] private Slider levelSlider;
    [Tooltip("The number to the LEFT of the bar: the level the player is on now.")]
    [SerializeField] private TMP_Text currentLevelText;
    [Tooltip("The number to the RIGHT of the bar: the level they're filling the bar toward.")]
    [SerializeField] private TMP_Text nextLevelText;
    [Tooltip("Format for both flanking numbers — {0} is the level. Just \"{0}\" for a bare numeral; \"Lv {0}\" if you want a prefix on each side.")]
    [SerializeField] private string levelNumberFormat = "{0}";
    [Tooltip("Shown in place of the right-hand number once every modifier in the catalog is unlocked and there's no next level to aim at. Set to an empty string to show nothing there instead.")]
    [SerializeField] private string maxLevelRightLabel = "MAX";

    [Header("Level Readout (optional combined text)")]
    [Tooltip("Optional single \"Level 6\" readout, from before the bar existed. Safe to leave unassigned — the flanking numbers above have replaced it.")]
    [SerializeField] private TMP_Text levelText;
    [Tooltip("Format for Level Text — {0} is the level number.")]
    [SerializeField] private string levelFormat = "Level {0}";
    [Tooltip("Used instead of Level Format once every modifier in the catalog is unlocked.")]
    [SerializeField] private string maxLevelFormat = "Level {0}  (MAX)";

    [Header("Unlock Popup")]
    [Tooltip("Root of the \"Modifier Unlocked\" popup. Leave it INACTIVE in the scene — it's only shown when there's something to announce.")]
    [SerializeField] private GameObject popupRoot;
    [Tooltip("Shows the unlocked modifier's own tile art, so the popup and the grid tile the player then goes looking for are the same picture. Deliberately the ONLY thing identifying the modifier here — see the class doc comment.")]
    [SerializeField] private Image popupIconImage;
    [SerializeField] private TMP_Text popupTitleText;
    [SerializeField] private string popupTitle = "Modifier Unlocked!";
    [Tooltip("The popup's own UISlidePanel, so it slides in and back out instead of popping. Put it on the popup's CONTENT (the card and its children), not on the full-screen root that holds the dim overlay, or the screen dimming slides in with it. Leave unassigned to fall back to an instant show/hide.")]
    [SerializeField] private UISlidePanel popupSlide;

    /// <summary>Modifiers still waiting to be announced, oldest unlock first.</summary>
    private readonly Queue<GameModifierDefinition> pendingUnlocks = new Queue<GameModifierDefinition>();

    private void Start()
    {
        if (popupRoot != null) popupRoot.SetActive(false);

        if (progression == null)
        {
            Debug.LogWarning("MenuProgressionUI: no Progression assigned — drag the ModifiersCatalog prefab onto this field. No level readout and no unlock popups.");
            return;
        }

        // Read once and thread it through: LevelFromXp and XpIntoLevel have to
        // agree about the same total, and PlayerPrefs is a lookup, not a field.
        int totalXp = PlayerProgression.TotalXp;
        int level = progression.LevelFromXp(totalXp);

        RefreshLevelReadout(level, totalXp);
        BuildUnlockQueue(level);
        ShowNextUnlock();
    }

    /// <summary>
    /// Fills the bar and writes the numbers flanking it: the current level on
    /// the left, the one being worked toward on the right.
    ///
    /// The bar measures progress through the CURRENT level, not lifetime XP, so
    /// it empties and refills with each level rather than creeping ever more
    /// slowly toward a distant maximum. At max level there's nothing left to
    /// fill toward, so it pins full and the right-hand number gives way to
    /// Max Level Right Label.
    /// </summary>
    private void RefreshLevelReadout(int level, int totalXp)
    {
        bool atMaxLevel = level >= progression.MaxLevel;

        if (levelSlider != null)
        {
            // Transition first, THEN interactable: flipping interactable off
            // under the default Color Tint transition would immediately repaint
            // the rock handle in Unity's washed-out disabled color. With
            // transition None there's no state tint to apply at all, and the
            // handle keeps the sprite's own colors while still being undraggable.
            levelSlider.transition = Selectable.Transition.None;
            levelSlider.interactable = false;

            levelSlider.minValue = 0f;
            levelSlider.maxValue = 1f;
            levelSlider.value = atMaxLevel ? 1f : ProgressThroughLevel(level, totalXp);
        }

        if (currentLevelText != null) currentLevelText.text = string.Format(levelNumberFormat, level);
        if (nextLevelText != null) nextLevelText.text = atMaxLevel ? maxLevelRightLabel : string.Format(levelNumberFormat, level + 1);

        RefreshLevelText(level, atMaxLevel);
    }

    /// <summary>How far through the current level this XP total is, as 0-1. A level costing nothing reads as complete rather than dividing by zero — the same reading ProgressionCatalog.LevelFromXp gives an unauthored curve, where a zero cost stops progression instead of being free.</summary>
    private float ProgressThroughLevel(int level, int totalXp)
    {
        int needed = progression.XpToAdvance(level);
        if (needed <= 0) return 1f;

        return Mathf.Clamp01((float)progression.XpIntoLevel(totalXp) / needed);
    }

    private void RefreshLevelText(int level, bool atMaxLevel)
    {
        if (levelText == null) return;

        levelText.text = string.Format(atMaxLevel ? maxLevelFormat : levelFormat, level);
    }

    /// <summary>Collects every unlock earned since the last visit. Advances AnnouncedLevel up front — see the class doc comment on why that's deliberate rather than per-dismissal.</summary>
    private void BuildUnlockQueue(int level)
    {
        pendingUnlocks.Clear();

        int announced = PlayerProgression.AnnouncedLevel;
        if (announced >= level) return;

        for (int l = announced + 1; l <= level; l++)
        {
            GameModifierDefinition modifier = progression.UnlockedAt(l);
            // Null means the catalog shrank since that level was earned; the
            // level still counts, there's just nothing left to show for it.
            if (modifier != null) pendingUnlocks.Enqueue(modifier);
        }

        PlayerProgression.AnnouncedLevel = level;

        Debug.Log($"MenuProgressionUI: level {level}, {pendingUnlocks.Count} unlock(s) to announce (last announced level was {announced}).");
    }

    private void ShowNextUnlock()
    {
        if (pendingUnlocks.Count == 0)
        {
            if (popupRoot != null) popupRoot.SetActive(false);
            return;
        }

        GameModifierDefinition modifier = pendingUnlocks.Dequeue();

        if (popupRoot == null)
        {
            Debug.LogWarning($"MenuProgressionUI: unlocked \"{modifier.Id}\" but no Popup Root is assigned, so the player was never told. Build the popup and assign it.");
            return;
        }

        if (popupIconImage != null)
        {
            // The tile's unpressed face, not the effect card's Icon — this
            // should be the same picture the player will then hunt for in the
            // modifier grid.
            Sprite art = modifier.UnpressedSprite != null ? modifier.UnpressedSprite : modifier.Icon;
            popupIconImage.sprite = art;
            popupIconImage.enabled = art != null;
        }

        if (popupTitleText != null) popupTitleText.text = popupTitle;

        // Read BEFORE activating: a queued second unlock arrives with the popup
        // already up, so SetActive is a no-op and UISlidePanel's Play In On
        // Enable never fires — the art would silently swap in place while the
        // first unlock got a slide. Drive PlayIn by hand in exactly that case.
        bool alreadyOnScreen = popupRoot.activeSelf;
        popupRoot.SetActive(true);

        if (alreadyOnScreen && popupSlide != null) popupSlide.PlayIn();
    }

    /// <summary>
    /// Wire this up to the popup's continue button OnClick() in the Inspector.
    /// Slides the popup off, then either brings the next queued unlock in or
    /// closes the popup for good once they've all been seen — both deferred to
    /// the slide's completion callback, or the swap would happen on top of a
    /// slide-out nobody gets to see.
    ///
    /// Mashing the button mid-slide is safe rather than skipping an unlock:
    /// PlayOut stops any slide already running, and stopping that coroutine
    /// drops its pending callback with it, so the restarted slide-out is the
    /// only one that ends up dequeuing anything.
    /// </summary>
    public void DismissPopup()
    {
        // PlayOut runs a coroutine, which an inactive GameObject can't start —
        // and there'd be nothing on screen to slide off anyway.
        if (popupSlide != null && popupSlide.isActiveAndEnabled)
        {
            popupSlide.PlayOut(ShowNextUnlock);
            return;
        }

        ShowNextUnlock();
    }

    [ContextMenu("Reset Progression")]
    private void DebugResetProgression()
    {
        PlayerProgression.ResetProgress();
        Debug.Log("MenuProgressionUI: progression reset — re-enter Play mode to see the menu rebuild at level 0.");
    }

    [ContextMenu("Grant 10,000 XP")]
    private void DebugGrantXp()
    {
        PlayerProgression.AddXp(10000);
        Debug.Log("MenuProgressionUI: granted 10,000 XP — re-enter Play mode to see the unlocks announced.");
    }
}
