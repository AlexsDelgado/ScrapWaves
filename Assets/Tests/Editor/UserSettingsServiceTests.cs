using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class UserSettingsServiceTests
{
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.DeleteKey(UserSettingsService.PlayerPrefsKey);
        EnemyReactionRuntime.ApplyUserPreferences(false, true);
    }

    [TearDown]
    public void TearDown()
    {
        UserSettingsApplier[] appliers = UnityEngine.Object.FindObjectsByType<UserSettingsApplier>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < appliers.Length; i++)
            InvokePrivate(appliers[i], "OnDisable");

        PlayerPrefs.DeleteKey(UserSettingsService.PlayerPrefsKey);
        PlayerPrefs.Save();
        EnemyReactionRuntime.ApplyUserPreferences(false, true);
    }

    [Test]
    public void DefaultsChangesAndPersistenceUseOneClampedSourceOfTruth()
    {
        UserSettingsService service = CreateService();

        Assert.That(service.HorizontalSensitivity, Is.EqualTo(UserSettingsData.DefaultHorizontalSensitivity));
        Assert.That(service.VerticalSensitivity, Is.EqualTo(UserSettingsData.DefaultVerticalSensitivity));
        Assert.That(service.InvertY, Is.False);
        Assert.That(service.SfxVolume, Is.EqualTo(UserSettingsData.DefaultSfxVolume));
        Assert.That(service.MusicVolume, Is.EqualTo(UserSettingsData.DefaultMusicVolume));
        Assert.That(service.ReducedMotion, Is.False);
        Assert.That(service.ScreenShake, Is.True);
        Assert.That(service.ScreenFlash, Is.True);

        service.HorizontalSensitivity = 99f;
        service.VerticalSensitivity = float.NaN;
        service.InvertY = true;
        service.SfxVolume = -3f;
        service.MusicVolume = 3f;
        service.ReducedMotion = true;
        service.ScreenShake = false;
        service.ScreenFlash = false;

        Assert.That(service.HorizontalSensitivity, Is.EqualTo(UserSettingsData.MaximumSensitivity));
        Assert.That(service.VerticalSensitivity, Is.EqualTo(UserSettingsData.DefaultVerticalSensitivity));
        Assert.That(service.SfxVolume, Is.Zero);
        Assert.That(service.MusicVolume, Is.EqualTo(1f));

        UnityEngine.Object.DestroyImmediate(service.gameObject);
        UserSettingsService reloaded = CreateService();

        Assert.That(reloaded.HorizontalSensitivity, Is.EqualTo(UserSettingsData.MaximumSensitivity));
        Assert.That(reloaded.VerticalSensitivity, Is.EqualTo(UserSettingsData.DefaultVerticalSensitivity));
        Assert.That(reloaded.InvertY, Is.True);
        Assert.That(reloaded.SfxVolume, Is.Zero);
        Assert.That(reloaded.MusicVolume, Is.EqualTo(1f));
        Assert.That(reloaded.ReducedMotion, Is.True);
        Assert.That(reloaded.ScreenShake, Is.False);
        Assert.That(reloaded.ScreenFlash, Is.False);
    }

    [Test]
    public void CorruptOrOutOfRangeStoredValuesFallBackOrClamp()
    {
        UserSettingsData stored = UserSettingsData.CreateDefault();
        stored.HorizontalSensitivity = -4f;
        stored.VerticalSensitivity = 8f;
        stored.SfxVolume = -2f;
        stored.MusicVolume = 5f;
        PlayerPrefs.SetString(UserSettingsService.PlayerPrefsKey, JsonUtility.ToJson(stored));

        UserSettingsService service = CreateService();

        Assert.That(service.HorizontalSensitivity, Is.EqualTo(UserSettingsData.MinimumSensitivity));
        Assert.That(service.VerticalSensitivity, Is.EqualTo(UserSettingsData.MaximumSensitivity));
        Assert.That(service.SfxVolume, Is.Zero);
        Assert.That(service.MusicVolume, Is.EqualTo(1f));

        UnityEngine.Object.DestroyImmediate(service.gameObject);
        PlayerPrefs.SetString(UserSettingsService.PlayerPrefsKey, "not valid settings json");
        UserSettingsService corrupt = CreateService();

        Assert.That(corrupt.Current.HorizontalSensitivity, Is.EqualTo(UserSettingsData.DefaultHorizontalSensitivity));
        Assert.That(corrupt.Current.MusicVolume, Is.EqualTo(UserSettingsData.DefaultMusicVolume));
        Assert.That(corrupt.ScreenShake, Is.True);
        Assert.That(corrupt.ScreenFlash, Is.True);
    }

    [Test]
    public void ResetCategoryAndResetAllNotifyOnlyActuallyChangedValues()
    {
        UserSettingsService service = CreateService();
        List<UserSettingsChange> changes = new();
        service.Changed += changes.Add;

        service.HorizontalSensitivity = 0.3f;
        service.InvertY = true;
        service.MusicVolume = 0.2f;
        service.ReducedMotion = true;
        changes.Clear();

        service.ResetCategory(UserSettingsCategory.Controls);

        Assert.That(service.HorizontalSensitivity, Is.EqualTo(UserSettingsData.DefaultHorizontalSensitivity));
        Assert.That(service.InvertY, Is.False);
        Assert.That(service.MusicVolume, Is.EqualTo(0.2f));
        Assert.That(service.ReducedMotion, Is.True);
        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0], Is.EqualTo(UserSettingsChange.HorizontalSensitivity | UserSettingsChange.InvertY));

        changes.Clear();
        service.ResetAll();

        Assert.That(service.MusicVolume, Is.EqualTo(UserSettingsData.DefaultMusicVolume));
        Assert.That(service.ReducedMotion, Is.False);
        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0], Is.EqualTo(UserSettingsChange.MusicVolume | UserSettingsChange.ReducedMotion));
    }

    [Test]
    public void ApplierUpdatesExistingAndLateCameraAudioTargetsImmediately()
    {
        UserSettingsService service = CreateService();
        service.HorizontalSensitivity = 0.28f;
        service.VerticalSensitivity = 0.31f;
        service.InvertY = true;
        service.SfxVolume = 0.25f;
        service.MusicVolume = 0.65f;
        service.ScreenShake = false;
        service.ScreenFlash = false;

        UserSettingsApplier applier = new GameObject("User Settings Applier").AddComponent<UserSettingsApplier>();
        InvokePrivate(applier, "OnEnable");

        ThirdPersonCamera camera = new GameObject("Late Camera").AddComponent<ThirdPersonCamera>();
        InvokePrivate(camera, "OnEnable");
        GameObject audioObject = new("Late Audio Manager");
        AudioManager audioManager = audioObject.AddComponent<AudioManager>();
        AudioSource normalMusic = audioObject.AddComponent<AudioSource>();
        AudioSource overheatMusic = audioObject.AddComponent<AudioSource>();
        SetPrivateField(audioManager, "_musicNormal", normalMusic);
        SetPrivateField(audioManager, "_musicOverheatLayer", overheatMusic);
        InvokePrivate(audioManager, "OnEnable");
        WeaponPresentationController presentation = new GameObject("Late Weapon Presentation")
            .AddComponent<WeaponPresentationController>();
        InvokePrivate(presentation, "OnEnable");

        Assert.That(camera.HorizontalSensitivity, Is.EqualTo(0.28f));
        Assert.That(camera.VerticalSensitivity, Is.EqualTo(0.31f));
        Assert.That(camera.InvertVertical, Is.True);
        Assert.That(camera.ScreenShakeEnabled, Is.False);
        Assert.That(camera.AddPresentationImpulse(Vector3.one, Vector3.one), Is.False);
        Assert.That(audioManager.SfxVolume, Is.EqualTo(0.25f));
        Assert.That(audioManager.MusicVolume, Is.EqualTo(0.65f));
        Assert.That(normalMusic.volume, Is.EqualTo(0.65f));
        audioManager.SetOverheatLayerActive(true);
        Assert.That(overheatMusic.volume, Is.EqualTo(0.35f * (0.65f / 0.45f)).Within(0.0001f));
        Assert.That(EnemyReactionRuntime.ScreenFlashEnabled, Is.False);
        Assert.That(presentation.RuntimeOptions.ScreenShakeEnabled, Is.False);
        Assert.That(presentation.RuntimeOptions.ScreenFlashEnabled, Is.False);
        Assert.That(presentation.RuntimeOptions.ReducedShake, Is.False,
            "The user Screen Shake preference must not enter the debug reduced-shake/hit-stop path.");
        Assert.That(presentation.RuntimeOptions.ReducedFlash, Is.False,
            "The user Screen Flash preference must not enter the debug reduced-flash/hit-stop path.");

        service.ScreenShake = true;
        Assert.That(camera.ScreenShakeEnabled, Is.True);

        service.ReducedMotion = true;
        Assert.That(camera.ScreenShakeEnabled, Is.False,
            "Reduced Motion should suppress camera impulses without changing the stored Screen Shake choice.");
        Assert.That(service.ScreenShake, Is.True);
        Assert.That(presentation.RuntimeOptions.ReducedMotion, Is.True);
        Assert.That(presentation.RuntimeOptions.ScreenShakeEnabled, Is.False);

        service.ReducedMotion = false;
        Assert.That(camera.ScreenShakeEnabled, Is.True);
        Assert.That(presentation.RuntimeOptions.ScreenShakeEnabled, Is.True);
    }

    [Test]
    public void MenuAudioFeedback_UsesSharedSfxVolumeAtPlaybackTime()
    {
        UserSettingsService service = CreateService();
        service.SfxVolume = 0.25f;
        MethodInfo resolveVolume = typeof(MenuAudioFeedback).GetMethod(
            "ResolvePlaybackVolume",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(resolveVolume, Is.Not.Null);
        float resolved = (float)resolveVolume.Invoke(null, new object[] { 0.8f });

        Assert.That(resolved, Is.EqualTo(0.2f).Within(0.0001f));
    }

    private static UserSettingsService CreateService()
    {
        UserSettingsService service = new GameObject("User Settings Service").AddComponent<UserSettingsService>();
        InvokePrivate(service, "Awake");
        return service;
    }

    private static void InvokePrivate(Component component, string methodName)
    {
        MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Expected {component.GetType().Name}.{methodName}().");
        method.Invoke(component, null);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected {instance.GetType().Name}.{fieldName}.");
        field.SetValue(instance, value);
    }
}
