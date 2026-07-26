using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Lives in the gameplay scene. Reads the one-shot "TutorialEnabled" PlayerPrefs
/// flag set by MainMenuController and, if set, immediately clears it (so it
/// covers exactly one playthrough) and runs a fixed scripted sequence: select a
/// turtle, direct it to a rock and collect some, direct it to a tree and start
/// collecting wood, teach camera pan/zoom while that wood collection finishes,
/// then place a Turtle Bed — each of those steps gated by IsActive freezing
/// DayStormCycle's day clock (see DayStormCycle.Update) so no storm can interrupt
/// it. IsActive drops right after that, letting the day clock run for real, and
/// the sequence then waits for the first actual storm before teaching base
/// defense (send turtles at the incoming trash), then a final sign-off
/// message once a turtle actually engages. Step completion is detected by
/// polling already-public state on TurtleAgent/ResourceManager/TurtleBed/
/// DayStormCycle rather than adding events to those systems. All prompt wording
/// and step tuning are Inspector fields so the tutorial script can be rewritten
/// without touching this file.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    private const string TutorialEnabledKey = "TutorialEnabled";

    public static bool IsActive { get; private set; }

    /// <summary>True for the rest of this scene's lifetime once the tutorial sequence actually starts running this session (the one-shot PlayerPrefs flag was set) — unlike IsActive, this never flips back false once the sequence ends, so DayStormCycle can still tell afterward (once day 1's duration check runs) that the tutorial ran and shorten day 1 accordingly (see DayStormCycle's Tutorial Day One Reduction).</summary>
    public static bool DidRunThisSession { get; private set; }

    /// <summary>Non-null while the rock/wood collection steps are gating on one specific type — TurtleAgent.HandleHeadHit checks IsHarvestAllowed so the player can't stockpile the other resource in the meantime (the day clock is frozen the whole tutorial, so nothing else limits how long they could farm it).</summary>
    private static ResourceManager.ResourceType? restrictedHarvestType;

    public static bool IsHarvestAllowed(ResourceManager.ResourceType type) => restrictedHarvestType == null || restrictedHarvestType == type;

    [Header("UI References")]
    [Tooltip("Root object for the tutorial prompt box, toggled active/inactive as steps show and hide their message.")]
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField] private TMP_Text tutorialText;

    [Header("Step Text")]
    [SerializeField, TextArea] private string selectTurtleMessage = "Click a turtle to select it.";
    [SerializeField, TextArea] private string directToRockMessage = "Now direct your turtle to a rock to collect some stone.";
    [SerializeField, TextArea] private string directToTreeMessage = "Great! Now direct your turtle to a tree to collect some wood.";
    [SerializeField, TextArea] private string cameraControlsMessage = "While it collects, try panning the camera (click and drag) and zooming (scroll wheel) to look around the island.";
    [SerializeField, TextArea] private string continueHarvestingMessage = "Continue harvesting resources.";
    [SerializeField, TextArea] private string placeBedMessage = "Hold Shift to enter build mode, then place a Turtle Bed to hatch a new turtle.";
    [SerializeField, TextArea] private string goodLuckMessage = "That's everything — good luck out there!";
    [SerializeField, TextArea] private string sendTurtlesToPlasticMessage = "Night has fallen! Send your turtles toward the incoming plastic to protect the island.";
    [SerializeField, TextArea] private string onYourOwnMessage = "You're on your own now. Good luck.";

    [Header("Step Tuning")]
    [SerializeField] private int rocksToCollect = 5;
    [SerializeField] private int woodToCollect = 15;
    [Tooltip("How long normal play continues after the turtle is selected, before the next prompt appears.")]
    [SerializeField] private float briefPlayDuration = 4f;
    [Tooltip("How long the final message stays up before handing off to normal play.")]
    [SerializeField] private float finalMessageDuration = 4f;
    [Tooltip("World-unit camera movement (click-drag) required during the camera-controls step before panning counts as learned.")]
    [SerializeField] private float cameraPanDetectionDistance = 1f;
    [Tooltip("Orthographic-size change (scroll zoom) required during the camera-controls step before zooming counts as learned.")]
    [SerializeField] private float cameraZoomDetectionAmount = 0.5f;
    [Tooltip("The camera-controls step also completes on its own after this long, whether or not the player actually pans/zooms — wood keeps being collected the whole time this step is up, so this is tuned to land comfortably before Wood To Collect would be reached on its own, and well before the following continueHarvestingMessage step would otherwise show.")]
    [SerializeField] private float cameraControlsTimeout = 12f;
    [Tooltip("How long sendTurtlesToPlasticMessage stays up before onYourOwnMessage replaces it — a fixed delay rather than waiting for a turtle to actually engage, so the sign-off is guaranteed to show while the storm (night) is still ongoing instead of risking never firing at all if the player is slow to react.")]
    [SerializeField] private float sendTurtlesFollowUpDelay = 15f;

    private void Awake()
    {
        // IsActive is static, so force a clean state on domain reload the same
        // way DayStormCycle.IsStorming/UpgradeSelectionUI.IsActive do.
        IsActive = false;
        DidRunThisSession = false;
        restrictedHarvestType = null;
    }

    private void Start()
    {
        if (PlayerPrefs.GetInt(TutorialEnabledKey, 0) != 1)
        {
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
            return;
        }

        PlayerPrefs.SetInt(TutorialEnabledKey, 0);
        PlayerPrefs.Save();

        DidRunThisSession = true;
        StartCoroutine(RunTutorialSequence());
    }

    private IEnumerator RunTutorialSequence()
    {
        IsActive = true;

        // Captured up front, not right before the placeBedMessage step —
        // build mode is never actually blocked during the earlier steps
        // (only DayStormCycle's clock and harvesting are gated by IsActive),
        // so a player who builds a bed early (out of curiosity, while
        // otherwise following the rock/wood steps) would otherwise have that
        // early bed baked into a baseline captured later, requiring a second,
        // redundant one to satisfy the WaitUntil below.
        int startingBedCount = TurtleBed.AllBeds.Count;

        ShowMessage(selectTurtleMessage);
        yield return new WaitUntil(() => TurtleAgent.AllTurtles.Any(t => t.IsSelected));

        HideMessage();
        yield return new WaitForSeconds(briefPlayDuration);

        ShowMessage(directToRockMessage);
        restrictedHarvestType = ResourceManager.ResourceType.Rock;
        yield return new WaitUntil(() => AnyTurtleTargeting(ResourceManager.ResourceType.Rock));
        yield return new WaitUntil(() => ResourceManager.Instance.GetCount(ResourceManager.ResourceType.Rock) >= rocksToCollect);
        restrictedHarvestType = null;

        ShowMessage(directToTreeMessage);
        restrictedHarvestType = ResourceManager.ResourceType.Wood;
        yield return new WaitUntil(() => AnyTurtleTargeting(ResourceManager.ResourceType.Wood));

        yield return RunCameraControlsStep();

        ShowMessage(continueHarvestingMessage);
        yield return new WaitUntil(() => ResourceManager.Instance.GetCount(ResourceManager.ResourceType.Wood) >= woodToCollect);
        restrictedHarvestType = null;

        ShowMessage(placeBedMessage);
        yield return new WaitUntil(() => TurtleBed.AllBeds.Count > startingBedCount);

        ShowMessage(goodLuckMessage);
        yield return new WaitForSeconds(finalMessageDuration);

        HideMessage();
        IsActive = false;

        // IsActive is false from here on, so DayStormCycle's day clock (frozen
        // by every step above) now runs for real — wait for the first storm to
        // actually arrive before teaching base defense, rather than timing it
        // off a fixed delay.
        yield return new WaitUntil(() => DayStormCycle.IsStorming);

        ShowMessage(sendTurtlesToPlasticMessage);
        yield return new WaitForSeconds(sendTurtlesFollowUpDelay);

        ShowMessage(onYourOwnMessage);
        yield return new WaitForSeconds(finalMessageDuration);

        HideMessage();
    }

    /// <summary>Teaches camera pan/zoom while the wood-collection step's turtle is busy harvesting — waits for both a click-drag pan and a scroll zoom of at least the tuned thresholds, so the player has actually exercised each control, but also completes on its own after Camera Controls Timeout regardless, so a player who ignores the prompt entirely isn't stuck here indefinitely (see the field's own tooltip for how that timeout is tuned).</summary>
    private IEnumerator RunCameraControlsStep()
    {
        ShowMessage(cameraControlsMessage);

        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 startPosition = cam.transform.position;
        float startZoom = cam.orthographicSize;
        bool hasPanned = false;
        bool hasZoomed = false;
        float elapsed = 0f;

        while ((!hasPanned || !hasZoomed) && elapsed < cameraControlsTimeout)
        {
            if (!hasPanned && Vector3.Distance(cam.transform.position, startPosition) >= cameraPanDetectionDistance) hasPanned = true;
            if (!hasZoomed && Mathf.Abs(cam.orthographicSize - startZoom) >= cameraZoomDetectionAmount) hasZoomed = true;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private static bool AnyTurtleTargeting(ResourceManager.ResourceType type)
    {
        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            Transform target = turtle.CurrentTaskTarget;
            if (target == null) continue;

            ResourceNode node = target.GetComponent<ResourceNode>();
            if (node != null && node.ResourceType == type) return true;
        }

        return false;
    }

    private void ShowMessage(string message)
    {
        if (tutorialPanel != null) tutorialPanel.SetActive(true);
        if (tutorialText != null) tutorialText.text = message;
    }

    private void HideMessage()
    {
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
    }
}
