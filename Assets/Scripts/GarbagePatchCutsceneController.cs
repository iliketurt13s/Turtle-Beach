using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Runs once per storm-end, right after the normal helpful upgrade pick
/// completes: pans the camera out to the current island's GarbagePatch, plays
/// a hit effect and knocks off one health segment, then pans back. Two things
/// can then happen, independently of each other: the harmful upgrade pick
/// (UpgradeSelectionUI.ShowHazardChoice) fires on its own fixed Rounds Per
/// Hazard Pick cadence (see below), while the island transition
/// (IslandTransitionController) still only fires once the patch actually
/// depletes — which now varies per game mode (GarbagePatch's Rounds Per
/// Island By Mode, see GarbagePatchSpawner). Deliberately decoupled: Big
/// Island's patch is set to essentially never deplete, and Cove's depletes
/// half as often as Archipelago's — without splitting these, hazard picks
/// would silently follow whichever cadence the active game mode's patch
/// happens to use instead of staying consistent across all three.
///
/// IsActive suspends the same player-input systems that already stand down
/// for BuildModeController.IsActive/UpgradeSelectionUI.IsActive, and stays
/// true across the hazard pick + transition too, not just the camera pan.
/// </summary>
public class GarbagePatchCutsceneController : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    [SerializeField] private CameraController cameraController;
    [Tooltip("The same UpgradeSelectionUI instance already wired into DayStormCycle — not a separate panel.")]
    [SerializeField] private UpgradeSelectionUI upgradeSelectionUI;
    [SerializeField] private IslandTransitionController islandTransitionController;
    [SerializeField] private ParticleSystem hitParticlePrefab;
    [Tooltip("GameScene's screen-wipe panel — same instance GameOverUI uses. Left unassigned = the island transition just runs with no cover.")]
    [SerializeField] private SceneTransitionPanel transitionPanel;

    [Header("Timing")]
    [SerializeField] private float panOutDuration = 2f;
    [SerializeField] private float holdAtPatchDuration = 1.5f;
    [SerializeField] private float panBackDuration = 2f;

    [Header("Hazard Cadence")]
    [Tooltip("Storms survived between hazard upgrade picks — fires on this fixed schedule every game mode, entirely decoupled from GarbagePatch depleting/the island transition below (see class doc comment). Counts every storm survived continuously, never resetting on an island transition, so the cadence stays exactly every N rounds across the whole run regardless of how often islands change.")]
    [SerializeField] private int roundsPerHazardPick = 5;

    private int roundsSinceLastHazard;

    private void Awake()
    {
        // IsActive is static, so if the Editor's Domain Reload is disabled it
        // survives across stopping and re-entering Play mode — if Play mode
        // was ever stopped mid-cutscene, this would otherwise stay true
        // forever and silently block all player input from the next session's
        // first frame (see DayStormCycle's own IsStorming reset for the same
        // reasoning).
        IsActive = false;
    }

    public void RunPostStormSequence(Action onComplete)
    {
        StartCoroutine(RunPostStormSequenceRoutine(onComplete));
    }

    private IEnumerator RunPostStormSequenceRoutine(Action onComplete)
    {
        GarbagePatch patch = GarbagePatch.Instance;
        if (patch == null || cameraController == null)
        {
            Debug.LogWarning("GarbagePatchCutsceneController: no GarbagePatch/CameraController to run the cutscene against, skipping.");
            onComplete?.Invoke();
            yield break;
        }

        IsActive = true;

        Vector3 originalCameraPosition = cameraController.transform.position;

        // Pans toward the patch's LIVE position each frame (it's continuously
        // orbiting, see GarbagePatchOrbit) rather than a snapshot taken now,
        // then keeps tracking it exactly for the whole hold below — otherwise
        // the camera would arrive at (or hold on) wherever the patch used to
        // be and it'd have visibly drifted off-center by the time this ends.
        bool arrived = false;
        cameraController.PanToFollowing(patch.transform, panOutDuration, () => arrived = true);
        yield return new WaitUntil(() => arrived);
        cameraController.BeginFollowing(patch.transform);

        if (hitParticlePrefab != null) Instantiate(hitParticlePrefab, patch.transform.position, Quaternion.identity);
        patch.TakeHit();
        Debug.Log($"GarbagePatchCutsceneController: garbage patch hit, {patch.CurrentHealth}/{patch.MaxSegments} segments remaining.");

        yield return new WaitForSeconds(holdAtPatchDuration);

        cameraController.StopFollowing();

        bool returned = false;
        cameraController.PanTo(originalCameraPosition, panBackDuration, () => returned = true);
        yield return new WaitUntil(() => returned);

        bool patchDepleted = patch.CurrentHealth <= 0;

        roundsSinceLastHazard++;
        bool hazardDue = roundsSinceLastHazard >= roundsPerHazardPick;
        if (hazardDue) roundsSinceLastHazard = 0;

        if (!patchDepleted && !hazardDue)
        {
            Debug.Log("GarbagePatchCutsceneController: round survived, garbage patch still standing.");
            IsActive = false;
            onComplete?.Invoke();
            yield break;
        }

        if (hazardDue && upgradeSelectionUI != null)
        {
            Debug.Log("GarbagePatchCutsceneController: hazard cadence due — running hazard pick.");
            bool hazardPicked = false;
            upgradeSelectionUI.ShowHazardChoice(() => hazardPicked = true);
            yield return new WaitUntil(() => hazardPicked);
        }

        if (patchDepleted && islandTransitionController != null)
        {
            Debug.Log("GarbagePatchCutsceneController: garbage patch depleted — running island transition.");
            bool covered = true;
            if (transitionPanel != null)
            {
                covered = false;
                transitionPanel.PlayCoverThenInvoke(() => covered = true);
            }
            else
            {
                Debug.LogWarning("GarbagePatchCutsceneController: no Transition Panel assigned — island transition will run with no wipe.");
            }
            yield return new WaitUntil(() => covered);

            bool transitionComplete = false;
            islandTransitionController.RunTransition(() => transitionComplete = true);
            yield return new WaitUntil(() => transitionComplete);

            transitionPanel?.Reveal();
        }

        IsActive = false;
        onComplete?.Invoke();
    }
}
