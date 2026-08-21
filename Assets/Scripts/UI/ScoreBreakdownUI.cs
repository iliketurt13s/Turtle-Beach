using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Animates the end-of-run score breakdown one line at a time: the result
/// title types itself out, then each scoring line's label types out and its
/// number spins up beside it, then the final score lands underneath. Owned by
/// GameOverUI, which finalizes the score first and moves on to the level page
/// once this reports done — this component only handles presentation, never
/// scene loading or pause state.
///
/// This is page one of two inside one panel frame, so Root should be the page
/// object, NOT the panel itself: GameOverUI hides this whole page to reveal
/// LevelUpPanelUI in its place.
///
/// Lines are built from ScoreManager's per-category buckets rather than one
/// total, and read in the same order the score is actually computed (see
/// ScoreManager.FinalizeRun). Beating the garbage patch deliberately has no
/// line of its own — its bonus is already inside the trash total by the time
/// this runs. All four numbers are snapshotted into locals at the top of
/// PlayRoutine rather than read as each row comes up: ScoreManager.ModifierBonus
/// and FinalScore are live computed properties, so reading them seconds apart
/// is what would let one line disagree with another.
///
/// A row's LABEL carries words only, never a number: exactly one number appears
/// per line, in that line's CountUpText. Labels used to have the raw count
/// appended ("Resources Collected  47"), which was wrong twice over. It read as
/// the score printed twice — for resources it literally was the same figure,
/// since deliveries are worth 1 point each — and because the label is revealed a
/// character at a time, a number sitting at the end of it visibly counted
/// through 4, 47, 476 as it typed, so the line appeared to change its own mind
/// after it had already been written. Anything worth showing alongside a line's
/// points belongs in its own row with its own number, not spliced into a label.
///
/// Every line is PURELY ADDITIVE and nothing is ever revised: a number that
/// has landed stays exactly as it landed, and the three lines sum to the final
/// score with no arithmetic left for the player to do. That's why the modifier
/// line shows the POINTS THAT MULTIPLIER ADDED rather than the multiplier
/// itself — printing "x1.50" next to a line would suggest the resource and
/// trash totals above it are about to be rewritten, when in fact the
/// multiplier's whole contribution is the "+N" on its own line. The sum is
/// exact rather than approximately right, because ScoreManager derives that
/// bonus as the difference between two already-rounded totals (ModifierBonus =
/// Final - Base), so rounding can never leave the visible lines a point or two
/// off the total underneath them.
///
/// There used to be a fourth line here for the Easy/Medium/Hard difficulty
/// bonus. Difficulty is gone from the game, so Difficulty Row is now reset like
/// the others but never played — it stays blank for the whole sequence rather
/// than being removed outright, so existing scene wiring survives.
///
/// Everything is unscaled-time (see TypewriterText/CountUpText) since the
/// panel can come up at any Time.timeScale. Clicking or pressing a key during
/// the sequence skips to the end — the whole thing is only a few seconds, but
/// this screen gets replayed constantly across restarts, and being unable to
/// skip it wears thin fast. Skipping is armed a moment after the sequence
/// starts so the same click that ended the run can't blow straight through it.
/// </summary>
public class ScoreBreakdownUI : MonoBehaviour
{
    [Serializable]
    private struct BreakdownRow
    {
        [Tooltip("The whole row object, hidden until its turn in the sequence.")]
        public GameObject root;
        [Tooltip("The row's label — types out first.")]
        public TypewriterText label;
        [Tooltip("The row's number — counts up once the label has finished typing.")]
        public CountUpText value;
    }

    [Tooltip("Root of the breakdown (title + rows), hidden while the panel is merely paused.")]
    [SerializeField] private GameObject root;

    [Header("Title")]
    [SerializeField] private TypewriterText titleText;
    [SerializeField] private string gameOverTitle = "Game Over";
    [Tooltip("Shown instead of Game Over Title when the run ended by depleting the garbage patch.")]
    [SerializeField] private string winTitle = "You Win!";

    [Header("Rows (played top to bottom, in this order)")]
    [SerializeField] private BreakdownRow resourcesRow;
    [SerializeField] private BreakdownRow trashRow;
    [Tooltip("DEFUNCT — difficulty was removed from the game, so this row is never played and stays blank for the whole sequence. The field is kept only so existing scene wiring doesn't break; delete the row object from the panel and clear this whenever you like.")]
    [SerializeField] private BreakdownRow difficultyRow;
    [SerializeField] private BreakdownRow modifiersRow;

    [Header("Final Score")]
    [Tooltip("Counts the run's actual score up last, with no label of its own — just the number. Belongs inside Root along with the rows, so it's hidden with the rest of the page.")]
    [SerializeField] private CountUpText finalScoreValue;

    [Header("Labels")]
    [Tooltip("Words only, no number — the row's number is the CountUpText beside it. See the class doc comment on why a count typed into the label is worse than useless here.")]
    [SerializeField] private string resourcesLabel = "Resources Collected";
    [Tooltip("Words only, no number — same as Resources Label.")]
    [SerializeField] private string trashLabel = "Trash Destroyed";
    [Tooltip("Label for the modifier line, whose number is the points the run's modifiers ADDED. Deliberately carries no \"x1.50\" — see the class doc comment on why a multiplier here would read as a threat to rewrite the lines above.")]
    [SerializeField] private string modifiersLabel = "Modifier Bonus";

    [Header("Timing")]
    [Tooltip("Pause after the title finishes typing, before the first row.")]
    [SerializeField] private float delayAfterTitle = 0.35f;
    [Tooltip("Pause between a label finishing and its number starting to count.")]
    [SerializeField] private float delayBeforeValue = 0.1f;
    [Tooltip("Pause after a row's number lands, before the next row starts.")]
    [SerializeField] private float delayBetweenRows = 0.25f;
    [Tooltip("Extra pause before the final score line, to set it apart from the rows that add up to it.")]
    [SerializeField] private float delayBeforeFinal = 0.5f;
    [Tooltip("How long after the sequence starts before a click/keypress will skip it — stops the click that ended the run from skipping straight past the whole thing.")]
    [SerializeField] private float skipArmDelay = 0.4f;

    private Coroutine sequenceCoroutine;
    private Action onSequenceComplete;
    private bool skipRequested;
    private float skipArmedTime;

    private void Awake()
    {
        // Same full hide as the pause path, so however the rows were left
        // authored in the scene they start blank rather than flashing a
        // previous run's placeholder text on the first pause.
        HideEverything();
    }

    private void Update()
    {
        if (sequenceCoroutine == null || skipRequested) return;
        if (Time.unscaledTime < skipArmedTime) return;

        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool keyed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        if (clicked || keyed) skipRequested = true;
    }

    /// <summary>Hides the breakdown without playing anything — used when the panel comes up as a pause menu rather than a result screen, so pausing never shows score lines from a run that hasn't ended.</summary>
    public void Hide()
    {
        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }

        HideEverything();
    }

    /// <summary>
    /// Deactivates every piece this component owns individually rather than
    /// relying on Root alone. Root is optional — and when it's left unassigned
    /// (or the title/rows were parented somewhere outside it), hiding only
    /// Root would leave whatever those texts were authored as in the scene
    /// sitting visible behind the pause menu. Hiding each piece by its own
    /// reference can't be defeated by how the hierarchy happens to be nested.
    /// </summary>
    private void HideEverything()
    {
        if (root != null) root.SetActive(false);
        if (titleText != null) titleText.gameObject.SetActive(false);

        ResetRow(resourcesRow);
        ResetRow(trashRow);
        ResetRow(difficultyRow);
        ResetRow(modifiersRow);

        if (finalScoreValue != null)
        {
            finalScoreValue.Clear();
            finalScoreValue.gameObject.SetActive(false);
        }
    }

    /// <summary>Plays the whole breakdown top to bottom, calling onComplete once the final score has landed (or immediately after, if the player skipped). Expects ScoreManager.FinalizeRun to already have run, so the numbers it reads are the run's real totals.</summary>
    public void Play(bool won, Action onComplete = null)
    {
        onSequenceComplete = onComplete;

        if (root != null) root.SetActive(true);
        // Re-activated explicitly because Hide/Awake deactivate it directly,
        // and TypewriterText can't run its reveal coroutine on an inactive
        // object.
        if (titleText != null) titleText.gameObject.SetActive(true);

        if (sequenceCoroutine != null) StopCoroutine(sequenceCoroutine);
        sequenceCoroutine = StartCoroutine(PlayRoutine(won));
    }

    private IEnumerator PlayRoutine(bool won)
    {
        skipRequested = false;
        skipArmedTime = Time.unscaledTime + skipArmDelay;

        ResetRow(resourcesRow);
        ResetRow(trashRow);
        ResetRow(difficultyRow);
        ResetRow(modifiersRow);
        if (finalScoreValue != null)
        {
            finalScoreValue.Clear();
            finalScoreValue.gameObject.SetActive(false);
        }

        // Every number this sequence will ever print is read HERE, once, before
        // a single character is typed — see the class doc comment. Reading them
        // per-row instead (the old behavior) meant four separate reads spread
        // across several seconds of animation, so any late change to the run's
        // totals landed on some lines and not others and left the visible lines
        // no longer adding up to the total underneath them.
        ScoreManager score = ScoreManager.Instance;
        int resourcePoints = score != null ? score.ResourcePoints : 0;
        int trashPoints = score != null ? score.TrashPoints : 0;
        int modifierBonus = score != null ? score.ModifierBonus : 0;
        int finalScore = score != null ? score.FinalScore : 0;

        yield return TypeTitle(won ? winTitle : gameOverTitle);
        yield return Wait(delayAfterTitle);

        yield return PlayRow(resourcesRow, resourcesLabel, resourcePoints, "{0}");
        yield return Wait(delayBetweenRows);

        yield return PlayRow(trashRow, trashLabel, trashPoints, "{0}");
        yield return Wait(delayBetweenRows);

        // Difficulty Row is deliberately NOT played — see its own tooltip. It
        // stays reset (hidden) for the whole sequence.
        //
        // This line names what the bonus came FROM and lets its number say how
        // much it was worth. No "x1.50" anywhere on screen — the points it
        // bought are already the whole story, and printing the multiplier as
        // well is what makes the lines above look provisional.
        yield return PlayRow(modifiersRow, modifiersLabel, modifierBonus, "+{0}");
        yield return Wait(delayBeforeFinal);

        // No label and no row wrapper — the run's score is just the number.
        yield return CountFinalScore(finalScore);

        sequenceCoroutine = null;

        Action callback = onSequenceComplete;
        onSequenceComplete = null;
        callback?.Invoke();
    }

    /// <summary>Counts the final score up. Honors a skip request the same way a row's value does.</summary>
    private IEnumerator CountFinalScore(int value)
    {
        if (finalScoreValue == null) yield break;

        // Activated by its own reference for the same reason a row's pieces are
        // (see PlayRow): CountUpText can't run its coroutine on an inactive
        // object, and would land instantly instead of animating.
        finalScoreValue.gameObject.SetActive(true);
        finalScoreValue.SetFormat("{0}");

        bool counted = false;
        finalScoreValue.Play(value, () => counted = true);
        while (!counted)
        {
            if (skipRequested) finalScoreValue.Skip();
            yield return null;
        }
    }

    private IEnumerator TypeTitle(string text)
    {
        if (titleText == null) yield break;

        bool done = false;
        titleText.Play(text, () => done = true);

        while (!done)
        {
            if (skipRequested) titleText.Skip();
            yield return null;
        }
    }

    /// <summary>Shows one row, types its label, then counts its number up. Both stages honor a skip request by jumping straight to their finished state, so skipping mid-sequence still leaves every earlier row fully written rather than half-typed.</summary>
    private IEnumerator PlayRow(BreakdownRow row, string label, int value, string valueFormat)
    {
        // Label and value are activated by their own references, not just via
        // Root: they aren't necessarily children of it (Root is optional, and
        // a row's text can be parented anywhere), and TypewriterText/CountUpText
        // can't start their coroutines on an inactive object — which used to
        // strand this sequence forever, since the completion callback it waits
        // on would never fire and the buttons would never appear.
        if (row.root != null) row.root.SetActive(true);
        if (row.label != null) row.label.gameObject.SetActive(true);
        if (row.value != null) row.value.gameObject.SetActive(true);

        if (row.label != null)
        {
            bool typed = false;
            row.label.Play(label, () => typed = true);
            while (!typed)
            {
                if (skipRequested) row.label.Skip();
                yield return null;
            }
        }

        yield return Wait(delayBeforeValue);

        if (row.value != null)
        {
            row.value.SetFormat(valueFormat);

            bool counted = false;
            row.value.Play(value, () => counted = true);
            while (!counted)
            {
                if (skipRequested) row.value.Skip();
                yield return null;
            }
        }
    }

    /// <summary>Skips a delay entirely once the player has asked to skip, rather than still making them sit through every inter-row pause.</summary>
    private IEnumerator Wait(float seconds)
    {
        if (skipRequested || seconds <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < seconds && !skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>Hides a row by each of its own references rather than relying on Root alone, mirroring PlayRow — otherwise a label or value parented outside Root stays visible between runs.</summary>
    private void ResetRow(BreakdownRow row)
    {
        if (row.root != null) row.root.SetActive(false);
        if (row.label != null) row.label.gameObject.SetActive(false);
        if (row.value != null)
        {
            row.value.Clear();
            row.value.gameObject.SetActive(false);
        }
    }

}
