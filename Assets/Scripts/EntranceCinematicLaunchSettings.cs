using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-authored launch policy. This is developer authoring data, never a player preference.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("ScrapWaves/Entrance Cinematic Launch Settings")]
public sealed class EntranceCinematicLaunchSettings : MonoBehaviour
{
    [Header("Menu launches - Editor and builds")]
    [SerializeField, Tooltip("Play the entrance cinematic when launching gameplay or the tutorial from this menu. OFF keeps the same safe player/camera placement and tutorial or weapon selection, without the cinematic wait. Save this scene to apply the choice to builds. Direct GameplayScene entry retains its existing cinematic; retry still skips it.")]
    private bool _playEntranceCinematic = false;

    public bool PlayEntranceCinematic => _playEntranceCinematic;
    private static bool? _nextPlayback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => _nextPlayback = null;

    private void Awake() => ResetSession();

    /// <summary>Called immediately before a validated scene load, including the menu transition route.</summary>
    public static void PrepareSceneLoad(SceneDestination destination)
    {
        _nextPlayback = null;
        if (destination != SceneDestination.Play) return;
        EntranceCinematic.PrepareNewRun();

        Scene source = SceneManager.GetActiveScene();
        if (source.name != SceneNavigation.TitleSceneName) return;

        // A missing menu setting also hides unfinished content. Direct scene workflows
        // have no menu request and keep their previous default.
        _nextPlayback = false;
        foreach (var settings in Object.FindObjectsByType<EntranceCinematicLaunchSettings>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (settings.gameObject.scene != source) continue;
            _nextPlayback = settings._playEntranceCinematic;
            break;
        }
    }

    /// <summary>Consume once: later direct scene entries cannot inherit the previous menu's choice.</summary>
    public static bool ConsumePlaybackDecision()
    {
        bool play = _nextPlayback ?? true;
        _nextPlayback = null;
        return play;
    }
}
