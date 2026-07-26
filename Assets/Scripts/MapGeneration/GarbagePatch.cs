using System;
using UnityEngine;

/// <summary>
/// The narrative source of the trash: one of these exists per island, orbiting
/// offshore (see GarbagePatchOrbit) with a handful of hit points — one per
/// round survived. GarbagePatchCutsceneController calls TakeHit() once at the
/// end of every storm; once the last hit lands, Depleted fires and
/// IslandTransitionController takes over to move the run to a new island.
///
/// Its entire visual is generated at runtime rather than hand-authored: on
/// spawn, GarbagePatchSpawner calls SpawnDebris, which scatters ~50 sprites
/// (picked at random from Debris Sprites below) as children of Debris Root in
/// a loose pile. Debris Root — not this object's own transform — is what
/// GarbagePatchOrbit spins for the tumbling look, so the health bar (a
/// separate child of this transform, not of Debris Root) never rotates and
/// stays fixed below the patch regardless of how the pile tumbles.
/// </summary>
public class GarbagePatch : MonoBehaviour
{
    /// <summary>The one currently-spawned patch, if any (mirrors TurtleNest.Instance — one per island, not a list).</summary>
    public static GarbagePatch Instance { get; private set; }

    /// <summary>Raised the instant health newly reaches 0.</summary>
    public static event Action Depleted;

    [Tooltip("Hit points — one per round the patch must survive before it's destroyed and the run moves to a new island.")]
    [SerializeField] private int maxSegments = 5;
    [Tooltip("Kept as a direct child of this transform (not of Debris Root), so it never spins along with the tumbling debris pile and always sits at the same fixed offset below the patch.")]
    [SerializeField] private GarbagePatchHealthBar healthBar;

    [Header("Debris Visual")]
    [Tooltip("Sprites the debris pile picks from at random when spawned. Populate with whatever trash art should visually make up the patch.")]
    [SerializeField] private Sprite[] debrisSprites;
    [Tooltip("Transform the scattered debris pieces are parented to — GarbagePatchOrbit spins this (not the patch's own transform), so the pile tumbles independently of the health bar.")]
    [SerializeField] private Transform debrisRoot;
    [Tooltip("How many decorative sprite pieces are scattered as children of Debris Root.")]
    [SerializeField] private int debrisCount = 50;
    [Tooltip("Radius (world units) of the loose pile the debris pieces are scattered within.")]
    [SerializeField] private float debrisClusterRadius = 1.5f;

    public int MaxSegments => maxSegments;
    public int CurrentHealth { get; private set; }

    /// <summary>Overrides Max Segments — called by GarbagePatchSpawner right after spawning, to apply the chosen game mode's rounds-per-island. Awake/OnEnable above already ran with the prefab's baked-in default by that point, so this also resets CurrentHealth to match and refreshes the health bar rather than leaving them stale.</summary>
    public void SetMaxSegments(int value)
    {
        maxSegments = value;
        CurrentHealth = maxSegments;
        if (healthBar != null) healthBar.SetSegments(CurrentHealth, maxSegments);
    }

    private SquashAndStretch squashAndStretch;

    private void Awake()
    {
        CurrentHealth = maxSegments;
        squashAndStretch = GetComponent<SquashAndStretch>();
    }

    private void OnEnable()
    {
        Instance = this;
        if (healthBar != null) healthBar.SetSegments(CurrentHealth, maxSegments);
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Scatters debrisCount decorative sprite children (from Debris Sprites) under Debris Root in a loose pile. Purely visual — no collider, no TrashAgent/TrashHealth, not real trash instances.</summary>
    public void SpawnDebris()
    {
        if (debrisSprites == null || debrisSprites.Length == 0) return;

        Transform parent = debrisRoot != null ? debrisRoot : transform;

        for (int i = 0; i < debrisCount; i++)
        {
            GameObject piece = new GameObject("Debris");
            piece.transform.SetParent(parent, false);

            Vector2 offset = UnityEngine.Random.insideUnitCircle * debrisClusterRadius;
            piece.transform.localPosition = new Vector3(offset.x, offset.y, UnityEngine.Random.Range(-0.05f, 0.05f));
            piece.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));

            SpriteRenderer renderer = piece.AddComponent<SpriteRenderer>();
            renderer.sprite = debrisSprites[UnityEngine.Random.Range(0, debrisSprites.Length)];
        }
    }

    /// <summary>No-ops if already depleted. Decrements health, pushes the new value to the health bar, and once it newly reaches 0 raises Depleted then destroys this patch entirely (debris pile and all) rather than leaving an empty husk orbiting until the next island's spawn replaces it.</summary>
    public void TakeHit()
    {
        if (CurrentHealth <= 0) return;

        CurrentHealth--;
        if (healthBar != null) healthBar.SetSegments(CurrentHealth, maxSegments);
        squashAndStretch?.Play();

        if (CurrentHealth <= 0)
        {
            Depleted?.Invoke();
            Destroy(gameObject);
        }
    }
}
