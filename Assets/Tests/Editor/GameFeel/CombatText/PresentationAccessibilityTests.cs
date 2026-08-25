using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PresentationAccessibilityTests
{
    [SetUp]
    public void SetUp()
    {
        PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
        Time.timeScale = 1f;
    }

    [Test]
    public void Settings_SanitizeInvalidModeAndScale()
    {
        var settings = new PresentationAccessibilitySettings
        {
            ReducedMotion = true,
            CombatText = (CombatTextMode)99,
            CombatTextScale = float.NaN
        };

        settings.Sanitize();

        Assert.That(settings.ReducedMotion, Is.True);
        Assert.That(settings.CombatText, Is.EqualTo(CombatTextMode.Full));
        Assert.That(settings.CombatTextScale, Is.EqualTo(1f));

        settings.CombatTextScale = -10f;
        settings.Sanitize();
        Assert.That(settings.CombatTextScale, Is.EqualTo(PresentationAccessibilitySettings.MinimumCombatTextScale));

        settings.CombatTextScale = 10f;
        settings.Sanitize();
        Assert.That(settings.CombatTextScale, Is.EqualTo(PresentationAccessibilitySettings.MaximumCombatTextScale));
    }

    [Test]
    public void OlderSaveJson_MissingAccessibilityUsesSafeDefaultsWithoutLosingProgress()
    {
        const string legacyJson =
            "{\"Version\":1,\"Scrap\":37,\"TotalEnemiesKilled\":91,\"UnlockedIds\":[\"weapon.cannon\"]}";

        SaveData data = JsonUtility.FromJson<SaveData>(legacyJson);
        data.Sanitize();
        PresentationAccessibilityState state = data.PresentationAccessibility.ToState();

        Assert.That(data.Version, Is.EqualTo(SaveData.CurrentVersion));
        Assert.That(data.Scrap, Is.EqualTo(37));
        Assert.That(data.TotalEnemiesKilled, Is.EqualTo(91));
        CollectionAssert.Contains(data.UnlockedIds, "weapon.cannon");
        Assert.That(state, Is.EqualTo(PresentationAccessibilityState.Default));
    }

    [Test]
    public void Runtime_NotifiesOncePerSanitizedStateChange()
    {
        int notifications = 0;
        PresentationAccessibilityState observed = default;
        Action<PresentationAccessibilityState> handler = state =>
        {
            notifications++;
            observed = state;
        };

        PresentationAccessibilityRuntime.Changed += handler;
        try
        {
            PresentationAccessibilityState changed = PresentationAccessibilityState.Default
                .WithReducedMotion(true)
                .WithReducedShake(true)
                .WithCombatText(CombatTextMode.ImportantOnly)
                .WithCombatTextScale(0.8f);

            PresentationAccessibilityRuntime.Apply(changed);
            PresentationAccessibilityRuntime.Apply(changed);

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(changed));
            Assert.That(PresentationAccessibilityRuntime.Current, Is.EqualTo(changed));
        }
        finally
        {
            PresentationAccessibilityRuntime.Changed -= handler;
        }
    }

    [Test]
    public void SaveManager_SettingsWritePreservesProgressAndResetPreservesSettings()
    {
        string path = Path.Combine(
            Application.temporaryCachePath,
            $"scrapwaves_accessibility_test_{Guid.NewGuid():N}.json");
        GameObject root = new("Inactive SaveManager Test");
        root.SetActive(false);
        SaveManager manager = root.AddComponent<SaveManager>();
        SetPrivateField(manager, "_path", path);

        try
        {
            manager.AddScrap(73);
            PresentationAccessibilityState expected = new(
                reducedMotion: true,
                reducedShake: true,
                reducedFlash: true,
                combatText: CombatTextMode.ImportantOnly,
                combatTextScale: 1.2f);

            manager.SetPresentationAccessibility(expected);

            Assert.That(manager.Scrap, Is.EqualTo(73), "Changing settings must not replace progression.");
            Assert.That(manager.PresentationAccessibility, Is.EqualTo(expected));
            Assert.That(File.Exists(path), Is.True);

            SaveData persisted = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            persisted.Sanitize();
            Assert.That(persisted.Scrap, Is.EqualTo(73));
            Assert.That(persisted.PresentationAccessibility.ToState(), Is.EqualTo(expected));

            manager.ResetProgress();

            Assert.That(manager.Scrap, Is.Zero);
            Assert.That(manager.PresentationAccessibility, Is.EqualTo(expected));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    public void PauseSettings_ExposeAllPresentationAccessibilityControls()
    {
        GameObject root = new("Pause Accessibility Test");
        try
        {
            PauseMenuUI pauseMenu = root.AddComponent<PauseMenuUI>();
            MethodInfo awake = typeof(PauseMenuUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(pauseMenu, null);

            Transform panel = root.transform.Find("PauseRoot/SettingsPanel");
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.Find("Reduced Motion/Toggle")?.GetComponent<Toggle>(), Is.Not.Null);
            Assert.That(panel.Find("Reduced Shake/Toggle")?.GetComponent<Toggle>(), Is.Not.Null);
            Assert.That(panel.Find("Reduced Flash/Toggle")?.GetComponent<Toggle>(), Is.Not.Null);

            TMP_Dropdown mode = panel.Find("Combat Text/Dropdown")?.GetComponent<TMP_Dropdown>();
            Assert.That(mode, Is.Not.Null);
            CollectionAssert.AreEqual(
                new[] { "Off", "On" },
                mode.options.ConvertAll(option => option.text));

            Slider scale = panel.Find("Combat Text Scale/Slider")?.GetComponent<Slider>();
            Assert.That(scale, Is.Not.Null);
            Assert.That(scale.minValue, Is.EqualTo(0.75f));
            Assert.That(scale.maxValue, Is.EqualTo(1.25f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void GameFeelOptions_DefaultToGlobalPresentationDefaults()
    {
        var options = new GameFeelRuntimeOptions();

        Assert.That(options.ReducedMotion, Is.False);
        Assert.That(options.ReducedShake, Is.False);
        Assert.That(options.ReducedFlash, Is.False);
        Assert.That(options.CombatText, Is.EqualTo(CombatTextMode.Full));
        Assert.That(options.CombatTextScale, Is.EqualTo(1f));
    }

    [Test]
    public void SandboxController_LocalOverrideIsVisibleAndCanResetToPersistedState()
    {
        PresentationAccessibilityState persisted = new(
            reducedMotion: false,
            reducedShake: true,
            reducedFlash: false,
            combatText: CombatTextMode.ImportantOnly,
            combatTextScale: 0.8f);
        PresentationAccessibilityRuntime.Apply(persisted);
        GameObject root = new("Presentation Override Test");
        try
        {
            WeaponPresentationController controller = root.AddComponent<WeaponPresentationController>();
            // EditMode does not dispatch MonoBehaviour.OnEnable for this component;
            // initialize through the same public path used by the sandbox reset control.
            controller.ApplyPersistedAccessibility();
            Assert.That(controller.HasLocalAccessibilityOverride, Is.False);
            Assert.That(controller.AppliedAccessibilityState, Is.EqualTo(persisted));

            controller.SetReducedMotion(true);
            controller.SetCombatTextMode(CombatTextMode.Full);
            controller.SetCombatTextScale(1.25f);
            Assert.That(controller.HasLocalAccessibilityOverride, Is.True);
            Assert.That(controller.AppliedAccessibilityState.ReducedMotion, Is.True);
            Assert.That(controller.AppliedAccessibilityState.CombatText, Is.EqualTo(CombatTextMode.Full));

            controller.ApplyPersistedAccessibility();
            Assert.That(controller.HasLocalAccessibilityOverride, Is.False);
            Assert.That(controller.AppliedAccessibilityState, Is.EqualTo(persisted));

            controller.SetCombatTextCompactFormatting(false);
            Assert.That(controller.CombatTextCompactFormatting, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SandboxController_UserFeedbackPreferencesPreserveLocalReducedMotionOverride()
    {
        PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
        GameObject root = new("Presentation User Preferences Override Test");
        try
        {
            WeaponPresentationController controller = root.AddComponent<WeaponPresentationController>();
            controller.ApplyPersistedAccessibility();
            controller.SetReducedMotion(true);

            controller.ApplyUserFeedbackPreferences(
                reducedMotion: false,
                screenShake: true,
                screenFlash: false);

            Assert.That(controller.HasLocalAccessibilityOverride, Is.True);
            Assert.That(controller.AppliedAccessibilityState.ReducedMotion, Is.True,
                "User preferences must not replace an active sandbox-local Reduced Motion override.");
            Assert.That(controller.RuntimeOptions.ScreenShakeEnabled, Is.False,
                "Screen shake must use the effective local Reduced Motion value.");
            Assert.That(controller.RuntimeOptions.ScreenFlashEnabled, Is.False,
                "Independent user feedback preferences must still be applied.");
            Assert.That(EnemyReactionRuntime.ReducedMotion, Is.True);
            Assert.That(EnemyReactionRuntime.ScreenFlashEnabled, Is.False);
        }
        finally
        {
            EnemyReactionRuntime.ApplyUserPreferences(false, true);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }
}
