using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EntranceCinematicLaunchSettingsTests
{
    [SetUp]
    public void Setup()
    {
        ResetSession();
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity");
    }

    [TearDown]
    public void Cleanup()
    {
        ResetSession();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [Test]
    public void AuthoredMenuSwitchIsOffAndHasNoPlayerFacingPresentation()
    {
        var all = Object.FindObjectsByType<EntranceCinematicLaunchSettings>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.AreEqual(1, all.Length);
        var settings = all[0];
        Assert.AreEqual("Developer - Entrance Cinematic", settings.gameObject.name);
        Assert.IsNull(settings.transform.parent);
        Assert.True(settings.gameObject.activeSelf);
        Assert.True(settings.enabled);
        Assert.False(settings.PlayEntranceCinematic);
        Assert.AreEqual(2, settings.GetComponents<Component>().Length, "Only Transform and launch settings; no UI or renderer.");
        var field = typeof(EntranceCinematicLaunchSettings).GetField("_playEntranceCinematic", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field.GetCustomAttribute<SerializeField>(), "The build must receive scene-authored data.");
        Assert.NotNull(field.GetCustomAttribute<TooltipAttribute>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MenuChoiceIsSampledAtLaunchAndConsumedExactlyOnce(bool enabled)
    {
        SetSwitch(enabled);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        SetSwitch(!enabled);
        Assert.AreEqual(enabled, EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
        Assert.True(EntranceCinematicLaunchSettings.ConsumePlaybackDecision(), "A subsequent direct scene entry retains its own default.");
    }

    [Test]
    public void ReturningToMenuAndAnotherLaunchReplaceThePreviousDecision()
    {
        SetSwitch(true);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Title);
        Assert.True(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
        SetSwitch(false);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        Assert.False(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
    }

    [Test]
    public void LeavingForSandboxDiscardsUnconsumedMenuDecision()
    {
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.WeaponSandbox);
        Assert.True(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
    }

    [Test]
    public void DirectSceneWorkflowKeepsCinematicEnabled()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        Assert.True(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
    }

    [Test]
    public void MissingMenuComponentDefaultsToHidden()
    {
        Object.DestroyImmediate(Object.FindFirstObjectByType<EntranceCinematicLaunchSettings>().gameObject);
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        Assert.False(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
    }

    [Test]
    public void SubsystemRegistrationAndMenuAwakeClearAnUnconsumedRequest()
    {
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        ResetSession();
        Assert.True(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
        EntranceCinematicLaunchSettings.PrepareSceneLoad(SceneDestination.Play);
        typeof(EntranceCinematicLaunchSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(Object.FindFirstObjectByType<EntranceCinematicLaunchSettings>(), null);
        Assert.True(EntranceCinematicLaunchSettings.ConsumePlaybackDecision());
        var reset = typeof(EntranceCinematicLaunchSettings).GetMethod("ResetSession", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType);
    }

    private static void SetSwitch(bool enabled)
    {
        var serialized = new SerializedObject(Object.FindFirstObjectByType<EntranceCinematicLaunchSettings>());
        serialized.FindProperty("_playEntranceCinematic").boolValue = enabled;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ResetSession() => typeof(EntranceCinematicLaunchSettings)
        .GetMethod("ResetSession", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
}
