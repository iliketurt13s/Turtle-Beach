using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The narrative source of the trash: one of these exists per island, orbiting
/// offshore (see GarbagePatchOrbit) with a handful of hit points — one per
/// round survived. GarbagePatchCutsceneController calls TakeHit() once at the
/// end of every storm, and once the last hit lands it shows the win screen
/// directly (GameOverUI.ShowWin) — the run ends there rather than moving on to
/// a new island the way it used to. Depleted is left in place as the general
/// "the patch is gone" signal but currently has no subscribers, since that
/// move-to-a-new-island path is what used to listen for it.
///
/// Its entire visual is generated at runtime rather than hand-authored: on
/// spawn, GarbagePatchSpawner calls SpawnDebris, which scatters ~50 sprites
/// (picked at random from Debris Sprites below) as children of Debris Root in
/// a loose pile. Debris Root — not this object's own transform — is what
/// GarbagePatchOrbit spins for the tumbling look.
///
/// That pile IS the health display; there's no bar any more. Each hit thins the
/// pile down to a piece count proportional to remaining health, and pieces go
/// from the outside in (see RemoveOneDebrisPiece), so the patch loses radius as
/// well as density — a nearly-dead patch reads as a small tight knot rather
/// than a sparse cloud still spanning the original circle. The final hit takes
/// the target to zero, so the patch is watched emptying out completely and only
/// then destroys itself, instead of blinking away with debris still on it.
///
/// Removal is deliberately only MOSTLY outside-in: each piece destroyed is the
/// outermost of a small random sample, never the outermost overall, so the edge
/// stays ragged and a few stragglers linger out past it. A strict
/// furthest-first rule would eat a perfect circle inward and read as a shrinking
/// dial rather than something coming apart. See Debris Removal Candidates.
///
/// A knocked-off piece is not deleted on the spot — it sinks (see
/// SinkDebrisPiece): shrinking partway down, fading out and juddering as it
/// goes. Combined with the per-hit squash across the whole pile, that is the
/// entire hit visual. It replaced a burst of particle explosions fired from
/// GarbagePatchCutsceneController, which read as the patch detonating each
/// storm rather than being gradually worn down and lost to the sea.
/// </summary>
public class GarbagePatch : MonoBehaviour
{
    /// <summary>The one currently-spawned patch, if any (mirrors TurtleNest.Instance — one per island, not a list).</summary>
    public static GarbagePatch Instance { get; private set; }

    /// <summary>Raised the instant health newly reaches 0.</summary>
    public static event Action Depleted;

    [Tooltip("Hit points — one per round the patch must survive before it's destroyed and the run moves to a new island.")]
    [SerializeField] private int maxSegments = 5;

    [Header("Debris Visual")]
    [Tooltip("Sprites the debris pile picks from at random when spawned. Populate with whatever trash art should visually make up the patch.")]
    [SerializeField] private Sprite[] debrisSprites;
    [Tooltip("Transform the scattered debris pieces are parented to — GarbagePatchOrbit spins this rather than the patch's own transform, so the pile tumbles on the spot as the patch orbits.")]
    [SerializeField] private Transform debrisRoot;
    [Tooltip("How many decorative sprite pieces are scattered as children of Debris Root at full health. Doubles as the resolution of the health display: the pile is thinned to a proportion of this, so a low count makes each hit's worth of removal coarser and more obvious.")]
    [SerializeField] private int debrisCount = 50;
    [Tooltip("Radius (world units) of the loose pile the debris pieces are scattered within at full health. Shrinks on its own as the patch takes damage — nothing else needs setting for that.")]
    [SerializeField] private float debrisClusterRadius = 1.5f;

    [Header("Debris Removal")]
    [Tooltip("How many surviving pieces are compared each time one is picked for removal — the outermost of that sample is the one destroyed. 1 removes a piece at random (density thins but the radius never shrinks); higher hugs the outside more strictly and pulls the radius in faster, at the cost of a cleaner, more obviously circular edge. Middling values read as eaten away rather than trimmed.")]
    [SerializeField, Min(1)] private int debrisRemovalCandidates = 4;
    [Tooltip("Seconds one hit's worth of debris takes to disappear, spread evenly across the pieces rather than popping them all in the same frame. Keep it under GarbagePatchCutsceneController's Hold At Patch Duration so the whole thinning plays out while the camera is still parked on the patch.")]
    [SerializeField] private float debrisRemovalDuration = 0.8f;

    [Header("Debris Sinking")]
    [Tooltip("Optional effect played on the patch each time a hit's worth of debris starts sinking. Meant to be a particle system parented under the patch so it covers the whole pile and rides along with the orbit, rather than a burst spawned loose in the water — this is the patch settling as a mass, not a point impact. Left unassigned, the debris simply sinks with no effect over it.")]
    [SerializeField] private ParticleSystem sinkEffect;
    [Tooltip("Seconds one knocked-off piece takes to sink out of sight. Pieces are released one after another across Debris Removal Duration and each sinks on its own clock, so these overlap — the pile dissolves continuously instead of in visible batches.")]
    [SerializeField] private float debrisSinkDuration = 1.2f;
    [Tooltip("Fraction of its original size a sinking piece shrinks to before it vanishes. Deliberately not 0: the piece fades out while still recognisable, which reads as sinking under the surface, where shrinking to nothing reads as being sucked into a point.")]
    [SerializeField, Range(0f, 1f)] private float debrisSinkScale = 0.55f;
    [Tooltip("How far (world units) a sinking piece jitters from where it sat. Small — this is debris bobbing as it goes under, not an explosion.")]
    [SerializeField] private float debrisSinkShake = 0.06f;
    [Tooltip("How many times a second a sinking piece rolls a fresh jitter offset. The offset is HELD between rolls, which is what makes it judder like something caught in chop rather than glide.")]
    [SerializeField] private float debrisSinkShakeFrequency = 20f;
    [Tooltip("Shapes the shrink and the fade over the sink. Ease-out (fast at first, settling) reads as the piece dropping away and then slipping under; linear reads more mechanical.")]
    [SerializeField] private AnimationCurve debrisSinkCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public int MaxSegments => maxSegments;
    public int CurrentHealth { get; private set; }

    /// <summary>Overrides Max Segments — called by GarbagePatchSpawner right after spawning, to apply the chosen game mode's rounds-per-island. Awake/OnEnable above already ran with the prefab's baked-in default by that point, so this also resets CurrentHealth to match rather than leaving it stale. Runs before SpawnDebris, so the pile is always scattered at the full count for a fresh patch.</summary>
    public void SetMaxSegments(int value)
    {
        maxSegments = value;
        CurrentHealth = maxSegments;
    }

    /// <summary>Every debris piece still in the pile. Entries are dropped as pieces are destroyed, so Count is the live piece count each hit thins down against.</summary>
    private readonly List<Transform> debrisPieces = new List<Transform>();
    private Coroutine debrisShrinkRoutine;

    /// <summary>How many knocked-off pieces are still playing their sink animation. The final hit has to wait this back down to zero before the patch destroys itself, or the last few pieces would be cut off mid-sink by their own parent disappearing.</summary>
    private int sinkingPieces;

    private SquashAndStretch squashAndStretch;

    private void Awake()
    {
        CurrentHealth = maxSegments;
        squashAndStretch = GetComponent<SquashAndStretch>();
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// World-space radius of what this patch currently LOOKS like: the reach of
    /// the furthest-out live debris sprite, its own extent included. Measured
    /// off the renderers rather than reported from Debris Cluster Radius,
    /// because the two diverge by design — the pile is eaten away from the
    /// outside in as health drops (see RemoveOneDebrisPiece), so the authored
    /// radius describes only the full-health pile while this describes the one
    /// actually on screen right now.
    ///
    /// Used by GarbagePatchCutsceneController to frame the patch, which is why
    /// it has to track the shrinking: a fixed zoom that suits a full pile
    /// leaves a nearly-depleted one as a speck in the middle of the ocean.
    ///
    /// Falls back to the authored cluster radius when nothing is rendered yet
    /// (debris not spawned) or nothing is left, so a caller always gets a
    /// usable number rather than zero.
    /// </summary>
    public float MeasureVisualRadius()
    {
        float radius = 0f;
        Vector2 center = transform.position;

        foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
        {
            if (renderer == null || !renderer.enabled) continue;

            // Bounds are already world-space, so this covers the debris root's
            // own rotation (GarbagePatchOrbit tumbles it) and any scaling
            // without having to reconstruct either.
            Bounds bounds = renderer.bounds;
            float reach = Vector2.Distance(center, bounds.center) + Mathf.Max(bounds.extents.x, bounds.extents.y);
            radius = Mathf.Max(radius, reach);
        }

        if (radius > 0f) return radius;

        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        return debrisClusterRadius * Mathf.Max(scale, 0.01f);
    }

    /// <summary>Scatters debrisCount decorative sprite children (from Debris Sprites) under Debris Root in a loose pile, and records them so damage can thin them back out again. Purely visual — no collider, no TrashAgent/TrashHealth, not real trash instances.</summary>
    public void SpawnDebris()
    {
        if (debrisSprites == null || debrisSprites.Length == 0) return;

        Transform parent = debrisRoot != null ? debrisRoot : transform;

        debrisPieces.Clear();

        for (int i = 0; i < debrisCount; i++)
        {
            GameObject piece = new GameObject("Debris");
            piece.transform.SetParent(parent, false);

            Vector2 offset = UnityEngine.Random.insideUnitCircle * debrisClusterRadius;
            piece.transform.localPosition = new Vector3(offset.x, offset.y, UnityEngine.Random.Range(-0.05f, 0.05f));
            piece.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));

            SpriteRenderer renderer = piece.AddComponent<SpriteRenderer>();
            renderer.sprite = debrisSprites[UnityEngine.Random.Range(0, debrisSprites.Length)];

            debrisPieces.Add(piece.transform);
        }
    }

    /// <summary>
    /// No-ops if already depleted. Decrements health and thins the debris pile
    /// down to match, which is the whole health display now that the bar is
    /// gone.
    ///
    /// On the final hit the target is zero pieces, so destruction is left to
    /// the shrink to do once it's actually emptied the pile — Depleted still
    /// fires immediately, since IslandTransitionController/GameOverUI shouldn't
    /// be made to wait on an animation.
    /// </summary>
    public void TakeHit()
    {
        if (CurrentHealth <= 0) return;

        CurrentHealth--;
        squashAndStretch?.Play();

        bool depleted = CurrentHealth <= 0;
        ShrinkDebrisTo(TargetPieceCount(), destroyWhenEmpty: depleted);

        if (depleted) Depleted?.Invoke();
    }

    /// <summary>How many pieces the pile should be down to at the current health — a straight proportion of Debris Count, so what's left on screen is a direct read-out of what's left of the patch. Big Island's game-mode preset sets Max Segments so high (see GarbagePatchSpawner) that this rounds to the full count for every hit a player will ever land; that's the intended "effectively never depletes" behavior for that mode, not an edge case to correct for.</summary>
    private int TargetPieceCount()
    {
        if (maxSegments <= 0) return 0;

        return Mathf.RoundToInt(debrisCount * Mathf.Clamp01((float)CurrentHealth / maxSegments));
    }

    /// <summary>Starts thinning the pile down to targetCount pieces, spread over Debris Removal Duration. Restarts cleanly over a shrink already in progress: the target is absolute (derived from current health) rather than a per-hit delta, so an interrupted shrink is corrected by the next one instead of leaving the pile permanently over-full.</summary>
    private void ShrinkDebrisTo(int targetCount, bool destroyWhenEmpty)
    {
        if (debrisShrinkRoutine != null) StopCoroutine(debrisShrinkRoutine);
        debrisShrinkRoutine = StartCoroutine(ShrinkDebrisRoutine(targetCount, destroyWhenEmpty));
    }

    private IEnumerator ShrinkDebrisRoutine(int targetCount, bool destroyWhenEmpty)
    {
        debrisPieces.RemoveAll(piece => piece == null);

        int removalCount = Mathf.Max(debrisPieces.Count - targetCount, 0);
        float interval = removalCount > 0 ? debrisRemovalDuration / removalCount : 0f;

        // Guarded so a hit that removes nothing (a patch already at its target
        // count, or Big Island's effectively-never-depleting preset) doesn't play
        // an effect over debris that never moves.
        if (removalCount > 0) sinkEffect?.Play();

        for (int i = 0; i < removalCount; i++)
        {
            RemoveOneDebrisPiece();

            // Scaled time, unlike this project's UI animations: this plays
            // inside GarbagePatchCutsceneController's own WaitForSeconds hold,
            // and the two have to stay in step if the player has time paused or
            // running at double speed.
            if (interval > 0f) yield return new WaitForSeconds(interval);
        }

        debrisShrinkRoutine = null;

        // The last pieces released above are still sinking at this point, and on
        // the final hit destroying the patch now would take them with it —
        // the run would end on debris blinking out mid-fade. Waiting is only
        // correct because this is the terminal hit: nothing else is coming, so
        // there is no later shrink for the delay to hold up.
        if (!destroyWhenEmpty) yield break;

        yield return new WaitUntil(() => sinkingPieces <= 0);
        Destroy(gameObject);
    }

    /// <summary>
    /// Destroys one piece: the outermost of Debris Removal Candidates randomly
    /// sampled survivors.
    ///
    /// Sampling rather than scanning for the true outermost is the entire
    /// "semi-organic" trick, and it's worth not optimizing away — the outside
    /// still goes first on average, so the radius pulls in, but exactly which
    /// piece goes stays unpredictable, so the edge frays and the odd straggler
    /// survives out past it for a few hits.
    /// </summary>
    private void RemoveOneDebrisPiece()
    {
        if (debrisPieces.Count == 0) return;

        int chosenIndex = 0;
        float chosenDistance = -1f;

        for (int i = 0; i < Mathf.Max(debrisRemovalCandidates, 1); i++)
        {
            int candidate = UnityEngine.Random.Range(0, debrisPieces.Count);
            Transform piece = debrisPieces[candidate];
            if (piece == null) continue;

            // Local space, so this is distance from the pile's own center
            // whatever the patch's orbit position and however far Debris Root
            // has spun. Left squared — only the ordering is used.
            Vector3 local = piece.localPosition;
            float distance = local.x * local.x + local.y * local.y;
            if (distance <= chosenDistance) continue;

            chosenDistance = distance;
            chosenIndex = candidate;
        }

        Transform chosen = debrisPieces[chosenIndex];
        debrisPieces.RemoveAt(chosenIndex);
        if (chosen != null) StartCoroutine(SinkDebrisPiece(chosen));
    }

    /// <summary>
    /// Plays one knocked-off piece out instead of destroying it outright: it
    /// shrinks part of the way down, fades to nothing and judders as it goes,
    /// then removes itself. The piece is already out of debrisPieces by the
    /// time this starts, so it can't be picked again and doesn't count toward
    /// the live piece total while it plays — what's sinking has, as far as the
    /// patch's health display is concerned, already gone.
    ///
    /// Shrinking only partway (Debris Sink Scale) and fading at the same time
    /// is what sells it as going under the surface. A piece that scaled all the
    /// way to zero would read as being crushed or sucked into a point instead.
    ///
    /// Runs on scaled time, matching the removal cadence it's driven from — the
    /// whole sequence sits inside GarbagePatchCutsceneController's hold, so all
    /// of it has to speed up and slow down together with the player's time
    /// controls.
    /// </summary>
    private IEnumerator SinkDebrisPiece(Transform piece)
    {
        sinkingPieces++;

        SpriteRenderer renderer = piece.GetComponent<SpriteRenderer>();
        Vector3 startScale = piece.localScale;
        Vector3 endScale = startScale * debrisSinkScale;
        Vector3 restPosition = piece.localPosition;
        Color startColor = renderer != null ? renderer.color : Color.white;

        float elapsed = 0f;
        float shakeTimer = 0f;
        Vector3 shakeOffset = Vector3.zero;

        while (elapsed < debrisSinkDuration && piece != null)
        {
            elapsed += Time.deltaTime;
            float t = debrisSinkCurve.Evaluate(Mathf.Clamp01(elapsed / debrisSinkDuration));

            piece.localScale = Vector3.LerpUnclamped(startScale, endScale, t);

            if (renderer != null)
            {
                Color color = startColor;
                // Clamped where the scale above is not: an overshooting curve is
                // fine on a size, but an alpha has nowhere to overshoot to.
                color.a = startColor.a * (1f - Mathf.Clamp01(t));
                renderer.color = color;
            }

            shakeTimer -= Time.deltaTime;
            if (shakeTimer <= 0f)
            {
                // Held between rolls rather than re-randomised every frame,
                // which is what makes this judder rather than blur.
                shakeTimer += debrisSinkShakeFrequency > 0f ? 1f / debrisSinkShakeFrequency : float.MaxValue;
                shakeOffset = UnityEngine.Random.insideUnitCircle * debrisSinkShake;
            }

            piece.localPosition = restPosition + shakeOffset;

            yield return null;
        }

        // Decremented before the Destroy, and reached by every exit path
        // including the piece being torn out from under this (a scene unload
        // mid-sink) — a count left stranded above zero would hang the final
        // hit's wait forever and the patch would never destroy itself.
        sinkingPieces--;

        if (piece != null) Destroy(piece.gameObject);
    }
}
