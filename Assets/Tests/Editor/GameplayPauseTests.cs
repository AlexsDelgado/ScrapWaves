using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public class GameplayPauseTests
{
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Time.timeScale = 1f;
        GameplayPause.Reset();
        RunSessionStats.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        GameplayPause.Reset();
    }

    [Test]
    public void SetHeld_PushesAndPopsWithoutDoubleLock()
    {
        bool held = false;
        GameplayPause.SetHeld(ref held, true);
        GameplayPause.SetHeld(ref held, true);

        Assert.That(held, Is.True);
        Assert.That(GameplayPause.IsUiPaused, Is.True);
        Assert.That(GameplayPause.LockCount, Is.EqualTo(1));

        GameplayPause.SetHeld(ref held, false);
        GameplayPause.SetHeld(ref held, false);

        Assert.That(held, Is.False);
        Assert.That(GameplayPause.IsUiPaused, Is.False);
        Assert.That(GameplayPause.LockCount, Is.Zero);
    }

    [Test]
    public void PauseMenu_ShowAndResume_HoldsUiPauseLock()
    {
        GameObject root = new("PauseMenuRoot");
        try
        {
            PauseMenuUI pauseMenu = root.AddComponent<PauseMenuUI>();
            InvokePrivate(pauseMenu, "Awake");
            InvokePrivate(pauseMenu, "ShowPause");

            Assert.That(GameplayPause.IsUiPaused, Is.True);
            Assert.That(Time.timeScale, Is.Zero);

            InvokePrivate(pauseMenu, "Resume");

            Assert.That(GameplayPause.IsUiPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PlayerMovement_UpdateWhileUiPaused_DoesNotConsumeDash()
    {
        GameObject player = new("Paused movement");
        try
        {
            player.AddComponent<Rigidbody>();
            player.AddComponent<PlayerStats>();
            PlayerMovement movement = player.AddComponent<PlayerMovement>();
            InvokePrivate(movement, "Awake");
            SetPrivateField(movement, "_currentDashCharges", 2);
            SetPrivateField(movement, "_dashPressed", true);
            SetPrivateField(movement, "_moveDirectionWorld", Vector3.forward);

            GameplayPause.Push();
            InvokePrivate(movement, "Update");

            Assert.That(movement.CurrentDashCharges, Is.EqualTo(2));
            Assert.That(ReadPrivate<bool>(movement, "_dashPressed"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void PlayerMovement_UpdateWhenTimeScaleIsZero_DoesNotConsumeDash()
    {
        GameObject player = new("Hitstop movement");
        try
        {
            player.AddComponent<Rigidbody>();
            player.AddComponent<PlayerStats>();
            PlayerMovement movement = player.AddComponent<PlayerMovement>();
            InvokePrivate(movement, "Awake");
            SetPrivateField(movement, "_currentDashCharges", 2);
            SetPrivateField(movement, "_dashPressed", true);
            SetPrivateField(movement, "_moveDirectionWorld", Vector3.forward);

            Time.timeScale = 0f;
            InvokePrivate(movement, "Update");

            Assert.That(movement.CurrentDashCharges, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void HeatManager_DecayStopsWhileUiPausedAndResumesAfter()
    {
        GameObject go = new("Heat");
        try
        {
            HeatManager heat = go.AddComponent<HeatManager>();
            heat.BeginPostOverheatCooldown(50f);
            Assert.That(heat.CurrentHeat, Is.EqualTo(50f).Within(0.001f));

            GameplayPause.Push();
            InvokePrivate(heat, "TickPostOverheatDecay", 1f);
            Assert.That(heat.CurrentHeat, Is.EqualTo(50f).Within(0.001f));

            GameplayPause.Pop();
            InvokePrivate(heat, "TickPostOverheatDecay", 1f);
            Assert.That(heat.CurrentHeat, Is.LessThan(50f));
            Assert.That(heat.CurrentHeat, Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void RunSessionStats_ElapsedUsesScaledTime()
    {
        RunSessionStats.Reset();
        Assert.That(RunSessionStats.ElapsedSeconds, Is.EqualTo(0f).Within(0.0001f));

        Time.timeScale = 0f;
        RunSessionStats.Reset();
        Assert.That(RunSessionStats.ElapsedSeconds, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(RunSessionStats.ElapsedSeconds, Is.EqualTo(Time.time - Time.time).Within(0.0001f));
    }

    [Test]
    public void WeaponPresentationController_UpdateWhileUiPaused_DoesNotThrow()
    {
        GameObject go = new("Presentation");
        try
        {
            WeaponPresentationController presentation = go.AddComponent<WeaponPresentationController>();
            GameplayPause.Push();
            InvokePrivate(presentation, "Update");
            Assert.That(GameplayPause.IsUiPaused, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void LevelUpChoiceUI_ShowWithPause_HoldsUiPauseLock()
    {
        GameObject go = new("LevelUp");
        try
        {
            EnsureEventSystem();
            LevelUpChoiceUI ui = go.AddComponent<LevelUpChoiceUI>();
            ui.Show("Level up", new[] { new LevelUpChoiceOption("A") }, _ => { });

            Assert.That(ui.IsVisible, Is.True);
            Assert.That(GameplayPause.IsUiPaused, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    private static void InvokePrivate(object instance, string methodName, params object[] args)
    {
        MethodInfo method = null;
        foreach (MethodInfo candidate in instance.GetType().GetMethods(
                     BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (candidate.Name != methodName || candidate.GetParameters().Length != args.Length)
                continue;
            method = candidate;
            break;
        }

        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(instance, args);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(instance, value);
    }

    private static T ReadPrivate<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        return (T)field.GetValue(instance);
    }
}
