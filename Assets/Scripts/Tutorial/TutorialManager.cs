using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Lives in the gameplay scene. Reads the one-shot "TutorialEnabled" PlayerPrefs
/// flag set by MainMenuController and, if set, immediately clears it (so it
/// covers exactly one playthrough) and walks the player through a fixed scripted
/// sequence: select a turtle, collect wood, collect stone, open the build drawer,
/// place a Turtle Bed, then stock up until nightfall — and once the first real
/// storm lands, send turtles at the incoming plastic and pick an upgrade. The
/// steps up to and including the bed are gated by IsActive freezing
/// DayStormCycle's day clock (see DayStormCycle.Update) so no storm can
/// interrupt them. IsActive drops the moment the bed is placed, which is what
/// starts the clock the stock-up step then waits on.
///
/// Nothing here draws anything. Each step is one line spoken by TutorialMentor
/// (a turtle that slides into the bottom-left corner, wobbles while it talks and
/// types into a speech bubble) plus one TutorialArrow aimed at whatever the
/// player has to touch to finish the step. Both are dismissed between steps, so
/// the screen belongs to the player while they actually carry the instruction
/// out. See Step, which is the whole shape of this file.
///
/// Arrow targets are passed as PROVIDERS, not fixed transforms — "the nearest
/// harvestable tree", not "that tree" — so the arrow re-picks by itself when a
/// node depletes, a piece of trash is destroyed, or a build-drawer row is
/// instantiated a moment after the drawer opens. See TutorialArrow.
///
/// The two harvest targets are silently coupled to one number elsewhere: they
/// have to cover the Turtle Bed's price, or the player finishes the stone step
/// unable to afford the bed the very next step demands, with the drawer showing
/// a cost they can't meet and no way forward. Wood To Collect and Stone To
/// Collect are currently set to exactly the bed's authored cost (15 Wood,
/// 5 Rock — see Assets/Prefabs/Buildings/TurtleBed.prefab), which funds it with
/// nothing to spare. That's fine today because the bed is the only unlocked
/// buildable and nothing else spends Wood or Rock during the tutorial, but if
/// the bed's price ever changes, these must move with it.
///
/// Step completion is detected by polling already-public state on TurtleAgent/
/// ResourceManager/TurtleBed/BuildModeController/UpgradeSelectionUI/
/// DayStormCycle rather than adding events to those systems; keep that direction
/// if you extend it. All prompt wording and step tuning are Inspector fields so
/// the script can be rewritten without touching this file.
///
/// Finishing the daytime portion permanently sets
/// PlayerProgression.HasCompletedTutorial, which is what unlocks the menu's
/// modifier grid. That's a different flag from the "TutorialEnabled" key read
/// below — that one is a one-shot "run me next time" request, cleared the
/// instant the sequence starts, and so it can't answer "has this player ever
/// actually been taught the game".
/// </summary>
public class TutorialManager : MonoBehaviour
{
    private const string TutorialEnabledKey = "TutorialEnabled";

    public static bool IsActive { get; private set; }

    /// <summary>True for the rest of this scene's lifetime once the tutorial sequence actually starts running this session (the one-shot PlayerPrefs flag was set) — unlike IsActive, this never flips back false once the sequence ends, so DayStormCycle can still tell afterward (once day 1's duration check runs) that the tutorial ran and shorten day 1 accordingly (see DayStormCycle's Tutorial Day One Reduction).</summary>
    public static bool DidRunThisSession { get; private set; }

    /// <summary>Non-null while the wood/stone collection steps are gating on one specific type — TurtleAgent.HandleHeadHit checks IsHarvestAllowed so the player can't stockpile the other resource in the meantime (the day clock is frozen the whole time, so nothing else limits how long they could farm it). This is also what keeps the arrow honest: it points at trees, and only trees pay out.</summary>
    private static ResourceManager.ResourceType? restrictedHarvestType;

    /// <summary>Non-null while one specific type is locked OUT, everything else being fair game — the inverse of restrictedHarvestType, and the shape the stone step's tail needs: once the player has the 5 stone the bed costs, more stone is barred until that bed is actually placed, so they can't wander off mining instead of spending it. Kept separate rather than folded into restrictedHarvestType because "only this" and "anything but this" are genuinely different constraints, and the bed step wants the second.</summary>
    private static ResourceManager.ResourceType? barredHarvestType;

    /// <summary>True only while the tutorial actually has a harvest lock of either kind in force — which is to say, only during the tutorial, and only during the few steps of it that lock anything. Exists so TurtleAgent can gate its "my objective was just barred, go home" handling behind one check that is plainly false in all normal play, rather than inferring the same thing from IsHarvestAllowed happening to return true. Nothing outside a tutorial run should ever behave differently because this type exists.</summary>
    public static bool IsHarvestRestricted => restrictedHarvestType != null || barredHarvestType != null;

    public static bool IsHarvestAllowed(ResourceManager.ResourceType type)
    {
        if (barredHarvestType == type) return false;

        return restrictedHarvestType == null || restrictedHarvestType == type;
    }

    [Header("Presentation")]
    [Tooltip("The mentor turtle that slides in and speaks each line. Required — without it the sequence still runs and still gates correctly, but silently.")]
    [SerializeField] private TutorialMentor mentor;
    [Tooltip("The pointer arrows, re-aimed each step. Most steps use only the first and park the rest; the upgrade pick uses one per card on offer, so wire at least as many as UpgradeSelectionUI has card slots (2) to have every card pointed at. Optional; leave empty for a tutorial that only talks.")]
    [SerializeField] private TutorialArrow[] arrows;

    [Header("Arrow Targets")]
    [Tooltip("The build drawer's hammer tab, so the 'open the build menu' step can point at it. Drag the tab's own RectTransform in.")]
    [SerializeField] private RectTransform hammerTabTarget;
    [Tooltip("The build drawer, so the 'place a bed' step can point at the Bed's row inside it. Rows only exist while the drawer is built, which is why this is asked for rather than a direct row reference.")]
    [SerializeField] private BuildSelectionUI buildSelectionUI;
    [Tooltip("The upgrade choice UI, so the 'pick an upgrade' step can point at a card. Also how this script notices the pick appearing and being made.")]
    [SerializeField] private UpgradeSelectionUI upgradeSelectionUI;
    [Tooltip("The Turtle Bed prefab (Assets/Prefabs/Buildings/TurtleBed.prefab), matched by REFERENCE against BuildModeController's Buildables to find its drawer row. Deliberately not matched by display name — that prefab's is 'Bed', and a name match would break silently the day someone renames it.")]
    [SerializeField] private BuildableDefinition bedBuildable;

    [Header("Arrow Approach")]
    [Tooltip("Which side of a WORLD target (turtle, tree, rock, trash) the arrow sits on, in screen terms. Up parks it above, pointing down.")]
    [SerializeField] private Vector2 worldApproach = Vector2.up;
    [Tooltip("Which side of the hammer tab and the build drawer's rows the arrow sits on. Set this to point AWAY from the screen edge the drawer is docked against — right for a left-docked drawer, left for a right-docked one — or the arrow ends up off-screen.")]
    [SerializeField] private Vector2 drawerApproach = Vector2.right;
    [Tooltip("Which side of the upgrade card the arrow sits on. Up parks it above the card, pointing down.")]
    [SerializeField] private Vector2 cardApproach = Vector2.up;

    [Header("Step Text")]
    [SerializeField, TextArea] private string selectTurtleLine = "Welcome to the island! Click one of your turtles to select it.";
    [SerializeField, TextArea] private string collectWoodLine = "Now click a tree. Your turtle will head over and start chopping — bring me 15 wood.";
    [SerializeField, TextArea] private string collectStoneLine = "Nice work. Select a turtle again and send it to a rock. We need 5 stone.";
    [SerializeField, TextArea] private string openBuildMenuLine = "Time to build. Click the hammer to open the build menu.";
    [SerializeField, TextArea] private string placeBedLine = "Pick the Turtle Bed, then click somewhere on the sand to place it. That's another sibling hatched.";
    [SerializeField, TextArea] private string keepCollectingLine = "Good. Now stock up while you can — keep collecting until the storm rolls in.";
    [SerializeField, TextArea] private string stormDefenseLine = "The storm is here! Send your turtles at the incoming plastic before it reaches the nest.";
    [SerializeField, TextArea] private string pickUpgradeLine = "You survived. Choose an upgrade to carry into tomorrow.";
    [SerializeField, TextArea] private string signOffLine = "That's everything I can teach you. You're on your own now — good luck!";

    [Header("Step Tuning")]
    [Tooltip("Seconds (unscaled) the sequence waits after the scene loads before the mentor makes its first entrance. The scene starts FULLY COVERED by SceneTransitionPanel's wipe, which sits still for its Reveal Delay and then slides off over its Slide Duration — and the wipe draws over the tutorial, so anything shorter than those two added together plays the mentor's arrival behind a curtain. The day clock is already frozen before this wait, so it costs the player nothing.")]
    [SerializeField] private float openingDelay = 0.9f;
    [Tooltip("Must stay at or above the Turtle Bed's authored Wood cost, or the bed step can never be completed — see the class doc comment. The bed is currently 15 Wood, so this exactly funds it.")]
    [SerializeField] private int woodToCollect = 15;
    [Tooltip("Must stay at or above the Turtle Bed's authored Rock cost, or the bed step can never be completed — see the class doc comment. The bed is currently 5 Rock, so this exactly funds it.")]
    [SerializeField] private int stoneToCollect = 5;
    [Tooltip("Extra seconds the stock-up line sits on screen after it finishes typing, before the mentor slides away. Longer than the mentor's own Hold After Line because this one hands the player off to free play rather than asking them to do something immediately — there's no next prompt to make them feel hurried, so it can afford to be read properly. Raise it further for a slower read; it comes out of the day, so keep it comfortably under how long the day actually lasts.")]
    [SerializeField] private float keepCollectingHold = 3.5f;
    [Tooltip("How long the sign-off line stays up before the mentor leaves for good.")]
    [SerializeField] private float signOffDuration = 3.5f;
    [Tooltip("The storm-defense step also completes on its own after this long, whether or not a turtle actually engages the trash — so a player who is slow to react still gets the upgrade prompt and the sign-off rather than being stranded on this step for the whole night. It aborts early anyway the moment the upgrade pick appears.")]
    [SerializeField] private float stormStepTimeout = 25f;

    /// <summary>Which arrow from Arrows is currently calling out which piece of trash. An assignment is only ever broken by the trash dying — never by anything the player does — which is the whole point of this being tracked per-target instead of re-derived from a "nearest" search each frame.</summary>
    private readonly Dictionary<TrashHealth, TutorialArrow> trashArrowAssignments = new Dictionary<TrashHealth, TutorialArrow>();
    /// <summary>Whether Update is currently maintaining the trash arrows. Set for the storm-defense step and cleared once the storm resolves — deliberately NOT cleared when that step completes, since the step ends the moment a turtle engages and the arrows are meant to outlive that.</summary>
    private bool isPointingAtTrash;
    private readonly List<TrashHealth> finishedTrashScratch = new List<TrashHealth>();

    private void Awake()
    {
        // IsActive is static, so force a clean state on domain reload the same
        // way DayStormCycle.IsStorming/UpgradeSelectionUI.IsActive do.
        IsActive = false;
        DidRunThisSession = false;
        restrictedHarvestType = null;
        barredHarvestType = null;
    }

    private void Start()
    {
        if (PlayerPrefs.GetInt(TutorialEnabledKey, 0) != 1)
        {
            mentor?.HideImmediate();
            ClearArrows();
            return;
        }

        PlayerPrefs.SetInt(TutorialEnabledKey, 0);
        PlayerPrefs.Save();

        DidRunThisSession = true;
        StartCoroutine(RunTutorialSequence());
    }

    private void Update()
    {
        // Polled, like every other piece of step detection here, rather than
        // subscribing to trash spawning and dying — see the class doc comment.
        if (isPointingAtTrash) RefreshTrashArrows();
    }

    /// <summary>Starts handing the arrows out to trash. Idempotent — the first Update after this does the actual work.</summary>
    private void BeginTrashArrows() => isPointingAtTrash = true;

    /// <summary>Stops calling out trash and parks every arrow, handing the Arrows list back to the scripted steps.</summary>
    private void EndTrashArrows()
    {
        isPointingAtTrash = false;
        trashArrowAssignments.Clear();
        ClearArrows();
    }

    /// <summary>
    /// Hands an arrow to every piece of live trash that hasn't got one, and
    /// takes back the arrows whose trash has been destroyed.
    ///
    /// An assignment is broken by exactly one thing: the trash dying. Nothing
    /// the player does releases an arrow, which is what separates this from the
    /// scripted steps — those point at something until the player has done the
    /// thing being asked of them, whereas a storm is a list of targets and each
    /// one stays marked until it is gone.
    ///
    /// The Arrows list is used directly rather than cloned, so how many pieces
    /// of trash can be marked at once is however many arrows are wired — three
    /// today, which covers the first storm's round with room to spare. Freed
    /// arrows are handed straight back out to any trash still waiting, so a
    /// bigger round would still call everything out, just not all at once.
    /// </summary>
    private void RefreshTrashArrows()
    {
        if (arrows == null) return;

        // Unity-null: a destroyed TrashHealth is still a live dictionary key
        // (its hash doesn't change), so this can't be left to a null check on
        // the reference itself.
        finishedTrashScratch.Clear();
        foreach (KeyValuePair<TrashHealth, TutorialArrow> pair in trashArrowAssignments)
        {
            if (pair.Key == null) finishedTrashScratch.Add(pair.Key);
        }

        foreach (TrashHealth finished in finishedTrashScratch)
        {
            if (trashArrowAssignments.TryGetValue(finished, out TutorialArrow freed) && freed != null) freed.Clear();
            trashArrowAssignments.Remove(finished);
        }

        foreach (TrashHealth trash in TrashHealth.AllTrash)
        {
            if (trash == null || trashArrowAssignments.ContainsKey(trash)) continue;

            TutorialArrow arrow = TakeFreeArrow();
            if (arrow == null) return; // all in use — the rest wait for one to free up

            trashArrowAssignments[trash] = arrow;

            // The transform is captured directly rather than looked up through
            // the TrashHealth each frame: TutorialArrow keeps asking its picker
            // until it gets an answer, and reading .transform off a destroyed
            // component would throw rather than simply returning null.
            Transform target = trash.transform;
            arrow.Track(() => target, worldApproach);
        }
    }

    /// <summary>An arrow from Arrows with no trash assigned to it, or null when every one is already spoken for.</summary>
    private TutorialArrow TakeFreeArrow()
    {
        foreach (TutorialArrow arrow in arrows)
        {
            if (arrow != null && !trashArrowAssignments.ContainsValue(arrow)) return arrow;
        }

        return null;
    }

    private IEnumerator RunTutorialSequence()
    {
        IsActive = true;

        // Captured up front, not right before the bed step — build mode is
        // never actually blocked during the earlier steps (only DayStormCycle's
        // clock and harvesting are gated by IsActive), so a player who builds a
        // bed early out of curiosity would otherwise have that early bed baked
        // into a baseline captured later, requiring a second, redundant one.
        int startingBedCount = TurtleBed.AllBeds.Count;

        // Held back until the scene wipe has finished uncovering — IsActive is
        // already true above, so the day clock stays frozen throughout and this
        // wait comes out of nobody's playing time. See openingDelay's tooltip.
        if (openingDelay > 0f) yield return new WaitForSecondsRealtime(openingDelay);

        yield return Step(
            selectTurtleLine,
            NearestTurtle, worldApproach,
            () => TurtleAgent.AllTurtles.Any(t => t.IsSelected));

        restrictedHarvestType = ResourceManager.ResourceType.Wood;
        yield return Step(
            collectWoodLine,
            () => NearestNode(ResourceManager.ResourceType.Wood), worldApproach,
            () => CountOf(ResourceManager.ResourceType.Wood) >= woodToCollect);

        restrictedHarvestType = ResourceManager.ResourceType.Rock;
        yield return Step(
            collectStoneLine,
            () => NearestNode(ResourceManager.ResourceType.Rock), worldApproach,
            () => CountOf(ResourceManager.ResourceType.Rock) >= stoneToCollect);

        // Stone is now locked out (rather than simply un-restricting everything)
        // until the bed is down: the player has exactly the 5 the bed costs, and
        // the next two steps are about spending it. Without this they can keep
        // mining indefinitely while the build prompt sits there unanswered.
        restrictedHarvestType = null;
        barredHarvestType = ResourceManager.ResourceType.Rock;

        yield return Step(
            openBuildMenuLine,
            () => hammerTabTarget, drawerApproach,
            () => BuildModeController.IsToggledOpen);

        yield return Step(
            placeBedLine,
            BedRowTarget, drawerApproach,
            () => TurtleBed.AllBeds.Count > startingBedCount);

        barredHarvestType = null;
        IsActive = false;

        // Banked HERE rather than at the very end of this coroutine, and the
        // distinction matters: everything below waits on a storm that might be
        // a while away, and the player can lose the nest or quit outright
        // before it lands. The scripted teaching is finished at this line — the
        // rest is storm-time coaching — so crediting completion now means
        // nobody has to replay the whole tutorial just because they didn't
        // survive to their first night. Unlocks the menu's modifier grid (see
        // ModifierSelectionUI).
        PlayerProgression.HasCompletedTutorial = true;

        // IsActive is false from here on, so DayStormCycle's day clock (frozen
        // by every step above) now runs for real. That ordering is load-bearing
        // for the step below: it waits on a storm that can only arrive once the
        // clock is moving, so raising this line above the IsActive drop would
        // park the tutorial here forever.
        //
        // The mentor leaves as soon as it has spoken here rather than waiting
        // the step out, because "keep collecting" is an instruction to go and
        // play, not a task to be supervised — and what it's waiting for is the
        // rest of the day, which is far too long to stand in the corner for.
        // The wait is what gives the player their first stretch of unguided
        // play, and the storm ends it.
        yield return Step(
            keepCollectingLine,
            null, worldApproach,
            () => DayStormCycle.IsStorming,
            dismissAfterSpeaking: true,
            holdBeforeDismiss: keepCollectingHold);

        // Every piece of trash gets its own arrow here, rather than the single
        // "nearest" arrow the scripted steps use. Two differences, both
        // deliberate: the player is being shown the whole threat rather than
        // one thing to click, and an arrow is only released when ITS trash is
        // destroyed — not when the player reacts. So this is driven outside
        // Step's arrow lifecycle (Step clears the scripted arrows the instant
        // its task is done, which here is the moment one turtle engages) and is
        // handed a null arrow target of its own.
        BeginTrashArrows();

        yield return Step(
            stormDefenseLine,
            null, worldApproach,
            () => TurtleAgent.AllTurtles.Any(t => t.IsAggroed),
            // The pick appearing means the storm is over and there is no trash
            // left to point at, so this step has to let go regardless of
            // whether a turtle ever engaged.
            abort: () => UpgradeSelectionUI.IsActive,
            timeout: stormStepTimeout);

        yield return new WaitUntil(() => UpgradeSelectionUI.IsActive);

        // Only now, with the storm resolved: the mentor stopped supervising
        // this step long ago, but the arrows keep marking whatever is still
        // alive until it isn't. In practice they've each let go on their own by
        // this point (the pick only appears once no trash is left), so this is
        // the backstop for the one case that doesn't reach zero — the run
        // ending mid-storm.
        EndTrashArrows();

        // Written out rather than run through Step, because it's the only step
        // that breaks both of Step's rules: it points at every card at once
        // instead of one thing, and the mentor leaves as soon as it has spoken
        // instead of waiting out the task. That second part is deliberate — the
        // cards are centre-screen and the choice is the player's to weigh, so a
        // turtle lingering in the corner is just something else to look at. The
        // arrows stay behind and do the pointing.
        if (mentor != null) yield return mentor.Speak(pickUpgradeLine);

        TrackCardArrows();

        if (mentor != null) yield return mentor.Dismiss();

        // Select() clears IsActive on the same line it applies the card, so
        // this is simply "a card has been chosen".
        yield return WaitForStep(() => !UpgradeSelectionUI.IsActive, null, 0f);

        ClearArrows();

        // The garbage-patch cutscene pans the camera off to sea the moment the
        // pick resolves. Signing off over the top of that would put the mentor
        // in the corner of a shot it has nothing to do with, so wait it out.
        yield return new WaitUntil(() => !GarbagePatchCutsceneController.IsActive);

        yield return Step(signOffLine, null, Vector2.up, null, timeout: signOffDuration);
    }

    /// <summary>
    /// One tutorial step, and the shape of the whole sequence: the mentor slides
    /// in and speaks its line, the arrow starts pointing at whatever the player
    /// has to touch, and then both are cleared away once the step's own
    /// condition says the player has done it.
    ///
    /// The mentor deliberately STAYS on screen for the whole wait rather than
    /// leaving as soon as it stops talking, so the instruction is still there to
    /// re-read while it's being carried out; it only slides off once the step is
    /// genuinely finished, which is what makes the gap between steps read as
    /// progress. Passing a null arrowTarget gives a line with nothing to point
    /// at; passing a null isDone with a timeout gives a line that simply holds
    /// for a beat.
    /// </summary>
    /// <remarks>
    /// dismissAfterSpeaking flips the staying behavior above for a step whose
    /// wait is long and unsupervised — "keep collecting until the storm" waits
    /// out the rest of the day, which is far too long to leave a turtle
    /// standing in the corner. The mentor leaves the moment it finishes
    /// talking, and any arrow stays behind to do the pointing.
    ///
    /// holdBeforeDismiss buys that kind of line more reading time without
    /// slowing every other line down: such a step is a hand-off to free play
    /// rather than something the player acts on immediately, so it wants a
    /// longer beat on screen than the mentor's own Hold After Line gives, which
    /// applies to all of them.
    /// </remarks>
    private IEnumerator Step(string line, Func<Transform> arrowTarget, Vector2 approach, Func<bool> isDone, bool dismissAfterSpeaking = false, float holdBeforeDismiss = 0f, Func<bool> abort = null, float timeout = 0f)
    {
        if (mentor != null) yield return mentor.Speak(line);

        if (arrowTarget != null) TrackSingleArrow(arrowTarget, approach);

        if (dismissAfterSpeaking)
        {
            // Only meaningful on this path: a step whose mentor stays put until
            // the task is done is already leaving the line up for as long as it
            // takes, so there's nothing for a hold to extend.
            if (holdBeforeDismiss > 0f) yield return new WaitForSecondsRealtime(holdBeforeDismiss);

            if (mentor != null) yield return mentor.Dismiss();
        }

        yield return WaitForStep(isDone, abort, timeout);

        ClearArrows();

        if (!dismissAfterSpeaking && mentor != null) yield return mentor.Dismiss();
    }

    /// <summary>Points the first arrow at target and parks every other one — what an ordinary step wants, since all but the upgrade pick are about exactly one thing.</summary>
    private void TrackSingleArrow(Func<Transform> target, Vector2 approach)
    {
        if (arrows == null) return;

        for (int i = 0; i < arrows.Length; i++)
        {
            if (arrows[i] == null) continue;

            if (i == 0) arrows[i].Track(target, approach);
            else arrows[i].Clear();
        }
    }

    /// <summary>Points one arrow at each upgrade card on offer. Any arrow past the number of cards dealt is parked — Show draws as many as the eligible pool allows, which can be fewer than there are slots late in a run.</summary>
    private void TrackCardArrows()
    {
        if (arrows == null) return;

        int cardCount = upgradeSelectionUI != null ? upgradeSelectionUI.ShownCardCount : 0;

        for (int i = 0; i < arrows.Length; i++)
        {
            if (arrows[i] == null) continue;

            if (i >= cardCount)
            {
                arrows[i].Clear();
                continue;
            }

            // Copied into a local: the closure below outlives this loop, and
            // capturing the loop variable itself would leave every arrow asking
            // for the same final index.
            int cardIndex = i;
            arrows[i].Track(() => upgradeSelectionUI != null ? upgradeSelectionUI.GetShownCardRect(cardIndex) : null, cardApproach);
        }
    }

    /// <summary>Parks every arrow. Called at the end of each step — and deliberately refused while the storm arrows are out, since those share this same list and the step they run under completes the moment ONE turtle engages, which is exactly the moment they must not be taken down. EndTrashArrows is what releases them.</summary>
    private void ClearArrows()
    {
        if (arrows == null || isPointingAtTrash) return;

        foreach (TutorialArrow tutorialArrow in arrows) tutorialArrow?.Clear();
    }

    /// <summary>Holds until isDone says the step is complete, abort says it can no longer be completed, or timeout seconds have passed — whichever comes first. Any of the three may be left out; with all three out this returns immediately. Unscaled, so a player who pauses to read isn't also stalling every timeout in the sequence.</summary>
    private static IEnumerator WaitForStep(Func<bool> isDone, Func<bool> abort, float timeout)
    {
        float elapsed = 0f;

        while (true)
        {
            if (isDone != null && isDone()) yield break;
            if (abort != null && abort()) yield break;

            if (timeout > 0f)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= timeout) yield break;
            }
            else if (isDone == null && abort == null)
            {
                yield break;
            }

            yield return null;
        }
    }

    private static int CountOf(ResourceManager.ResourceType type)
    {
        return ResourceManager.Instance != null ? ResourceManager.Instance.GetCount(type) : 0;
    }

    /// <summary>
    /// Where "nearest" is measured from: the turtle the player currently has
    /// selected, since that's who actually has to travel to the thing being
    /// pointed at, falling back to the camera (i.e. the middle of what the
    /// player is looking at) when nothing is selected — which is the case for
    /// the very first step, whose whole job is to find them a turtle.
    /// </summary>
    private static Vector3 FocusPoint()
    {
        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            if (turtle != null && turtle.IsSelected) return turtle.transform.position;
        }

        Camera cam = Camera.main;
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    private static Transform NearestTurtle()
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        Vector3 focus = FocusPoint();

        foreach (TurtleAgent turtle in TurtleAgent.AllTurtles)
        {
            // Crabs are excluded: they're recruited by an upgrade card the
            // player can't have yet, but a crab wandering into the opening step
            // would still be a valid "click a turtle" target visually while
            // behaving nothing like one.
            if (turtle == null || turtle.IsCrab) continue;

            float distance = (turtle.transform.position - focus).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = turtle.transform;
        }

        return best;
    }

    private static Transform NearestNode(ResourceManager.ResourceType type)
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        Vector3 focus = FocusPoint();

        foreach (ResourceNode node in ResourceNode.AllNodes)
        {
            // IsHarvestable is doing real work here: a depleted node STAYS
            // registered in AllNodes (only its visual is switched off), so
            // without this the arrow happily points at an invisible stump.
            if (node == null || !node.IsHarvestable || node.ResourceType != type) continue;

            float distance = (node.transform.position - focus).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = node.transform;
        }

        return best;
    }

    /// <summary>The Bed's row in the build drawer, or null until there is one. Returning null while the drawer is shut is what makes the arrow wait for the row rather than settle for something else: a target is latched permanently once chosen (see TutorialArrow), so handing back the hammer tab as a stand-in here would leave the arrow stuck on the tab for the whole step. The row is looked up rather than stored because rows don't exist until the drawer builds them, and are destroyed and rebuilt whenever the unlocked set changes.</summary>
    private Transform BedRowTarget()
    {
        if (buildSelectionUI == null || bedBuildable == null || BuildModeController.Instance == null) return null;

        IReadOnlyList<BuildableDefinition> buildables = BuildModeController.Instance.Buildables;
        if (buildables == null) return null;

        for (int i = 0; i < buildables.Count; i++)
        {
            if (buildables[i] == bedBuildable) return buildSelectionUI.GetRowRect(i);
        }

        return null;
    }
}
