using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where turtles carry Seaweed/Coconut (and future food items — see
/// ResourceManager.IsFoodType) instead of the Nest. During a storm (night)
/// only, any turtle within Eat Range is fed automatically (see
/// UpdateProximityEating) on its own per-turtle interval timer, regardless of
/// what it's currently doing — feeding is purely proximity-based, so this
/// building forces itself non-interactable (see OnEnable) rather than relying
/// on a prefab checkbox: a turtle should never need to be explicitly sent here
/// (TurtleAgent.MoveToBuilding) or physically bump it, just be nearby. Each
/// bite consumes 1 stored food, and is refused once the eating turtle has hit
/// its nightly appetite cap (TurtleAgent.TryConsumeAppetite) — see
/// RegisterEatHit. Shows one of three pile sprites depending on how much food
/// is stored (no sprite while empty). Only one is expected to exist at a time
/// — see BuildModeController.EnsureFoodBuildingPlaced, which forces the player
/// to place one the first time a food-granting upgrade is picked.
/// </summary>
public class FoodBuilding : MonoBehaviour
{
    public static FoodBuilding Instance { get; private set; }

    /// <summary>Set the instant trash destroys the placed Food Building (see HandleBuildingDestroyed), holding the spot it stood in. Consumed and cleared by DayStormCycle.BeginDay, which rebuilds it there for free at the start of the next day. Also cleared whenever any FoodBuilding (rebuilt or freshly placed) becomes active, so a stale position never lingers or causes a duplicate rebuild.</summary>
    public static Vector3? PendingRebuildPosition { get; private set; }

    [Header("Food Amount")]
    [Tooltip("Stored food strictly below this is the empty (no sprite) tier.")]
    [SerializeField] private int lowFoodThreshold = 5;
    [SerializeField] private int mediumFoodThreshold = 15;
    [SerializeField] private int lotsFoodThreshold = 30;

    [Header("Pile Visual")]
    [SerializeField] private SpriteRenderer pileRenderer;
    [SerializeField] private Sprite lowFoodSprite;
    [SerializeField] private Sprite mediumFoodSprite;
    [SerializeField] private Sprite lotsFoodSprite;

    [Header("Eating")]
    [SerializeField] private float buffSpeedMultiplier = 1.25f;
    [Tooltip("Small pop-and-fade effect (see ResourcePopEffect) spawned at the eating turtle's position on every bite, for visual feedback that a hit actually landed.")]
    [SerializeField] private GameObject eatPopEffectPrefab;
    [Tooltip("Icon shown on the eat pop effect. Leave unassigned for no icon.")]
    [SerializeField] private Sprite eatPopIcon;
    [Tooltip("Radius within which a turtle eats automatically, regardless of what it's currently doing.")]
    [SerializeField] private float eatRange = 2f;
    [Tooltip("Seconds a turtle must wait between bites while within Eat Range, so eating isn't instant/continuous.")]
    [SerializeField] private float eatInterval = 1f;

    private int storedFood;

    // Per-turtle cooldown while within eatRange — starts at 0 so a turtle
    // that just entered range bites immediately, then waits eatInterval
    // between every bite after that. Entries are removed the moment a turtle
    // leaves range, so re-entering later always bites immediately again.
    private readonly Dictionary<TurtleAgent, float> eatTimers = new Dictionary<TurtleAgent, float>();

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("FoodBuilding: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        PendingRebuildPosition = null;
        RefreshVisualTier();
        BuildingHealth.Destroyed += HandleBuildingDestroyed;

        // Feeding is purely proximity-based (see UpdateProximityEating) — this
        // is never meant to be an explicit player order, so enforce that in
        // code rather than leaving it to whatever a prefab's Is Interactable
        // checkbox happens to be set to.
        GetComponent<BuildingHealth>()?.SetInteractable(false);

        // The prefab's own GameObject layer is still "InteractableBuilding"
        // (authored back when this actually was one) — that layer physically
        // collides with any turtle currently on the TurtleInteracting layer
        // (approaching some OTHER interactable building elsewhere), regardless
        // of the IsInteractable override above, since that's a completely
        // separate mechanism (see TurtleAgent.UpdateBuildingCollision). Force
        // it onto the plain "Building" layer instead, which turtles never
        // physically collide with, so it can't still get bumped in passing.
        int buildingLayer = LayerMask.NameToLayer("Building");
        if (buildingLayer >= 0) gameObject.layer = buildingLayer;
    }

    private void OnDisable()
    {
        BuildingHealth.Destroyed -= HandleBuildingDestroyed;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        UpdateProximityEating();
    }

    /// <summary>Feeds every turtle within eatRange on its own per-turtle interval timer (see eatTimers), independent of each turtle's actual current task/order. Turtles that leave range have their timer forgotten, so a later return always bites immediately rather than resuming a stale cooldown. Only active during a storm (night) — during the day, turtles don't eat at all; each turtle's cooldown simply stops advancing rather than resetting, so eating resumes right where it left off once the next storm starts.</summary>
    private void UpdateProximityEating()
    {
        if (!DayStormCycle.IsStorming) return;

        float rangeSqr = eatRange * eatRange;

        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle == null) continue;

            float sqrDistance = ((Vector2)turtle.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqrDistance > rangeSqr)
            {
                eatTimers.Remove(turtle);
                continue;
            }

            if (!eatTimers.TryGetValue(turtle, out float timer)) timer = 0f;
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                if (storedFood > 0)
                {
                    RegisterEatHit(turtle);
                    timer = eatInterval;
                }
                else
                {
                    // Nothing to eat yet — pin at 0 rather than losing a full
                    // interval to a bite that never actually happened, so
                    // feeding resumes the instant more food is deposited.
                    timer = 0f;
                }
            }

            eatTimers[turtle] = timer;
        }
    }

    /// <summary>Notices trash finishing off this specific building (BuildingHealth.Destroyed fires for every building, not just this one) and remembers its spot for DayStormCycle to rebuild at the start of the next day.</summary>
    private void HandleBuildingDestroyed(BuildingHealth building)
    {
        if (building == null || building.gameObject != gameObject) return;

        PendingRebuildPosition = transform.position;
    }

    /// <summary>Called by TurtleAgent as each carried food unit's delivery pop-effect completes.</summary>
    public void Deposit(int amount)
    {
        storedFood += amount;
        RefreshVisualTier();
    }

    /// <summary>Called by UpdateProximityEating once per bite for a turtle within eatRange. Harmless (no-op) if stored food has run out, turtle is null, or the turtle has already hit its nightly appetite cap (TryConsumeAppetite) — a full turtle just doesn't eat anymore until appetite resets the next day.</summary>
    private void RegisterEatHit(TurtleAgent turtle)
    {
        if (storedFood <= 0 || turtle == null) return;
        if (!turtle.TryConsumeAppetite()) return;

        storedFood--;
        RefreshVisualTier();
        turtle.ApplyFoodBuff(buffSpeedMultiplier);
        SpawnEatPopEffect(turtle.transform.position);
    }

    private void SpawnEatPopEffect(Vector3 position)
    {
        if (eatPopEffectPrefab == null) return;

        GameObject instance = Instantiate(eatPopEffectPrefab, position, Quaternion.identity);
        instance.GetComponent<ResourcePopEffect>()?.Initialize(eatPopIcon, position, null);
    }

    private void RefreshVisualTier()
    {
        if (pileRenderer == null) return;

        pileRenderer.sprite = storedFood < lowFoodThreshold ? null
            : storedFood < mediumFoodThreshold ? lowFoodSprite
            : storedFood < lotsFoodThreshold ? mediumFoodSprite
            : lotsFoodSprite;
        pileRenderer.enabled = storedFood >= lowFoodThreshold;
    }
}
