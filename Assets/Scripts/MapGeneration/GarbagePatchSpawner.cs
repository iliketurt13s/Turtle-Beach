using UnityEngine;

/// <summary>
/// Spawns one GarbagePatch per island as soon as it generates, mirroring
/// StarterTurtleBedSpawner's IslandGenerated-subscription shape. Places it on
/// a circle guaranteed to sit outside the core map (and its shallow-water
/// ring) at every angle, so it always orbits in open water beyond anywhere
/// TrashSpawner can ever place trash — using the corner distance of the core
/// rectangle rather than its half-width, since a circle sized off half-width
/// alone still cuts inside a square map near the diagonals.
/// </summary>
public class GarbagePatchSpawner : MonoBehaviour
{
    [SerializeField] private IslandGenerator islandGenerator;
    [SerializeField] private GameObject garbagePatchPrefab;
    [Tooltip("Extra distance (tiles) beyond the core map's corner plus the shallow-water ring that the patch's orbit sits at.")]
    [SerializeField] private float orbitMarginBeyondCore = 15f;

    private const string GameModeIndexKey = "GameModeIndex";

    [Header("Game Mode Presets")]
    [Tooltip("Hit points (rounds survived) before the garbage patch depletes and the run moves to the next island, indexed by the game mode picked on the menu's options screen (0=Big Island, 1=Cove, 2=Archipelago) via PlayerPrefs \"GameModeIndex\" — see MainMenuController.StartGame. Big Island's huge value is effectively \"never\" rather than a true structural block — the debris pile just stays at full count, since each hit thins it by a proportion that rounds to nothing (see GarbagePatch.TargetPieceCount). No perf cost. Index 2 (Archipelago) intentionally matches GarbagePatch's own default Max Segments, so picking Archipelago changes nothing.")]
    [SerializeField] private int[] roundsPerIslandByMode = { 999999, 10, 5 };

    private GameObject spawnedPatch;
    private int resolvedGameModeIndex;

    private void Awake()
    {
        resolvedGameModeIndex = roundsPerIslandByMode != null && roundsPerIslandByMode.Length > 0
            ? Mathf.Clamp(PlayerPrefs.GetInt(GameModeIndexKey, 1), 0, roundsPerIslandByMode.Length - 1)
            : 0;
    }

    private void OnEnable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated += SpawnPatch;
    }

    private void OnDisable()
    {
        if (islandGenerator != null) islandGenerator.IslandGenerated -= SpawnPatch;
    }

    private void SpawnPatch()
    {
        if (garbagePatchPrefab == null || islandGenerator == null) return;

        if (spawnedPatch != null) Destroy(spawnedPatch);

        float halfW = islandGenerator.Width / 2f;
        float halfH = islandGenerator.Height / 2f;
        float coreCornerRadius = Mathf.Sqrt(halfW * halfW + halfH * halfH);
        float radius = coreCornerRadius + islandGenerator.ShallowWaterRadius + orbitMarginBeyondCore;

        spawnedPatch = Instantiate(garbagePatchPrefab, Vector3.zero, Quaternion.identity);

        GarbagePatch patch = spawnedPatch.GetComponent<GarbagePatch>();
        if (patch != null)
        {
            // Each island's patch is a fresh Instantiate of the prefab's own
            // baked-in default Max Segments — re-applied every spawn, unlike
            // DayStormCycle/IslandGenerator's one-shot Awake overrides above.
            if (roundsPerIslandByMode != null && roundsPerIslandByMode.Length > 0)
            {
                patch.SetMaxSegments(roundsPerIslandByMode[resolvedGameModeIndex]);
            }
            patch.SpawnDebris();
        }

        GarbagePatchOrbit orbit = spawnedPatch.GetComponent<GarbagePatchOrbit>();
        if (orbit != null) orbit.Initialize(radius, Random.Range(0f, 360f));
    }
}
