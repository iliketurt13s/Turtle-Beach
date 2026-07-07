using UnityEngine;

/// <summary>
/// Added dynamically to a turtle whose TurtleBed was destroyed. Waits out any
/// ongoing storm (so a turtle is never yanked away mid-fight) and destroys the
/// turtle the moment DayStormCycle.IsStorming goes false.
/// </summary>
public class TurtleDeparture : MonoBehaviour
{
    private bool departing;

    public void BeginDeparture()
    {
        departing = true;
    }

    private void Update()
    {
        if (!departing || DayStormCycle.IsStorming) return;

        Destroy(gameObject);
    }
}
