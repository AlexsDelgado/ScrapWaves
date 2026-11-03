using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Production projectile geometry is authoritative; presentation follows physical growth once.</summary>
public sealed class ProjectileVisualSizeTests
{
    private readonly List<GameObject> _created = new();
    private ProjectilePool _pool;
    private GameObject _prefab;
    private WeaponPresentationProfile _rocketProfile;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectile.prefab");
        _rocketProfile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(
            "Assets/ScriptableObjects/WeaponPresentation/RocketLauncherPresentation.asset");
        Assert.That(_prefab, Is.Not.Null);
        Assert.That(_rocketProfile, Is.Not.Null);
        GameObject container = NewObject("Projectile visual size instances");
        GameObject poolRoot = NewObject("Projectile visual size pool");
        poolRoot.SetActive(false);
        _pool = poolRoot.AddComponent<ProjectilePool>();
        Set(_pool, "_projectilePrefab", _prefab);
        Set(_pool, "_container", container.transform);
        Set(_pool, "_initialPoolSize", 1);
        Set(_pool, "_maxPoolSize", 2);
        poolRoot.SetActive(true);
        Invoke(_pool, "Awake");
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    [TestCase(ProjectilePresentationArchetypeId.CannonRound)]
    [TestCase(ProjectilePresentationArchetypeId.Rocket)]
    public void OrdinaryFlight_LargerExplosionEnvelopeDoesNotEnlargeItsFixedColliderOrBody(
        ProjectilePresentationArchetypeId id)
    {
        Projectile projectile = SpawnOrdinary();
        WeaponPresentationProfile profile = id == ProjectilePresentationArchetypeId.CannonRound
            ? AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(
                "Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset")
            : _rocketProfile;
        Assert.That(profile.TryGetProjectileArchetype(id, out ProjectileArchetypePresentation archetype), Is.True);
        ProjectileVisualController view = projectile.GetComponent<ProjectileVisualController>();
        Transform visual = projectile.transform.Find("Visual");
        Vector3 rootScale = projectile.transform.localScale;
        Quaternion rootRotation = projectile.transform.localRotation;
        float colliderRadius = projectile.GetComponent<SphereCollider>().radius;

        projectile.ConfigureExplosion(2f, .5f);
        view.Apply(archetype, profile.Heat, .6f, true, GameFeelQualityLevel.High, profile.QualitySettings);
        Vector3 firstSize = visual.GetComponent<MeshRenderer>().bounds.size;
        float firstTrail = visual.GetComponentInChildren<TrailRenderer>(true).startWidth;
        projectile.ConfigureExplosion(20f, .5f);
        view.Apply(archetype, profile.Heat, .6f, true, GameFeelQualityLevel.High, profile.QualitySettings);

        AssertVector(visual.GetComponent<MeshRenderer>().bounds.size, firstSize);
        Assert.That(visual.GetComponentInChildren<TrailRenderer>(true).startWidth, Is.EqualTo(firstTrail).Within(.0001f));
        Assert.That(projectile.CollisionSizeMultiplier, Is.EqualTo(1f).Within(.0001f));
        AssertGameplayUnchanged(projectile, rootScale, rootRotation, colliderRadius);
    }

    [TestCase(GameFeelQualityLevel.Low, false)]
    [TestCase(GameFeelQualityLevel.Medium, false)]
    [TestCase(GameFeelQualityLevel.High, false)]
    [TestCase(GameFeelQualityLevel.Low, true)]
    [TestCase(GameFeelQualityLevel.Medium, true)]
    [TestCase(GameFeelQualityLevel.High, true)]
    public void ClusterParent_FirstPresentationAfterPhysicalGrowth_ScalesMeshTrailAndSmokeExactlyOnce(
        GameFeelQualityLevel quality, bool heatEnabled)
    {
        Projectile ordinary = SpawnOrdinary();
        Projectile cluster = SpawnCluster();
        Assert.That(_rocketProfile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.ClusterRocket,
            out ProjectileArchetypePresentation archetype), Is.True);
        float colliderBefore = WorldCollisionRadius(ordinary);
        float colliderAfter = WorldCollisionRadius(cluster);
        Assert.That(colliderAfter / colliderBefore, Is.EqualTo(5f).Within(.0001f));
        Assert.That(cluster.CollisionSizeMultiplier, Is.EqualTo(5f).Within(.0001f));
        Vector3 rootScale = cluster.transform.localScale;
        Quaternion rootRotation = cluster.transform.localRotation;
        float localRadius = cluster.GetComponent<SphereCollider>().radius;

        Apply(ordinary, archetype, quality, heatEnabled);
        Apply(cluster, archetype, quality, heatEnabled);
        Transform ordinaryVisual = ordinary.transform.Find("Visual");
        Transform clusterVisual = cluster.transform.Find("Visual");
        AssertVector(clusterVisual.localScale, ordinaryVisual.localScale);
        AssertVector(clusterVisual.GetComponent<MeshRenderer>().bounds.size,
            ordinaryVisual.GetComponent<MeshRenderer>().bounds.size * 5f);
        TrailRenderer ordinaryTrail = ordinaryVisual.GetComponentInChildren<TrailRenderer>(true);
        TrailRenderer clusterTrail = clusterVisual.GetComponentInChildren<TrailRenderer>(true);
        Assert.That(clusterTrail.startWidth, Is.EqualTo(ordinaryTrail.startWidth * 5f).Within(.0001f));
        Assert.That(clusterTrail.endWidth, Is.EqualTo(ordinaryTrail.endWidth * 5f).Within(.0001f));
        float expectedHeat = _rocketProfile.Heat.TrailWidth.Evaluate(heatEnabled ? .75f : 0f);
        Assert.That(clusterTrail.startWidth, Is.EqualTo(archetype.TrailStartWidth * expectedHeat * 5f).Within(.0001f));

        ParticleSystem ordinarySmoke = Smoke(ordinary);
        ParticleSystem clusterSmoke = Smoke(cluster);
        Assert.That(clusterSmoke.main.scalingMode, Is.EqualTo(ParticleSystemScalingMode.Local));
        Assert.That(clusterSmoke.main.startSize.constant, Is.EqualTo(ordinarySmoke.main.startSize.constant * 5f).Within(.0001f));
        AssertSpeedRatio(clusterSmoke.main.startSpeed, ordinarySmoke.main.startSpeed, 5f);
        AssertVector(clusterSmoke.shape.scale, ordinarySmoke.shape.scale * 5f);
        Assert.That(clusterSmoke.main.startLifetime.constant, Is.EqualTo(ordinarySmoke.main.startLifetime.constant));
        Assert.That(clusterSmoke.emission.rateOverTime.constant, Is.EqualTo(ordinarySmoke.emission.rateOverTime.constant));

        Apply(cluster, archetype, quality, heatEnabled);
        Assert.That(clusterTrail.startWidth, Is.EqualTo(ordinaryTrail.startWidth * 5f).Within(.0001f));
        AssertSpeedRatio(clusterSmoke.main.startSpeed, ordinarySmoke.main.startSpeed, 5f);
        AssertGameplayUnchanged(cluster, rootScale, rootRotation, localRadius);
        Assert.That(WorldCollisionRadius(cluster), Is.EqualTo(colliderAfter).Within(.0001f));
    }

    [Test]
    public void PoolReuse_ClusterToOrdinaryRestoresAuthoredDimensionsAndCollisionBaseline()
    {
        Projectile cluster = SpawnCluster();
        Assert.That(_rocketProfile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.ClusterRocket,
            out ProjectileArchetypePresentation clusterArt), Is.True);
        Apply(cluster, clusterArt, GameFeelQualityLevel.High, true);
        _pool.Release(cluster.gameObject);
        ParticleSystem prefabSmoke = _prefab.GetComponentInChildren<ParticleSystem>(true);
        AssertSpeedRatio(Smoke(cluster).main.startSpeed, prefabSmoke.main.startSpeed, 1f);
        AssertVector(Smoke(cluster).shape.scale, prefabSmoke.shape.scale);

        Projectile ordinary = SpawnOrdinary();
        Assert.That(ordinary, Is.SameAs(cluster), "The regression must exercise the same pooled instance.");
        Assert.That(ordinary.CollisionSizeMultiplier, Is.EqualTo(1f).Within(.0001f));
        Assert.That(_rocketProfile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.Rocket,
            out ProjectileArchetypePresentation rocketArt), Is.True);
        Apply(ordinary, rocketArt, GameFeelQualityLevel.Low, false);
        AssertVector(ordinary.transform.localScale, _prefab.transform.localScale);
        AssertVector(ordinary.transform.Find("Visual").localScale, rocketArt.LocalScale);
        Assert.That(Smoke(ordinary).main.startSize.constant, Is.EqualTo(rocketArt.FlightSmokeSize).Within(.0001f));
        AssertSpeedRatio(Smoke(ordinary).main.startSpeed, prefabSmoke.main.startSpeed, 1f);
        AssertVector(Smoke(ordinary).shape.scale, prefabSmoke.shape.scale);
        Assert.That(ordinary.GetComponentInChildren<TrailRenderer>(true).startWidth,
            Is.EqualTo(rocketArt.TrailStartWidth * _rocketProfile.Heat.TrailWidth.Evaluate(0f)).Within(.0001f));
    }

    [Test]
    public void HierarchySmoke_DoesNotMultiplyThePhysicalGrowthItAlreadyInherits()
    {
        Projectile cluster = SpawnCluster();
        ParticleSystem smoke = Smoke(cluster);
        ParticleSystem.MainModule main = smoke.main;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        ParticleSystem prefabSmoke = _prefab.GetComponentInChildren<ParticleSystem>(true);
        Assert.That(_rocketProfile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.ClusterRocket,
            out ProjectileArchetypePresentation archetype), Is.True);
        Apply(cluster, archetype, GameFeelQualityLevel.High, true);
        Assert.That(smoke.main.startSize.constant, Is.EqualTo(archetype.FlightSmokeSize).Within(.0001f));
        AssertSpeedRatio(smoke.main.startSpeed, prefabSmoke.main.startSpeed, 1f);
        AssertVector(smoke.shape.scale, prefabSmoke.shape.scale);
    }

    [Test]
    public void MissingVisualChild_ApplyAndResetNeverChangeGameplayRootScaleRotationOrCollider()
    {
        GameObject root = NewObject("Malformed projectile without a visual child");
        root.transform.localScale = new Vector3(.3f, .4f, .5f);
        root.transform.localRotation = Quaternion.Euler(17f, 23f, 31f);
        Projectile projectile = root.AddComponent<Projectile>();
        ProjectileVisualController view = root.AddComponent<ProjectileVisualController>();
        projectile.Launch(Vector3.forward);
        Vector3 scale = root.transform.localScale;
        Quaternion rotation = root.transform.localRotation;
        float radius = root.GetComponent<SphereCollider>().radius;
        var archetype = new ProjectileArchetypePresentation
        {
            LocalScale = new Vector3(3f, 4f, 5f),
            LocalEulerAngles = new Vector3(90f, 80f, 70f)
        };
        view.Apply(archetype, null, 0f, false, GameFeelQualityLevel.High, null);
        AssertGameplayUnchanged(projectile, scale, rotation, radius);
        view.ResetVisual();
        AssertGameplayUnchanged(projectile, scale, rotation, radius);
    }

    private Projectile SpawnOrdinary()
    {
        Assert.That(_pool.TrySpawnProjectile(Vector3.zero, Quaternion.identity, Vector3.forward,
            2, 0f, default, out Projectile projectile), Is.True);
        return projectile;
    }

    private Projectile SpawnCluster()
    {
        Assert.That(_pool.TrySpawnExplosiveProjectileWithAmplifierAndCluster(
            position: Vector3.right * 10f, rotation: Quaternion.identity, fireDirection: Vector3.forward,
            damage: 2, explosionRadius: 3f, falloff: .5f, knockback: 0f, speedMultiplier: 1f,
            maxTravelDistance: 30f, explodeOnMaxTravel: true, amplifierMultiplier: 1f, amplifierDuration: 0f,
            fragmentConeAngle: 0f, fragmentConeRange: 0f, fragmentDamageScale: 0f,
            clusterProjectileCount: 3, clusterDamage: 1, clusterExplosionRadius: 1f, clusterFalloff: .5f,
            clusterKnockback: 0f, clusterSpeedMultiplier: 1f, clusterTravelDistance: 5f,
            clusterFragmentConeAngle: 0f, clusterFragmentConeRange: 0f, clusterFragmentDamageScale: 0f,
            visualScaleMultiplier: 5f, damageContext: default, clusterDamageContext: default,
            spawnedProjectile: out Projectile projectile), Is.True);
        return projectile;
    }

    private void Apply(Projectile projectile, ProjectileArchetypePresentation archetype,
        GameFeelQualityLevel quality, bool heatEnabled) =>
        projectile.GetComponent<ProjectileVisualController>().Apply(archetype, _rocketProfile.Heat, .75f,
            heatEnabled, quality, _rocketProfile.QualitySettings);

    private static ParticleSystem Smoke(Projectile projectile) => projectile.GetComponentInChildren<ParticleSystem>(true);

    private static float WorldCollisionRadius(Projectile projectile)
    {
        Physics.SyncTransforms();
        Vector3 extents = projectile.GetComponent<SphereCollider>().bounds.extents;
        return Mathf.Max(extents.x, extents.y, extents.z);
    }

    private static void AssertGameplayUnchanged(Projectile projectile, Vector3 scale, Quaternion rotation, float radius)
    {
        AssertVector(projectile.transform.localScale, scale);
        Assert.That(Quaternion.Angle(projectile.transform.localRotation, rotation), Is.LessThan(.001f));
        Assert.That(projectile.GetComponent<SphereCollider>().radius, Is.EqualTo(radius));
    }

    private static void AssertSpeedRatio(ParticleSystem.MinMaxCurve actual, ParticleSystem.MinMaxCurve baseline, float ratio)
    {
        Assert.That(actual.mode, Is.EqualTo(baseline.mode));
        Assert.That(actual.Evaluate(.25f, .3f), Is.EqualTo(baseline.Evaluate(.25f, .3f) * ratio).Within(.0001f));
        Assert.That(actual.Evaluate(.75f, .8f), Is.EqualTo(baseline.Evaluate(.75f, .8f) * ratio).Within(.0001f));
    }

    private GameObject NewObject(string name)
    {
        GameObject root = new(name);
        _created.Add(root);
        return root;
    }

    private static void AssertVector(Vector3 actual, Vector3 expected) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
