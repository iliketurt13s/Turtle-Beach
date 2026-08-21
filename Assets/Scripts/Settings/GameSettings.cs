using UnityEngine;

/// <summary>
/// The player's own knobs — camera feel, and the two volume channels — stored
/// permanently and read live by whatever they affect.
///
/// Static and PlayerPrefs-backed for exactly the same reasons PlayerProgression
/// and GameModifierSelection are: PlayerPrefs is this project's only
/// cross-scene channel (there are no DontDestroyOnLoad objects anywhere), and
/// holding the keys, their ranges and their defaults in ONE place is what stops
/// the writer (SettingsMenuUI's sliders, which exist in both scenes) and the
/// readers (CameraController, AudioManager, MusicPlayer) from drifting apart on
/// any of the three.
///
/// Values are cached in static fields and loaded on first touch, because these
/// sit on hot paths — every sound played, every frame of a camera drag — and a
/// PlayerPrefs lookup on each of those is pure waste. Writes update the cache
/// and PlayerPrefs together but deliberately do NOT flush: a slider being
/// dragged writes every frame, and PlayerPrefs.Save() goes to disk.
/// SettingsMenuUI calls Flush() once, when the screen closes.
///
/// The two camera settings are MULTIPLIERS over the speeds authored on
/// CameraController, never absolute speeds. That keeps the tuned feel of the
/// game as the default (1x), lets the slider read the same regardless of what
/// the camera happens to be tuned to, and means retuning that camera in the
/// Inspector doesn't leave every existing save sitting on a stale absolute
/// number that no longer means what it did.
///
/// Nothing here raises a change event — every reader polls (see
/// AudioManager.MasterVolume, MusicPlayer.Update, CameraController.HandleDrag),
/// matching how the rest of this project follows cross-system state.
/// </summary>
public static class GameSettings
{
    private const string CameraDragSpeedKey = "SettingCameraDragSpeed";
    private const string CameraZoomSpeedKey = "SettingCameraZoomSpeed";
    private const string MusicVolumeKey = "SettingMusicVolume";
    private const string SfxVolumeKey = "SettingSfxVolume";

    /// <summary>Slowest the player can make the camera, as a fraction of CameraController's authored speed. Not 0 — a camera that can be set to never move at all is a settings screen that can soft-lock the game.</summary>
    public const float MinCameraSpeedMultiplier = 0.25f;
    /// <summary>Fastest the player can make the camera, as a multiple of CameraController's authored speed.</summary>
    public const float MaxCameraSpeedMultiplier = 3f;
    /// <summary>1x — i.e. exactly the speed authored on CameraController, which is what the game is tuned at.</summary>
    public const float DefaultCameraSpeedMultiplier = 1f;

    /// <summary>Music starts a little under the effects, since it plays continuously and they don't.</summary>
    public const float DefaultMusicVolume = 0.7f;
    public const float DefaultSfxVolume = 1f;

    private static bool loaded;
    private static float cameraDragSpeed;
    private static float cameraZoomSpeed;
    private static float musicVolume;
    private static float sfxVolume;

    /// <summary>Multiplier over CameraController's authored Drag Speed — see the class doc comment on why this is a multiplier.</summary>
    public static float CameraDragSpeed
    {
        get { EnsureLoaded(); return cameraDragSpeed; }
        set
        {
            EnsureLoaded();
            cameraDragSpeed = ClampCameraSpeed(value);
            PlayerPrefs.SetFloat(CameraDragSpeedKey, cameraDragSpeed);
        }
    }

    /// <summary>Multiplier over CameraController's authored Zoom Speed.</summary>
    public static float CameraZoomSpeed
    {
        get { EnsureLoaded(); return cameraZoomSpeed; }
        set
        {
            EnsureLoaded();
            cameraZoomSpeed = ClampCameraSpeed(value);
            PlayerPrefs.SetFloat(CameraZoomSpeedKey, cameraZoomSpeed);
        }
    }

    /// <summary>0-1 scale over MusicPlayer's own authored track volume. Independent of Sfx Volume — the point of two sliders is that either can be taken to silence on its own.</summary>
    public static float MusicVolume
    {
        get { EnsureLoaded(); return musicVolume; }
        set
        {
            EnsureLoaded();
            musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        }
    }

    /// <summary>0-1 scale over AudioManager's own Master Volume, so it covers every pooled one-shot AND the ambience beds that read AudioManager.MasterVolume (the beach loop and the rain are effects, not music — they answer to this slider).</summary>
    public static float SfxVolume
    {
        get { EnsureLoaded(); return sfxVolume; }
        set
        {
            EnsureLoaded();
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        }
    }

    private static float ClampCameraSpeed(float value) =>
        Mathf.Clamp(value, MinCameraSpeedMultiplier, MaxCameraSpeedMultiplier);

    private static void EnsureLoaded()
    {
        if (loaded) return;

        // Set before reading, not after: the getters below go through the
        // properties' own clamping in ResetToDefaults, and a re-entrant
        // EnsureLoaded would otherwise recurse forever.
        loaded = true;

        cameraDragSpeed = ClampCameraSpeed(PlayerPrefs.GetFloat(CameraDragSpeedKey, DefaultCameraSpeedMultiplier));
        cameraZoomSpeed = ClampCameraSpeed(PlayerPrefs.GetFloat(CameraZoomSpeedKey, DefaultCameraSpeedMultiplier));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume));
    }

    /// <summary>Writes the pending values to disk. Called when the settings screen closes rather than on every setter, since a dragged slider is a write per frame — see the class doc comment.</summary>
    public static void Flush() => PlayerPrefs.Save();

    /// <summary>Puts every setting back to the value the game is tuned at. Wire an optional "Defaults" button on the settings screen to SettingsMenuUI.ResetToDefaults, which calls this and then re-syncs its sliders.</summary>
    public static void ResetToDefaults()
    {
        CameraDragSpeed = DefaultCameraSpeedMultiplier;
        CameraZoomSpeed = DefaultCameraSpeedMultiplier;
        MusicVolume = DefaultMusicVolume;
        SfxVolume = DefaultSfxVolume;
        Flush();
    }

    /// <summary>Drops the cache so the next read comes back out of PlayerPrefs. Only needed after something has gone behind these properties and changed the keys directly — i.e. the Editor's Tools > Turtle Beach reset items, which would otherwise leave a running Play session still holding the old values.</summary>
    public static void Reload() => loaded = false;
}
