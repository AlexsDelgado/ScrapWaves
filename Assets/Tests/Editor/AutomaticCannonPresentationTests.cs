using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class AutomaticCannonPresentationTests
{
    private readonly List<Object> _createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _createdObjects.Count - 1; i >= 0; i--)
        {
            if (_createdObjects[i] != null)
                Object.DestroyImmediate(_createdObjects[i]);
        }

        _createdObjects.Clear();
    }

    [Test]
    public void PresentationContext_IsValueTypeAndNormalizesRuntimeValues()
    {
        Assert.That(typeof(WeaponPresentationContext).IsValueType, Is.True);
        Assert.That(typeof(WeaponPresentationLoopHandle).IsValueType, Is.True);

        WeaponPresentationContext context = new(
            WeaponPresentationCue.AutomaticCannonManualShot,
            weapon: null,
            new Vector3(1f, 2f, 3f),
            new Vector3(0f, 0f, 5f),
            intensity: -2f,
            isAbility: true,
            isCritical: true,
            isWeakPoint: true);

        Assert.That(context.Cue, Is.EqualTo(WeaponPresentationCue.AutomaticCannonManualShot));
        Assert.That(context.Position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        Assert.That(context.Direction, Is.EqualTo(Vector3.forward));
        Assert.That(context.Intensity, Is.Zero);
        Assert.That(context.IsAbility, Is.True);
        Assert.That(context.IsCritical, Is.True);
        Assert.That(context.IsWeakPoint, Is.True);
    }

    [Test]
    public void NullSink_AcceptsEveryOperationAndNeverCreatesLoop()
    {
        WeaponPresentationContext context = new(
            WeaponPresentationCue.AutomaticCannonContinuousLoop,
            weapon: null,
            Vector3.zero,
            Vector3.forward);

        Assert.DoesNotThrow(() => NullWeaponPresentationSink.Instance.Emit(in context));

        WeaponPresentationLoopHandle handle = default;
        Assert.DoesNotThrow(() => handle = NullWeaponPresentationSink.Instance.BeginLoop(in context));
        Assert.That(handle.IsValid, Is.False);
        Assert.DoesNotThrow(() => NullWeaponPresentationSink.Instance.UpdateLoop(handle, in context));
        Assert.DoesNotThrow(() => NullWeaponPresentationSink.Instance.EndLoop(handle, in context));
    }

    [Test]
    public void CueData_SanitizeClampsPoolAudioAndTimingValues()
    {
        WeaponPresentationCueData cueData = new()
        {
            Cue = WeaponPresentationCue.AutomaticCannonAutoShot,
            AudioClips = null,
            Volume = 4f,
            PitchMin = 2f,
            PitchMax = 0.5f,
            Duration = -1f,
            MinReplayInterval = -1f,
            PrewarmCount = 20,
            MaxSimultaneous = 3
        };

        cueData.Sanitize();

        Assert.That(cueData.AudioClips, Is.Not.Null);
        Assert.That(cueData.Volume, Is.EqualTo(1f));
        Assert.That(cueData.PitchMin, Is.EqualTo(0.5f));
        Assert.That(cueData.PitchMax, Is.EqualTo(2f));
        Assert.That(cueData.Duration, Is.Zero);
        Assert.That(cueData.MinReplayInterval, Is.Zero);
        Assert.That(cueData.MaxSimultaneous, Is.EqualTo(3));
        Assert.That(cueData.PrewarmCount, Is.EqualTo(3));
    }

    [Test]
    public void Profile_ResolvesFirstCueAndReportsDuplicates()
    {
        WeaponPresentationProfile profile = CreateProfile();
        List<WeaponPresentationCueData> cues = GetPrivateField<List<WeaponPresentationCueData>>(profile, "_cues");
        WeaponPresentationCueData first = new()
        {
            Cue = WeaponPresentationCue.AutomaticCannonAutoShot,
            Volume = 0.4f
        };
        cues.Add(first);
        cues.Add(new WeaponPresentationCueData
        {
            Cue = WeaponPresentationCue.AutomaticCannonAutoShot,
            Volume = 0.9f
        });
        cues.Add(new WeaponPresentationCueData
        {
            Cue = WeaponPresentationCue.None
        });

        profile.RebuildCache();

        Assert.That(profile.TryGetCueData(WeaponPresentationCue.AutomaticCannonAutoShot, out WeaponPresentationCueData resolved), Is.True);
        Assert.That(resolved, Is.SameAs(first));
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.None, out _), Is.False);
        Assert.That(profile.HasDuplicateCues, Is.True);
    }

    [Test]
    public void ProductionAutomaticCannon_ReferencesCompletePresentationProfile()
    {
        WeaponData cannon = AssetDatabase.LoadAssetAtPath<WeaponData>(
            "Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset");

        Assert.That(cannon, Is.Not.Null);
        Assert.That(cannon.PresentationProfile, Is.Not.Null);
        Assert.That(cannon.PresentationProfile.WeaponType, Is.EqualTo(WeaponType.AutomaticCannon));
        Assert.That(cannon.PresentationProfile.HasDuplicateCues, Is.False);

        WeaponPresentationCue[] expectedCues =
        {
            WeaponPresentationCue.AutomaticCannonAutoBurst,
            WeaponPresentationCue.AutomaticCannonAutoShot,
            WeaponPresentationCue.AutomaticCannonManualVolley,
            WeaponPresentationCue.AutomaticCannonManualShot,
            WeaponPresentationCue.AutomaticCannonBaseActive,
            WeaponPresentationCue.AutomaticCannonContinuousShot,
            WeaponPresentationCue.AutomaticCannonContinuousActive,
            WeaponPresentationCue.AutomaticCannonContinuousLoop,
            WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic,
            WeaponPresentationCue.AutomaticCannonHeadHunterManual,
            WeaponPresentationCue.AutomaticCannonHeadHunterCharge,
            WeaponPresentationCue.AutomaticCannonHeadHunterActive,
            WeaponPresentationCue.AutomaticCannonImpact,
            WeaponPresentationCue.AutomaticCannonCriticalImpact,
            WeaponPresentationCue.AutomaticCannonWeakPointImpact
        };

        foreach (WeaponPresentationCue cue in expectedCues)
            Assert.That(cannon.PresentationProfile.TryGetCueData(cue, out _), Is.True, $"Missing {cue}");
    }

    [Test]
    public void ProductionBaseCannon_PlaysAudioPerProjectileInsteadOfPerVolley()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(
            "Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset");

        Assert.That(profile, Is.Not.Null);
        Assert.That(
            profile.TryGetCueData(WeaponPresentationCue.AutomaticCannonAutoShot, out WeaponPresentationCueData autoShot),
            Is.True);
        Assert.That(
            profile.TryGetCueData(WeaponPresentationCue.AutomaticCannonManualShot, out WeaponPresentationCueData manualShot),
            Is.True);
        Assert.That(
            profile.TryGetCueData(WeaponPresentationCue.AutomaticCannonAutoBurst, out WeaponPresentationCueData autoBurst),
            Is.True);
        Assert.That(
            profile.TryGetCueData(WeaponPresentationCue.AutomaticCannonManualVolley, out WeaponPresentationCueData manualVolley),
            Is.True);

        Assert.That(autoShot.AudioClips, Is.Not.Empty);
        Assert.That(manualShot.AudioClips, Is.Not.Empty);
        Assert.That(autoBurst.AudioClips, Is.Empty);
        Assert.That(manualVolley.AudioClips, Is.Empty);
    }

    [Test]
    public void ProductionBaseCannon_ReferencesPrewarmedAuthoredVfx()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(
            "Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset");
        Assert.That(profile, Is.Not.Null);

        (WeaponPresentationCue Cue, AutomaticCannonVfxStyle Style, int LineCount, int ParticleCount)[] expected =
        {
            (WeaponPresentationCue.AutomaticCannonAutoShot, AutomaticCannonVfxStyle.AutomaticShot, 0, 1),
            (WeaponPresentationCue.AutomaticCannonManualShot, AutomaticCannonVfxStyle.ManualShot, 0, 1),
            (WeaponPresentationCue.AutomaticCannonBaseActive, AutomaticCannonVfxStyle.BaseActive, 5, 3),
            (WeaponPresentationCue.AutomaticCannonImpact, AutomaticCannonVfxStyle.Impact, 3, 3),
            (WeaponPresentationCue.AutomaticCannonCriticalImpact, AutomaticCannonVfxStyle.CriticalImpact, 5, 3),
            (WeaponPresentationCue.AutomaticCannonWeakPointImpact, AutomaticCannonVfxStyle.WeakPointImpact, 4, 3)
        };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(profile.TryGetCueData(expected[i].Cue, out WeaponPresentationCueData cueData), Is.True);
            Assert.That(cueData.VfxPrefab, Is.Not.Null, expected[i].Cue.ToString());
            Assert.That(cueData.VfxPrefab.activeSelf, Is.False, $"{expected[i].Cue} prefab must remain inactive until leased.");
            Assert.That(cueData.VfxPrefab.GetComponent<PooledWeaponVfx>(), Is.Not.Null);

            AutomaticCannonCueVfx authored = cueData.VfxPrefab.GetComponent<AutomaticCannonCueVfx>();
            Assert.That(authored, Is.Not.Null);
            Assert.That(authored.Style, Is.EqualTo(expected[i].Style));
            Assert.That(authored.HasAuthoredTextures, Is.True);

            GameObject instance = Object.Instantiate(cueData.VfxPrefab);
            _createdObjects.Add(instance);
            AutomaticCannonCueVfx runtimeVfx = instance.GetComponent<AutomaticCannonCueVfx>();
            runtimeVfx.Prewarm();
            Assert.That(runtimeVfx.RuntimeLineCount, Is.EqualTo(expected[i].LineCount));
            Assert.That(runtimeVfx.RuntimeParticleSystemCount, Is.EqualTo(expected[i].ParticleCount));
            Assert.That(
                instance.transform.childCount,
                Is.EqualTo(runtimeVfx.RuntimeLineCount + runtimeVfx.RuntimeParticleSystemCount));

            if (expected[i].Style is AutomaticCannonVfxStyle.AutomaticShot or AutomaticCannonVfxStyle.ManualShot)
            {
                ParticleSystem flash = instance.GetComponentInChildren<ParticleSystem>(true);
                Assert.That(flash, Is.Not.Null);
                Assert.That(flash.shape.enabled, Is.False);
                Assert.That(flash.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
                Assert.That(
                    flash.main.startLifetime.constantMax,
                    Is.LessThan(0.05f),
                    "The muzzle flash must finish before the next round in the burst.");
            }
        }
    }

    [TestCase("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset")]
    public void BaseCannon_BurstProjectilesRemainCollinear(string assetPath)
    {
        WeaponData cannon = AssetDatabase.LoadAssetAtPath<WeaponData>(assetPath);

        Assert.That(cannon, Is.Not.Null, assetPath);
        Assert.That(cannon.AutomaticCannon.CannonAutoAccuracySpreadDegrees, Is.Zero);
        Assert.That(cannon.AutomaticCannon.CannonBurstProjectileScatterDegrees, Is.Zero);
        Assert.That(cannon.AutomaticCannon.CannonAutoLineSpacing, Is.GreaterThan(0f));
    }

    [Test]
    public void ManualBurst_DelayedRoundsFollowLatestAimDirection()
    {
        GameObject spawnObject = CreateGameObject("Live manual burst spawn");
        GameObject ownerObject = CreateGameObject("Live manual burst owner");
        ProjectilePool pool = CreateProjectilePool(maxPoolSize: 5);
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponType = WeaponType.AutomaticCannon;
        data.BaseDamage = 10f;
        data.BaseManualAmmo = 5f;
        data.EnsureSpecificTuningForCurrentType();
        data.AutomaticCannon.CannonBurstShotInterval = 0.05f;
        _createdObjects.Add(data);

        WeaponInstance instance = new()
        {
            Data = data,
            State = WeaponState.Manual,
            CurrentAmmo = 5f
        };
        AutomaticCannonWeapon weapon = new(null, pool, spawnObject.transform);
        RecordingPresentationSink presentation = new();
        weapon.SetPresentationSink(presentation);
        weapon.Setup(instance, ownerObject.transform, stats: null, heat: null);

        weapon.TickManual(1f, Vector3.forward, isFiring: true);
        spawnObject.transform.position = Vector3.right * 2f;
        weapon.TickManual(0.05f, Vector3.right, isFiring: false);

        List<Projectile> activeProjectiles = FindActivePooledProjectiles();
        Assert.That(activeProjectiles, Has.Count.EqualTo(2));
        Assert.That(
            activeProjectiles.Exists(projectile => Vector3.Dot(projectile.transform.forward, Vector3.forward) > 0.999f),
            Is.True);
        Assert.That(
            activeProjectiles.Exists(projectile => Vector3.Dot(projectile.transform.forward, Vector3.right) > 0.999f),
            Is.True);

        List<WeaponPresentationContext> shotContexts = presentation.Contexts.FindAll(
            context => context.Cue == WeaponPresentationCue.AutomaticCannonManualShot);
        Assert.That(shotContexts, Has.Count.EqualTo(2));
        Assert.That(shotContexts[0].Position, Is.EqualTo(Vector3.zero));
        Assert.That(shotContexts[1].Position, Is.EqualTo(spawnObject.transform.position));
        Assert.That(shotContexts[0].Anchor, Is.SameAs(spawnObject.transform));
        Assert.That(shotContexts[1].Anchor, Is.SameAs(spawnObject.transform));
    }

    [Test]
    public void AutomaticBurst_DelayedRoundsTrackTargetFromCurrentMuzzlePosition()
    {
        GameObject spawnObject = CreateGameObject("Live automatic burst spawn");
        GameObject ownerObject = CreateGameObject("Live automatic burst owner");
        GameObject targetObject = CreateGameObject("Live automatic burst target");
        targetObject.transform.position = Vector3.forward * 10f;
        ProjectilePool pool = CreateProjectilePool(maxPoolSize: 3);
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponType = WeaponType.AutomaticCannon;
        data.BaseDamage = 10f;
        data.BaseRange = 50f;
        data.EnsureSpecificTuningForCurrentType();
        data.AutomaticCannon.CannonBurstShotInterval = 0.05f;
        _createdObjects.Add(data);

        WeaponInstance instance = new()
        {
            Data = data,
            State = WeaponState.Automatic
        };
        FixedTargeting targeting = new(targetObject.transform);
        AutomaticCannonWeapon weapon = new(targeting, pool, spawnObject.transform);
        weapon.Setup(instance, ownerObject.transform, stats: null, heat: null);

        weapon.TickAutomatic(1f, Vector3.forward);
        spawnObject.transform.position = Vector3.right * 5f;
        Vector3 expectedDirection = (targetObject.transform.position - spawnObject.transform.position).normalized;
        weapon.TickAutomatic(0.05f, Vector3.forward);

        List<Projectile> activeProjectiles = FindActivePooledProjectiles();
        Assert.That(activeProjectiles, Has.Count.EqualTo(2));
        Assert.That(
            activeProjectiles.Exists(projectile => Vector3.Dot(projectile.transform.forward, expectedDirection) > 0.999f),
            Is.True);
    }

    [Test]
    public void SandboxBootstrap_DisablesInitialWeaponChoice()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(playerPrefab, Is.Not.Null);

        GameObject player = Object.Instantiate(playerPrefab);
        _createdObjects.Add(player);
        RunStartWeaponChoice initialChoice = player.GetComponent<RunStartWeaponChoice>();
        Assert.That(initialChoice, Is.Not.Null);
        Assert.That(initialChoice.enabled, Is.True);

        MethodInfo disableMethod = typeof(WeaponTestingSandboxManager).GetMethod(
            "DisableProductionRuntimeComponents",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(disableMethod, Is.Not.Null);
        disableMethod.Invoke(null, new object[] { player });

        Assert.That(initialChoice.enabled, Is.False);
    }

    [Test]
    public void ProductionPlayerAndSandboxCannon_ArePresentationWired()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        WeaponData sandboxCannon = AssetDatabase.LoadAssetAtPath<WeaponData>(
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset");

        Assert.That(playerPrefab, Is.Not.Null);
        WeaponPresentationController controller = playerPrefab.GetComponent<WeaponPresentationController>();
        WeaponManager manager = playerPrefab.GetComponent<WeaponManager>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(manager, Is.Not.Null);
        Assert.That(
            GetPrivateField<WeaponPresentationController>(manager, "_presentationController"),
            Is.SameAs(controller));

        Assert.That(sandboxCannon, Is.Not.Null);
        Assert.That(sandboxCannon.PresentationProfile, Is.Not.Null);
        Assert.That(
            sandboxCannon.PresentationProfile.WeaponType,
            Is.EqualTo(WeaponType.AutomaticCannon));
    }

    [Test]
    public void VfxPool_PrewarmsReusesAndFailsQuietlyAtCapacity()
    {
        GameObject prefab = CreateGameObject("Test weapon VFX prefab");
        GameObject root = CreateGameObject("Test weapon VFX root");
        GameObject anchor = CreateGameObject("Test weapon VFX anchor");
        WeaponPresentationCueData cueData = new()
        {
            Cue = WeaponPresentationCue.AutomaticCannonAutoShot,
            VfxPrefab = prefab,
            Duration = 0.1f,
            PrewarmCount = 1,
            MaxSimultaneous = 1
        };
        cueData.Sanitize();

        WeaponVfxPool pool = new(cueData, root.transform);
        WeaponPresentationContext context = new(
            cueData.Cue,
            weapon: null,
            new Vector3(1f, 2f, 3f),
            Vector3.forward,
            anchor: anchor.transform);

        Assert.That(pool.Count, Is.EqualTo(1));
        Assert.That(pool.TryPlay(in context, now: 0f, loop: false, out PooledWeaponVfx first), Is.True);
        Assert.That(first.gameObject.activeSelf, Is.True);
        Assert.That(first.CurrentAnchor, Is.SameAs(anchor.transform));
        Assert.That(first.transform.parent, Is.SameAs(anchor.transform));
        Assert.That(first.transform.localPosition, Is.EqualTo(Vector3.zero));

        anchor.transform.position = new Vector3(7f, 3f, -2f);
        Assert.That(first.transform.position, Is.EqualTo(anchor.transform.position));
        Assert.That(pool.TryPlay(in context, now: 0.01f, loop: false, out _), Is.False);

        pool.Tick(0.11f);

        Assert.That(pool.ActiveCount, Is.Zero);
        Assert.That(first.CurrentAnchor, Is.Null);
        Assert.That(first.transform.parent, Is.SameAs(root.transform));
        Assert.That(pool.TryPlay(in context, now: 0.11f, loop: false, out PooledWeaponVfx reused), Is.True);
        Assert.That(reused, Is.SameAs(first));
        pool.ReleaseAll();
        Assert.That(pool.ActiveCount, Is.Zero);
    }

    [Test]
    public void AudioVoicePool_ReusesBoundedVoiceAndAppliesGlobalSfxVolume()
    {
        GameObject root = CreateGameObject("Test weapon audio root");
        AudioClip clip = AudioClip.Create("Test weapon transient", 4410, 1, 44100, false);
        _createdObjects.Add(clip);

        WeaponPresentationCueData cueData = new()
        {
            Cue = WeaponPresentationCue.AutomaticCannonManualShot,
            AudioClips = new List<AudioClip> { clip },
            Volume = 0.5f,
            PitchMin = 1f,
            PitchMax = 1f
        };
        cueData.Sanitize();

        WeaponAudioVoicePool pool = new(root.transform, capacity: 1, spatialBlend: 1f);
        Assert.That(
            pool.TryPlayOneShot(cueData, Vector3.one, globalVolume: 0.4f, now: 0f, out WeaponAudioVoiceHandle firstHandle),
            Is.True);
        Assert.That(pool.TryGetSource(firstHandle, out AudioSource firstSource), Is.True);
        Assert.That(firstSource.volume, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(firstSource.transform.position, Is.EqualTo(Vector3.one));
        Assert.That(
            pool.TryPlayOneShot(cueData, Vector3.zero, globalVolume: 1f, now: 0.01f, out _),
            Is.False);

        pool.Tick(0.11f);

        Assert.That(pool.ActiveCount, Is.Zero);
        Assert.That(
            pool.TryPlayOneShot(cueData, Vector3.zero, globalVolume: 1f, now: 0.11f, out WeaponAudioVoiceHandle reusedHandle),
            Is.True);
        Assert.That(pool.TryGetSource(reusedHandle, out AudioSource reusedSource), Is.True);
        Assert.That(reusedSource, Is.SameAs(firstSource));
        pool.ReleaseAll();
    }

    [Test]
    public void PresentationController_RateLimitsCueAndReleasesLoopsOnDisable()
    {
        GameObject vfxPrefab = CreateGameObject("Test controller VFX prefab");
        AudioClip loopClip = AudioClip.Create("Test controller loop", 4410, 1, 44100, false);
        _createdObjects.Add(loopClip);

        WeaponPresentationProfile profile = CreateProfile();
        List<WeaponPresentationCueData> cues = GetPrivateField<List<WeaponPresentationCueData>>(profile, "_cues");
        cues.Add(new WeaponPresentationCueData
        {
            Cue = WeaponPresentationCue.AutomaticCannonContinuousShot,
            VfxPrefab = vfxPrefab,
            Duration = 0.05f,
            MinReplayInterval = 0.5f,
            PrewarmCount = 1,
            MaxSimultaneous = 1,
            CameraRotationImpulse = new Vector3(-0.1f, 0f, 0f)
        });
        cues.Add(new WeaponPresentationCueData
        {
            Cue = WeaponPresentationCue.AutomaticCannonContinuousLoop,
            VfxPrefab = vfxPrefab,
            AudioClips = new List<AudioClip> { loopClip },
            Volume = 0.5f,
            PitchMin = 1f,
            PitchMax = 1f,
            PrewarmCount = 1,
            MaxSimultaneous = 1,
            Loop = true
        });
        profile.RebuildCache();

        GameObject cameraObject = CreateGameObject("Test presentation camera");
        ThirdPersonCamera camera = cameraObject.AddComponent<ThirdPersonCamera>();
        camera.CameraFeedbackScale = 1f;

        GameObject controllerObject = CreateGameObject("Test presentation controller");
        WeaponPresentationController controller = controllerObject.AddComponent<WeaponPresentationController>();
        controller.Configure(profile, camera, audioManager: null, audioVoiceCount: 1);

        WeaponPresentationContext shot = new(
            WeaponPresentationCue.AutomaticCannonContinuousShot,
            weapon: null,
            Vector3.zero,
            Vector3.forward);

        Assert.That(controller.TryEmitAtTime(in shot, 0f), Is.True);
        Vector3 firstImpulse = camera.CurrentPresentationRotationImpulse;
        Assert.That(controller.TryEmitAtTime(in shot, 0.1f), Is.False);
        Assert.That(camera.CurrentPresentationRotationImpulse, Is.EqualTo(firstImpulse));
        Assert.That(controller.TryEmitAtTime(in shot, 0.5f), Is.True);

        WeaponPresentationContext loop = new(
            WeaponPresentationCue.AutomaticCannonContinuousLoop,
            weapon: null,
            Vector3.zero,
            Vector3.forward);
        WeaponPresentationLoopHandle loopHandle = controller.BeginLoopAtTime(in loop, 1f);

        Assert.That(loopHandle.IsValid, Is.True);
        Assert.That(controller.ActiveLoopCount, Is.EqualTo(1));

        InvokePrivate(controller, "OnDisable");
        controllerObject.SetActive(false);

        Assert.That(controller.ActiveLoopCount, Is.Zero);
        Assert.That(controller.ActiveVfxCount, Is.Zero);
        Assert.That(controller.ActiveAudioVoiceCount, Is.Zero);
    }

    [Test]
    public void ThirdPersonCamera_PresentationImpulseIsAdditiveScalableAndClearedOnDisable()
    {
        GameObject followObject = CreateGameObject("Test camera follow target");
        GameObject cameraObject = CreateGameObject("Test additive camera");
        ThirdPersonCamera camera = cameraObject.AddComponent<ThirdPersonCamera>();
        camera.SetFollowTarget(followObject.transform);
        camera.CameraFeedbackScale = 1f;
        SetPrivateField(camera, "_avoidCameraClipping", false);
        SetPrivateField(camera, "_lookBlockedByUi", true);

        InvokePrivate(camera, "LateUpdate");
        Vector3 basePosition = camera.transform.position;
        Quaternion baseRotation = camera.transform.rotation;

        Assert.That(
            camera.AddPresentationImpulse(
                new Vector3(0.25f, 0.1f, 0f),
                new Vector3(1f, 2f, 3f)),
            Is.True);
        InvokePrivate(camera, "LateUpdate");

        Assert.That(
            Vector3.Distance(camera.transform.position, basePosition + new Vector3(0.25f, 0.1f, 0f)),
            Is.LessThan(0.001f));
        Assert.That(
            Quaternion.Angle(camera.transform.rotation, baseRotation * Quaternion.Euler(1f, 2f, 3f)),
            Is.LessThan(0.001f));

        camera.CameraFeedbackScale = 0f;
        Assert.That(camera.CurrentPresentationPositionImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(camera.CurrentPresentationRotationImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(camera.AddPresentationImpulse(Vector3.one, Vector3.one), Is.False);

        InvokePrivate(camera, "LateUpdate");
        Assert.That(Vector3.Distance(camera.transform.position, basePosition), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(camera.transform.rotation, baseRotation), Is.LessThan(0.001f));

        camera.CameraFeedbackScale = 1f;
        camera.AddPresentationImpulse(Vector3.one, Vector3.one);
        InvokePrivate(camera, "OnDisable");
        cameraObject.SetActive(false);
        Assert.That(camera.CurrentPresentationPositionImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(camera.CurrentPresentationRotationImpulse, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void WeaponFactory_AssignsRequestedPresentationSinkAndUsesNullFallback()
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponType = WeaponType.AutomaticCannon;
        _createdObjects.Add(data);

        RecordingPresentationSink recordingSink = new();
        IWeaponBehaviour configured = WeaponBehaviourFactory.Create(
            data,
            targeting: null,
            projectilePool: null,
            spawn: null,
            movement: null,
            presentationSink: recordingSink);
        IWeaponBehaviour fallback = WeaponBehaviourFactory.Create(
            data,
            targeting: null,
            projectilePool: null,
            spawn: null,
            movement: null);

        Assert.That(configured, Is.InstanceOf<IWeaponPresentationReceiver>());
        Assert.That(
            ((IWeaponPresentationReceiver)configured).PresentationSink,
            Is.SameAs(recordingSink));
        Assert.That(
            ((IWeaponPresentationReceiver)fallback).PresentationSink,
            Is.SameAs(NullWeaponPresentationSink.Instance));
    }

    [Test]
    public void CannonProjectile_EmitsAfterSuccessfulSpawnAndConfirmedImpactThenResets()
    {
        GameObject spawnObject = CreateGameObject("Presentation cannon spawn");
        GameObject ownerObject = CreateGameObject("Presentation cannon owner");
        ProjectilePool pool = CreateProjectilePool();
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponType = WeaponType.AutomaticCannon;
        data.BaseDamage = 10f;
        data.BaseManualAmmo = 5f;
        data.BaseRange = 12f;
        data.EnsureSpecificTuningForCurrentType();
        _createdObjects.Add(data);

        WeaponInstance instance = new()
        {
            Data = data,
            State = WeaponState.Manual,
            CurrentAmmo = 5f
        };
        RecordingPresentationSink recordingSink = new();
        AutomaticCannonWeapon weapon = new(null, pool, spawnObject.transform);
        weapon.SetPresentationSink(recordingSink);
        weapon.Setup(instance, ownerObject.transform, stats: null, heat: null);

        weapon.TickManual(1f, Vector3.forward, isFiring: true);

        Assert.That(recordingSink.Count(WeaponPresentationCue.AutomaticCannonManualShot), Is.EqualTo(1));
        Assert.That(recordingSink.Count(WeaponPresentationCue.AutomaticCannonManualVolley), Is.EqualTo(1));

        Projectile projectile = FindActivePooledProjectile();
        Assert.That(projectile, Is.Not.Null);
        Assert.That(projectile.HasPresentationContext, Is.True);

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.name = "Presentation impact target";
        _createdObjects.Add(target);
        PresentationTestDamageable damageable = target.AddComponent<PresentationTestDamageable>();
        Collider targetCollider = target.GetComponent<Collider>();

        InvokePrivate(projectile, "OnTriggerEnter", targetCollider);

        Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
        Assert.That(recordingSink.Count(WeaponPresentationCue.AutomaticCannonImpact), Is.EqualTo(1));
        Assert.That(projectile.HasPresentationContext, Is.False);
        Assert.That(pool.ActiveLeasedCount, Is.Zero);
    }

    [Test]
    public void CannonPresentation_DoesNotEmitWhenProjectileSpawnFails()
    {
        GameObject spawnObject = CreateGameObject("Failed presentation cannon spawn");
        GameObject ownerObject = CreateGameObject("Failed presentation cannon owner");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponType = WeaponType.AutomaticCannon;
        data.BaseManualAmmo = 5f;
        data.EnsureSpecificTuningForCurrentType();
        _createdObjects.Add(data);

        WeaponInstance instance = new()
        {
            Data = data,
            State = WeaponState.Manual,
            CurrentAmmo = 5f
        };
        RecordingPresentationSink recordingSink = new();
        AutomaticCannonWeapon weapon = new(null, null, spawnObject.transform);
        weapon.SetPresentationSink(recordingSink);
        weapon.Setup(instance, ownerObject.transform, stats: null, heat: null);

        weapon.TickManual(1f, Vector3.forward, isFiring: true);

        Assert.That(recordingSink.Contexts, Is.Empty);
    }

    private WeaponPresentationProfile CreateProfile()
    {
        WeaponPresentationProfile profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
        _createdObjects.Add(profile);
        return profile;
    }

    private GameObject CreateGameObject(string name)
    {
        GameObject gameObject = new(name);
        _createdObjects.Add(gameObject);
        return gameObject;
    }

    private ProjectilePool CreateProjectilePool(int maxPoolSize = 1)
    {
        GameObject prefab = CreateGameObject("Presentation projectile prefab");
        prefab.AddComponent<Rigidbody>();
        prefab.AddComponent<SphereCollider>();
        prefab.AddComponent<Projectile>();
        prefab.SetActive(false);

        GameObject container = CreateGameObject("Presentation projectile container");
        GameObject poolObject = CreateGameObject("Presentation projectile pool");
        poolObject.SetActive(false);
        ProjectilePool pool = poolObject.AddComponent<ProjectilePool>();
        SetPrivateField(pool, "_projectilePrefab", prefab);
        SetPrivateField(pool, "_container", container.transform);
        SetPrivateField(pool, "_initialPoolSize", 1);
        SetPrivateField(pool, "_maxPoolSize", Mathf.Max(1, maxPoolSize));
        SetPrivateField(pool, "_allowPoolGrowth", maxPoolSize > 1);
        poolObject.SetActive(true);
        InvokePrivate(pool, "Awake");
        return pool;
    }

    private static Projectile FindActivePooledProjectile()
    {
        Projectile[] projectiles = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (projectiles[i].gameObject.activeSelf &&
                projectiles[i].GetComponent<ProjectilePoolMember>() != null)
            {
                return projectiles[i];
            }
        }

        return null;
    }

    private static List<Projectile> FindActivePooledProjectiles()
    {
        Projectile[] projectiles = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        List<Projectile> active = new();
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (projectiles[i].gameObject.activeSelf &&
                projectiles[i].GetComponent<ProjectilePoolMember>() != null)
            {
                active.Add(projectiles[i]);
            }
        }

        return active;
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
        return (T)field.GetValue(target);
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName}");
        method.Invoke(target, null);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName}");
        method.Invoke(target, arguments);
    }

    private sealed class RecordingPresentationSink : IWeaponPresentationSink
    {
        public readonly List<WeaponPresentationContext> Contexts = new();

        public void Emit(in WeaponPresentationContext context)
        {
            Contexts.Add(context);
        }

        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)
        {
            Contexts.Add(context);
            return new WeaponPresentationLoopHandle(1);
        }

        public void UpdateLoop(
            WeaponPresentationLoopHandle handle,
            in WeaponPresentationContext context)
        {
        }

        public void EndLoop(
            WeaponPresentationLoopHandle handle,
            in WeaponPresentationContext context)
        {
        }

        public int Count(WeaponPresentationCue cue)
        {
            int count = 0;
            for (int i = 0; i < Contexts.Count; i++)
            {
                if (Contexts[i].Cue == cue)
                    count++;
            }

            return count;
        }
    }

    private sealed class PresentationTestDamageable : MonoBehaviour, IDamageable
    {
        public int TotalDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            TotalDamage += amount;
            return true;
        }
    }

    private sealed class FixedTargeting : IWeaponTargeting
    {
        private readonly Transform _target;

        public FixedTargeting(Transform target)
        {
            _target = target;
        }

        public bool TryGetTarget(
            WeaponInstance weapon,
            Transform owner,
            float range,
            Vector3 aimDirection,
            out Transform target)
        {
            target = _target;
            return target != null;
        }
    }
}
