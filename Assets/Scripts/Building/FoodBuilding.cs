using UnityEngine;

/// <summary>
/// Where turtles carry Seaweed/Coconut (and future food items — see
/// ResourceManager.IsFoodType) instead of the Nest, and where a turtle sent
/// here explicitly (see TurtleAgent.MoveToBuilding/TurtleSelectionController,
/// via BuildingHealth.IsInteractable) grazes continuously — each physical
/// head-bump (see HandleHeadHit's FoodBuilding branch) consumes 1 stored food
/// and extends the eating turtle's shared speed buff (TurtleAgent.ApplyFoodBuff)
/// rather than resetting it. Shows one of three pile sprites depending on how
/// much food is stored (no sprite while empty). Only one is expected to exist
/// at a time — see BuildModeController.EnsureFoodBuildingPlaced, which forces
/// the player to place one the first time a food-granting upgrade is picked.
/// </summary>
public class FoodBuilding : MonoBehaviour
{
    public static FoodBuilding Instance { get; private set; }

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
    [Tooltip("Speed-buff duration added per bite. Repeated bites stack additively (see TurtleAgent.ApplyFoodBuff), not reset.")]
    [SerializeField] private float buffDurationPerEat = 5f;

    private int storedFood;

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("FoodBuilding: duplicate instance in scene, destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RefreshVisualTier();
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called by TurtleAgent as each carried food unit's delivery pop-effect completes.</summary>
    public void Deposit(int amount)
    {
        storedFood += amount;
        RefreshVisualTier();
    }

    /// <summary>Called by TurtleAgent.HandleHeadHit on every physical bump from a turtle sent here — continuous grazing, same idiom as a resource node. Harmless (no-op) once stored food runs out; the turtle just keeps bumping until re-tasked.</summary>
    public void RegisterEatHit(TurtleAgent turtle)
    {
        if (storedFood <= 0 || turtle == null) return;

        storedFood--;
        RefreshVisualTier();
        turtle.ApplyFoodBuff(buffDurationPerEat, buffSpeedMultiplier);
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
