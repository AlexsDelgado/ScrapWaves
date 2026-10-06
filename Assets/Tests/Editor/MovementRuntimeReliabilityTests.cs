using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class MovementRuntimeReliabilityTests
{
    [DefaultExecutionOrder(-300)]
    private sealed class RuntimeInputPump : MonoBehaviour
    {
        public Keyboard Keyboard;
        public Key[] Keys = Array.Empty<Key>();
        public int FixedTicks;
        public int Updates;
        public bool WAtUpdate;
        private void Update()
        {
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Keys));
            InputSystem.Update();
            Keyboard.MakeCurrent();
            Updates++;
            WAtUpdate = Keyboard.wKey.isPressed;
        }
        private void FixedUpdate() => FixedTicks++;
    }
    [UnityTest]
    public IEnumerator LivePlayerLoop_StopsPreciselyAndBuffedBackDashEscapesBackward()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        InputSettings.BackgroundBehavior oldBackground = InputSystem.settings.backgroundBehavior;
        InputSettings.EditorInputBehaviorInPlayMode oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        bool oldRunInBackground = Application.runInBackground;
        InputSettings.UpdateMode oldUpdateMode = InputSystem.settings.updateMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        GameObject actor = null, floor = null, camera = null;
        try
        {
            Time.timeScale = 1f; GameplayPause.Reset();
            actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"));
            foreach (MonoBehaviour script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            actor.transform.localScale = Vector3.one * 1.2f;
            actor.transform.position = Vector3.up * 1.2f;
            PlayerMovement movement = actor.GetComponent<PlayerMovement>();
            Rigidbody body = actor.GetComponent<Rigidbody>();
            PlayerStats stats = actor.GetComponent<PlayerStats>();
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Vector3.down * .5f;
            floor.transform.localScale = new Vector3(100f, 1f, 100f);
            camera = new GameObject("Runtime camera", typeof(Camera), typeof(ThirdPersonCamera));
            ThirdPersonCamera orbit = camera.GetComponent<ThirdPersonCamera>();
            orbit.SetFollowTarget(actor.transform); orbit.SetLookBlockedByUi(true);
            typeof(PlayerMovement).GetField("_cameraTransform", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(movement, camera.transform);
            movement.enabled = true;
            Physics.SyncTransforms();
            RuntimeInputPump pump = camera.AddComponent<RuntimeInputPump>();
            pump.Keyboard = keyboard; pump.Keys = new[] { Key.W };
            yield return WaitForTicks(pump, 15);
            TestContext.WriteLine($"runtime playing={Application.isPlaying}; enabled={movement.enabled}; WAtUpdate={pump.WAtUpdate}; stat={stats.GetMoveSpeed()}; simulation={Physics.simulationMode}; pause={GameplayPause.LockCount}; intent={movement.CurrentMoveDirectionWorld}; velocity={body.linearVelocity}; ticks={pump.FixedTicks}; updates={pump.Updates}");
            Assert.That(pump.FixedTicks, Is.GreaterThanOrEqualTo(10));
            Assert.That(movement.PlanarSpeed, Is.EqualTo(6.8f).Within(.02f));
            Assert.That(movement.IsGroundedOnSurface, Is.True);
            Vector3 releasePosition = body.position;
            pump.Keys = Array.Empty<Key>();
            yield return WaitForTicks(pump, 15);
            float stopDistance = Vector3.Distance(Vector3.ProjectOnPlane(body.position, Vector3.up), Vector3.ProjectOnPlane(releasePosition, Vector3.up));
            Assert.That(movement.PlanarSpeed, Is.LessThan(.01f));
            Assert.That(stopDistance, Is.InRange(.45f, .9f));
            TestContext.WriteLine($"live stop distance={stopDistance:F4}; step={Time.fixedDeltaTime:F4}; interpolation={body.interpolation}");
            stats.AddModifier(new StatModifier(StatType.MovementSpeed, 20.4f, StatUpgradeSource.TemporaryEffect, this));
            body.linearVelocity = Vector3.forward * 27.2f;
            pump.Keys = new[] { Key.S, Key.LeftShift };
            yield return WaitForTicks(pump, 4);
            Assert.That(movement.IsDashing, Is.True);
            Assert.That(body.linearVelocity.z, Is.EqualTo(-37.2f).Within(.02f));
            Assert.That(Mathf.Abs(body.linearVelocity.x), Is.LessThan(.01f));
            Assert.That(movement.CurrentDashCharges, Is.Zero);
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            InputSystem.settings.updateMode = oldUpdateMode;
            Application.runInBackground = oldRunInBackground;
            if (actor != null) Object.Destroy(actor);
            if (floor != null) Object.Destroy(floor);
            if (camera != null) Object.Destroy(camera);
            Time.timeScale = 1f; GameplayPause.Reset();
        }
        yield return new ExitPlayMode();
    }

    private static IEnumerator WaitForTicks(RuntimeInputPump pump, int ticks)
    {
        int target = pump.FixedTicks + ticks;
        float deadline = Time.realtimeSinceStartup + 5f;
        // EditMode UnityTests interpret yield instructions differently from a gameplay coroutine.
        // Wait for measured native FixedUpdate callbacks, not an assumed number of test yields.
        while (pump.FixedTicks < target && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(pump.FixedTicks, Is.GreaterThanOrEqualTo(target), "Native physics loop timed out.");
    }

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) { Time.timeScale = 1f; yield return new ExitPlayMode(); }
    }
}
