using System;
using UnityEngine;

/// <summary>
/// Drives the repeating day/storm cycle: daylight lets the player see and
/// prepare for the next wave of trash; storm sets TrashAgent instances loose
/// to burst toward the nest. Each cycle spawns more trash than the last and
/// heals all buildings back to full once the storm ends. The instant a storm
/// starts, UpgradeSelectionUI's backdrop fades in as "it's night" feedback
/// (see BeginStormFadeIn) well before its cards actually appear at the
/// storm's end, and fades back out once a card is picked.
/// </summary>
public class DayStormCycle : MonoBehaviour
{
    /// <summary>Scene-wide singleton (mirrors ResourceManager/PathfindingManager/etc.) so e.g. TrashAgent can read CurrentRound — an instance property, unlike the static IsStorming/StormStarted/StormEnded above — without a serialized scene reference.</summary>
    public static DayStormCycle Instance { get; private set; }

    /// <summary>True during the storm phase; polled by TrashAgent to know whether to burst-move.</summary>
    public static bool IsStorming { get; private set; }

    /// <summary>Raised the instant a storm ends (right after buildings are healed, right before the round counter increments). Lets temporary per-storm effects (e.g. Iron Ingot's building health buff) know exactly when to revert.</summary>
    public static event Action StormEnded;

    /// <summary>Raised the instant a storm begins (right after IsStorming flips true, before the upgrade UI's backdrop fade-in). Lets dusk-triggered effects (e.g. TurtleNest re-arming its per-food-type distribution cooldowns) fire at the exact moment night falls, ahead of any turtle's own storm-transition logic that same frame.</summary>
    public static event Action StormStarted;

    [Header("References")]
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private TrashSpawner trashSpawner;
    [Tooltip("Shown at the end of every storm before the next day begins. If left unassigned, the next day begins immediately with no upgrade choice.")]
    [SerializeField] private UpgradeSelectionUI upgradeSelectionUI;
    [Tooltip("Runs right after the helpful upgrade card above is picked: pans to the garbage patch, knocks off a health segment, and (once depleted) runs the hazard pick + island transition. If left unassigned, the next day begins immediately with no cutscene.")]
    [SerializeField] private GarbagePatchCutsceneController garbagePatchCutsceneController;
    [Tooltip("Screen-covering particle effect (e.g. rain/wind) that plays for the duration of the storm. Played the instant the storm starts (alongside StormStarted) and stopped the instant it ends (alongside StormEnded) — just Play()/Stop(), no per-frame tuning. Left unassigned, no storm VFX plays.")]
    [SerializeField] private ParticleSystem stormOverlayEffect;

    [Header("Timing")]
    [SerializeField] private float dayDuration = 30f;
    [SerializeField] private float fadeOutDuration = 2f;
    [Tooltip("Subtracted from Day Duration for just the very first day, when the tutorial actually ran this session (see TutorialManager.DidRunThisSession) — the day clock is already frozen for the whole scripted tutorial (see Update's IsActive guard), so this isn't recovering lost time so much as not piling a full day on top of however long the tutorial itself took, before the first storm arrives.")]
    [SerializeField] private float firstDayDurationReduction = 30f;

    private bool isFirstDay = true;

    [Header("Trash Scaling")]
    [Tooltip("Total plastic 'rating' budget spawned on round 1. Each piece of trash consumes an amount of this budget equal to its own TrashDefinition.Rating, so this controls both how much trash appears and, once the budget is large enough, whether higher-rated (harder) plastic types can afford to show up at all.")]
    [SerializeField] private float baseRatingBudget = 8f;
    [Tooltip("Flat amount added to the rating budget every round, on top of the exponential growth below. Makes early rounds ramp up noticeably instead of the first few rounds barely differing from round 1 (a pure percentage is a tiny absolute amount when the budget is still small).")]
    [SerializeField] private float linearRatingPerRound = 2f;
    [Tooltip("Multiplier applied to the rating budget every round, compounding on top of the linear growth above. Kept modest (e.g. 1.08 = 8%/round) rather than large, since compounding on an already-growing budget is what makes a pure exponential curve feel fine for a while and then suddenly unbeatable — the linear term above carries most of the early-game ramp so this doesn't have to.")]
    [SerializeField] private float ratingGrowthPerRound = 1.08f;

    [Header("Per-Island Escalation")]
    [Tooltip("Added to Linear Rating Per Round every time a garbage patch is depleted and the run moves to a new island — so each island's round-over-round ramp is steeper than the last one's, even though the round counter (and so the spawned amount) itself resets back to Base Rating Budget.")]
    [SerializeField] private float linearRatingIncreasePerIsland = 1f;
    [Tooltip("Added to Rating Growth Per Round every new island. See Linear Rating Increase Per Island.")]
    [SerializeField] private float ratingGrowthIncreasePerIsland = 0.02f;
    [Tooltip("Multiplier applied to the rating budget on the final wave before the garbage patch depletes (i.e. the round spawned while it's down to its last segment) — one extra spike of difficulty right before moving to a new island. 1 = no change.")]
    [SerializeField] private float finalWaveRatingMultiplier = 1.5f;

    public int CurrentRound { get; private set; } = 1;

    private float phaseTimer;
    private bool awaitingUpgradeChoice;

    private void Awake()
    {
        Instance = this;

        // IsStorming is static, so if the Editor's Domain Reload is disabled it
        // survives across stopping and re-entering Play mode. Force a clean
        // state here rather than relying on its default value.
        IsStorming = false;
        phaseTimer = 0f;
        awaitingUpgradeChoice = false;
        isFirstDay = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += BeginDay;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= BeginDay;
    }

    private void Update()
    {
        // TutorialManager.IsActive freezes the day clock the same way
        // awaitingUpgradeChoice does, so no storm can start mid-tutorial.
        if (awaitingUpgradeChoice || TutorialManager.IsActive) return;

        phaseTimer += Time.deltaTime;

        float effectiveDayDuration = isFirstDay && TutorialManager.DidRunThisSession
            ? Mathf.Max(0f, dayDuration - firstDayDurationReduction)
            : dayDuration;

        if (!IsStorming && phaseTimer >= effectiveDayDuration)
        {
            phaseTimer -= effectiveDayDuration;
            IsStorming = true;
            isFirstDay = false;
            StormStarted?.Invoke();
            stormOverlayEffect?.Play();
            upgradeSelectionUI?.BeginStormFadeIn();
        }
        else if (IsStorming && !trashSpawner.AnyTrashAlive())
        {
            phaseTimer = 0f;
            trashSpawner.BeginFadeOutAndClear(fadeOutDuration);
            BuildingHealth.HealAll();
            StormEnded?.Invoke();
            stormOverlayEffect?.Stop();
            CurrentRound++;

            // The run's last storm offers no upgrade pick: the cutscene that
            // follows is about to knock the garbage patch's final segment off
            // and end the run as a win, so a card chosen here could never be
            // used — it just delays the win screen behind a meaningless
            // choice. The patch's health is still pre-hit at this point (the
            // cutscene's TakeHit hasn't run yet), so one segment left means
            // this round is the last one.
            bool finalStorm = GarbagePatch.Instance != null && GarbagePatch.Instance.CurrentHealth <= 1;

            if (upgradeSelectionUI != null && !finalStorm)
            {
                awaitingUpgradeChoice = true;
                upgradeSelectionUI.Show(HandleUpgradeChoiceComplete);
            }
            else if (garbagePatchCutsceneController != null)
            {
                // Skipping the pick still has to clear the storm darkening
                // (normally UpgradeSelectionUI.Select's job) and still has to
                // park the day clock while the cutscene and win screen run.
                upgradeSelectionUI?.EndStormFadeOut();
                awaitingUpgradeChoice = true;
                HandleUpgradeChoiceComplete();
            }
            else
            {
                upgradeSelectionUI?.EndStormFadeOut();
                IsStorming = false;
                BeginDay();
            }
        }
    }

    // IsStorming deliberately stays true (see below) for the whole
    // awaitingUpgradeChoice window rather than flipping the instant trash
    // clears — TurtleAgent reads it independently every frame, and every
    // day-only behavior (resuming the target resource objective, harvesting,
    // resource respawn, etc.) is gated on it, so turtles would otherwise
    // start moving and collecting again while the player is still looking at
    // the upgrade cards (or, now, while the garbage-patch cutscene/hazard
    // pick/island transition below is still running).
    private void HandleUpgradeChoiceComplete()
    {
        if (garbagePatchCutsceneController != null) garbagePatchCutsceneController.RunPostStormSequence(HandlePostStormSequenceComplete);
        else HandlePostStormSequenceComplete();
    }

    private void HandlePostStormSequenceComplete()
    {
        awaitingUpgradeChoice = false;
        IsStorming = false;
        BeginDay();
    }

    private void BeginDay()
    {
        float ratingBudget = baseRatingBudget * Mathf.Pow(ratingGrowthPerRound, CurrentRound - 1) + linearRatingPerRound * (CurrentRound - 1);

        // GarbagePatch.CurrentHealth == 1 means this round, once survived, is
        // exactly what depletes it — the last wave before the run moves to a
        // new island — so spike the budget one extra time right before that
        // happens. A fresh island's own first round starts with a full-health
        // patch again, so this never fires on round 1 of a new island.
        if (GarbagePatch.Instance != null && GarbagePatch.Instance.CurrentHealth == 1)
        {
            ratingBudget *= finalWaveRatingMultiplier;
            Debug.Log($"DayStormCycle: final wave before the garbage patch depletes — rating budget multiplied by {finalWaveRatingMultiplier}.");
        }

        trashSpawner.SpawnRound(ratingBudget);
    }

    /// <summary>Called by IslandTransitionController when a garbage patch is depleted and the run moves to a new island: resets the round counter (so BeginDay's rating-budget formula starts back at Base Rating Budget, the round-1 amount) but permanently steepens the linear/exponential per-round ramp, so each successive island's difficulty curve climbs faster than the last one's despite the reset.</summary>
    public void AdvanceToNextIsland()
    {
        CurrentRound = 1;
        linearRatingPerRound += linearRatingIncreasePerIsland;
        ratingGrowthPerRound += ratingGrowthIncreasePerIsland;

        Debug.Log($"DayStormCycle: advanced to next island — round reset to 1, linearRatingPerRound now {linearRatingPerRound}, ratingGrowthPerRound now {ratingGrowthPerRound:F3}");
    }
}
