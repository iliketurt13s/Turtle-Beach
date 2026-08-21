using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Runs once per storm-end, right after the normal helpful upgrade pick
/// completes: pans the camera out to the current island's GarbagePatch, knocks
/// off one health segment, then pans back. The hit's own visuals belong to the
/// patch itself (see GarbagePatch — a squash across the whole pile, then the
/// knocked-off debris sinking away piece by piece), not to this controller,
/// which used to also fire a burst of particle explosions here: that read as
/// the patch detonating rather than being steadily worn down. Once the
/// patch actually depletes — after however many rounds GarbagePatchSpawner's
/// Rounds Per Island By Mode gave it — the run ends as a win (see
/// GameOverUI.ShowWin) rather than moving to a new island. onComplete is
/// deliberately never invoked in that case, so DayStormCycle stays parked
/// with awaitingUpgradeChoice still true and never spawns another round.
///
/// The shot is composed, not fixed: the camera zooms to frame the patch at
/// whatever size it currently is (GarbagePatch.MeasureVisualRadius, times
/// Framing Padding) and zooms back to the player's own zoom on the way home.
/// That matters because the pile is eaten away from the outside in over a run,
/// so a single authored zoom would either frame the first hit and lose the last
/// or vice versa.
///
/// Finishing the pan home is also what ends the weather
/// (AmbienceController.EndStormWeather). DayStormCycle deliberately keeps
/// IsStorming true past this whole sequence, so the rain has to be told
/// directly rather than left to poll — and this controller is the only thing
/// that knows the animation is over. On the winning run it is the only thing
/// that ever could: the branch below never hands control back, so nothing else
/// would ever clear the storm at all.
///
/// This used to also fire a forced harmful ("hazard") upgrade pick on its own
/// fixed cadence every N storms. That's gone: harmful effects are now opt-in
/// run modifiers picked on the menu before the run starts, in exchange for a
/// score multiplier (see GameModifierManager). All this controller does now is
/// the patch hit and the win check.
///
/// IsActive suspends the same player-input systems that already stand down
/// for BuildModeController.IsActive/UpgradeSelectionUI.IsActive, and stays
/// true across the win screen too, not just the camera pan — deliberately
/// never reset back to false once the patch depletes, since the run is over
/// and nothing should hand input back.
/// </summary>
public class GarbagePatchCutsceneController : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    [SerializeField] private CameraController cameraController;
    [Tooltip("Shown once the garbage patch is fully depleted — the run ends as a win rather than moving to a new island.")]
    [SerializeField] private GameOverUI gameOverUI;

    [Header("Timing")]
    [SerializeField] private float panOutDuration = 2f;
    [SerializeField] private float holdAtPatchDuration = 1.5f;
    [SerializeField] private float panBackDuration = 2f;

    [Header("Framing")]
    [Tooltip("How much of the view the patch fills once the camera arrives. 1 puts the edge of the pile exactly on the nearest screen edge; higher leaves ocean around it. Applied to the patch's LIVE size, so the shot stays composed the same way as the pile is eaten away over the run instead of the patch shrinking into a speck.")]
    [SerializeField, Min(1f)] private float framingPadding = 1.6f;
    [Tooltip("Closest this cutscene will zoom in, whatever the framing math asks for. The last segment or two of a patch is a very small thing to fill a screen with, and without a floor the camera dives right onto it. Also stops a patch measured at nearly nothing (debris mid-sink) from slamming the camera to its Min Zoom.")]
    [SerializeField, Min(0.1f)] private float minFramedSize = 4f;

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

        // Captured, not assumed: the player was free to zoom wherever they
        // liked right up until this started, and that is what the camera owes
        // them back at the end.
        float originalOrthographicSize = cameraController.OrthographicSize;

        // Measured before TakeHit, so the shot is composed around the pile the
        // player is about to watch get hit rather than what is left afterwards
        // — and measured fresh every storm, which is the whole point: the pile
        // is eaten away from the outside in as the run goes on (see
        // GarbagePatch.MeasureVisualRadius), so a fixed zoom would frame the
        // first hit and lose the last.
        float framedSize = Mathf.Max(
            minFramedSize,
            cameraController.FramingSizeForRadius(patch.MeasureVisualRadius() * framingPadding));

        // Pans toward the patch's LIVE position each frame (it's continuously
        // orbiting, see GarbagePatchOrbit) rather than a snapshot taken now,
        // then keeps tracking it exactly for the whole hold below — otherwise
        // the camera would arrive at (or hold on) wherever the patch used to
        // be and it'd have visibly drifted off-center by the time this ends.
        bool arrived = false;
        cameraController.PanToFollowing(patch.transform, panOutDuration, () => arrived = true);

        // Alongside the pan rather than after it, on the same duration and the
        // same easing, so the camera arrives already framed instead of settling
        // and then adjusting.
        cameraController.ZoomTo(framedSize, panOutDuration);
        yield return new WaitUntil(() => arrived);
        cameraController.BeginFollowing(patch.transform);

        patch.TakeHit();
        Debug.Log($"GarbagePatchCutsceneController: garbage patch hit, {patch.CurrentHealth}/{patch.MaxSegments} segments remaining.");

        yield return new WaitForSeconds(holdAtPatchDuration);

        cameraController.StopFollowing();

        bool returned = false;
        cameraController.PanTo(originalCameraPosition, panBackDuration, () => returned = true);
        cameraController.ZoomTo(originalOrthographicSize, panBackDuration);
        yield return new WaitUntil(() => returned);

        // The storm is over as far as the player is concerned the moment this
        // animation lands, so the rain goes with it. DayStormCycle keeps
        // IsStorming true past this point (it gates daytime turtle behavior,
        // and the day hasn't begun yet), which is why this is a direct call
        // rather than something AmbienceController could poll — and why it also
        // has to happen BEFORE the win branch below, which never hands control
        // back to DayStormCycle at all.
        AmbienceController.Instance?.EndStormWeather();

        if (patch.CurrentHealth > 0)
        {
            Debug.Log("GarbagePatchCutsceneController: round survived, garbage patch still standing.");
            IsActive = false;
            onComplete?.Invoke();
            yield break;
        }

        Debug.Log("GarbagePatchCutsceneController: garbage patch depleted — player wins.");
        if (gameOverUI != null)
        {
            gameOverUI.ShowWin();
        }
        else
        {
            Debug.LogWarning("GarbagePatchCutsceneController: no Game Over UI assigned — can't show the win screen.");
        }

        // Deliberately never invoked: the run has ended, so DayStormCycle must
        // never resume BeginDay()/spawn another round. IsActive also
        // deliberately stays true (see class doc comment) rather than
        // resetting to false.
    }
}
