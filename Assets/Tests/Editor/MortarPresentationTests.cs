using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class MortarPresentationTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/MortarPresentation.asset";

    private sealed class RecordingPresentationSink : IWeaponPresentationSink
    {
        public readonly List<WeaponPresentationCue> Cues = new();

        public void Emit(in WeaponPresentationContext context) => Cues.Add(context.Cue);
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    [Test]
    public void ProductionMortar_ReferencesCompleteAuthoredProfile()
    {
        WeaponData production = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        WeaponData sandbox = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset");
        Assert.That(production, Is.Not.Null);
        Assert.That(sandbox, Is.Not.Null);
        Assert.That(production.PresentationProfile, Is.Not.Null);
        Assert.That(sandbox.PresentationProfile, Is.SameAs(production.PresentationProfile));
        Assert.That(production.PresentationProfile.WeaponType, Is.EqualTo(WeaponType.Mortar));
        Assert.That(production.PresentationProfile.HasDuplicateCues, Is.False);

        WeaponPresentationCue[] required =
        {
            WeaponPresentationCue.MortarAutomaticLaunch,
            WeaponPresentationCue.MortarManualLaunch,
            WeaponPresentationCue.MortarActiveBarrage,
            WeaponPresentationCue.MortarImpact,
            WeaponPresentationCue.MortarGrapeshotAirburst,
            WeaponPresentationCue.MortarGrapeshotImpact,
            WeaponPresentationCue.MortarMultiChargedImpact,
            WeaponPresentationCue.MortarMultiChargedRepeat
        };
        for (int i = 0; i < required.Length; i++)
            Assert.That(production.PresentationProfile.TryGetCueData(required[i], out _), Is.True, required[i].ToString());
    }

    [Test]
    public void AuthoredShell_ReplacesRuntimeSphereAndFullTrajectoryLine()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        GameObject prefab = profile.Mortar.ShellPrefab;
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<MortarShellImpact>(), Is.Not.Null);
        MortarShellVfx vfx = prefab.GetComponent<MortarShellVfx>();
        Assert.That(vfx, Is.Not.Null);
        Assert.That(vfx.ShellRendererCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(vfx.HasLandingIndicator, Is.True);
        Assert.That(profile.Mortar.LandingIndicatorPrefab, Is.Not.Null);
        Assert.That(profile.Mortar.LandingIndicatorPrefab.GetComponent<MortarLandingIndicatorVfx>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<LineRenderer>(), Is.Null, "Production shells must not expose the complete debug trajectory.");
        Assert.That(prefab.GetComponentsInChildren<TrailRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(profile.Mortar.ShellPrewarmCount, Is.GreaterThanOrEqualTo(16));
        Assert.That(profile.Mortar.ShellPoolCapacity, Is.GreaterThanOrEqualTo(64));
    }

    [Test]
    public void ManualLandingPrediction_CommunicatesRadiusTravelTimeAndUpgradePath()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(profile.Mortar.LandingIndicatorPrefab);
            MortarLandingIndicatorVfx marker = instance.GetComponent<MortarLandingIndicatorVfx>();
            marker.Configure(new Vector3(3f, 0.04f, 5f), Vector3.up, 2.9f, 0.55f, WeaponUpgradePath.PathB);
            Assert.That(marker.BlastRadius, Is.EqualTo(2.9f).Within(0.001f));
            Assert.That(marker.TravelTime, Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(marker.CurrentPath, Is.EqualTo(WeaponUpgradePath.PathB));
            Assert.That(Vector3.Distance(instance.transform.position, new Vector3(3f, 0.04f, 5f)), Is.LessThan(0.0001f));
        }
        finally
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void LandingIndicator_RemainsAtWorldImpactWhenShellMoves()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        MortarShellImpact shell = null;
        GameObject floor = null;
        try
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(2f, -0.05f, 1f);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Physics.SyncTransforms();
            Vector3 target = new(4f, 0f, 2f);
            shell = LaunchAuthored(profile, target, MortarUpgradePayload.None, null, default);
            MortarShellVfx vfx = shell.GetComponent<MortarShellVfx>();
            Assert.That(shell.HasPredictedPresentationCollision, Is.True);
            Assert.That(vfx.LandingIndicatorVisible, Is.True);
            Vector3 before = vfx.LandingIndicatorPosition;
            shell.transform.position = new Vector3(100f, 20f, -50f);
            Invoke(vfx, "LateUpdate");
            Assert.That(Vector3.Distance(vfx.LandingIndicatorPosition, before), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(before, shell.PredictedPresentationCollisionPoint + Vector3.up * 0.035f), Is.LessThan(0.001f));
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            if (floor != null)
                Object.DestroyImmediate(floor);
        }
    }

    [Test]
    public void LandingIndicator_StaysHiddenWhenTrajectoryHasNoMapCollision()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        MortarShellImpact shell = null;
        try
        {
            shell = LaunchAuthored(profile, new Vector3(4f, 8f, 2f), MortarUpgradePayload.None, null, default);
            MortarShellVfx vfx = shell.GetComponent<MortarShellVfx>();
            Assert.That(shell.HasPredictedPresentationCollision, Is.False);
            Assert.That(vfx.LandingIndicatorVisible, Is.False);
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
        }
    }

    [Test]
    public void ShellWithoutCollision_ReleasesWithoutLandingOrImpactEffect()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        WeaponInstance runtime = new() { Data = data, Level = 1 };
        WeaponFeedbackContext feedback = new(
            runtime,
            WeaponFeedbackMode.Manual,
            0.5f,
            Vector3.up * 2f,
            Vector3.forward,
            explosionRadius: 2f);
        RecordingPresentationSink sink = new();
        MortarShellImpact shell = null;
        try
        {
            shell = LaunchAuthored(profile, new Vector3(4f, 8f, 2f), MortarUpgradePayload.None, sink, feedback);
            SetField(shell, "_elapsed", 10f);
            Invoke(shell, "Update");

            Assert.That(shell.gameObject.activeSelf, Is.False);
            Assert.That(sink.Cues, Is.Empty);
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
        }
    }

    [Test]
    public void AuthoredShellPool_ReusesReleasedShellInstance()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        MortarShellImpact first = null;
        MortarShellImpact second = null;
        try
        {
            first = LaunchAuthored(profile, Vector3.forward * 3f, MortarUpgradePayload.None, null, default);
            Invoke(first, "Detonate", Vector3.forward * 3f);
            Assert.That(first.gameObject.activeSelf, Is.False);

            second = LaunchAuthored(profile, Vector3.forward * 5f, MortarUpgradePayload.None, null, default);
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.gameObject.activeSelf, Is.True);
        }
        finally
        {
            if (second != null)
                Object.DestroyImmediate(second.gameObject);
            else if (first != null)
                Object.DestroyImmediate(first.gameObject);
        }
    }

    [Test]
    public void MultiChargedShell_EmitsPresentationForEveryDamagePulse()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        WeaponInstance runtime = new() { Data = data, Level = 6, SelectedPath = WeaponUpgradePath.PathB };
        WeaponFeedbackContext feedback = new(runtime, WeaponFeedbackMode.Manual, 0.5f, Vector3.up * 2f, Vector3.down, explosionRadius: 2f);
        RecordingPresentationSink sink = new();
        MortarShellImpact shell = null;
        try
        {
            MortarUpgradePayload payload = new(false, 0, 0f, 0f, 3, 2f);
            shell = LaunchAuthored(profile, Vector3.zero, payload, sink, feedback);
            Invoke(shell, "Detonate", Vector3.zero);
            SetField(shell, "_repeatExplosionTimer", 0f);
            Invoke(shell, "Update");
            SetField(shell, "_repeatExplosionTimer", 0f);
            Invoke(shell, "Update");

            Assert.That(sink.Cues, Is.EqualTo(new[]
            {
                WeaponPresentationCue.MortarMultiChargedImpact,
                WeaponPresentationCue.MortarMultiChargedRepeat,
                WeaponPresentationCue.MortarMultiChargedRepeat
            }));
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
        }
    }

    [Test]
    public void ImpactPrefabs_SeparateBaseAirburstChildAndChargedGrammar()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        AssertCueStyle(profile, WeaponPresentationCue.MortarImpact, MortarCueStyle.Impact, 3);
        AssertCueStyle(profile, WeaponPresentationCue.MortarGrapeshotAirburst, MortarCueStyle.GrapeshotAirburst, 1);
        AssertCueStyle(profile, WeaponPresentationCue.MortarGrapeshotImpact, MortarCueStyle.GrapeshotImpact, 2);
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.MortarGrapeshotImpact, out WeaponPresentationCueData grapeshotImpact), Is.True);
        Assert.That(grapeshotImpact.EssentialGameplayCue, Is.True, "Every damaging submunition must retain a visible impact confirmation.");
        AssertCueStyle(profile, WeaponPresentationCue.MortarMultiChargedImpact, MortarCueStyle.MultiChargedImpact, 3);
        AssertCueStyle(profile, WeaponPresentationCue.MortarMultiChargedRepeat, MortarCueStyle.MultiChargedRepeat, 3);
    }

    [Test]
    public void ActiveBarrageWarning_KeepsRainButHasNoLargeCircularMesh()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.MortarActiveBarrage, out WeaponPresentationCueData data), Is.True);
        Assert.That(data.VfxPrefab, Is.Not.Null);
        MortarCueVfx vfx = data.VfxPrefab.GetComponent<MortarCueVfx>();
        Assert.That(vfx, Is.Not.Null);
        Assert.That(vfx.Style, Is.EqualTo(MortarCueStyle.BarrageWarning));
        Assert.That(vfx.RuntimeMeshLayerCount, Is.Zero);
        Assert.That(vfx.RuntimeParticleSystemCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void PresentationSurface_UsesLargeSlopePointAndNormal()
    {
        GameObject slope = null;
        try
        {
            slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.transform.position = Vector3.zero;
            slope.transform.localScale = new Vector3(20f, 0.5f, 20f);
            slope.transform.rotation = Quaternion.Euler(0f, 0f, 24f);
            Physics.SyncTransforms();

            Assert.That(Physics.Raycast(new Vector3(0f, 8f, 0f), Vector3.down, out RaycastHit hit, 20f), Is.True);
            RaycastHit[] supportHits = new RaycastHit[16];
            MortarPresentationSurface.Resolve(hit, 2f, null, supportHits, out Vector3 position, out Vector3 normal);

            Assert.That(Vector3.Distance(position, hit.point), Is.LessThan(0.0001f));
            Assert.That(Vector3.Dot(normal, hit.normal.normalized), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Angle(normal, Vector3.up), Is.GreaterThan(10f));
        }
        finally
        {
            if (slope != null)
                Object.DestroyImmediate(slope);
        }
    }

    [Test]
    public void PresentationSurface_AnchorsSmallStoneEffectsToSupportingGround()
    {
        GameObject floor = null;
        GameObject stone = null;
        try
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(20f, 0.2f, 20f);
            stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.transform.position = new Vector3(0f, 0.3f, 0f);
            stone.transform.localScale = Vector3.one * 0.6f;
            Physics.SyncTransforms();

            Assert.That(Physics.Raycast(new Vector3(0f, 3f, 0f), Vector3.down, out RaycastHit hit, 10f), Is.True);
            Assert.That(hit.collider, Is.EqualTo(stone.GetComponent<Collider>()));
            RaycastHit[] supportHits = new RaycastHit[16];
            MortarPresentationSurface.Resolve(hit, 2f, null, supportHits, out Vector3 position, out Vector3 normal);

            Assert.That(position.y, Is.EqualTo(0f).Within(0.002f));
            Assert.That(Vector3.Dot(normal, Vector3.up), Is.GreaterThan(0.999f));
        }
        finally
        {
            if (stone != null)
                Object.DestroyImmediate(stone);
            if (floor != null)
                Object.DestroyImmediate(floor);
        }
    }

    [Test]
    public void Profile_RoutesAutomaticManualAndActiveLaunches()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        AssertResolved(profile, WeaponFeedbackMode.Automatic, WeaponPresentationCue.MortarAutomaticLaunch);
        AssertResolved(profile, WeaponFeedbackMode.Manual, WeaponPresentationCue.MortarManualLaunch);
        AssertResolved(profile, WeaponFeedbackMode.Active, WeaponPresentationCue.MortarActiveBarrage);
    }

    [Test]
    public void ActiveBarrageWarning_RequiresPredictedMapCollision()
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        GameObject owner = new("Mortar Active Prediction Owner");
        GameObject floor = null;
        try
        {
            MortarWeapon weapon = new(null, null, owner.transform);
            WeaponInstance runtime = new() { Data = data, Level = 1, State = WeaponState.Manual };
            weapon.Setup(runtime, owner.transform, null, null);
            MethodInfo method = typeof(MortarWeapon).GetMethod(
                "TryGetActiveBarrageCollision",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] noMapArguments = { new Vector3(0f, 8f, 4f), data.Mortar, null };
            Assert.That((bool)method.Invoke(weapon, noMapArguments), Is.False);

            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.05f, 4f);
            floor.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Physics.SyncTransforms();
            object[] mapArguments = { new Vector3(0f, 8f, 4f), data.Mortar, null };
            Assert.That((bool)method.Invoke(weapon, mapArguments), Is.True);
            RaycastHit hit = (RaycastHit)mapArguments[2];
            Assert.That(hit.collider, Is.EqualTo(floor.GetComponent<Collider>()));
        }
        finally
        {
            if (floor != null)
                Object.DestroyImmediate(floor);
            Object.DestroyImmediate(owner);
        }
    }

    private static MortarShellImpact LaunchAuthored(
        WeaponPresentationProfile profile,
        Vector3 target,
        MortarUpgradePayload payload,
        IWeaponPresentationSink sink,
        WeaponFeedbackContext feedback)
    {
        return MortarShellImpact.LaunchAuthored(
            Vector3.up * 2f,
            target,
            0.5f,
            2f,
            10,
            2f,
            0.5f,
            1f,
            0.12f,
            null,
            payload,
            payload.UseGrapeshot,
            default,
            profile.Mortar.ShellPrefab,
            profile.Mortar.ShellPoolCapacity,
            true,
            true,
            sink,
            feedback);
    }

    private static void AssertCueStyle(
        WeaponPresentationProfile profile,
        WeaponPresentationCue cue,
        MortarCueStyle style,
        int minimumParticles)
    {
        Assert.That(profile.TryGetCueData(cue, out WeaponPresentationCueData data), Is.True);
        Assert.That(data.VfxPrefab, Is.Not.Null);
        MortarCueVfx vfx = data.VfxPrefab.GetComponent<MortarCueVfx>();
        Assert.That(vfx, Is.Not.Null);
        Assert.That(vfx.Style, Is.EqualTo(style));
        Assert.That(vfx.RuntimeParticleSystemCount, Is.GreaterThanOrEqualTo(minimumParticles));
    }

    private static void AssertResolved(
        WeaponPresentationProfile profile,
        WeaponFeedbackMode mode,
        WeaponPresentationCue expected)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset");
        WeaponInstance runtime = new() { Data = data, Level = 1 };
        WeaponFeedbackContext context = new(runtime, mode, 0.5f, Vector3.zero, Vector3.forward);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.ShotFired, in context, out WeaponPresentationCueData resolved), Is.True);
        Assert.That(resolved.Cue, Is.EqualTo(expected));
    }

    private static void Invoke(object target, string methodName, params object[] args)
    {
        System.Type[] signature = new System.Type[args.Length];
        for (int i = 0; i < args.Length; i++)
            signature[i] = args[i].GetType();
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            signature,
            null);
        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(target, args);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
