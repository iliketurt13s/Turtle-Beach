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

    [Header("UI References")]
    [Tooltip("Root object for the whole choice UI (backdrop + cards), toggled active/inactive by BeginStormFadeIn/Select.")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image backdrop;
    [Tooltip("Text shown while hovering a card's icon, set to that card's DisplayName.")]
    [SerializeField] private TMP_Text nameText;
    [Tooltip("Bottom-center text shown while hovering a card's icon.")]
    [SerializeField] private TMP_Text descriptionText;
    [Tooltip("The card view slots shown to the player (2 expected).")]
    [SerializeField] private UpgradeCardView[] cardSlots;

    [Header("Storm Backdrop Fade")]
    [Tooltip("How long the backdrop takes to fade fully in (storm start) or out (upgrade picked).")]
    [SerializeField] private float backdropFadeDuration = 1f;

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

    /// <summary>Draws up to 2 distinct eligible cards and shows the choice UI. Calls onComplete immediately if no cards are eligible.</summary>
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

        int drawCount = Mathf.Min(2, eligible.Count);
        List<UpgradeCardDefinition> drawn = new List<UpgradeCardDefinition>();
        for (int i = 0; i < drawCount; i++)
        {
            int index = UnityEngine.Random.Range(0, eligible.Count);
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
            if (used) cardSlots[i].Bind(drawn[i], this);
        }
    }

    /// <summary>Called by a hovered UpgradeCardView to show its name. Mirrors ShowDescription exactly.</summary>
    public void ShowName(string text)
    {
        if (nameText == null) return;
        nameText.text = text;
        nameText.gameObject.SetActive(true);
    }

    /// <summary>Called when the mouse leaves a card's icon.</summary>
    public void HideName()
    {
        if (nameText != null) nameText.gameObject.SetActive(false);
    }

    /// <summary>Called by a hovered UpgradeCardView to show its description bottom-center.</summary>
    public void ShowDescription(string text)
    {
        if (descriptionText == null) return;
        descriptionText.text = text;
        descriptionText.gameObject.SetActive(true);
    }

    /// <summary>Called when the mouse leaves a card's icon.</summary>
    public void HideDescription()
    {
        if (descriptionText != null) descriptionText.gameObject.SetActive(false);
    }

    /// <summary>Called by a card's Select button. Applies the card's effect, hides the cards immediately, resumes the game right away, and fades the backdrop back out over BackdropFadeDuration rather than snapping it away instantly.</summary>
    public void Select(UpgradeCardDefinition card)
    {
        card.Apply();
        if (!card.Stackable) pickedNonStackable.Add(card);

        IsActive = false;
        SetCardSlotsActive(false);
        HideName();
        HideDescription();
        StartBackdropFade(0f);

        Action callback = pendingOnComplete;
        pendingOnComplete = null;
        callback?.Invoke();
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
