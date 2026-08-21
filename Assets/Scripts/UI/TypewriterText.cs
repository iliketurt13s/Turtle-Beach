using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Reveals a TMP_Text one character at a time. Used by ScoreBreakdownUI to
/// type each line of the end-of-run score breakdown in sequence, but written
/// as a standalone component so anything else can type text the same way.
///
/// Reveals via TMP's maxVisibleCharacters rather than by appending substrings
/// to .text — the full string is laid out once up front, so word wrapping is
/// decided by the final text and lines don't visibly re-flow as they fill in
/// (and there's no per-character string allocation or mesh rebuild from
/// scratch).
///
/// Runs on unscaled time throughout, like UISlidePanel: the game-over panel it
/// belongs to can be up while Time.timeScale is still whatever TimeControlUI
/// left it at, and a paused-then-shown panel would otherwise type at 0 speed.
/// </summary>
public class TypewriterText : MonoBehaviour
{
    [Tooltip("The text to type into. Left unassigned = whatever TMP_Text is on this same GameObject.")]
    [SerializeField] private TMP_Text target;
    [Tooltip("Reveal speed. Higher is faster; 0 or less reveals instantly.")]
    [SerializeField] private float charactersPerSecond = 45f;

    private Coroutine typeCoroutine;
    private Action onComplete;

    /// <summary>True while characters are still being revealed.</summary>
    public bool IsTyping => typeCoroutine != null;

    private void Awake()
    {
        ResolveTarget();
    }

    /// <summary>Awake normally covers this, but resolving again on demand keeps Play working even if it's somehow reached before this component's GameObject was ever activated (Awake only runs on activation).</summary>
    private void ResolveTarget()
    {
        if (target == null) target = GetComponent<TMP_Text>();
    }

    /// <summary>Types text out from empty, calling onComplete once every character is visible. Interrupts any type already in progress — the previous callback is dropped rather than fired, since its line is being replaced.</summary>
    public void Play(string text, Action onComplete = null)
    {
        ResolveTarget();

        if (target == null)
        {
            onComplete?.Invoke();
            return;
        }

        Stop();

        target.text = text;
        this.onComplete = onComplete;

        // ForceMeshUpdate so textInfo.characterCount reflects the string we
        // just assigned rather than the previous frame's — TMP only recomputes
        // it lazily, and the coroutine below needs the real count immediately.
        target.ForceMeshUpdate();
        int characterCount = target.textInfo.characterCount;

        // isActiveAndEnabled guards the animated path for the same reason
        // CountUpText does: StartCoroutine throws on an inactive GameObject,
        // and a caller waiting on onComplete would hang forever. Showing the
        // text instantly beats never showing it and stalling the sequence.
        if (charactersPerSecond <= 0f || characterCount == 0 || !isActiveAndEnabled)
        {
            target.maxVisibleCharacters = int.MaxValue;
            FireComplete();
            return;
        }

        target.maxVisibleCharacters = 0;
        typeCoroutine = StartCoroutine(TypeRoutine(characterCount));
    }

    private IEnumerator TypeRoutine(int characterCount)
    {
        float revealed = 0f;

        while (revealed < characterCount)
        {
            revealed += charactersPerSecond * Time.unscaledDeltaTime;
            target.maxVisibleCharacters = Mathf.Min(Mathf.FloorToInt(revealed), characterCount);
            yield return null;
        }

        target.maxVisibleCharacters = int.MaxValue;
        typeCoroutine = null;
        FireComplete();
    }

    /// <summary>Jumps straight to fully revealed and fires the pending callback, as if the type had finished normally — this is what a player skipping the sequence hits.</summary>
    public void Skip()
    {
        if (typeCoroutine == null) return;

        Stop();
        if (target != null) target.maxVisibleCharacters = int.MaxValue;
        FireComplete();
    }

    private void Stop()
    {
        if (typeCoroutine != null)
        {
            StopCoroutine(typeCoroutine);
            typeCoroutine = null;
        }
    }

    /// <summary>Clears the stored callback before invoking it, so a callback that starts another Play on this same component can't be re-entered by its own completion.</summary>
    private void FireComplete()
    {
        Action callback = onComplete;
        onComplete = null;
        callback?.Invoke();
    }
}
