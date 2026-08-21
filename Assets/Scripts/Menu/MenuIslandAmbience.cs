using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decorates the Menu scene's static island backdrop with a living little
/// colony: nature props scattered into authored zones, a nest in the middle, and
/// turtles wandering aimlessly around them. Purely cosmetic — nothing here feeds
/// ResourceManager, ScoreManager, or any other gameplay system, and none of the
/// gameplay singletons (PathfindingManager, UpgradeManager, DayStormCycle) exist
/// in this scene at all.
///
/// The turtles have no job. They pick a random point in the Roam Area, amble
/// over, pause, and pick another — they never harvest the props or visit the
/// nest, which are both there purely to be wandered among.
///
/// That last point is the whole reason this class exists instead of just
/// dropping the gameplay spawners into the menu: IslandPropSpawner needs
/// IslandGenerator's sand tilemap to know where land is, and TurtleAgent reads
/// PathfindingManager/TurtleNest/UpgradeManager constantly and would throw the
/// moment it tried to path anywhere. Here, "where the land is" is instead
/// authored by hand as Prop Zones (rectangles drawn as Scene-view gizmos, sized
/// against the backdrop sprite rather than against real tiles), and turtle
/// behavior is the deliberately tiny MenuAmbienceTurtle state machine, which
/// drives the prefab's own TurtleTargetSteering/TurtleLocomotion — the two
/// components that are genuinely self-contained physics — and nothing else.
///
/// Everything is built under one runtime root that stays INACTIVE while it's
/// assembled, so TurtleAgent can be stripped off each spawned turtle before its
/// Awake ever runs (Awake fires on activation, not on Instantiate, for an object
/// created under an inactive parent). BuildAmbience waits a frame before
/// activating that root because Destroy is deferred to the end of the current
/// frame — activating in the same frame would let the not-yet-destroyed
/// TurtleAgent wake up after all. This matters beyond tidiness: DayStormCycle
/// .IsStorming is a static that only ever resets in DayStormCycle.Awake, which
/// this scene has none of, so a player who returns to the menu mid-storm leaves
/// it latched true — a live TurtleAgent here would take its storm branch and
/// dereference the PathfindingManager that doesn't exist.
///
/// Prop prefabs keep all their own components (ResourceNode, ResourceClickTarget,
/// colliders) rather than being stripped down to sprites: every one of them is
/// already inert without the gameplay systems that drive it, and the colliders
/// are worth keeping so wandering turtles physically bump around the scenery.
/// </summary>
public class MenuIslandAmbience : MonoBehaviour
{
    /// <summary>One authored rectangle of ground, plus what scatters into it. Two of these — trees on one side of the island, rocks on the other — is the intended setup, but there's no limit; the center of the backdrop is deliberately left to the menu UI.</summary>
    [Serializable]
    public class PropZone
    {
        [Tooltip("Label only — names this entry in the Inspector and its gizmo in the Scene view. Never read at runtime.")]
        public string label = "Trees (left)";
        [Tooltip("Prop prefabs picked from at random, once per spawn (e.g. Assets/Prefabs/Nature/PalmTree). Anything with a sprite works — these are pure scenery, so a prefab's ResourceNode (if it has one) is never used for anything here.")]
        public GameObject[] prefabs;
        [Tooltip("Lowest number of props this zone spawns. The actual count is rolled between this and Max Count on every menu load, so the layout differs run to run.")]
        [Min(0)] public int minCount = 4;
        [Tooltip("Highest number of props this zone spawns.")]
        [Min(0)] public int maxCount = 7;
        [Tooltip("Center of the rectangle props scatter inside, relative to this object's own position. The menu camera (orthographic size 27) shows roughly x -48..48, y -27..27, and the backdrop's sand covers about x -43..43, y -26..26.")]
        public Vector2 areaCenter = new Vector2(-28f, 0f);
        [Tooltip("Width and height, in world units, of that rectangle. Drawn in the Scene view so it can be sized against the backdrop by eye.")]
        public Vector2 areaSize = new Vector2(20f, 34f);
        [Tooltip("Minimum world-unit gap kept from every prop already placed — including ones from other zones — so trees and rocks never pile up on each other. Placement retries a bounded number of times, so an impossibly large value here just means fewer props spawn, never a hang.")]
        [Min(0f)] public float minSpacing = 5f;
        [Tooltip("Randomly mirror each prop horizontally, the same variety trick IslandPropSpawner uses in the gameplay scene.")]
        public bool randomizeFlip = true;
        [Tooltip("Random uniform scale multiplier rolled per prop, as (min, max). Leave at (1, 1) to use each prefab's own authored scale untouched.")]
        public Vector2 scaleRange = new Vector2(1f, 1f);
        [Tooltip("Color this zone's rectangle is drawn in, in the Scene view.")]
        public Color gizmoColor = new Color(0.45f, 1f, 0.55f, 1f);
    }

    /// <summary>Every knob MenuAmbienceTurtle runs on. It lives here rather than on that component because ambience turtles get it added at runtime — a serialized field over there would never show up in an Inspector for anyone to tune.</summary>
    [Serializable]
    public class TurtleTuning
    {
        [Header("Movement")]
        [Tooltip("Stroke strength while ambling (TurtleLocomotion.SetSpeedMultiplier). 1 = the same cruising speed turtles use in the gameplay scene; below that reads as a calmer, more aimless potter, which suits a title screen.")]
        public float wanderSpeedMultiplier = 0.6f;
        [Tooltip("How close (world units) a turtle must get to its wander point to count as arrived. Movement is physics-driven and coasts, so this can't be tiny — keep it comfortably above zero or turtles will circle their destination forever.")]
        public float arriveRadius = 2.2f;
        [Tooltip("Shortest distance a newly-rolled wander point may be from where the turtle currently stands, so it always has somewhere to actually walk rather than arriving instantly and re-rolling on the spot.")]
        public float minWanderDistance = 8f;
        [Tooltip("Safety valve: seconds a turtle may spend heading for one point before giving up and picking another. Stops a turtle wedged against a prop's collider from stalling forever.")]
        public float legTimeoutSeconds = 25f;

        [Header("Pauses")]
        [Tooltip("Chance (0-1) a turtle stops and looks around on reaching its wander point, rather than immediately setting off for the next one. Pausing every time reads as mechanical; never pausing reads as a patrol.")]
        [Range(0f, 1f)] public float pauseChance = 0.6f;
        [Tooltip("Seconds a paused turtle stays put, rolled between these two values.")]
        public Vector2 pauseSecondsRange = new Vector2(0.75f, 3f);
    }

    [Header("Nest")]
    [Tooltip("Nest prefab dropped in the middle of the island — Assets/Prefabs/Turtle/TurtleNest. Safe to use as-is: TurtleNest only does anything while a storm is running with trash near the island, neither of which can happen here. Purely decorative — turtles pay it no attention. Leave empty to skip it.")]
    [SerializeField] private GameObject nestPrefab;
    [Tooltip("Where the nest sits, relative to this object.")]
    [SerializeField] private Vector2 nestPosition = Vector2.zero;

    [Header("Resource Props")]
    [Tooltip("Rectangles of ground and what scatters into each. Add one entry per side of the island — the intended setup is trees on the left, rocks on the right, with the middle left clear for the menu UI.")]
    [SerializeField]
    private PropZone[] propZones = BuildDefaultPropZones();

    [Header("Turtles")]
    [Tooltip("Turtle prefab — Assets/Prefabs/Turtle/Turtle. Its TurtleAgent is stripped off each spawned copy before it can wake up (see class doc comment); everything else about the prefab, fins and squash included, is kept and driven by MenuAmbienceTurtle instead.")]
    [SerializeField] private GameObject turtlePrefab;
    [Tooltip("How many turtles wander the island.")]
    [Min(0)] [SerializeField] private int turtleCount = 2;

    [Header("Roam Area")]
    [Tooltip("Center of the rectangle turtles wander inside, relative to this object. They start scattered across it too. Drawn in the Scene view — size it to the backdrop's sand so nobody strolls out to sea.")]
    [SerializeField] private Vector2 roamAreaCenter = Vector2.zero;
    [Tooltip("Width and height, in world units, of that rectangle. The menu camera (orthographic size 27) shows roughly x -48..48, y -27..27, and the backdrop's sand covers about x -43..43, y -26..26.")]
    [SerializeField] private Vector2 roamAreaSize = new Vector2(76f, 44f);
    [Tooltip("Color the roam rectangle is drawn in, in the Scene view.")]
    [SerializeField] private Color roamGizmoColor = new Color(1f, 0.6f, 0.3f, 1f);

    [Header("Scale")]
    [Tooltip("Uniform scale applied to every spawned turtle. The menu backdrop draws the whole island at roughly 96x54 world units, far larger than the gameplay scene's ~1-unit tiles, so a turtle at its authored prefab scale reads as a speck here. Scale it up HERE rather than by editing Turtle.prefab (which would resize turtles in the gameplay scene too) or by scaling this GameObject (which multiplies every Prop Zone's offsets right along with it, and flings the zones off camera).")]
    [SerializeField] private float turtleScale = 1f;
    [Tooltip("Uniform scale applied to the spawned nest, for the same reason as Turtle Scale above.")]
    [SerializeField] private float nestScale = 1f;

    [Header("Turtle Behaviour")]
    [Tooltip("Speeds, distances and timings every ambience turtle runs on. Note these are all in world units/seconds and are NOT affected by the scale fields above — a bigger turtle covering the same island still wants the same arrive radius.")]
    [SerializeField] private TurtleTuning turtleTuning = new TurtleTuning();

    [Header("Seed")]
    [Tooltip("Seed for the prop scatter. Logged to the Console on every build so a layout you like can be reproduced.")]
    [SerializeField] private int seed;
    [Tooltip("Check this, and set Seed above to a value from the Console, to pin the prop layout instead of rolling a fresh one each time the menu opens.")]
    [SerializeField] private bool useFixedSeed;

    [Header("Gizmos")]
    [Tooltip("Draw the prop zone rectangles, the roam area and the nest marker in the Scene view. Editor-only — never affects the built game.")]
    [SerializeField] private bool drawGizmos = true;

    /// <summary>Every prop's world position, kept flat so the min-spacing check stays a simple distance sweep.</summary>
    private readonly List<Vector3> propPositions = new List<Vector3>();

    private Transform root;
    private Coroutine buildRoutine;

    public TurtleTuning Tuning => turtleTuning;

    /// <summary>A uniformly random world point inside the roam rectangle. Called by MenuAmbienceTurtle every time one picks somewhere new to potter off to.</summary>
    public Vector3 RandomRoamPoint()
    {
        Vector2 local = roamAreaCenter + new Vector2(
            (UnityEngine.Random.value - 0.5f) * roamAreaSize.x,
            (UnityEngine.Random.value - 0.5f) * roamAreaSize.y);

        return transform.TransformPoint(local);
    }

    /// <summary>The one place the intended zone layout is written down, so the field initializer above and the Inspector's own reset paths can't drift apart. Zones added by hand with the Inspector's + button bypass all of this and arrive zeroed — see IsZoneUnconfigured.</summary>
    private static PropZone[] BuildDefaultPropZones()
    {
        return new[]
        {
            new PropZone
            {
                label = "Trees (left)",
                areaCenter = new Vector2(-28f, 0f),
                gizmoColor = new Color(0.45f, 1f, 0.55f, 1f),
            },
            new PropZone
            {
                label = "Rocks (right)",
                areaCenter = new Vector2(28f, 0f),
                gizmoColor = new Color(0.8f, 0.8f, 0.9f, 1f),
            },
        };
    }

#if UNITY_EDITOR
    /// <summary>Restores both zones to the counts, sizes and spacing this script was written around, carrying whatever prefabs are already assigned across so the nature prefabs don't have to be dragged in again. The escape hatch for a zone that came out of the Inspector's + button zeroed.</summary>
    [ContextMenu("Reset Prop Zones To Defaults")]
    private void ResetPropZonesToDefaults()
    {
        UnityEditor.Undo.RecordObject(this, "Reset Prop Zones");

        PropZone[] defaults = BuildDefaultPropZones();
        for (int i = 0; i < defaults.Length; i++)
        {
            if (propZones != null && i < propZones.Length && propZones[i] != null)
            {
                defaults[i].prefabs = propZones[i].prefabs;
            }
        }

        propZones = defaults;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"MenuIslandAmbience ({name}): prop zones reset to defaults (4-7 props over a 20 x 34 area at x -28 and x +28), keeping whatever prefabs were already assigned.", this);
    }
#endif

    private void Start() => Rebuild();

    /// <summary>Tears down whatever's there and builds the whole scene dressing again. Exposed as a context-menu item so a prop layout can be re-rolled from the Inspector during Play mode without leaving the menu.</summary>
    [ContextMenu("Rebuild Ambience")]
    public void Rebuild()
    {
        if (!Application.isPlaying) return;

        if (buildRoutine != null) StopCoroutine(buildRoutine);
        buildRoutine = StartCoroutine(BuildAmbience());
    }

    private IEnumerator BuildAmbience()
    {
        if (root != null) Destroy(root.gameObject);

        propPositions.Clear();

        // Everything is assembled inside this root while it's inactive, so
        // nothing spawned Awakes until the stripping below has already
        // happened. See the class doc comment for why that matters.
        root = new GameObject("Menu Ambience (runtime)").transform;
        root.SetParent(transform, false);
        root.gameObject.SetActive(false);

        if (!useFixedSeed) seed = Environment.TickCount;
        System.Random rng = new System.Random(seed);

        WarnIfRigIsTransformed();

        SpawnNest();
        SpawnProps(rng);
        List<MenuAmbienceTurtle> turtles = SpawnTurtles();

        Debug.Log($"MenuIslandAmbience ({name}): seed = {seed} (check 'Use Fixed Seed' and set this value to reproduce this layout) — {propPositions.Count} prop(s), {turtles.Count} turtle(s). Seeing this line twice per menu load means there are two of these components in the scene.", this);

        // Destroy() only takes effect at the end of the current frame, so
        // activating the root right here would still wake the TurtleAgents
        // SpawnTurtles just asked to be destroyed. One frame of an invisible
        // menu backdrop is imperceptible; a TurtleAgent looking for a
        // PathfindingManager that doesn't exist is not.
        yield return null;

        if (root == null) yield break;

        root.gameObject.SetActive(true);

        foreach (MenuAmbienceTurtle turtle in turtles)
        {
            if (turtle != null) turtle.Begin(this);
        }

        buildRoutine = null;
    }

    /// <summary>Every authored coordinate on this component is fed through transform.TransformPoint, so a scaled or rotated rig silently multiplies each zone's offset instead of just resizing what's in it — scale this up to make turtles bigger and the zones sail off camera while the nest, sitting at the origin, stays put and looks fine. Turtle Scale/Nest Scale exist precisely so nobody has to reach for the rig's own Transform to do that.</summary>
    private void WarnIfRigIsTransformed()
    {
        Vector3 scale = transform.lossyScale;
        if (!Mathf.Approximately(scale.x, 1f) || !Mathf.Approximately(scale.y, 1f))
        {
            Debug.LogWarning($"MenuIslandAmbience ({name}): this object's scale is {scale}, which multiplies every Prop Zone's Area Center and Area Size — zones will land {scale.x:0.##}x further out than the numbers in the Inspector say, quite possibly off camera. Set the scale back to 1 and use Turtle Scale / Nest Scale / each zone's Scale Range to resize things instead.", this);
        }

        if (transform.localRotation != Quaternion.identity)
        {
            Debug.LogWarning($"MenuIslandAmbience ({name}): this object is rotated, which rotates every Prop Zone's position with it. Zero the rotation unless that's deliberate.", this);
        }
    }

    private void SpawnNest()
    {
        if (nestPrefab == null) return;

        GameObject instance = Instantiate(nestPrefab, transform.TransformPoint(nestPosition), Quaternion.identity, root);
        instance.transform.localScale *= nestScale;
    }

    private void SpawnProps(System.Random rng)
    {
        if (propZones == null || propZones.Length == 0)
        {
            Debug.LogWarning($"MenuIslandAmbience ({name}): no Prop Zones configured — no trees or rocks will appear. Add at least one zone and put a nature prefab in its Prefabs list.", this);
            return;
        }

        foreach (PropZone zone in propZones)
        {
            if (zone == null) continue;

            if (!HasAnyPrefab(zone))
            {
                // The common wiring slip: the Prefabs list exists but its
                // entries are still None, which would otherwise fail silently
                // (every random draw picks a null and gets skipped).
                Debug.LogWarning($"MenuIslandAmbience ({name}): prop zone '{zone.label}' has no prefab assigned, so nothing spawns there. Drag a nature prefab (e.g. Assets/Prefabs/Nature/PalmTree) into its Prefabs list.", this);
                continue;
            }

            if (IsZoneUnconfigured(zone)) continue;

            int low = Mathf.Min(zone.minCount, zone.maxCount);
            int high = Mathf.Max(zone.minCount, zone.maxCount);
            int target = rng.Next(low, high + 1);
            if (target <= 0) continue;

            // Same bounded-retry shape as IslandPropSpawner.SpawnCategory: with
            // a spacing rule, valid points get scarcer as the zone fills, so the
            // attempt cap (not the target count) is what actually ends the loop
            // once a zone is as packed as its spacing allows.
            int attempts = 0;
            int maxAttempts = target * 40;
            int spawned = 0;

            while (spawned < target && attempts < maxAttempts)
            {
                attempts++;

                Vector2 local = zone.areaCenter + new Vector2(
                    ((float)rng.NextDouble() - 0.5f) * zone.areaSize.x,
                    ((float)rng.NextDouble() - 0.5f) * zone.areaSize.y);
                Vector3 world = transform.TransformPoint(local);

                if (IsTooCloseToExistingProp(world, zone.minSpacing)) continue;

                GameObject prefab = zone.prefabs[rng.Next(zone.prefabs.Length)];
                if (prefab == null) continue;

                GameObject instance = Instantiate(prefab, world, Quaternion.identity, root);
                ApplyPropVariation(instance, zone, rng);

                propPositions.Add(world);
                spawned++;
            }

            if (spawned < target)
            {
                Debug.LogWarning($"MenuIslandAmbience ({name}): prop zone '{zone.label}' only fit {spawned} of {target} props. Min Spacing ({zone.minSpacing}) is large relative to Area Size ({zone.areaSize.x} x {zone.areaSize.y}) — lower the spacing or grow the area.", this);
            }

            if (spawned > 0)
            {
                Vector3 first = propPositions[propPositions.Count - spawned];
                Debug.Log($"MenuIslandAmbience ({name}): prop zone '{zone.label}' spawned {spawned}, centered on world {transform.TransformPoint(zone.areaCenter)} (first one at {first}). If nothing is visible, that world position is off camera — check this object's own Transform position AND scale, since both offset and multiply every zone.", this);
            }
        }
    }

    /// <summary>
    /// Catches a zone that can't possibly produce anything, and says which
    /// field is at fault. Worth a dedicated check because of how easily it
    /// happens: adding an element to a [Serializable] class array with the
    /// Inspector's + button zero-initializes the new element instead of running
    /// this class's own C# field initializers, so a hand-added zone arrives with
    /// Max Count 0 and Area Size (0,0) rather than the 4-7 props over 20x34
    /// written above. Max Count 0 then means the zone is skipped entirely, with
    /// nothing scattered and — before this check — nothing said about it.
    /// Deliberately reports rather than repairs: 0 is a legitimate way to mute a
    /// zone, and quietly substituting numbers the user never typed would be a
    /// worse surprise than the one it's fixing.
    /// </summary>
    private bool IsZoneUnconfigured(PropZone zone)
    {
        if (zone.maxCount <= 0)
        {
            Debug.LogWarning($"MenuIslandAmbience ({name}): prop zone '{zone.label}' has Max Count 0, so it spawns nothing. Set Min Count / Max Count to a real range (the intended default is 4 to 7). Zones added with the Inspector's + button start fully zeroed rather than at this script's defaults, so also check Area Size — it wants something like 20 x 34, and at 0 x 0 every prop would stack on one point.", this);
            return true;
        }

        if (Mathf.Approximately(zone.areaSize.x, 0f) && Mathf.Approximately(zone.areaSize.y, 0f))
        {
            Debug.LogWarning($"MenuIslandAmbience ({name}): prop zone '{zone.label}' has Area Size (0, 0), so every prop would land on the exact same point (and Min Spacing would then reject all but the first). Give it a real size — the intended default is 20 x 34.", this);
            return true;
        }

        return false;
    }

    private static bool HasAnyPrefab(PropZone zone)
    {
        if (zone.prefabs == null) return false;

        foreach (GameObject prefab in zone.prefabs)
        {
            if (prefab != null) return true;
        }

        return false;
    }

    private static void ApplyPropVariation(GameObject instance, PropZone zone, System.Random rng)
    {
        Vector3 scale = instance.transform.localScale;

        float low = Mathf.Min(zone.scaleRange.x, zone.scaleRange.y);
        float high = Mathf.Max(zone.scaleRange.x, zone.scaleRange.y);
        if (high > 0f) scale *= low + (float)rng.NextDouble() * (high - low);

        if (zone.randomizeFlip && rng.Next(2) == 0) scale.x *= -1f;

        instance.transform.localScale = scale;
    }

    private bool IsTooCloseToExistingProp(Vector3 world, float minSpacing)
    {
        if (minSpacing <= 0f) return false;

        float sqrSpacing = minSpacing * minSpacing;
        foreach (Vector3 existing in propPositions)
        {
            if ((existing - world).sqrMagnitude < sqrSpacing) return true;
        }

        return false;
    }

    /// <summary>Instantiates the turtles scattered across the roam area, strips TurtleAgent off each (see class doc comment), and attaches MenuAmbienceTurtle in its place. Returns them un-started — BuildAmbience calls Begin only after the root is live, so each turtle's own components have Awoken and cached themselves first.</summary>
    private List<MenuAmbienceTurtle> SpawnTurtles()
    {
        List<MenuAmbienceTurtle> spawned = new List<MenuAmbienceTurtle>();
        if (turtlePrefab == null) return spawned;

        for (int i = 0; i < turtleCount; i++)
        {
            GameObject instance = Instantiate(turtlePrefab, RandomRoamPoint(), Quaternion.identity, root);
            instance.transform.localScale *= turtleScale;

            TurtleAgent agent = instance.GetComponent<TurtleAgent>();
            if (agent != null)
            {
                // Barnacles (and any future upgrade-driven overlay) are run
                // state this scene has no concept of, so a menu turtle must
                // never wear them however the prefab happens to be authored.
                // Cleared explicitly rather than trusting that TurtleAgent's
                // own Awake ran during the Instantiate above and got there
                // first — that's true today, but it's the kind of ordering
                // assumption that breaks quietly.
                agent.HideUpgradeVisuals();
                Destroy(agent);
            }

            spawned.Add(instance.AddComponent<MenuAmbienceTurtle>());
        }

        return spawned;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        if (propZones != null)
        {
            foreach (PropZone zone in propZones)
            {
                if (zone == null) continue;

                Gizmos.color = zone.gizmoColor;
                Gizmos.DrawWireCube(zone.areaCenter, new Vector3(zone.areaSize.x, zone.areaSize.y, 0f));
            }
        }

        Gizmos.color = roamGizmoColor;
        Gizmos.DrawWireCube(roamAreaCenter, new Vector3(roamAreaSize.x, roamAreaSize.y, 0f));

        Gizmos.color = new Color(1f, 0.85f, 0.35f, 1f);
        Gizmos.DrawWireSphere(nestPosition, 1.5f);
    }
}
