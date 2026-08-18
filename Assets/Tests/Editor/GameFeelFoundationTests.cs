using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class GameFeelFoundationTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset";

    [Test]
    public void CannonProfile_ResolvesSemanticModesPathsAndConfirmedHitAccents()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset");
        Assert.That(profile, Is.Not.Null);
        Assert.That(data, Is.Not.Null);

        WeaponInstance weapon = new() { Data = data, Level = 1, State = WeaponState.Automatic };
        WeaponFeedbackContext automatic = new(weapon, WeaponFeedbackMode.Automatic, 0.5f, Vector3.zero, Vector3.forward);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.ShotFired, in automatic, out WeaponPresentationCueData autoCue), Is.True);
        Assert.That(autoCue.Cue, Is.EqualTo(WeaponPresentationCue.AutomaticCannonAutoShot));

        weapon.Level = 6;
        weapon.SelectedPath = WeaponUpgradePath.PathA;
        WeaponFeedbackContext continuous = new(weapon, WeaponFeedbackMode.Manual, 0.8f, Vector3.zero, Vector3.forward);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.ShotFired, in continuous, out WeaponPresentationCueData continuousCue), Is.True);
        Assert.That(continuousCue.Cue, Is.EqualTo(WeaponPresentationCue.AutomaticCannonContinuousShot));

        weapon.SelectedPath = WeaponUpgradePath.PathB;
        WeaponFeedbackContext weakPoint = new(
            weapon,
            WeaponFeedbackMode.Manual,
            1f,
            Vector3.zero,
            Vector3.forward,
            damageAmount: 40,
            isWeakPoint: true,
            targetClass: WeaponEnemyKind.Elite,
            surfaceType: ImpactSurfaceType.EnemyMetal);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.ProjectileImpact, in weakPoint, out WeaponPresentationCueData weakCue), Is.True);
        Assert.That(weakCue.Cue, Is.EqualTo(WeaponPresentationCue.AutomaticCannonWeakPointImpact));
        Assert.That(weakCue.HitStopDuration, Is.InRange(0.018f, 0.03f));

        WeaponFeedbackContext normalHit = new(
            weapon,
            WeaponFeedbackMode.Manual,
            0.5f,
            Vector3.zero,
            Vector3.forward,
            damageAmount: 10,
            surfaceType: ImpactSurfaceType.EnemyMetal);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.ProjectileImpact, in normalHit, out _), Is.True);
        Assert.That(
            profile.TryResolveCue(WeaponFeedbackEvent.DamageConfirmed, in normalHit, out _),
            Is.False,
            "A normal bullet must not spawn the same impact VFX twice.");

        WeaponFeedbackContext kill = new(
            weapon,
            WeaponFeedbackMode.Manual,
            0.5f,
            Vector3.zero,
            Vector3.forward,
            damageAmount: 10,
            isKill: true,
            surfaceType: ImpactSurfaceType.EnemyMetal);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.DamageConfirmed, in kill, out WeaponPresentationCueData killCue), Is.True);
        Assert.That(killCue.Cue, Is.EqualTo(WeaponPresentationCue.AutomaticCannonKillImpact));
    }

    [Test]
    public void CannonProfile_ContainsHeatCurvesQualityDensityAndProjectileArchetypes()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.QualitySettings, Is.Not.Null);
        Assert.That(profile.Heat.Emission.Evaluate(1f), Is.GreaterThan(profile.Heat.Emission.Evaluate(0f)));
        Assert.That(profile.Heat.AudioPitch.Evaluate(1f), Is.GreaterThan(profile.Heat.AudioPitch.Evaluate(0f)));
        Assert.That(profile.Density.GetSecondaryLimit(GameFeelQualityLevel.Low), Is.LessThan(profile.Density.GetSecondaryLimit(GameFeelQualityLevel.High)));

        foreach (ProjectilePresentationArchetypeId id in new[]
                 {
                     ProjectilePresentationArchetypeId.CannonRound,
                     ProjectilePresentationArchetypeId.CannonTracer,
                     ProjectilePresentationArchetypeId.HeadHunterBolt
                 })
        {
            Assert.That(profile.TryGetProjectileArchetype(id, out ProjectileArchetypePresentation archetype), Is.True, id.ToString());
            Assert.That(archetype.Mesh, Is.Not.Null);
            Assert.That(archetype.Material, Is.Not.Null);
            Assert.That(archetype.TrailMaterial, Is.Not.Null);
        }

        Assert.That(profile.TryGetProjectileArchetype(
            ProjectilePresentationArchetypeId.CannonRound,
            out ProjectileArchetypePresentation round), Is.True);
        Assert.That(round.TrailLifetime, Is.InRange(0.02f, 0.035f), "Rounds need a short dash that remains visibly separate from the next shot.");
        Assert.That(round.TrailStartWidth, Is.InRange(0.015f, 0.03f));

        Assert.That(profile.TryGetProjectileArchetype(
            ProjectilePresentationArchetypeId.CannonTracer,
            out ProjectileArchetypePresentation tracer), Is.True);
        Assert.That(tracer.TrailLifetime, Is.InRange(0.04f, 0.08f));
        Assert.That(tracer.TrailStartWidth, Is.InRange(0.02f, 0.05f));
        Assert.That(round.TrailLifetime, Is.LessThan(tracer.TrailLifetime), "Only the selected tracer should draw a long trail.");
    }

    [Test]
    public void ProductionWeaponProfiles_RestorePreGameFeelAudioContract()
    {
        AudioClip originalShoot = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/shoot.wav");
        WeaponPresentationProfile cannon = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(originalShoot, Is.Not.Null);
        Assert.That(cannon, Is.Not.Null);

        Dictionary<WeaponPresentationCue, Vector3> originalCannonCues = new()
        {
            { WeaponPresentationCue.AutomaticCannonAutoShot, new Vector3(0.4f, 0.96f, 1.04f) },
            { WeaponPresentationCue.AutomaticCannonManualShot, new Vector3(0.5f, 0.94f, 1.02f) },
            { WeaponPresentationCue.AutomaticCannonBaseActive, new Vector3(1f, 0.92f, 1f) },
            { WeaponPresentationCue.AutomaticCannonContinuousShot, new Vector3(0.18f, 1f, 1.08f) },
            { WeaponPresentationCue.AutomaticCannonContinuousActive, new Vector3(1f, 0.98f, 1.04f) },
            { WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic, new Vector3(0.8f, 0.98f, 1.02f) },
            { WeaponPresentationCue.AutomaticCannonHeadHunterManual, new Vector3(1f, 0.96f, 1f) },
            { WeaponPresentationCue.AutomaticCannonHeadHunterActive, new Vector3(1f, 0.94f, 0.98f) }
        };

        foreach (WeaponPresentationCueData cue in cannon.Cues)
        {
            Assert.That(cue.LayerAudioClips, Is.False, cue.Cue.ToString());
            Assert.That(cue.ApplyHeatStrainToMechanicalLayer, Is.False, cue.Cue.ToString());
            Assert.That(cue.ApplyEventIntensityToPitch, Is.False, cue.Cue.ToString());

            if (originalCannonCues.TryGetValue(cue.Cue, out Vector3 expected))
            {
                Assert.That(cue.AudioClips, Has.Count.EqualTo(1), cue.Cue.ToString());
                Assert.That(cue.AudioClips[0], Is.SameAs(originalShoot), cue.Cue.ToString());
                Assert.That(cue.Volume, Is.EqualTo(expected.x).Within(0.001f), cue.Cue.ToString());
                Assert.That(cue.PitchMin, Is.EqualTo(expected.y).Within(0.001f), cue.Cue.ToString());
                Assert.That(cue.PitchMax, Is.EqualTo(expected.z).Within(0.001f), cue.Cue.ToString());
            }
            else
            {
                Assert.That(cue.AudioClips, Is.Empty, cue.Cue.ToString());
            }
        }

        Assert.That(
            cannon.Cues.Count(cue => cue.AudioClips.Count > 0),
            Is.EqualTo(originalCannonCues.Count));

        foreach (string path in new[]
                 {
                     "Assets/ScriptableObjects/WeaponPresentation/RocketLauncherPresentation.asset",
                     "Assets/ScriptableObjects/WeaponPresentation/FlamethrowerPresentation.asset",
                     "Assets/ScriptableObjects/WeaponPresentation/MortarPresentation.asset",
                     "Assets/ScriptableObjects/WeaponPresentation/RotatingBladePresentation.asset"
                 })
        {
            WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(path);
            Assert.That(profile, Is.Not.Null, path);
            foreach (WeaponPresentationCueData cue in profile.Cues)
            {
                Assert.That(cue.AudioClips, Is.Empty, $"{path}: {cue.Cue}");
                Assert.That(cue.LayerAudioClips, Is.False, $"{path}: {cue.Cue}");
                Assert.That(cue.ApplyHeatStrainToMechanicalLayer, Is.False, $"{path}: {cue.Cue}");
                Assert.That(cue.ApplyEventIntensityToPitch, Is.False, $"{path}: {cue.Cue}");
            }
        }
    }

    [Test]
    public void SandboxHeatOverride_MapsDisplayedPercentAcrossBothHeatSegments()
    {
        GameObject heatObject = new("Heat override test");
        GameObject overrideObject = new("Heat control test");
        try
        {
            HeatManager heat = heatObject.AddComponent<HeatManager>();
            WeaponHeatOverride heatOverride = overrideObject.AddComponent<WeaponHeatOverride>();
            heatOverride.Bind(heat);

            foreach (float expectedPercent in new[] { 0f, 25f, 50f, 75f, 80f, 90f, 100f })
            {
                heatOverride.SetHeatPercent(expectedPercent);
                Assert.That(
                    heatOverride.NormalizedHeat * 100f,
                    Is.EqualTo(expectedPercent).Within(0.001f),
                    $"Displayed heat should remain stable at {expectedPercent}%.");
            }
        }
        finally
        {
            Object.DestroyImmediate(overrideObject);
            Object.DestroyImmediate(heatObject);
        }
    }

    [Test]
    public void PlayerRecoilAnchor_DoesNotAddVisiblePlaceholderGeometry()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(player, Is.Not.Null);

        WeaponRecoilFeedback recoil = player.GetComponent<WeaponRecoilFeedback>();
        Assert.That(recoil, Is.Not.Null);
        Assert.That(recoil.RecoilRoot, Is.Not.Null);
        Assert.That(recoil.RecoilRoot.GetComponentsInChildren<Renderer>(true), Is.Empty);
    }

    [Test]
    public void WeaponAudioDirector_PlaysAuthoredShotAndMechanicalClipsAsLayers()
    {
        GameObject root = new("Layered cannon audio test");
        AudioClip shot = AudioClip.Create("shot", 64, 1, 44100, false);
        AudioClip mechanical = AudioClip.Create("mechanical", 64, 1, 44100, false);
        try
        {
            WeaponPresentationCueData cue = new()
            {
                AudioClips = new List<AudioClip> { shot, mechanical },
                LayerAudioClips = true,
                MechanicalLayerVolume = 0.7f,
                Volume = 1f,
                PitchMin = 1f,
                PitchMax = 1f
            };
            GameFeelRuntimeOptions options = new();
            WeaponAudioDirector director = new(
                root.transform,
                capacity: 4,
                spatialBlend: 1f,
                heat: new WeaponHeatPresentationSettings(),
                options: options);
            WeaponFeedbackContext context = new(
                weapon: null,
                mode: WeaponFeedbackMode.Manual,
                normalizedHeat: 0.5f,
                origin: Vector3.zero,
                direction: Vector3.forward);

            Assert.That(director.TryPlayOneShot(cue, in context, 1f, 0f, out _), Is.True);
            Assert.That(director.ActiveCount, Is.EqualTo(2));
            director.ReleaseAll();
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(mechanical);
        }
    }

    [Test]
    public void ProductionProjectile_UsesAnAuthoredVisualControllerWithoutChangingGameplayRootCollider()
    {
        GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectile.prefab");
        Assert.That(projectile, Is.Not.Null);
        Assert.That(projectile.GetComponent<Projectile>(), Is.Not.Null);
        Assert.That(projectile.GetComponent<SphereCollider>(), Is.Not.Null);
        Assert.That(projectile.GetComponent<ProjectileVisualController>(), Is.Not.Null);
        Assert.That(projectile.GetComponent<MeshRenderer>(), Is.Null);
        Assert.That(projectile.transform.Find("Visual"), Is.Not.Null);
        Assert.That(projectile.GetComponentInChildren<TrailRenderer>(true), Is.Not.Null);
    }

    [Test]
    public void ProductionEnemies_HaveInspectorAuthoredHitAndDeathFeedback()
    {
        string[] enemyPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return prefab != null &&
                       (prefab.GetComponentInChildren<EnemyHealth>(true) != null ||
                        prefab.GetComponentInChildren<WeaponDummyEnemy>(true) != null);
            })
            .ToArray();

        Assert.That(enemyPaths, Is.Not.Empty);
        foreach (string path in enemyPaths)
        {
            GameObject enemy = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(enemy.GetComponent<EnemyHitFeedback>(), Is.Not.Null, path);
            Assert.That(enemy.GetComponent<EnemyDeathFeedback>(), Is.Not.Null, path);
            Assert.That(enemy.GetComponent<EnemyStatusFeedback>(), Is.Not.Null, path);
        }
    }

    [Test]
    public void EnemyHitFeedback_PreservesExistingMaterialColorOverrides()
    {
        GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Renderer renderer = enemy.GetComponent<Renderer>();
            int baseColorId = Shader.PropertyToID("_BaseColor");
            int colorId = Shader.PropertyToID("_Color");
            Color authoredBaseColor = new(0.16f, 0.42f, 0.73f, 1f);
            Color authoredLegacyColor = new(0.64f, 0.21f, 0.37f, 1f);
            MaterialPropertyBlock block = new();
            block.SetColor(baseColorId, authoredBaseColor);
            block.SetColor(colorId, authoredLegacyColor);
            renderer.SetPropertyBlock(block);

            EnemyHitFeedback feedback = enemy.AddComponent<EnemyHitFeedback>();
            WeaponFeedbackContext context = new(
                weapon: null,
                mode: WeaponFeedbackMode.Automatic,
                normalizedHeat: 0f,
                origin: Vector3.back,
                direction: Vector3.forward,
                target: enemy.transform);
            feedback.Play(in context, reducedFlash: false);

            renderer.GetPropertyBlock(block);
            Assert.That(Vector4.Distance(block.GetColor(baseColorId), authoredBaseColor), Is.LessThan(0.0001f));
            Assert.That(Vector4.Distance(block.GetColor(colorId), authoredLegacyColor), Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(enemy);
        }
    }

    [Test]
    public void GameFeelSandbox_IsASeparateSceneWithOriginalPreserved()
    {
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/WeaponTestingSandbox.unity"), Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity"), Is.Not.Null);
        Assert.That(
            AssetDatabase.AssetPathToGUID("Assets/Scenes/WeaponTestingSandbox.unity"),
            Is.Not.EqualTo(AssetDatabase.AssetPathToGUID("Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity")));
    }
}
