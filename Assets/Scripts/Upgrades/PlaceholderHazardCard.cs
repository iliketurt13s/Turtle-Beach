using UnityEngine;

/// <summary>
/// Framework placeholder for the harmful-upgrade pool (see
/// UpgradeSelectionUI.ShowHazardChoice) — proves the hazard pick/Apply
/// plumbing end-to-end without designing real "new enemy" content yet.
/// Deliberately inert.
/// </summary>
public class PlaceholderHazardCard : UpgradeCardDefinition
{
    public override void Apply()
    {
        Debug.Log($"PlaceholderHazardCard: '{DisplayName}' applied — framework placeholder, no gameplay effect yet.");
    }
}
