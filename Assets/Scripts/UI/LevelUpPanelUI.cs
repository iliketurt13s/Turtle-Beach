using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The game-over panel's second page: the player's level and how far along the
/// bar this run pushed them. Driven by GameOverUI exactly the way
/// ScoreBreakdownUI is — Play(onComplete) / Hide(), presentation only, no scene
/// loading or pause state — and shown inside the SAME panel frame once the
/// score breakdown has finished, so the two read as two pages of one result
/// screen rather than two windows.
///
/// The XP is already banked by the time this plays: ScoreManager.FinalizeRun
/// runs before the breakdown even starts, so what this animates is a REPLAY.
/// It reads the pre-run total off ScoreManager.XpBeforeRun, snaps the bar
/// there, and fills forward through however many levels the run's XpAwarded
/// bought. Nothing here writes progress, which means a player who alt-F4s
/// halfway through the fill still keeps every level they earned.
///
/// Multiple levels in one run are handled by looping rather than jumping
/// straight to the end: the bar fills, pins, punches the level number, resets
/// to empty, and fills again. A monster run visibly cashing out three levels
/// in a row is the payoff, not something to compress away.
///
/// Everything is unscaled-time (the panel can come up at any Time.timeScale)
/// and a click or keypress skips to the final state, mirroring
/// ScoreBreakdownUI for the same reason it does it: this screen is replayed on
/// every single restart and being unable to skip it wears thin fast. Skipping
/// is armed a moment after the sequence starts so the click that skipped the
/// breakdown can't blow straight through this too.
///
/// Notably absent: any mention of WHICH modifier a level unlocked. That
/// announcement lives on the main menu (see MenuProgressionUI), next to the
/// grid the player would actually use it in, rather than on a screen they're
/// about to leave.
/// </summary>
public class LevelUpPanelUI : MonoBehaviour
{
    [Tooltip("The shared ModifiersCatalog prefab asset — the same one GameModifierManager points at. It carries both the XP curve and the modifier list the level cap comes from.")]
    [SerializeField] private ProgressionCatalog progression;

    [Tooltip("Root of this page, hidden until the score breakdown has finished. Should be a sibling of the breakdown's own Root inside the same panel frame, NOT a parent of the Restart/Menu buttons — GameOverUI shows those separately so pausing can reach them without showing this page.")]
    [SerializeField] private GameObject root;

    [Header("Level")]
    [SerializeField] private TMP_Text levelText;
    [Tooltip("Format for Level Text — {0} is the level number.")]
    [SerializeField] private string levelFormat = "Level {0}";
    [Tooltip("Used instead of Level Format once every modifier in the catalog is unlocked.")]
    [SerializeField] private string maxLevelFormat = "Level {0}  (MAX)";
    [Tooltip("Optional scale punch played on the level number each time it ticks up. Put a SquashAndStretch on Level Text and assign it here.")]
    [SerializeField] private SquashAndStretch levelUpPunch;

    [Header("XP Bar")]
    [Tooltip("Progress through the current level, driven 0..1. Uncheck its Interactable so the player can't drag it.")]
    [SerializeField] private Slider xpBar;
    [Tooltip("Optional readout under the bar, e.g. \"4300 / 6500 XP\".")]
    [SerializeField] private TMP_Text xpText;
    [Tooltip("Format for Xp Text — {0} is XP into the current level, {1} is what the level costs.")]
    [SerializeField] private string xpFormat = "{0} / {1} XP";
    [Tooltip("Shown instead of Xp Format at max level, where there's no next level to be partway to.")]
    [SerializeField] private string maxXpText = "All modifiers unlocked";
    [Tooltip("Optional \"+N XP\" readout of what this run earned. Put a CountUpText on it (Format \"+{0} XP\") so the number spins up.")]
    [SerializeField] private CountUpText xpGainedText;

    [Header("Timing")]
    [Tooltip("Pause after the page appears (showing the pre-run state) before the bar starts moving.")]
    [SerializeField] private float delayBeforeFill = 0.4f;
    [Tooltip("How long a full level's worth of bar takes to fill. A partial fill takes proportionally less, so the bar always moves at the same speed.")]
    [SerializeField] private float fillDuration = 1.2f;
    [Tooltip("Pause on a full bar after a level completes, before it empties for the next one.")]
    [SerializeField] private float levelUpHold = 0.7f;
    [Tooltip("Pause after the bar lands, before the Restart/Menu buttons appear.")]
    [SerializeField] private float delayAfterFill = 0.3f;
    [Tooltip("How long after the sequence starts before a click/keypress will skip it — stops the click that skipped the score breakdown from skipping this too.")]
    [SerializeField] private float skipArmDelay = 0.4f;
    [SerializeField] private AnimationCurve fillEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine sequenceCoroutine;
    private Action onSequenceComplete;
    private bool skipRequested;
    private float skipArmedTime;

    private void Awake()
    {
        Hide();
    }

    private void Update()
    {
        if (sequenceCoroutine == null || skipRequested) return;
        if (Time.unscaledTime < skipArmedTime) return;

        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool keyed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        if (clicked || keyed) skipRequested = true;
    }

    /// <summary>Hides the page without playing anything — used on the pause path, and at Awake, so this never flashes the previous run's numbers.</summary>
    public void Hide()
    {
        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }
        onSequenceComplete = null;

        if (root != null) root.SetActive(false);
    }

    /// <summary>
    /// Shows the page and animates from the player's pre-run level to their
    /// post-run one, calling onComplete once the bar settles (or immediately
    /// after, if the player skipped). Expects ScoreManager.FinalizeRun to have
    /// already banked the XP, which GameOverUI guarantees.
    /// </summary>
    public void Play(Action onComplete = null)
    {
        onSequenceComplete = onComplete;

        if (root != null) root.SetActive(true);

        if (progression == null)
        {
            // Nothing sensible to show, but the buttons still have to appear or
            // the player is stranded on a dead screen with no way back.
            Debug.LogWarning("LevelUpPanelUI: no Progression assigned — drag the ModifiersCatalog prefab onto this field. Skipping the level page.");
            if (root != null) root.SetActive(false);
            FireComplete();
            return;
        }

        if (sequenceCoroutine != null) StopCoroutine(sequenceCoroutine);
        sequenceCoroutine = StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        skipRequested = false;
        skipArmedTime = Time.unscaledTime + skipArmDelay;

        ScoreManager score = ScoreManager.Instance;
        int xpBefore = score != null ? score.XpBeforeRun : PlayerProgression.TotalXp;
        int gained = score != null ? score.XpAwarded : 0;

        int level = progression.LevelFromXp(xpBefore);
        int xpIntoLevel = progression.XpIntoLevel(xpBefore);
        int maxLevel = progression.MaxLevel;

        WriteLevel(level, maxLevel);
        WriteXp(level, xpIntoLevel, maxLevel);
        SetBar(level, xpIntoLevel, maxLevel);

        if (xpGainedText != null) xpGainedText.Play(gained);

        yield return Wait(delayBeforeFill);

        int remaining = gained;
        while (remaining > 0 && level < maxLevel)
        {
            int needed = progression.XpToAdvance(level);
            if (needed <= 0) break;

            // Only ever fill to the end of THIS level; whatever's left over
            // carries into the next pass, which is what makes a multi-level run
            // read as several distinct fills rather than one long slide.
            int spend = Mathf.Min(remaining, needed - xpIntoLevel);
            int from = xpIntoLevel;
            int to = xpIntoLevel + spend;

            yield return FillBar(level, from, to, needed, maxLevel);

            remaining -= spend;
            xpIntoLevel = to;

            if (xpIntoLevel < needed) break;

            // Level complete: hold on the full bar so the moment lands, then
            // roll over to an empty bar on the next level.
            yield return Wait(levelUpHold);

            level++;
            xpIntoLevel = 0;

            WriteLevel(level, maxLevel);
            if (levelUpPunch != null) levelUpPunch.Play();

            SetBar(level, xpIntoLevel, maxLevel);
            WriteXp(level, xpIntoLevel, maxLevel);
        }

        // Land on the true final state regardless of how the loop exited —
        // skipped, capped at max level, or simply out of XP.
        WriteLevel(level, maxLevel);
        SetBar(level, xpIntoLevel, maxLevel);
        WriteXp(level, xpIntoLevel, maxLevel);
        if (xpGainedText != null) xpGainedText.Skip();

        yield return Wait(delayAfterFill);

        sequenceCoroutine = null;
        FireComplete();
    }

    /// <summary>Animates the bar across one level's worth of progress (or part of one). Duration is proportional to the fraction covered, so the bar always travels at the same visual speed however much XP a run earned.</summary>
    private IEnumerator FillBar(int level, int fromXp, int toXp, int needed, int maxLevel)
    {
        float fromFraction = needed > 0 ? (float)fromXp / needed : 1f;
        float toFraction = needed > 0 ? (float)toXp / needed : 1f;

        float duration = fillDuration * Mathf.Abs(toFraction - fromFraction);
        if (skipRequested || duration <= 0f)
        {
            SetBarFraction(toFraction);
            WriteXp(level, toXp, maxLevel);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && !skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = fillEase.Evaluate(Mathf.Clamp01(elapsed / duration));
            SetBarFraction(Mathf.LerpUnclamped(fromFraction, toFraction, t));
            WriteXp(level, Mathf.RoundToInt(Mathf.LerpUnclamped(fromXp, toXp, t)), maxLevel);
            yield return null;
        }

        SetBarFraction(toFraction);
        WriteXp(level, toXp, maxLevel);
    }

    private void WriteLevel(int level, int maxLevel)
    {
        if (levelText == null) return;

        string format = level >= maxLevel ? maxLevelFormat : levelFormat;
        levelText.text = string.Format(format, level);
    }

    private void WriteXp(int level, int xpIntoLevel, int maxLevel)
    {
        if (xpText == null) return;

        if (level >= maxLevel)
        {
            xpText.text = maxXpText;
            return;
        }

        xpText.text = string.Format(xpFormat, xpIntoLevel, progression.XpToAdvance(level));
    }

    /// <summary>Pins the bar full at max level, where there's no next level to be a fraction of.</summary>
    private void SetBar(int level, int xpIntoLevel, int maxLevel)
    {
        if (level >= maxLevel)
        {
            SetBarFraction(1f);
            return;
        }

        int needed = progression.XpToAdvance(level);
        SetBarFraction(needed > 0 ? (float)xpIntoLevel / needed : 0f);
    }

    private void SetBarFraction(float fraction)
    {
        if (xpBar == null) return;

        xpBar.minValue = 0f;
        xpBar.maxValue = 1f;
        xpBar.value = Mathf.Clamp01(fraction);
    }

    /// <summary>Skips a delay entirely once the player has asked to skip, rather than still making them sit through every pause. Same helper, same reasoning, as ScoreBreakdownUI.Wait.</summary>
    private IEnumerator Wait(float seconds)
    {
        if (skipRequested || seconds <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < seconds && !skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void FireComplete()
    {
        Action callback = onSequenceComplete;
        onSequenceComplete = null;
        callback?.Invoke();
    }
}
