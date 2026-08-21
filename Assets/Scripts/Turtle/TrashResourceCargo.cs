using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to a trash prefab alongside TrashHealth (Pallet and Box are the
/// intended ones — big crated things that plausibly have something strapped to
/// them) to give it a chance of washing ashore loaded with salvage. Destroy a
/// loaded piece and its cargo pops off and flies to the nest, crediting the
/// player, on the same effect turtles deliver their own harvests with.
///
/// Deliberately shaped like TrashDefinition's death drops, which it sits
/// beside: the roll is made once, the payload is authored per prefab, and the
/// whole thing is gated behind a run-wide unlock (see
/// UpgradeManager.TrashSalvageChance) so a component left on a prefab is inert
/// until the card that turns it on is picked. It hooks TrashHealth.Died — the
/// static event every piece of trash raises — and filters to itself, exactly
/// as BatteryAcidOnDeath does.
///
/// The roll happens at Start rather than on death so the cargo can be VISIBLE
/// beforehand (see Cargo Visuals): the player gets to see which piece of trash
/// is worth killing while there is still time to act on it, which is the point
/// of the upgrade. Rolling at death would make it a silent lottery.
///
/// Score is awarded per delivered unit, matching a turtle's own delivery — the
/// resources are genuinely new to the run's economy and were never scored on
/// the way in, unlike a demolition refund (see DemolishTool, which deliberately
/// skips score for exactly that reason).
/// </summary>
[RequireComponent(typeof(TrashHealth))]
public class TrashResourceCargo : MonoBehaviour
{
    [Serializable]
    private struct CargoOption
    {
        [Tooltip("Resource this option pays out.")]
        public ResourceManager.ResourceType type;
        [Tooltip("Sprite flown to the nest for it. An option with no icon still pays out, just instantly with no animation.")]
        public Sprite icon;
        [Tooltip("Fewest units of it this trash can be carrying (inclusive).")]
        public int minAmount;
        [Tooltip("Most units of it this trash can be carrying (inclusive).")]
        public int maxAmount;
        [Tooltip("Relative likelihood of this option being the one picked, against the other entries. Equal weights make every option equally likely; 0 takes it out of the draw.")]
        public float weight;
    }

    [Header("Cargo")]
    [Tooltip("What this trash type can be carrying. One is drawn at random by Weight when the roll succeeds. Leave empty and this component does nothing at all.")]
    [SerializeField] private CargoOption[] cargoOptions;

    [Tooltip("Objects switched on only while this piece of trash is actually carrying something — the visible tell that makes it worth prioritizing. Best SAVED INACTIVE on the prefab: Awake hides them anyway, but starting off means an unloaded piece never has to run anything to look right. Leave empty for no visual.")]
    [SerializeField] private GameObject[] cargoVisuals;

    [Header("Delivery")]
    [Tooltip("The same delivery pop-effect prefab turtles fly resources to the nest with — Assets/Prefabs/Animals/CarryingResources/PopToEffect. Leave unassigned and the salvage still lands correctly, just instantly with no animation.")]
    [SerializeField] private GameObject deliveryPopEffectPrefab;
    [Tooltip("Most effects spawned for one payout. A bigger haul rides fewer effects carrying more each, so a loaded Pallet doesn't spray a dozen sprites across the island (mirrors DemolishTool's refund).")]
    [SerializeField, Min(1)] private int maxEffects = 4;
    [Tooltip("Radius around the death point the effects start from, so several leaving at once fan out instead of stacking into what looks like one sprite.")]
    [SerializeField, Min(0f)] private float effectSpread = 0.35f;

    private TrashHealth trashHealth;
    private int cargoOptionIndex = -1;
    private int cargoAmount;

    /// <summary>True once this piece of trash has rolled successfully and is actually carrying something.</summary>
    public bool IsCarryingCargo => cargoOptionIndex >= 0 && cargoAmount > 0;

    private void Awake()
    {
        trashHealth = GetComponent<TrashHealth>();
        SetVisualsActive(false);
    }

    private void OnEnable() => TrashHealth.Died += HandleDied;

    private void OnDisable() => TrashHealth.Died -= HandleDied;

    private void Start()
    {
        // Start, not Awake: TrashSpawner instantiates trash during a storm
        // transition and UpgradeManager's own singleton has long since been
        // assigned by then, but reading run state from Awake is the kind of
        // ordering assumption this project deliberately avoids everywhere else.
        RollCargo();
    }

    private void RollCargo()
    {
        if (cargoOptions == null || cargoOptions.Length == 0) return;

        float chance = UpgradeManager.Instance != null ? UpgradeManager.Instance.TrashSalvageChance : 0f;
        if (chance <= 0f || UnityEngine.Random.value >= chance) return;

        int index = PickWeightedOption();
        if (index < 0) return;

        CargoOption option = cargoOptions[index];
        int min = Mathf.Max(1, option.minAmount);
        int max = Mathf.Max(min, option.maxAmount);

        cargoOptionIndex = index;
        cargoAmount = UnityEngine.Random.Range(min, max + 1);
        SetVisualsActive(true);
    }

    /// <summary>Draws one cargo option by Weight, or -1 if every weight is zero. Mirrors TrashSpawner.PickWeighted, including its fall back to a uniform pick over a degenerate weight configuration rather than silently paying nothing.</summary>
    private int PickWeightedOption()
    {
        float totalWeight = 0f;
        foreach (CargoOption option in cargoOptions) totalWeight += Mathf.Max(0f, option.weight);

        if (totalWeight <= 0f) return UnityEngine.Random.Range(0, cargoOptions.Length);

        float roll = UnityEngine.Random.value * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < cargoOptions.Length; i++)
        {
            cumulative += Mathf.Max(0f, cargoOptions[i].weight);
            if (roll < cumulative) return i;
        }

        return cargoOptions.Length - 1;
    }

    private void SetVisualsActive(bool active)
    {
        if (cargoVisuals == null) return;

        foreach (GameObject visual in cargoVisuals)
        {
            if (visual != null) visual.SetActive(active);
        }
    }

    /// <summary>TrashHealth.Died fires for every piece of trash in the scene, so filter to this one (same pattern as BatteryAcidOnDeath).</summary>
    private void HandleDied(TrashHealth trash)
    {
        if (trash != trashHealth || !IsCarryingCargo) return;

        DeliverCargo(cargoOptions[cargoOptionIndex], cargoAmount, transform.position);

        // Cleared so a second Died for this instance — however it arrived —
        // can't pay the same salvage out twice.
        cargoOptionIndex = -1;
        cargoAmount = 0;
    }

    /// <summary>
    /// Flies the salvage to the nest on the turtles' own delivery pop effect,
    /// crediting ResourceManager only as each one lands — the same deferred
    /// credit TurtleAgent.DeliverCarriedResources and DemolishTool both use, so
    /// the number on the HUD always ticks up in step with what the player can
    /// see arriving.
    /// </summary>
    private void DeliverCargo(CargoOption option, int amount, Vector3 origin)
    {
        if (ResourceManager.Instance == null) return;

        Transform nest = TurtleNest.Instance != null ? TurtleNest.Instance.transform : null;
        if (nest == null || deliveryPopEffectPrefab == null || option.icon == null)
        {
            // Nothing to fly it with, or nowhere to fly it to (the nest being
            // gone meaning the run is already over) — still credit it
            // correctly, just instantly. Same fallback every other delivery
            // path in the project takes.
            CreditSalvage(option.type, amount);
            return;
        }

        int effectCount = Mathf.Min(amount, maxEffects);
        int perEffect = amount / effectCount;
        int remainder = amount % effectCount;

        for (int i = 0; i < effectCount; i++)
        {
            // Locals, not the loop variables: a `for` variable is captured by
            // reference, so every closure below would otherwise see the final
            // iteration's values.
            ResourceManager.ResourceType type = option.type;
            int payload = perEffect + (i < remainder ? 1 : 0);

            Vector2 offset = UnityEngine.Random.insideUnitCircle * effectSpread;
            Vector3 from = origin + new Vector3(offset.x, offset.y, 0f);

            GameObject instance = Instantiate(deliveryPopEffectPrefab, from, Quaternion.identity);
            instance.GetComponent<ResourcePopEffect>()?.Initialize(
                option.icon, from, nest.position,
                () =>
                {
                    CreditSalvage(type, payload);
                    TurtleNest.Instance?.PlaySquash();
                });
        }
    }

    private static void CreditSalvage(ResourceManager.ResourceType type, int amount)
    {
        ResourceManager.Instance?.Add(type, amount);
        ScoreManager.Instance?.AddResourceScore(amount);
    }
}
