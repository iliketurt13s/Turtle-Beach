using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in the Start Menu scene. Loads the gameplay scene by name via
/// SceneManager when Play is pressed — both the menu scene and the gameplay
/// scene need to be added to Build Settings (File > Build Settings > Add
/// Open Scenes) for this to work in a built game, not just the Editor.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Name of the gameplay scene to load when Play is pressed. Must exactly match that scene's file name and be added to Build Settings.")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    /// <summary>Wire this up to the Play button's OnClick() in the Inspector.</summary>
    public void Play()
    {
        SceneManager.LoadScene(gameplaySceneName);
    }
}
