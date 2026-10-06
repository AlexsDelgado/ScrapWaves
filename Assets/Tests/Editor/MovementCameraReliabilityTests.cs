using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public sealed class MovementCameraReliabilityTests
{
    private readonly List<Object> _cleanup = new();
    private ThirdPersonCamera _camera;
    private Transform _target;
    private Keyboard _keyboard;

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _target = Go("Follow target").transform;
        _target.position = Vector3.right * 10f;
        GameObject camera = Go("Camera");
        camera.AddComponent<Camera>();
        _camera = camera.AddComponent<ThirdPersonCamera>();
        Set(_camera, "_followTarget", _target);
        Set(_camera, "_lookBlockedByUi", true);
    }

    [TearDown]
    public void Cleanup()
    {
        if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void Movement_UsesCurrentUnshakenLookBeforeLateUpdate()
    {
        GameObject actor = Go("Player");
        PlayerMovement movement = actor.AddComponent<PlayerMovement>();
        Set(movement, "_cameraTransform", _camera.transform);
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
        InputSystem.Update();
        Set(_camera, "_yaw", 90f);
        Call(_camera, "Update");
        Assert.That(_camera.transform.forward, Is.EqualTo(Vector3.forward));
        Call(movement, "ReadInput");
        Assert.That(Vector3.Angle(movement.CurrentMoveDirectionWorld, Vector3.right), Is.LessThan(.01f));
        _camera.AddPresentationImpulse(Vector3.zero, new Vector3(0f, 5f, 3f));
        Call(_camera, "LateUpdate");
        Call(movement, "ReadInput");
        Assert.That(Vector3.Angle(movement.CurrentMoveDirectionWorld, Vector3.right), Is.LessThan(.01f));
        Assert.That(typeof(ThirdPersonCamera).GetCustomAttribute<DefaultExecutionOrder>().order,
            Is.LessThan(typeof(PlayerMovement).GetCustomAttribute<DefaultExecutionOrder>().order));
    }

    [Test]
    public void Obstruction_ImmediateInwardAndBoundedOutward()
    {
        Assert.That((float)Call(_camera, "SmoothObstructionDistance", .5f, .02f), Is.EqualTo(.5f));
        Assert.That((float)Call(_camera, "SmoothObstructionDistance", 3.5f, .02f), Is.EqualTo(.66f).Within(.0001f));
        Assert.That((float)Call(_camera, "SmoothObstructionDistance", .2f, .02f), Is.EqualTo(.2f));
    }

    [Test]
    public void Camera_IgnoresDefaultLayerEnemyButStopsAtEnvironment()
    {
        GameObject enemy = Box("Enemy", new Vector3(0f, 0f, -1f), Vector3.one * .5f);
        enemy.AddComponent<EnemyHealth>();
        Box("Wall", new Vector3(0f, 0f, -2f), Vector3.one);
        Physics.SyncTransforms();
        Vector3 result = (Vector3)Call(_camera, "ResolveCameraPosition", Vector3.zero, Vector3.back * 3.5f);
        Assert.That(result.z, Is.EqualTo(-1.13f).Within(.01f));
        Assert.That((bool)Call(_camera, "HasCameraOverlap", result), Is.False, "Ignored enemies may overlap the camera, environment may not.");
    }

    [Test]
    public void Camera_SaturatedCastStillFindsNearestWall()
    {
        for (int i = 0; i < 30; i++)
            Box("Packed wall", new Vector3(0f, 0f, -.9f - i * .075f), new Vector3(2f, 2f, .05f));
        Physics.SyncTransforms();
        Vector3 result = (Vector3)Call(_camera, "ResolveCameraPosition", Vector3.zero, Vector3.back * 3.5f);
        Assert.That(result.z, Is.EqualTo(-.505f).Within(.01f));
    }

    [Test]
    public void Camera_OverlappingAnchorFindsClearPositionAndShakeCannotEnterWall()
    {
        Box("Anchor wall", Vector3.zero, Vector3.one);
        Physics.SyncTransforms();
        Vector3 result = (Vector3)Call(_camera, "ResolveCameraPosition", Vector3.zero, Vector3.back * 3.5f);
        Assert.That(Physics.CheckSphere(result, .249f, 129, QueryTriggerInteraction.Ignore), Is.False);
        Set(_camera, "_gameplayPosition", result);
        Vector3 shaken = (Vector3)Call(_camera, "SafePresentationPosition", Vector3.zero);
        Assert.That(Physics.CheckSphere(shaken, .249f, 129, QueryTriggerInteraction.Ignore), Is.False);
    }

    private GameObject Go(string name) { GameObject go = new(name); _cleanup.Add(go); return go; }
    private GameObject Box(string name, Vector3 position, Vector3 scale)
    {
        GameObject go = Go(name); go.AddComponent<BoxCollider>();
        go.transform.position = position; go.transform.localScale = scale; return go;
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
