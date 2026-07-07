using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Three clickable icons — pause, normal speed, double speed — that set
/// Time.timeScale directly (0 / 1 / 2). Whichever speed is currently active
/// has its icon tinted darker so the player can see the current state at a
/// glance; the other two stay at full brightness.
/// </summary>
public class TimeControlUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button doubleButton;

    [Header("Icons")]
    [SerializeField] private Image pauseIcon;
    [SerializeField] private Image normalIcon;
    [SerializeField] private Image doubleIcon;

    [Header("Selected Tint")]
    [SerializeField] private Color selectedColor = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private Color unselectedColor = Color.white;

    private void Awake()
    {
        // Time.timeScale is a global engine value, not one of our own statics —
        // if Domain Reload is disabled it can carry over from a previous Play
        // session, so force a clean start the same way other static state in
        // this project defensively resets in Awake.
        Time.timeScale = 1f;

        if (pauseButton != null) pauseButton.onClick.AddListener(() => SetTimeScale(0f));
        if (normalButton != null) normalButton.onClick.AddListener(() => SetTimeScale(1f));
        if (doubleButton != null) doubleButton.onClick.AddListener(() => SetTimeScale(2f));

        RefreshIcons();
    }

    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        RefreshIcons();
    }

    private void RefreshIcons()
    {
        Tint(pauseIcon, Mathf.Approximately(Time.timeScale, 0f));
        Tint(normalIcon, Mathf.Approximately(Time.timeScale, 1f));
        Tint(doubleIcon, Mathf.Approximately(Time.timeScale, 2f));
    }

    private void Tint(Image icon, bool selected)
    {
        if (icon != null) icon.color = selected ? selectedColor : unselectedColor;
    }
}
