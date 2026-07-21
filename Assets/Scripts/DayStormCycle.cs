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

    [Header("Timing")]
    [SerializeField] private float dayDuration = 30f;
    [SerializeField] private float fadeOutDuration = 2f;

    [Header("Trash Scaling")]
    [Tooltip("Total plastic 'rating' budget spawned on round 1. Each piece of trash consumes an amount of this budget equal to its own TrashDefinition.Rating, so this controls both how much trash appears and, once the budget is large enough, whether higher-rated (harder) plastic types can afford to show up at all.")]
    [SerializeField] private float baseRatingBudget = 8f;
    [Tooltip("Flat amount added to the rating budget every round, on top of the exponential growth below. Makes early rounds ramp up noticeably instead of the first few rounds barely differing from round 1 (a pure percentage is a tiny absolute amount when the budget is still small).")]
    [SerializeField] private float linearRatingPerRound = 2f;
    [Tooltip("Multiplier applied to the rating budget every round, compounding on top of the linear growth above. Kept modest (e.g. 1.08 = 8%/round) rather than large, since compounding on an already-growing budget is what makes a pure exponential curve feel fine for a while and then suddenly unbeatable — the linear term above carries most of the early-game ramp so this doesn't have to.")]
    [SerializeField] private float ratingGrowthPerRound = 1.08f;

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
        if (awaitingUpgradeChoice) return;

        phaseTimer += Time.deltaTime;

        if (!IsStorming && phaseTimer >= dayDuration)
        {
            phaseTimer -= dayDuration;
            IsStorming = true;
            StormStarted?.Invoke();
            upgradeSelectionUI?.BeginStormFadeIn();
        }
        else if (IsStorming && !trashSpawner.AnyTrashAlive())
        {
            phaseTimer = 0f;
            trashSpawner.BeginFadeOutAndClear(fadeOutDuration);
            BuildingHealth.HealAll();
            StormEnded?.Invoke();
            CurrentRound++;

            if (upgradeSelectionUI != null)
            {
                awaitingUpgradeChoice = true;
                upgradeSelectionUI.Show(HandleUpgradeChoiceComplete);
            }
            else
            {
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
    // the upgrade cards.
    private void HandleUpgradeChoiceComplete()
    {
        awaitingUpgradeChoice = false;
        IsStorming = false;
        BeginDay();
    }

    private void BeginDay()
    {
        float ratingBudget = baseRatingBudget * Mathf.Pow(ratingGrowthPerRound, CurrentRound - 1) + linearRatingPerRound * (CurrentRound - 1);
        trashSpawner.SpawnRound(ratingBudget);
    }
}
