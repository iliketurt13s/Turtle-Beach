using System;
using UnityEngine;

/// <summary>
/// Drives the repeating day/storm cycle: daylight lets the player see and
/// prepare for the next wave of trash; storm sets TrashAgent instances loose
/// to burst toward the nest. Each cycle spawns more trash than the last and
/// heals all buildings back to full once the storm ends.
/// </summary>
public class DayStormCycle : MonoBehaviour
{
    /// <summary>True during the storm phase; polled by TrashAgent to know whether to burst-move.</summary>
    public static bool IsStorming { get; private set; }

    /// <summary>Raised the instant a storm ends (right after buildings are healed, right before the round counter increments). Lets temporary per-storm effects (e.g. Iron Ingot's building health buff) know exactly when to revert.</summary>
    public static event Action StormEnded;

    [Header("References")]
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private TrashSpawner trashSpawner;
    [Tooltip("Optional — rolled once per round for a chance to spawn a new Jellyfish in the shallows. Leave unassigned if the Jellyfish upgrade isn't in use.")]
    [SerializeField] private JellyfishSpawner jellyfishSpawner;
    [Tooltip("Shown at the end of every storm before the next day begins. If left unassigned, the next day begins immediately with no upgrade choice.")]
    [SerializeField] private UpgradeSelectionUI upgradeSelectionUI;

    [Header("Timing")]
    [SerializeField] private float dayDuration = 30f;
    [SerializeField] private float fadeOutDuration = 2f;

    [Header("Trash Scaling")]
    [Tooltip("Total plastic 'rating' budget spawned on round 1. Each piece of trash consumes an amount of this budget equal to its own TrashDefinition.Rating, so this controls both how much trash appears and, once the budget is large enough, whether higher-rated (harder) plastic types can afford to show up at all.")]
    [SerializeField] private float baseRatingBudget = 8f;
    [Tooltip("Multiplier applied to the rating budget every round, so the budget grows exponentially rather than linearly. E.g. 1.15 means each round's budget is 15% bigger than the last.")]
    [SerializeField] private float ratingGrowthPerRound = 1.15f;

    public int CurrentRound { get; private set; } = 1;

    private float phaseTimer;
    private bool awaitingUpgradeChoice;

    private void Awake()
    {
        // IsStorming is static, so if the Editor's Domain Reload is disabled it
        // survives across stopping and re-entering Play mode. Force a clean
        // state here rather than relying on its default value.
        IsStorming = false;
        phaseTimer = 0f;
        awaitingUpgradeChoice = false;
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
        }
        else if (IsStorming && !trashSpawner.AnyTrashAlive())
        {
            phaseTimer = 0f;
            IsStorming = false;
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
                BeginDay();
            }
        }
    }

    private void HandleUpgradeChoiceComplete()
    {
        awaitingUpgradeChoice = false;
        BeginDay();
    }

    private void BeginDay()
    {
        float ratingBudget = baseRatingBudget * Mathf.Pow(ratingGrowthPerRound, CurrentRound - 1);
        trashSpawner.SpawnRound(ratingBudget);
        jellyfishSpawner?.TryRollSpawn();
    }
}
