using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class GameplayAimTests
{
    private readonly List<Object> _objects = new();

    [TearDown]
    public void Cleanup()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
    }

    [TestCase(WeaponType.AutomaticCannon, 1, WeaponUpgradePath.None, true)]
    [TestCase(WeaponType.AutomaticCannon, 5, WeaponUpgradePath.PathB, true)]
    [TestCase(WeaponType.AutomaticCannon, 6, WeaponUpgradePath.PathB, false)]
    [TestCase(WeaponType.AutomaticCannon, 10, WeaponUpgradePath.PathA, true)]
    [TestCase(WeaponType.RocketLauncher, 10, WeaponUpgradePath.PathB, true)]
    [TestCase(WeaponType.Mortar, 1, WeaponUpgradePath.None, false)]
    public void AimAssistanceOnlyExemptsHeadHunterPrecision(WeaponType type, int level, WeaponUpgradePath path, bool assisted)
    {
        WeaponInstance weapon = Weapon(type);
        weapon.Level = level;
        weapon.SelectedPath = path;
        Assert.That(WeaponAimPolicy.PreferDamageableAimPoint(weapon), Is.EqualTo(assisted));
    }

    [Test]
    public void HeadHunterKeepsCrosshairHitInsteadOfSnappingToBodyCenter()
    {
        Camera camera = CameraAt(new Vector3(0f, 1.8f, 0f));
        ReticleAimProvider provider = Provider(camera);
        GameObject enemy = Go("Offset weak point target");
        enemy.transform.position = new Vector3(0f, 1f, 10f);
        enemy.AddComponent<BoxCollider>().size = Vector3.one * 2f;
        enemy.AddComponent<AimTestDamageable>();
        Physics.SyncTransforms();
        WeaponInstance weapon = Weapon(WeaponType.AutomaticCannon);
        Vector3 muzzle = new(0.7f, 1.2f, 0f);
        AimSolution assisted = provider.ResolveWeaponAim(muzzle, weapon);
        weapon.Level = 6;
        weapon.SelectedPath = WeaponUpgradePath.PathB;
        AimSolution precision = provider.ResolveWeaponAim(muzzle, weapon);
        Assert.That(assisted.TargetPoint.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(precision.TargetPoint.y, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(precision.TargetPoint.z, Is.EqualTo(9f).Within(0.001f));
        Assert.That(precision.FrameNumber, Is.EqualTo(Time.frameCount));
    }

    [TestCase(0f, 2.4f)]
    [TestCase(2.4f, 2.4f)]
    [TestCase(5f, 2.4f)]
    [TestCase(0f, 20f)]
    public void NoHitFallbackNeverAimsBackward(float lateralOffset, float range)
    {
        Camera camera = CameraAt(new Vector3(lateralOffset, 0f, -3.5f));
        var provider = Provider(camera);
        Set(provider, "_aimMask", (LayerMask)0);
        Assert.That(provider.TryGetAimSolution(Vector3.zero, range, false, out var aim), Is.True);
        Assert.That(Vector3.Dot(aim.Direction, Vector3.forward), Is.GreaterThanOrEqualTo(-0.0001f));
        Assert.That(Vector3.Distance(aim.Origin, aim.TargetPoint), Is.EqualTo(range).Within(0.001f));
        if (lateralOffset > range)
            Assert.That(Vector3.Distance(aim.TargetPoint, Vector3.forward * range), Is.LessThan(0.001f));
    }

    [Test]
    public void ProviderIgnoresPlayerColliders()
    {
        Camera camera = CameraAt(new Vector3(0f, 0f, -5f));
        var provider = Provider(camera);
        provider.gameObject.AddComponent<BoxCollider>();
        GameObject child = Go("Player child");
        child.transform.SetParent(provider.transform, false);
        child.AddComponent<BoxCollider>();
        Set(provider, "_ignoredRoot", provider.transform);
        Physics.SyncTransforms();
        Assert.That(provider.TryGetAimSolution(Vector3.zero, 10f, false, out var aim), Is.True);
        Assert.That(aim.TargetPoint.z, Is.EqualTo(10f).Within(0.001f));
    }

    [Test]
    public void MissingCameraFallbackIsValidAndUsesOwnerForward()
    {
        Assert.That(ReticleAimProvider.TryGetGameplayRay(null, out _), Is.False);
        AimSolution aim = ReticleAimProvider.CreateFallback(Vector3.one, 4f, null, Vector3.right);
        Assert.That(aim.Direction, Is.EqualTo(Vector3.right));
        Assert.That(aim.TargetPoint, Is.EqualTo(Vector3.one + Vector3.right * 4f));
    }

    [Test]
    public void GameplayRayAndResolvedShotIgnorePresentationImpulses()
    {
        Camera camera = CameraAt(Vector3.zero);
        ThirdPersonCamera controller = Controller(camera);
        var provider = Provider(camera);
        Set(provider, "_aimMask", (LayerMask)0);
        Invoke(controller, "Update");
        var before = provider.ResolveWeaponAim(Vector3.zero, Weapon(WeaponType.AutomaticCannon));
        Ray ray = controller.GameplayCenterRay;
        controller.AddPresentationImpulse(new Vector3(0.2f, 0.1f, 0f), new Vector3(3f, 2f, 1f), 4f);
        Invoke(controller, "LateUpdate");
        var after = provider.ResolveWeaponAim(Vector3.zero, Weapon(WeaponType.AutomaticCannon));
        Assert.That(Vector3.Distance(before.Direction, after.Direction), Is.LessThan(0.00001f));
        Assert.That(controller.GameplayCenterRay.origin, Is.EqualTo(ray.origin));
        Assert.That(controller.GameplayCenterRay.direction, Is.EqualTo(ray.direction));
        Assert.That(Vector3.Angle(camera.transform.forward, ray.direction), Is.GreaterThan(0.1f));
        controller.CameraFeedbackScale = 0f;
        Invoke(controller, "LateUpdate");
        Assert.That(controller.GameplayCenterRay.direction, Is.EqualTo(ray.direction));
    }

    [Test]
    public void CurrentMouseLookIsAvailableToWeaponsBeforeLateUpdate()
    {
        Camera camera = CameraAt(Vector3.zero);
        var controller = Controller(camera);
        Set(controller, "_lookBlockedByUi", false);
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        try
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100f, 0f) });
            InputSystem.Update();
            Invoke(controller, "Update");
            Assert.That(Vector3.Angle(Vector3.forward, controller.GameplayForward), Is.EqualTo(12f).Within(0.01f));
            Assert.That(camera.transform.forward, Is.EqualTo(Vector3.forward), "Presentation should wait for LateUpdate.");
            var provider = Provider(camera);
            Set(provider, "_aimMask", (LayerMask)0);
            var aim = provider.ResolveWeaponAim(Vector3.zero, Weapon(WeaponType.AutomaticCannon));
            Invoke(controller, "LateUpdate");
            Assert.That(Vector3.Angle(camera.transform.forward, controller.GameplayForward), Is.LessThan(0.001f));
            Assert.That(aim.FrameNumber, Is.EqualTo(Time.frameCount));
            Assert.That(Vector3.Cross(aim.TargetPoint - controller.GameplayCenterRay.origin, controller.GameplayForward).magnitude,
                Is.LessThan(0.001f));
        }
        finally { InputSystem.RemoveDevice(mouse); }
    }

    [TestCase(16f / 9f, 1f)]
    [TestCase(4f / 3f, 0.75f)]
    [TestCase(21f / 9f, 1.5f)]
    public void ProjectedReticleTracksAimInShakenView(float aspect, float canvasScale)
    {
        Camera camera = CameraAt(Vector3.zero);
        camera.aspect = aspect;
        var controller = Controller(camera);
        Invoke(controller, "Update");
        var provider = Provider(camera);
        Set(provider, "_aimMask", (LayerMask)0);
        AimSolution aim = provider.ResolveWeaponAim(Vector3.zero, Weapon(WeaponType.AutomaticCannon));
        controller.AddPresentationImpulse(new Vector3(0.2f, 0.1f, 0f), new Vector3(3f, 2f, 0f), 4f);
        Invoke(controller, "LateUpdate");
        var canvas = Go("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.scaleFactor = canvasScale;
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)canvas.transform;
        Assert.That(ReticlePresentationLogic.TryProjectAimPoint(camera, rect, aim.TargetPoint, out var point), Is.True);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(point));
        Vector2 expected = camera.WorldToScreenPoint(aim.TargetPoint);
        Assert.That(Vector2.Distance(screen, expected), Is.LessThan(0.01f));
        Assert.That(ReticlePresentationLogic.TryProjectAimPoint(camera, rect, camera.transform.position - camera.transform.forward, out _), Is.False);
        Assert.That(ReticlePresentationLogic.TryProjectAimPoint(camera, rect, camera.transform.position + camera.transform.right * 100f + camera.transform.forward, out _), Is.False);
    }

    [Test]
    public void ManagersExposeSameAimAndRefreshWhenSwitchingWeapons()
    {
        Camera camera = CameraAt(new Vector3(0.6f, 1.6f, -3.5f));
        var provider = Provider(camera);
        Set(provider, "_aimMask", (LayerMask)0);
        var manager = provider.gameObject.AddComponent<WeaponManager>();
        Set(manager, "_reticleAimProvider", provider);
        var sandboxObject = Go("Inactive sandbox");
        sandboxObject.SetActive(false);
        var sandbox = sandboxObject.AddComponent<WeaponTestingSandboxManager>();
        Set(sandbox, "_aimProvider", provider);
        Set(sandbox, "<PlayerTransform>k__BackingField", provider.transform);
        var instances = (WeaponInstance[])Get(sandbox, "_instances");
        var behaviours = (IWeaponBehaviour[])Get(sandbox, "_behaviours");
        var equipped = (List<IWeaponBehaviour>)manager.GetEquippedWeapons();
        for (int i = 0; i < 2; i++)
        {
            instances[i] = Weapon(i == 0 ? WeaponType.AutomaticCannon : WeaponType.RotatingBlade);
            instances[i].Data.BaseRange = i == 0 ? 25f : 2.4f;
            var weapon = new BasicProjectileWeapon(null, null, provider.transform);
            weapon.Setup(instances[i], provider.transform, null, null);
            behaviours[i] = weapon;
            equipped.Add(weapon);
        }
        Invoke(manager, "GetAimDirection");
        Invoke(sandbox, "ResolveAimDirection");
        Assert.That(manager.CurrentAimSolution.TargetPoint, Is.EqualTo(sandbox.CurrentAimSolution.TargetPoint));
        Invoke(manager, "StartManualMode", 1);
        sandbox.SelectManualSlot(1);
        Assert.That(manager.CurrentAimSolution.TargetPoint, Is.EqualTo(sandbox.CurrentAimSolution.TargetPoint));
        Assert.That(manager.CurrentAimSolution.Direction.z, Is.GreaterThan(0f));
        Assert.That(manager.CurrentAimSolution.FrameNumber, Is.EqualTo(Time.frameCount));
    }

    private ThirdPersonCamera Controller(Camera camera)
    {
        var controller = camera.gameObject.AddComponent<ThirdPersonCamera>();
        controller.SetFollowTarget(Go("Follow target").transform);
        Set(controller, "_avoidCameraClipping", false);
        Set(controller, "_lookBlockedByUi", true);
        Set(controller, "_presentationImpulseDecay", 0f);
        return controller;
    }
    private Camera CameraAt(Vector3 position)
    {
        var camera = Go("Aim camera").AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.identity;
        return camera;
    }
    private ReticleAimProvider Provider(Camera camera)
    {
        var provider = Go("Aim provider").AddComponent<ReticleAimProvider>();
        Set(provider, "_aimCamera", camera);
        return provider;
    }
    private WeaponInstance Weapon(WeaponType type)
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        _objects.Add(data);
        data.WeaponType = type;
        data.BaseRange = 25f;
        return new WeaponInstance { Data = data };
    }
    private GameObject Go(string name, params Type[] components)
    {
        var obj = new GameObject(name, components);
        _objects.Add(obj);
        return obj;
    }
    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Get(object target, string field) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private sealed class AimTestDamageable : MonoBehaviour, IDamageable
    {
        public bool ApplyDamage(int amount) => true;
    }
}
