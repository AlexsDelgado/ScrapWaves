using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class FlamethrowerManualAreaTests
{
    private readonly List<Object> _cleanup = new();
    private GameObject _owner;
    private FlamethrowerManualAreas _areas;
    private FlamethrowerTuning _tuning;
    private int _hits;
    private SaveManager _previousSaveManager;
    private UnityEngine.Random.State _randomState;

    private static void SetSaveManager(SaveManager value) => typeof(SaveManager)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

    [SetUp]
    public void SetUp()
    {
        _previousSaveManager = SaveManager.Instance;
        SetSaveManager(null);
        _randomState = UnityEngine.Random.state;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Time.timeScale = 1f;
        GameplayPause.Reset();
        _owner = Track(new GameObject("Area owner"));
        if (ChallengeProgressTracker.Instance == null)
        {
            var tracker = Track(new GameObject("Test challenge tracker")).AddComponent<ChallengeProgressTracker>();
            typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
        }
        _areas = Track(new GameObject("Manual area test")).AddComponent<FlamethrowerManualAreas>();
        _areas.Initialize(_owner.transform, (target, origin) => { _hits++; return true; }, null);
        _tuning = new FlamethrowerTuning();
        _hits = 0;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (FlamethrowerFuelPuddle puddle in Object.FindObjectsByType<FlamethrowerFuelPuddle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(puddle.gameObject);
        foreach (FlamethrowerManualAreas areas in Object.FindObjectsByType<FlamethrowerManualAreas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(areas.gameObject);
        foreach (FlamethrowerStreamVfx vfx in Object.FindObjectsByType<FlamethrowerStreamVfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);
        for (int i = _cleanup.Count - 1; i >= 0; i--) if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        SetSaveManager(_previousSaveManager);
        UnityEngine.Random.state = _randomState;
        Time.timeScale = 1f;
        GameplayPause.Reset();
    }

    private void Emit(Vector3 origin, Vector3 direction, float range = 7f, float duration = 0.01f, float size = 1f)
        => _areas.Emit(duration, origin, direction, range, size, _tuning, FlamethrowerStreamStyle.Flame, 0f);

    [Test]
    public void Motion_GrowsDeceleratesAndStopsAtRangeIndependentlyOfOwner()
    {
        Emit(Vector3.up, Vector3.forward);
        Assert.That(_areas.ActiveCount, Is.EqualTo(1));
        _owner.transform.SetPositionAndRotation(new Vector3(50f, 20f, -30f), Quaternion.Euler(0f, 170f, 0f));
        _areas.Simulate(0.5f);
        Assert.That(_areas.GetCenter(0).z, Is.EqualTo(5.25f).Within(0.002f));
        Assert.That(_areas.GetCenter(0).y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(_areas.GetVelocity(0).magnitude, Is.EqualTo(7f).Within(0.002f));
        Assert.That(_areas.GetRadius(0), Is.EqualTo(Mathf.Lerp(0.35f, 0.75f, 1f / 3f)).Within(0.001f));
        _areas.Simulate(0.7f);
        Assert.That(_areas.GetCenter(0).z, Is.EqualTo(7f).Within(0.002f));
        Assert.That(_areas.GetVelocity(0).magnitude, Is.LessThan(0.001f));
        _areas.Simulate(0.31f);
        Assert.That(_areas.ActiveCount, Is.Zero);
    }

    [Test]
    public void Motion_CapturesTuningAtEmissionAndSupportsThreeDimensionalAim()
    {
        Vector3 direction = new Vector3(1f, 1f, 1f).normalized;
        Emit(Vector3.zero, direction, range: 12.25f, size: 2f);
        _tuning.FlameAreaTimeToStop = 20f;
        _tuning.FlameAreaFinalRadius = 10f;
        _areas.Simulate(1f);
        Assert.That(Vector3.Distance(_areas.GetCenter(0), direction * 12.25f), Is.LessThan(0.003f));
        Assert.That(_areas.GetRadius(0), Is.EqualTo(Mathf.Lerp(0.7f, 1.5f, 2f / 3f)).Within(0.001f));
    }

    [Test]
    public void Terrain_EdgeOnlyContactDoesNotStopCenter()
    {
        Wall(new Vector3(0.6f, 1f, 2f), new Vector3(0.4f, 4f, 0.5f));
        _tuning.FlameAreaInitialRadius = 1f;
        _tuning.FlameAreaFinalRadius = 2f;
        Emit(Vector3.up, Vector3.forward);
        _areas.Simulate(1.1f);
        Assert.That(_areas.GetCenter(0).z, Is.EqualTo(7f).Within(0.003f));
    }

    [TestCase(0.01f)]
    [TestCase(0.5f)]
    public void Terrain_CenterStopsAtThinAndThickHeadOnWallsButContinuesGrowing(float thickness)
    {
        Wall(new Vector3(0f, 1f, 2f), new Vector3(5f, 5f, thickness));
        Emit(Vector3.up, Vector3.forward);
        _areas.Simulate(0.3f);
        float radius = _areas.GetRadius(0);
        Assert.That(_areas.GetCenter(0).z, Is.EqualTo(2f - thickness * 0.5f - FlamethrowerManualAreas.TerrainCoreRadius - 0.002f).Within(0.003f));
        Assert.That(_areas.GetVelocity(0).magnitude, Is.LessThan(0.001f));
        _areas.Simulate(0.5f);
        Assert.That(_areas.ActiveCount, Is.EqualTo(1));
        Assert.That(_areas.GetRadius(0), Is.GreaterThan(radius));
    }

    [TestCase(30f)]
    [TestCase(60f)]
    [TestCase(80f)]
    public void Terrain_DiagonalContactKeepsSlidingOutsideWallUntilItSlowsToRest(float angle)
    {
        GameObject wall = Wall(new Vector3(0f, 0f, 2f), new Vector3(30f, 10f, 0.1f));
        wall.transform.rotation = Quaternion.Euler(0f, angle, 0f);
        Physics.SyncTransforms();
        Emit(Vector3.zero, Vector3.forward);
        float closestApproach = float.PositiveInfinity;
        float contactX = 0f;
        bool contacted = false;
        for (int i = 0; i < 70; i++)
        {
            _areas.Simulate(0.02f);
            Vector3 center = _areas.GetCenter(0);
            float separation = -Vector3.Dot(center - wall.transform.position, wall.transform.forward) - 0.05f;
            closestApproach = Mathf.Min(closestApproach, separation);
            if (!contacted && separation < FlamethrowerManualAreas.TerrainCoreRadius + 0.003f)
            {
                contacted = true;
                contactX = center.x;
            }
        }
        Assert.That(contacted, Is.True);
        Assert.That(closestApproach, Is.GreaterThanOrEqualTo(FlamethrowerManualAreas.TerrainCoreRadius - 0.001f));
        Assert.That(Mathf.Abs(_areas.GetCenter(0).x - contactX), Is.GreaterThan(0.1f));
        Assert.That(_areas.GetVelocity(0).magnitude, Is.LessThan(0.001f));
        Assert.That(_areas.ActiveCount, Is.EqualTo(1));
        Assert.That(_areas.GetRadius(0), Is.GreaterThan(0.7f));
    }

    [Test]
    public void Terrain_FixedCoreDoesNotGrowWithDamageRadiusOrAreaSize()
    {
        Wall(new Vector3(0.2f, 1f, 2f), new Vector3(0.2f, 4f, 0.5f));
        _tuning.FlameAreaInitialRadius = 1f;
        _tuning.FlameAreaFinalRadius = 2f;
        Emit(Vector3.up, Vector3.forward, size: 3f);
        _areas.Simulate(1.1f);
        Assert.That(_areas.GetCenter(0).z, Is.EqualTo(7f).Within(0.003f));
        Assert.That(_areas.GetRadius(0), Is.GreaterThan(5f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Terrain_SlidesAlongAngledWallsAndSlopesWithoutSpeedGain(bool slope)
    {
        GameObject wall = Wall(new Vector3(0f, 0f, 2f), new Vector3(20f, 20f, 0.1f));
        wall.transform.rotation = slope ? Quaternion.Euler(45f, 0f, 0f) : Quaternion.Euler(0f, 45f, 0f);
        Physics.SyncTransforms();
        Emit(Vector3.zero, Vector3.forward);
        _areas.Simulate(0.5f);
        Vector3 center = _areas.GetCenter(0);
        Assert.That(center.z, Is.GreaterThan(2f));
        Assert.That(slope ? Mathf.Abs(center.y) : Mathf.Abs(center.x), Is.GreaterThan(0.5f));
        Assert.That(_areas.GetVelocity(0).magnitude, Is.LessThan(7f));
        Assert.That(Mathf.Abs(Vector3.Dot(_areas.GetVelocity(0), wall.transform.forward)), Is.LessThan(0.002f));
    }

    [Test]
    public void Terrain_CornerCannotTunnelOnLargeFrame()
    {
        Wall(new Vector3(2f, 0f, 0f), new Vector3(0.01f, 10f, 20f));
        Wall(new Vector3(0f, 0f, 3f), new Vector3(20f, 10f, 0.01f));
        Emit(Vector3.zero, new Vector3(1f, 0f, 1f));
        _areas.Simulate(0.8f);
        Assert.That(_areas.GetCenter(0).x, Is.LessThan(2f));
        Assert.That(_areas.GetCenter(0).z, Is.LessThan(3f));
        Assert.That(_areas.GetVelocity(0).magnitude, Is.LessThan(0.001f));
    }

    [Test]
    public void Damage_OverlappingAreasAndMultipleCollidersReceiveOneSharedHitPerTick()
    {
        Target(Vector3.zero, colliders: 3);
        Emit(Vector3.zero, Vector3.forward, range: 0f, duration: 0.1f);
        _areas.Simulate(0.1f);
        Assert.That(_hits, Is.EqualTo(1));
        _areas.StopEmission();
        Emit(Vector3.zero, Vector3.right, range: 0f);
        _areas.Simulate(0.39f);
        Assert.That(_hits, Is.EqualTo(1), "New areas must not create additional damage clocks.");
        _areas.Simulate(0.01f);
        Assert.That(_hits, Is.EqualTo(2));
        _areas.Simulate(0.5f);
        Assert.That(_hits, Is.EqualTo(3));
    }

    [Test]
    public void Damage_DoesNotCrossWallEvenWhenExpandedTriggerOverlapsTarget()
    {
        Wall(Vector3.forward * 0.5f, new Vector3(10f, 10f, 0.05f));
        Target(Vector3.forward);
        _tuning.FlameAreaInitialRadius = 2f;
        _tuning.FlameAreaFinalRadius = 2f;
        Emit(Vector3.zero, Vector3.forward, range: 0f);
        _areas.Simulate(1f);
        Assert.That(_hits, Is.Zero);
    }

    [Test]
    public void Damage_EntryExitAndPooledEnemyReuseDoNotLeaveStaleOccupants()
    {
        Receiver target = Target(Vector3.zero);
        Emit(Vector3.zero, Vector3.forward, range: 0f);
        _areas.Simulate(0.1f);
        Assert.That(_hits, Is.EqualTo(1));
        target.gameObject.SetActive(false);
        _areas.Simulate(0.4f);
        Assert.That(_hits, Is.EqualTo(1));
        target.gameObject.SetActive(true);
        target.transform.position = Vector3.one * 50f;
        Physics.SyncTransforms();
        _areas.Simulate(0.5f);
        Assert.That(_hits, Is.EqualTo(1));
        target.transform.position = Vector3.zero;
        Physics.SyncTransforms();
        _areas.StopEmission();
        Emit(Vector3.zero, Vector3.forward, range: 0f);
        _areas.Simulate(0.5f);
        Assert.That(_hits, Is.EqualTo(2));
    }

    [Test]
    public void Damage_MaximumTargetsAppliesAcrossAllAreas()
    {
        Target(Vector3.zero);
        Target(Vector3.right * 4f);
        _tuning.FlameMaxTargetsPerTick = 1;
        Emit(Vector3.zero, Vector3.forward, range: 0f);
        _areas.StopEmission();
        Emit(Vector3.right * 4f, Vector3.forward, range: 0f);
        _areas.Simulate(0.1f);
        Assert.That(_hits, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Pause_FreezesMovementLifetimeAndDamage(bool uiPause)
    {
        Target(Vector3.zero);
        Emit(Vector3.zero, Vector3.forward);
        if (uiPause) GameplayPause.Push(); else Time.timeScale = 0f;
        _areas.Simulate(10f);
        Assert.That(_areas.ActiveCount, Is.EqualTo(1));
        Assert.That(_areas.GetCenter(0), Is.EqualTo(Vector3.zero));
        Assert.That(_areas.GetRadius(0), Is.EqualTo(0.35f));
        Assert.That(_hits, Is.Zero);
    }

    [Test]
    public void ShortLivedAreas_PreserveEmissionIntervalAndSharedDamageCooldownAcrossGaps()
    {
        Target(Vector3.zero);
        _tuning.FlameAreaEmissionInterval = 0.2f;
        _tuning.FlameAreaLifetime = 0.04f;
        for (int i = 0; i < 25; i++)
        {
            Emit(Vector3.zero, Vector3.forward, range: 0f, duration: 0.02f);
            _areas.Simulate(0.02f);
        }
        Assert.That(_hits, Is.EqualTo(1), "Emissions at 0, 0.2 and 0.4 cannot each restart damage.");
        Assert.That(_areas.ActiveCount, Is.Zero);
        Emit(Vector3.zero, Vector3.forward, range: 0f, duration: 0.02f);
        Assert.That(_areas.ActiveCount, Is.Zero, "The next scheduled emission is at 0.6 seconds.");
    }

    [Test]
    public void Pool_ReusesCloudsDuringSustainedFireAndClearDisablesTriggers()
    {
        _tuning.FlameAreaEmissionInterval = 0.05f; // Stress faster-than-default emission.
        for (int i = 0; i < 300; i++)
        {
            Emit(Vector3.zero, Vector3.forward, duration: 0.02f);
            _areas.Simulate(0.02f);
        }
        Assert.That(_areas.ActiveCount, Is.InRange(29, 31));
        Assert.That(_areas.CreatedCount, Is.LessThanOrEqualTo(32));
        _areas.Clear();
        Assert.That(_areas.ActiveCount, Is.Zero);
        Assert.That(_areas.GetComponentsInChildren<Collider>(), Is.Empty);
    }

    [TestCase(FlamethrowerStreamStyle.Flame)]
    [TestCase(FlamethrowerStreamStyle.JellifiedFuel)]
    [TestCase(FlamethrowerStreamStyle.LiquidNitrogen)]
    public void Visuals_UseWorldSpaceWispsWithDepthAndExistingTexture(FlamethrowerStreamStyle style)
    {
        _areas.Emit(0.01f, Vector3.up, Vector3.forward, 7f, 1f, _tuning, style, 0.5f);
        _areas.Simulate(0.5f);
        SphereCollider trigger = _areas.GetComponentInChildren<SphereCollider>();
        Assert.That(trigger.radius * trigger.transform.lossyScale.x, Is.EqualTo(_areas.GetRadius(0)).Within(0.001f));
        ParticleSystem system = _areas.GetComponentInChildren<ParticleSystem>();
        Assert.That(system.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
        Assert.That(system.emission.enabled, Is.False);
        Assert.That(system.collision.enabled, Is.False);
        Assert.That(_areas.GetComponentsInChildren<LineRenderer>(), Is.Empty);
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/GameFeel/Manual Flame Sprite"));
        Assert.That(renderer.sharedMaterial.mainTexture, Is.Not.Null);
        var particles = new ParticleSystem.Particle[768];
        int count = system.GetParticles(particles);
        Assert.That(count, Is.EqualTo(_areas.VisualParticleCount));
        Assert.That(count, Is.GreaterThan(10));
        Assert.That(Mathf.Abs(particles[0].position.x - particles[8].position.x), Is.GreaterThan(0.01f));
        Assert.That(Mathf.Abs(particles[0].position.y - particles[8].position.y), Is.GreaterThan(0.01f));
    }

    [Test]
    public void Plume_PauseFreezesWispsAndOwnerMovementDoesNotMoveThem()
    {
        Emit(Vector3.up, Vector3.forward);
        _areas.Simulate(0.2f);
        var system = _areas.GetComponentInChildren<ParticleSystem>();
        var before = new ParticleSystem.Particle[768];
        int count = system.GetParticles(before);
        float time = _areas.VisualTime;
        _owner.transform.position = Vector3.one * 50f;
        _areas.StopEmission();
        Time.timeScale = 0f;
        _areas.Simulate(0.5f);
        var after = new ParticleSystem.Particle[768];
        Assert.That(system.GetParticles(after), Is.EqualTo(count));
        Assert.That(_areas.VisualTime, Is.EqualTo(time));
        for (int i = 0; i < count; i++)
        {
            Assert.That(after[i].position, Is.EqualTo(before[i].position));
            Assert.That(after[i].rotation, Is.EqualTo(before[i].rotation));
            Assert.That(after[i].startColor, Is.EqualTo(before[i].startColor));
        }
        Time.timeScale = 1f;
        _areas.Clear();
        Assert.That(system.particleCount, Is.Zero);
        Emit(Vector3.right * 10f, Vector3.forward);
        Assert.That(_areas.CreatedCount, Is.EqualTo(1));
        system.GetParticles(after);
        Assert.That(after[0].position.x, Is.GreaterThan(9f));
    }

    [Test]
    public void Plume_StationaryFlameWispsKeepCurlingWithoutMovingDamageCenter()
    {
        Emit(Vector3.up, Vector3.forward, range: 0f);
        var system = _areas.GetComponentInChildren<ParticleSystem>();
        var particles = new ParticleSystem.Particle[768];
        system.GetParticles(particles);
        Vector3 position = particles[8].position;
        float rotation = particles[8].rotation;
        _areas.Simulate(0.1f);
        system.GetParticles(particles);
        Assert.That(_areas.GetCenter(0), Is.EqualTo(Vector3.up));
        Assert.That(Vector3.Distance(particles[8].position, position), Is.GreaterThan(0.005f));
        Assert.That(Mathf.Abs(particles[8].rotation - rotation), Is.GreaterThan(0.01f));
    }

    [Test]
    public void Plume_BoundedVisualBudgetDoesNotCreateMoreGameplayAreasOrColliders()
    {
        _tuning.FlameAreaEmissionInterval = 0.005f;
        _tuning.FlameAreaLifetime = 4f;
        for (int i = 0; i < 150; i++)
        {
            Emit(Vector3.up, Vector3.forward, duration: 0.02f);
            _areas.Simulate(0.02f);
        }
        Assert.That(_areas.ActiveCount, Is.GreaterThan(100));
        Assert.That(_areas.VisualParticleCount, Is.LessThanOrEqualTo(768));
        Assert.That(_areas.GetComponentsInChildren<ParticleSystemRenderer>(), Has.Length.EqualTo(1));
        Assert.That(_areas.GetComponentsInChildren<Collider>(), Has.Length.EqualTo(_areas.ActiveCount));
    }

    [Test]
    public void Plume_CoolsThenPoolReuseStartsWithBrightWisps()
    {
        _tuning.FlameAreaLifetime = 2f;
        Emit(Vector3.zero, Vector3.forward, range: 0f);
        _areas.Simulate(1.8f);
        var system = _areas.GetComponentInChildren<ParticleSystem>();
        var particles = new ParticleSystem.Particle[768];
        system.GetParticles(particles);
        Color cooled = particles[10].startColor;
        _areas.Clear();
        Emit(Vector3.zero, Vector3.forward, range: 0f);
        system.GetParticles(particles);
        Color fresh = particles[10].startColor;
        Assert.That(fresh.r, Is.GreaterThan(cooled.r + 0.3f));
        Assert.That(fresh.a, Is.GreaterThan(cooled.a));
    }

    [TestCase(FlamethrowerStreamStyle.Flame)]
    [TestCase(FlamethrowerStreamStyle.JellifiedFuel)]
    [TestCase(FlamethrowerStreamStyle.LiquidNitrogen)]
    public void Plume_RenderCompilesSharedShaderAndProducesVisibleFire(FlamethrowerStreamStyle style)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            Assert.Ignore("Requires a graphics device for shader and render verification.");
        for (int i = 0; i < 25; i++)
        {
            _areas.Emit(0.02f, Vector3.up, Vector3.forward, 7f, 1f, _tuning, style, 0.5f);
            _areas.Simulate(0.02f);
        }
        Camera camera = Track(new GameObject("Flame render camera")).AddComponent<Camera>();
        camera.transform.position = new Vector3(8f, 5f, -5f);
        camera.transform.LookAt(new Vector3(0f, 1f, 3f));
        camera.orthographic = true;
        camera.orthographicSize = 4f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        RenderTexture target = Track(new RenderTexture(512, 512, 24));
        camera.targetTexture = target;
        target.Create();
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.Render();
            Assert.That(ShaderUtil.ShaderHasError(_areas.GetComponentInChildren<ParticleSystemRenderer>().sharedMaterial.shader), Is.False);
            RenderTexture.active = target;
            Texture2D frame = Track(new Texture2D(512, 512, TextureFormat.RGB24, false));
            frame.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            frame.Apply();
            int visible = 0;
            foreach (Color pixel in frame.GetPixels()) if (pixel.maxColorComponent > 0.3f) visible++;
            Assert.That(visible, Is.GreaterThan(100));
            string output = Environment.GetEnvironmentVariable("FLAME_QA_DIR");
            if (!string.IsNullOrEmpty(output)) System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, "flame-wisps-" + style + ".png"), frame.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); }
    }

    [TestCase(WeaponUpgradePath.None)]
    [TestCase(WeaponUpgradePath.PathA)]
    [TestCase(WeaponUpgradePath.PathB)]
    public void Weapon_ReleaseAndSwitchPreserveAreaDamageAndPathStatus(WeaponUpgradePath path)
    {
        FlamethrowerWeapon weapon = CreateWeapon(path, out WeaponInstance instance);
        Receiver target = Target(Vector3.zero, colliders: 2);
        instance.Data.BaseRange = 0f;
        weapon.TickManual(0.1f, Vector3.forward, true);
        Track(weapon.ManualAreas.gameObject);
        Assert.That(instance.CurrentAmmo, Is.EqualTo(99f).Within(0.001f));
        weapon.TickManual(0.1f, Vector3.forward, false);
        weapon.ManualAreas.Simulate(0.1f);
        Assert.That(target.Hits, Is.EqualTo(1));
        instance.State = WeaponState.Automatic;
        weapon.ManualAreas.Simulate(0.4f);
        Assert.That(target.Hits, Is.EqualTo(2));
        if (path == WeaponUpgradePath.PathB)
        {
            Assert.That(target.GetComponent<WeaponMovementSlowStatus>(), Is.Not.Null);
            Assert.That(target.GetComponent<FlamethrowerBurnStatus>(), Is.Null);
        }
        else Assert.That(target.GetComponent<FlamethrowerBurnStatus>(), Is.Not.Null);
        if (path == WeaponUpgradePath.PathA)
            Assert.That(Object.FindObjectsByType<FlamethrowerFuelPuddle>(FindObjectsSortMode.None).Length, Is.GreaterThan(0));
    }

    [Test]
    public void Weapon_PartialAmmoOnlyEmitsForFundedTimeAndExistingAreasSurvive()
    {
        FlamethrowerWeapon weapon = CreateWeapon(WeaponUpgradePath.None, out WeaponInstance instance);
        instance.CurrentAmmo = 0.1f;
        weapon.TickManual(0.2f, Vector3.forward, true);
        Track(weapon.ManualAreas.gameObject);
        Assert.That(instance.CurrentAmmo, Is.Zero);
        Assert.That(weapon.ActiveManualAreaCount, Is.EqualTo(1));
        weapon.TickManual(0.2f, Vector3.forward, true);
        Assert.That(weapon.ActiveManualAreaCount, Is.EqualTo(1));
        weapon.ManualAreas.Simulate(1f);
        Assert.That(weapon.ActiveManualAreaCount, Is.EqualTo(1));
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset")]
    [TestCase("Assets/ScriptableObjects/WeaponSO/Sandbox_Flamethrower.asset")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Flamethrower.asset")]
    public void Assets_PersistAreaDefaultsAndKeepManualDamageAndAmmoSettings(string path)
    {
        FlamethrowerTuning tuning = AssetDatabase.LoadAssetAtPath<WeaponData>(path).Flamethrower;
        Assert.That(tuning.FlameAreaEmissionInterval, Is.EqualTo(0.15f));
        Assert.That(tuning.FlameAreaLifetime, Is.EqualTo(1.5f));
        Assert.That(tuning.FlameAreaTimeToStop, Is.EqualTo(1f));
        Assert.That(tuning.FlameAreaInitialRadius, Is.EqualTo(0.35f));
        Assert.That(tuning.FlameAreaFinalRadius, Is.EqualTo(0.75f));
        Assert.That(tuning.FlameManualTickInterval, Is.EqualTo(0.5f));
        Assert.That(tuning.FlameManualAmmoPerSecond, Is.EqualTo(10f));
    }

    [Test]
    public void Lifecycle_InactiveOwnerClearsAreasEvenWhilePaused()
    {
        Emit(Vector3.zero, Vector3.forward);
        _owner.SetActive(false);
        Time.timeScale = 0f;
        typeof(FlamethrowerManualAreas).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_areas, null);
        Assert.That(_areas.ActiveCount, Is.Zero);
        Assert.That(_areas.GetComponentsInChildren<Collider>(), Is.Empty);
    }

    [Test]
    public void Lifecycle_RunTerminationClearsAreasWhilePaused()
    {
        Emit(Vector3.zero, Vector3.forward);
        GameManager previous = GameManager.Instance;
        var manager = Track(new GameObject("Test run")).AddComponent<GameManager>();
        var singleton = typeof(GameManager).GetProperty(nameof(GameManager.Instance));
        singleton.SetValue(null, manager);
        try
        {
            SetField(manager, "_state", GameManager.GameState.GameOver);
            Time.timeScale = 0f;
            typeof(FlamethrowerManualAreas).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_areas, null);
            Assert.That(_areas.ActiveCount, Is.Zero);
        }
        finally { singleton.SetValue(null, previous); }
    }

    [Test]
    public void Lifecycle_DestroyedOwnerRemovesControllerAndClouds()
    {
        Emit(Vector3.zero, Vector3.forward);
        Object.DestroyImmediate(_owner);
        typeof(FlamethrowerManualAreas).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_areas, null);
        Assert.That(_areas == null, Is.True);
    }

    [Test]
    public void Damage_AfterSwitchReportsManualSourceAndAuthoritativeAppliedDamageOnce()
    {
        FlamethrowerWeapon weapon = CreateWeapon(WeaponUpgradePath.None, out WeaponInstance instance);
        instance.Data.BaseRange = 0f;
        var sink = new FeedbackRecorder();
        weapon.SetPresentationSink(sink);
        GameObject target = Track(new GameObject("Authoritative enemy"));
        var receiver = target.AddComponent<AuthoritativeReceiver>();
        target.AddComponent<SphereCollider>();
        target.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        weapon.TickManual(0.1f, Vector3.forward, true);
        instance.State = WeaponState.Automatic;
        weapon.ManualAreas.Simulate(0.1f);
        Assert.That(receiver.Hits, Is.EqualTo(1));
        Assert.That(sink.Damage.Count, Is.EqualTo(1));
        Assert.That(sink.Damage[0].Mode, Is.EqualTo(WeaponFeedbackMode.Manual));
        Assert.That(sink.Damage[0].DamageAmount, Is.EqualTo(2));
        Assert.That(sink.Status.Count, Is.EqualTo(1));
        Assert.That(sink.Status[0].Mode, Is.EqualTo(WeaponFeedbackMode.Manual));
    }

    private sealed class AuthoritativeReceiver : MonoBehaviour, IAuthoritativeDamageable
    {
        public int Hits;
        public bool ApplyDamage(int amount) => true;
        public DamageApplicationResult ApplyDamage(in DamageRequest request)
        {
            Hits++;
            return DamageApplicationResult.FromHealthDelta(request, 100, 98);
        }
    }

    private sealed class FeedbackRecorder : IWeaponFeedbackSink
    {
        public readonly List<WeaponFeedbackContext> Damage = new();
        public readonly List<WeaponFeedbackContext> Status = new();
        public void OnChargeStarted(in WeaponFeedbackContext context) { }
        public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress) { }
        public void OnChargeCancelled(in WeaponFeedbackContext context) { }
        public void OnShotFired(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
        public void OnProjectileImpact(in WeaponFeedbackContext context) { }
        public void OnDamageConfirmed(in WeaponFeedbackContext context) => Damage.Add(context);
        public void OnStatusApplied(in WeaponFeedbackContext context) => Status.Add(context);
        public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
        public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold) { }
        public void ConfigureProjectile(Projectile projectile, ProjectilePresentationArchetypeId archetype, in WeaponFeedbackContext context) { }
        public void Emit(in WeaponPresentationContext context) { }
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
    }

    private FlamethrowerWeapon CreateWeapon(WeaponUpgradePath path, out WeaponInstance instance)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.Flamethrower;
        data.WeaponId = "manual-area-test";
        data.EnsureSpecificTuningForCurrentType();
        data.BaseDamage = 5f; data.BaseRange = 7f;
        PlayerStats stats = _owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier, StatType.CriticalChance,
            StatType.CriticalDamage, StatType.AttackSpeedMultiplier, StatType.AmmoMultiplier, StatType.Knockback,
            StatType.ProjectileAreaSize, StatType.AbilityDamageMultiplier, StatType.AbilityCooldownReduction,
            StatType.CloseRangeDamageMultiplier, StatType.LongRangeDamageMultiplier })
        {
            StatDefinition definition = Track(ScriptableObject.CreateInstance<StatDefinition>());
            SetField(definition, "<StatType>k__BackingField", type);
            SetField(definition, "<BaseValue>k__BackingField", type == StatType.CriticalChance || type == StatType.AbilityCooldownReduction ? 0f : 1f);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
        instance = new WeaponInstance { Data = data, CurrentAmmo = 100f, Level = path == WeaponUpgradePath.None ? 1 : 6,
            SelectedPath = path, State = WeaponState.Manual };
        var weapon = new FlamethrowerWeapon(null, null, _owner.transform, null);
        weapon.Setup(instance, _owner.transform, stats, null);
        return weapon;
    }

    private GameObject Wall(Vector3 position, Vector3 size)
    {
        GameObject go = Track(new GameObject("Terrain obstacle"));
        go.transform.position = position;
        go.AddComponent<BoxCollider>().size = size;
        Physics.SyncTransforms();
        return go;
    }

    private Receiver Target(Vector3 position, int colliders = 1)
    {
        GameObject go = Track(new GameObject("Enemy"));
        go.transform.position = position;
        Receiver target = go.AddComponent<Receiver>();
        for (int i = 0; i < colliders; i++)
        {
            GameObject child = new("Enemy collider");
            child.transform.SetParent(go.transform, false);
            child.AddComponent<SphereCollider>().radius = 0.2f;
        }
        Physics.SyncTransforms();
        return target;
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void SetField(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private sealed class Receiver : MonoBehaviour, IDamageable
    {
        public int Hits;
        public bool ApplyDamage(int amount) { Hits++; return true; }
    }
}
