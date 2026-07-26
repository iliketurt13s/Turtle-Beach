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
/// Other systems (BuildModeController, TurtleSelectionController,
/// CameraController) poll the static IsActive flag to stand down while this
/// is up, the same way they already stand down for BuildModeController.IsActive.
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

    [Tooltip("Parallel pool drawn from only via ShowHazardChoice, when a GarbagePatch has just been depleted — represents the cost of moving on to a new island rather than a helpful pick.")]
    [SerializeField] private UpgradeCardDefinition[] hazardUpgradePool;

    [Tooltip("How many times more likely a non-stackable (one-time unlock) hazard card is to be drawn compared to a stackable one, e.g. 3 = three times as likely. 1 = no bias, same uniform draw the helpful pool always uses. Only affects ShowHazardChoice.")]
    [SerializeField, Min(1f)] private float hazardNonStackableWeight = 3f;
    [Tooltip("How many cards ShowHazardChoice draws, independent of how many Card Slots are wired up — the helpful pool (Show) always uses every available slot (see Card Slots below), but the hazard pool stays capped at this count regardless of how many slots exist.")]
    [SerializeField, Min(1)] private int hazardCardCount = 2;

    [Header("UI References")]
    [Tooltip("Root object for the whole choice UI (backdrop + cards), toggled active/inactive by BeginStormFadeIn/Select.")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image backdrop;
    [Tooltip("Text shown while hovering a card's icon, set to that card's DisplayName.")]
    [SerializeField] private TMP_Text nameText;
    [Tooltip("Bottom-center text shown while hovering a card's icon.")]
    [SerializeField] private TMP_Text descriptionText;
    [Tooltip("The card view slots shown to the player. The helpful pool (Show) draws up to however many are wired here (2, 3, ...); the hazard pool (ShowHazardChoice) always stays capped at Hazard Card Count regardless.")]
    [SerializeField] private UpgradeCardView[] cardSlots;

    [Header("Storm Backdrop Fade")]
    [Tooltip("How long the backdrop takes to fade fully in (storm start) or out (upgrade picked).")]
    [SerializeField] private float backdropFadeDuration = 1f;

    [Header("Card Text Color")]
    [Tooltip("Name/description text color while hovering a card drawn from the normal (helpful) pool.")]
    [SerializeField] private Color goodTextColor = Color.white;
    [Tooltip("Name/description text color while hovering a card drawn from the hazard pool, to visually flag it as a cost rather than a helpful pick.")]
    [SerializeField] private Color hazardTextColor = Color.red;

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
        HideName();
        HideDescription();
    }

    /// <summary>Called by DayStormCycle the instant a storm begins. Fades the backdrop in immediately and hides the (not-yet-populated) card slots, so nothing but the darkening itself is visible until Show() actually reveals cards at the storm's end.</summary>
    public void BeginStormFadeIn()
    {
        if (root != null) root.SetActive(true);
        HideName();
        HideDescription();
        SetCardSlotsActive(false);
        StartBackdropFade(backdropTargetAlpha);
    }

    /// <summary>Draws up to as many distinct eligible cards as Card Slots has room for from Upgrade Pool and shows the choice UI. Calls onComplete immediately if no cards are eligible.</summary>
    public void Show(Action onComplete) => ShowFromPool(upgradePool, onComplete, 1f, isHazardPool: false);

    /// <summary>Same as Show, but draws from Hazard Upgrade Pool instead — called only once a GarbagePatch has just been depleted, representing the cost of moving on to a new island. Non-stackable cards are favored in the draw by Hazard Non Stackable Weight, since a one-time unlock (a new trash type, a new mechanic) is more interesting to surface here than another stacking numeric bump. Always draws exactly Hazard Card Count cards (capped by however many are eligible), regardless of how many Card Slots exist for the helpful pool.</summary>
    public void ShowHazardChoice(Action onComplete) => ShowFromPool(hazardUpgradePool, onComplete, hazardNonStackableWeight, isHazardPool: true);

    private void ShowFromPool(UpgradeCardDefinition[] pool, Action onComplete, float nonStackableWeight, bool isHazardPool)
    {
        List<UpgradeCardDefinition> eligible = new List<UpgradeCardDefinition>();
        if (pool != null)
        {
            foreach (UpgradeCardDefinition card in pool)
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

        int maxCards = isHazardPool ? hazardCardCount : cardSlots.Length;
        int drawCount = Mathf.Min(maxCards, eligible.Count);
        List<UpgradeCardDefinition> drawn = new List<UpgradeCardDefinition>();
        for (int i = 0; i < drawCount; i++)
        {
            int index = PickWeightedIndex(eligible, nonStackableWeight);
            drawn.Add(eligible[index]);
            eligible.RemoveAt(index);
        }

        pendingOnComplete = onComplete;
        IsActive = true;
        if (root != null) root.SetActive(true);
        HideName();
        HideDescription();
        StartBackdropFade(backdropTargetAlpha); // normally already fully faded in from BeginStormFadeIn; harmless if so, catches Show() being called without it having run

        for (int i = 0; i < cardSlots.Length; i++)
        {
            bool used = i < drawn.Count;
            cardSlots[i].gameObject.SetActive(used);
            if (used) cardSlots[i].Bind(drawn[i], this, isHazardPool);
        }
    }

    /// <summary>Picks a random index from eligible, weighting non-stackable cards by nonStackableWeight relative to stackable ones (weight 1). nonStackableWeight of 1 collapses to a plain uniform pick.</summary>
    private static int PickWeightedIndex(List<UpgradeCardDefinition> eligible, float nonStackableWeight)
    {
        if (nonStackableWeight <= 1f) return UnityEngine.Random.Range(0, eligible.Count);

        float totalWeight = 0f;
        foreach (UpgradeCardDefinition card in eligible)
        {
            totalWeight += card.Stackable ? 1f : nonStackableWeight;
        }

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float cumulative = 0f;
        for (int i = 0; i < eligible.Count; i++)
        {
            cumulative += eligible[i].Stackable ? 1f : nonStackableWeight;
            if (roll < cumulative) return i;
        }

        return eligible.Count - 1;
    }

    /// <summary>Called by a hovered UpgradeCardView to show its name, tinted Good Text Color or Hazard Text Color depending on which pool that card was drawn from. Mirrors ShowDescription exactly.</summary>
    public void ShowName(string text, bool isHazard)
    {
        if (nameText == null) return;
        nameText.text = text;
        nameText.color = isHazard ? hazardTextColor : goodTextColor;
        nameText.gameObject.SetActive(true);
    }

    /// <summary>Called when the mouse leaves a card's icon.</summary>
    public void HideName()
    {
        if (nameText != null) nameText.gameObject.SetActive(false);
    }

    /// <summary>Called by a hovered UpgradeCardView to show its description bottom-center, tinted the same way as ShowName.</summary>
    public void ShowDescription(string text, bool isHazard)
    {
        if (descriptionText == null) return;
        descriptionText.text = text;
        descriptionText.color = isHazard ? hazardTextColor : goodTextColor;
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
