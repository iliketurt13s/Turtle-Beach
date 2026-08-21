using UnityEngine;

/// <summary>
/// Framework placeholder card — proves the Apply plumbing end-to-end without
/// designing real content yet. Deliberately inert.
///
/// Originally written for the harmful-upgrade pool that used to be drawn
/// mid-run; that pool is gone (harmful effects are opt-in run modifiers now —
/// see GameModifierDefinition), but this stays useful as the effect on a
/// modifier prefab that hasn't had its real behavior written yet, so the
/// modifier can already be picked and scored while its gameplay is pending.
/// </summary>
public class PlaceholderHazardCard : UpgradeCardDefinition
{
    public override void Apply()
    {
        Debug.Log($"PlaceholderHazardCard: '{DisplayName}' applied — framework placeholder, no gameplay effect yet.");
    }
}
