using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The home base the player protects. IslandGenerator instantiates this prefab
/// at the exact center of the map after generation. Ends the game the moment a
/// trash item (see TrashItem) reaches it. No longer spawns turtles on its own
/// timer — SpawnTurtle is called externally by each placed TurtleBed instead,
/// so at least one bed is required to grow the turtle population at all.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TurtleNest : MonoBehaviour
{
    /// <summary>The currently active nest, if any. TurtleAgent instances are always runtime-spawned (via SpawnTurtle), so they can't hold a pre-wired scene reference the way scene-placed objects can — this lets them find the nest to guard during a storm.</summary>
    public static TurtleNest Instance { get; private set; }

    [Header("Turtle Spawning")]
    [Tooltip("Baby turtle prefab instantiated at the nest whenever a TurtleBed calls SpawnTurtle.")]
    [SerializeField] private GameObject turtlePrefab;
    [Tooltip("Parent transform spawned turtles are placed under. Defaults to this object if left empty.")]
    [SerializeField] private Transform turtleSpawnParent;

    [Header("Game Over")]
    [Tooltip("Invoked once, the moment a trash item reaches the nest. Wire up UI / game-over flow here in the Inspector.")]
    [SerializeField] private UnityEvent onNestDestroyed;

    /// <summary>Raised once, the moment a trash item reaches the nest.</summary>
    public event Action NestDestroyed;

    public bool IsDestroyed { get; private set; }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Spawns one turtle at the nest's position. Called externally by a TurtleBed once its own placement delay elapses. Returns null if the nest is destroyed or has no turtle prefab configured.</summary>
    public GameObject SpawnTurtle()
    {
        if (IsDestroyed || turtlePrefab == null) return null;

        Transform parent = turtleSpawnParent != null ? turtleSpawnParent : transform;
        return Instantiate(turtlePrefab, transform.position, Quaternion.identity, parent);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandlePotentialTrashContact(other.GetComponentInParent<TrashItem>());
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandlePotentialTrashContact(collision.collider.GetComponentInParent<TrashItem>());
    }

    private void HandlePotentialTrashContact(TrashItem trash)
    {
        if (trash == null || IsDestroyed) return;

        IsDestroyed = true;
        Debug.Log("TurtleNest: reached by trash — game over.");
        NestDestroyed?.Invoke();
        onNestDestroyed?.Invoke();
    }
}
