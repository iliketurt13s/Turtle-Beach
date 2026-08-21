using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orchestrates the end-of-storm upgrade choice: draws 2 distinct cards from
/// Upgrade Pool (skipping non-stackable cards already picked this run),
/// shows the choice UI, and invokes a callback once the player selects one.
/// The draw is weighted, not uniform: a card with UpgradeCardDefinition's
/// Base Stat Upgrade ticked is offered Base Stat Draw Weight as often as any
/// other (0.5, so half as often), which keeps the picks the player actually
/// remembers — unlocks, new behaviors — from being crowded out by a pool that's
/// mostly small numeric bumps. See DrawWeightedIndex.
/// Other systems (BuildModeController, TurtleSelectionController,
/// CameraController) poll the static IsActive flag to stand down while this
/// is up, the same way they already stand down for BuildModeController.IsActive.
///
/// Every card offered here is a helpful one. The parallel "hazard" pool this
/// used to also draw from mid-run (a forced harmful pick every N storms) is
/// gone — harmful effects are now opt-in run modifiers the player chooses on
/// the menu before starting, in exchange for a score multiplier. See
/// GameModifierDefinition/GameModifierManager; those modifiers reuse the same
/// UpgradeCardDefinition prefabs as their effects, just applied once at run
/// start rather than drawn from here.
///
/// The backdrop doubles as storm visual feedback: DayStormCycle calls
/// BeginStormFadeIn the instant a storm starts, well before the actual card
/// choice is shown, so the darkening reads as "it's night" rather than only
/// appearing alongside the upgrade cards. It fades in to whatever alpha the
/// backdrop Image was originally authored with (captured once at Awake, then
/// forced to 0 until the first fade-in — so no new designer-facing field is
/// needed, the existing Image color is still the one source of truth for how
/// dark it gets). Cards themselves still only appear once Show() is called at
/// the actual end of the storm; the darkening alone spans the whole night.
/// Select() fades the backdrop back out (smoothing the return to gameplay)
/// rather than snapping it away instantly.
/// </summary>
public class UpgradeSelectionUI : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    [Tooltip("The full authored pool of upgrade cards to draw from. Add more here to expand the pool without touching this script.")]
    [SerializeField] private UpgradeCardDefinition[] upgradePool;
    [Tooltip("How likely a card with Base Stat Upgrade ticked is to be drawn, relative to a card without it. 0.5 = half as likely, 1 = no difference, 0 = never drawn while any other card is eligible.")]
    [SerializeField, Range(0f, 1f)] private float baseStatDrawWeight = 0.5f;

    [Header("UI References")]
    [Tooltip("Root object for the whole choice UI (backdrop + cards), toggled active/inactive by BeginStormFadeIn/Select.")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image backdrop;
    [Tooltip("Text shown while hovering a card's icon, set to that card's DisplayName.")]
    [SerializeField] private TMP_Text nameText;
    [Tooltip("Bottom-center text shown while hovering a card's icon.")]
    [SerializeField] private TMP_Text descriptionText;
    [Tooltip("The card view slots shown to the player. Show draws up to however many are wired here (2, 3, ...).")]
    [SerializeField] private UpgradeCardView[] cardSlots;

    [Header("Storm Backdrop Fade")]
    [Tooltip("How long the backdrop takes to fade fully in (storm start) or out (upgrade picked).")]
    [SerializeField] private float backdropFadeDuration = 1f;

    [Header("Card Text Color")]
    [Tooltip("Name/description text color while hovering a card.")]
    [SerializeField] private Color goodTextColor = Color.white;

    private readonly HashSet<UpgradeCardDefinition> pickedNonStackable = new HashSet<UpgradeCardDefinition>();
    private Action pendingOnComplete;
    private float backdropTargetAlpha;
    private Coroutine backdropFadeCoroutine;

    private void Awake()
    {
        IsActive = false;
        if (root != null) root.SetActive(false);
        if (backdrop != null)
        {
            backdropTargetAlpha = backdrop.color.a;
            SetBackdropAlpha(0f);
        }
        SetBackdropBlocking(false);
        HideName();
        HideDescription();
    }

    /// <summary>
    /// Whether the backdrop swallows pointer clicks, which must be true ONLY
    /// while the card choice is genuinely modal — never during the storm, when
    /// the very same Image is on screen purely as night-time darkening.
    ///
    /// This is not cosmetic. TurtleSelectionController and BuildModeController
    /// both ignore a click that lands on UI (so pressing a HUD button doesn't
    /// also order a turtle or place a building), and they ask the EventSystem,
    /// which answers for ANY graphic flagged Raycast Target — including a
    /// full-screen decorative one. Left on through the night, this backdrop
    /// covers the screen and silently eats every gameplay click from dusk until
    /// the upgrade pick: turtles can't be selected, nothing can be ordered, and
    /// there's no visible cause. Driven from code rather than left to the
    /// Image's Inspector checkbox precisely because the flag has to be false at
    /// one time and true at another, which no static authoring can express.
    /// </summary>
    private void SetBackdropBlocking(bool blocking)
    {
        if (backdrop != null) backdrop.raycastTarget = blocking;
    }

    /// <summary>Called by DayStormCycle the instant a storm begins. Fades the backdrop in immediately and hides the (not-yet-populated) card slots, so nothing but the darkening itself is visible until Show() actually reveals cards at the storm's end.</summary>
    public void BeginStormFadeIn()
    {
        if (root != null) root.SetActive(true);
        HideName();
        HideDescription();
        SetCardSlotsActive(false);
        // Explicit even though nothing should have left it on: this is the call
        // that puts a full-screen graphic over the game for the entire night,
        // so it's the one place it matters most. See SetBackdropBlocking.
        SetBackdropBlocking(false);
        StartBackdropFade(backdropTargetAlpha);
    }

    /// <summary>Fades the storm backdrop back out without offering any cards — for a storm that ends with no upgrade pick at all (see DayStormCycle: the run's final storm skips the pick, since the win screen follows immediately and a card picked there could never be used). Without this the darkening BeginStormFadeIn applied would stay up through the cutscene and win screen, since Select is normally what clears it.</summary>
    public void EndStormFadeOut() => StartBackdropFade(0f);

    /// <summary>Draws up to as many distinct eligible cards as Card Slots has room for from Upgrade Pool and shows the choice UI. Calls onComplete immediately if no cards are eligible.</summary>
    public void Show(Action onComplete)
    {
        List<UpgradeCardDefinition> eligible = new List<UpgradeCardDefinition>();
        if (upgradePool != null)
        {
            foreach (UpgradeCardDefinition card in upgradePool)
            {
                if (card == null) continue;
                if (!card.Stackable && pickedNonStackable.Contains(card)) continue;

                // Building-branch cards (see IRequiresBuilding) only enter the
                // draw pool once their required building is unlocked — e.g. a
                // Campfire upgrade shouldn't show up before the Campfire
                // itself does. Treated as ineligible (not just deprioritized)
                // if BuildModeController isn't around to check.
                if (card is IRequiresBuilding requiresBuilding
                    && (BuildModeController.Instance == null || !BuildModeController.Instance.IsUnlocked(requiresBuilding.RequiredBuilding)))
                {
                    continue;
                }

                // Same idea for branches whose parent is a plain upgrade rather
                // than a building (see IRequiresUpgrade) — a Barnacle or Crab
                // improvement shouldn't be offered before Barnacles/Crabs are.
                if (card is IRequiresUpgrade requiresUpgrade && !requiresUpgrade.IsPrerequisiteMet) continue;

                eligible.Add(card);
            }
        }

        if (eligible.Count == 0)
        {
            // Nothing to offer this round — still fade the backdrop back out
            // (BeginStormFadeIn already faded it in for the storm), or the
            // screen would stay darkened forever since Select() is what
            // normally does that and it'll never get called.
            StartBackdropFade(0f);
            onComplete?.Invoke();
            return;
        }

        int drawCount = Mathf.Min(cardSlots.Length, eligible.Count);
        List<UpgradeCardDefinition> drawn = new List<UpgradeCardDefinition>();
        for (int i = 0; i < drawCount; i++)
        {
            int index = DrawWeightedIndex(eligible);
            drawn.Add(eligible[index]);
            eligible.RemoveAt(index);
        }

        pendingOnComplete = onComplete;
        IsActive = true;
        // Now — and only now — the backdrop is a real modal shield: it keeps a
        // click aimed between two cards from reaching the HUD behind it.
        SetBackdropBlocking(true);
        if (root != null) root.SetActive(true);
        HideName();
        HideDescription();
        StartBackdropFade(backdropTargetAlpha); // normally already fully faded in from BeginStormFadeIn; harmless if so, catches Show() being called without it having run

        for (int i = 0; i < cardSlots.Length; i++)
        {
            bool used = i < drawn.Count;
            cardSlots[i].gameObject.SetActive(used);
            if (used) cardSlots[i].Bind(drawn[i], this);
        }
    }

    /// <summary>
    /// Picks one index out of the remaining eligible cards, weighted so that a
    /// card with Base Stat Upgrade ticked comes up Base Stat Draw Weight as
    /// often as an unticked one (0.5 = half as likely). Called once per slot,
    /// with the drawn card removed in between, so the weighting applies to each
    /// pick independently rather than only to the first.
    ///
    /// Falls back to a flat uniform pick if the total weight is zero — that
    /// happens when the weight is set to 0 and EVERY eligible card is a base
    /// stat upgrade, where "half as likely" has nothing left to be half of and
    /// showing something beats showing an empty slot.
    /// </summary>
    private int DrawWeightedIndex(List<UpgradeCardDefinition> pool)
    {
        float total = 0f;
        foreach (UpgradeCardDefinition card in pool) total += DrawWeightOf(card);

        if (total <= 0f) return UnityEngine.Random.Range(0, pool.Count);

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < pool.Count; i++)
        {
            roll -= DrawWeightOf(pool[i]);
            if (roll <= 0f) return i;
        }

        // Only reachable if floating-point drift leaves roll a hair above the
        // accumulated weights; the last card is the one it was rolling into.
        return pool.Count - 1;
    }

    private float DrawWeightOf(UpgradeCardDefinition card)
    {
        return card != null && card.IsBaseStatUpgrade ? baseStatDrawWeight : 1f;
    }

    /// <summary>How many cards are actually on offer right now — 0 when no pick is up. Show decides this at runtime (it draws up to as many as there are slots, but no more than the eligible pool holds), so it can't be assumed from the authored slot count.</summary>
    public int ShownCardCount
    {
        get
        {
            if (cardSlots == null) return 0;

            int count = 0;
            foreach (UpgradeCardView slot in cardSlots)
            {
                if (slot != null && slot.gameObject.activeInHierarchy) count++;
            }

            return count;
        }
    }

    /// <summary>RectTransform of the index'th card currently on offer, or null if there aren't that many. Exists for the tutorial to point an arrow at every card it's telling the player to choose between — the slots are a private authored array, so this hands out only what a pointer needs rather than the slots themselves. Counts the SHOWN cards, not the raw slots, so index 0 is always a real card even if a slot in the middle of the array went unused.</summary>
    public RectTransform GetShownCardRect(int index)
    {
        if (cardSlots == null || index < 0) return null;

        int seen = 0;
        foreach (UpgradeCardView slot in cardSlots)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy) continue;
            if (seen == index) return (RectTransform)slot.transform;
            seen++;
        }

        return null;
    }

    /// <summary>Called by a hovered UpgradeCardView to show its name. Mirrors ShowDescription exactly.</summary>
    public void ShowName(string text)
    {
        if (nameText == null) return;
        nameText.text = text;
        nameText.color = goodTextColor;
        nameText.gameObject.SetActive(true);
    }

    /// <summary>Called when the mouse leaves a card's icon.</summary>
    public void HideName()
    {
        if (nameText != null) nameText.gameObject.SetActive(false);
    }

    /// <summary>Called by a hovered UpgradeCardView to show its description bottom-center, tinted the same way as ShowName.</summary>
    public void ShowDescription(string text)
    {
        if (descriptionText == null) return;
        descriptionText.text = text;
        descriptionText.color = goodTextColor;
        descriptionText.gameObject.SetActive(true);
    }

    /// <summary>Called when the mouse leaves a card's icon.</summary>
    public void HideDescription()
    {
        if (descriptionText != null) descriptionText.gameObject.SetActive(false);
    }

    /// <summary>Called by a card's Select button (or clicking its icon — see UpgradeCardView). Applies the card's effect, resumes the game right away, and fades the backdrop back out over BackdropFadeDuration rather than snapping it away instantly. The IsActive guard also doubles as a re-entrancy lock: gameplay (and so world input) resumes the instant this runs, but the card slots themselves stay up a moment longer (see HideCardSlotsAfterSquash) — without the guard, a second click landing in that window would apply a second card.</summary>
    public void Select(UpgradeCardView selectedView, UpgradeCardDefinition card)
    {
        if (!IsActive) return;

        card.Apply();
        if (!card.Stackable) pickedNonStackable.Add(card);

        IsActive = false;
        // Gameplay input resumes on this very line (see the summary above), so
        // the shield has to come down with it — the backdrop is still visible
        // for a moment while it fades, and must not eat the player's first
        // click back.
        SetBackdropBlocking(false);
        HideName();
        HideDescription();
        StartBackdropFade(0f);

        Action callback = pendingOnComplete;
        pendingOnComplete = null;
        callback?.Invoke();

        // Deactivating the slots synchronously (the old behavior) raced
        // UIClickSquash's own click handler on the same GameObject/event —
        // SquashAndStretch.Play() no-ops on (and its coroutine is killed by)
        // a GameObject deactivated out from under it, so the punch effect
        // never got to actually play. Give it one duration's grace instead.
        float squashDuration = selectedView != null ? selectedView.GetComponent<SquashAndStretch>()?.Duration ?? 0f : 0f;
        StartCoroutine(HideCardSlotsAfterSquash(squashDuration));
    }

    private IEnumerator HideCardSlotsAfterSquash(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        SetCardSlotsActive(false);
    }

    private void SetCardSlotsActive(bool active)
    {
        if (cardSlots == null) return;

        foreach (UpgradeCardView slot in cardSlots)
        {
            if (slot != null) slot.gameObject.SetActive(active);
        }
    }

    private void StartBackdropFade(float targetAlpha)
    {
        if (backdrop == null) return;

        if (backdropFadeCoroutine != null) StopCoroutine(backdropFadeCoroutine);
        backdropFadeCoroutine = StartCoroutine(FadeBackdrop(targetAlpha));
    }

    /// <summary>Fades the backdrop's alpha to targetAlpha over BackdropFadeDuration. Deactivates root once a fade-to-0 finishes (fading in never needs to, root is already active by then), so the UI object doesn't linger fully transparent but still raycast-blocking/rendering.</summary>
    private IEnumerator FadeBackdrop(float targetAlpha)
    {
        float startAlpha = backdrop.color.a;
        float duration = Mathf.Max(backdropFadeDuration, 0.01f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetBackdropAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            yield return null;
        }

        SetBackdropAlpha(targetAlpha);
        backdropFadeCoroutine = null;

        if (targetAlpha <= 0f && root != null) root.SetActive(false);
    }

    private void SetBackdropAlpha(float alpha)
    {
        if (backdrop == null) return;

        Color color = backdrop.color;
        color.a = alpha;
        backdrop.color = color;
    }
}
