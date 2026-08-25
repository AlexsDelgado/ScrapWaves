using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class RocketLauncherPresentationTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/RocketLauncherPresentation.asset";

    [Test]
    public void ProductionRocketLauncher_ReferencesCompleteAuthoredProfile()
    {
        WeaponData rocket = AssetDatabase.LoadAssetAtPath<WeaponData>(
            "Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");
        WeaponData sandbox = AssetDatabase.LoadAssetAtPath<WeaponData>(
            "Assets/Scripts/Weapon/Testing/SO/Sandbox_RocketLauncher.asset");

        Assert.That(rocket, Is.Not.Null);
        Assert.That(sandbox, Is.Not.Null);
        Assert.That(rocket.PresentationProfile, Is.Not.Null);
        Assert.That(sandbox.PresentationProfile, Is.SameAs(rocket.PresentationProfile));
        Assert.That(rocket.PresentationProfile.WeaponType, Is.EqualTo(WeaponType.RocketLauncher));
        Assert.That(rocket.PresentationProfile.HasDuplicateCues, Is.False);

        WeaponPresentationCue[] required =
        {
            WeaponPresentationCue.RocketAutomaticLaunch,
            WeaponPresentationCue.RocketManualLaunch,
            WeaponPresentationCue.RocketActiveLaunch,
            WeaponPresentationCue.RocketClusterLaunch,
            WeaponPresentationCue.RocketClusterDetonation,
            WeaponPresentationCue.RocketFragmentChildImpact,
            WeaponPresentationCue.RocketTargetingLoop,
            WeaponPresentationCue.RocketLockAcquired,
            WeaponPresentationCue.RocketTargetingCancelled,
            WeaponPresentationCue.RocketImpact,
            WeaponPresentationCue.RocketKineticImpact,
            WeaponPresentationCue.RocketFragmentImpact,
            WeaponPresentationCue.RocketKineticStatus,
            WeaponPresentationCue.RocketKillImpact
        };
        for (int i = 0; i < required.Length; i++)
            Assert.That(rocket.PresentationProfile.TryGetCueData(required[i], out _), Is.True, required[i].ToString());
    }

    [Test]
    public void ProductionRocketProjectileArchetypes_AreDistinctAndDensitySafe()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.Rocket, out ProjectileArchetypePresentation rocket), Is.True);
        Assert.That(profile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.FragmentRocket, out ProjectileArchetypePresentation fragment), Is.True);
        Assert.That(profile.TryGetProjectileArchetype(ProjectilePresentationArchetypeId.ClusterRocket, out ProjectileArchetypePresentation cluster), Is.True);

        Assert.That(rocket.Mesh, Is.Not.Null);
        Assert.That(rocket.Material, Is.Not.Null);
        Assert.That(rocket.TrailMaterial, Is.Not.Null);
        Assert.That(rocket.FlightSmokeMaterial, Is.Not.Null);
        Assert.That(rocket.FlightSmokeRate, Is.GreaterThan(0f));
        Assert.That(fragment.LightIntensity, Is.Zero, "Twenty child rockets must not create twenty lights.");
        Assert.That(fragment.FlightSmokeRate, Is.Zero, "Child rockets stay lightweight under cluster density.");
        Assert.That(cluster.FlightSmokeRate, Is.GreaterThan(0f));
        Assert.That(cluster.LocalScale, Is.Not.EqualTo(fragment.LocalScale));
    }

    [Test]
    public void ProductionRocketImpactPrefabs_ContainLayeredMeshesAndParticles()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponPresentationCue[] impacts =
        {
            WeaponPresentationCue.RocketImpact,
            WeaponPresentationCue.RocketKineticImpact,
            WeaponPresentationCue.RocketFragmentImpact,
            WeaponPresentationCue.RocketKillImpact
        };
        for (int i = 0; i < impacts.Length; i++)
        {
            Assert.That(profile.TryGetCueData(impacts[i], out WeaponPresentationCueData cue), Is.True);
            Assert.That(cue.VfxPrefab, Is.Not.Null, impacts[i].ToString());
            RocketLauncherCueVfx vfx = cue.VfxPrefab.GetComponent<RocketLauncherCueVfx>();
            Assert.That(vfx, Is.Not.Null);
            Assert.That(vfx.RuntimeMeshLayerCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(vfx.RuntimeParticleSystemCount, Is.GreaterThanOrEqualTo(2));
        }
    }

    [Test]
    public void FragmentationImpact_PresentsTheSameForwardConeAsDamage()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile.TryGetCueData(
            WeaponPresentationCue.RocketFragmentImpact,
            out WeaponPresentationCueData fragmentImpact), Is.True);

        Transform root = fragmentImpact.VfxPrefab.transform;
        Transform explosionRadius = root.Find("Explosion Radius");
        Transform forwardMiniExplosions = root.Find("Forward Mini Explosions");
        Assert.That(explosionRadius, Is.Not.Null, "The central blast must show the real explosion radius.");
        Assert.That(explosionRadius.localScale, Is.EqualTo(Vector3.one * 2f));
        Assert.That(root.Find("Pressure Front"), Is.Not.Null, "The Fragmentation impact still needs a regular explosion.");
        Assert.That(forwardMiniExplosions, Is.Not.Null);
        Assert.That(forwardMiniExplosions.childCount, Is.EqualTo(7));
        Assert.That(root.Find("Forward Shrapnel Wedge"), Is.Null);
        Assert.That(root.Find("Forward Shrapnel Core"), Is.Null);

        float furthestVisibleDistance = 0f;
        for (int i = 0; i < forwardMiniExplosions.childCount; i++)
        {
            Transform miniExplosion = forwardMiniExplosions.GetChild(i);
            Vector3 localPosition = miniExplosion.localPosition;
            Assert.That(localPosition.z, Is.GreaterThan(0f));
            MeshFilter[] meshes = miniExplosion.GetComponentsInChildren<MeshFilter>(true);
            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
            {
                MeshFilter meshFilter = meshes[meshIndex];
                Vector3[] vertices = meshFilter.sharedMesh.vertices;
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    Vector3 point = forwardMiniExplosions.InverseTransformPoint(
                        meshFilter.transform.TransformPoint(vertices[vertexIndex]));
                    point.y = 0f;
                    float distance = point.magnitude;
                    furthestVisibleDistance = Mathf.Max(furthestVisibleDistance, distance);
                    bool insideMainExplosion = distance <= 0.25f + 0.001f;
                    bool insideForwardCone = distance <= 1f + 0.001f &&
                                             (distance <= 0.0001f ||
                                              Vector3.Angle(Vector3.forward, point) <= 22.5f + 0.001f);
                    Assert.That(insideMainExplosion || insideForwardCone, Is.True,
                        $"{miniExplosion.name}/{meshFilter.name} extends beyond both damage areas.");
                }
            }
        }
        Assert.That(furthestVisibleDistance, Is.GreaterThanOrEqualTo(0.98f),
            "The final mini explosion should visually reach the end of the damage cone.");

        SerializedObject fragmentVfx = new(fragmentImpact.VfxPrefab.GetComponent<RocketLauncherCueVfx>());
        Assert.That(fragmentVfx.FindProperty("_size").floatValue, Is.EqualTo(1f).Within(0.001f));
        Assert.That(fragmentVfx.FindProperty("_explosionRadiusMultiplier").floatValue,
            Is.EqualTo(1f).Within(0.001f));
        Assert.That(fragmentVfx.FindProperty("_forwardConeRangeMultiplier").floatValue,
            Is.EqualTo(4f).Within(0.001f));

        ParticleSystem debris = root.Find("Sparks and Mesh Debris").GetComponent<ParticleSystem>();
        Assert.That(debris.shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Cone));
        Assert.That(debris.shape.angle, Is.EqualTo(22.5f).Within(0.01f));

        Assert.That(profile.TryGetCueData(
            WeaponPresentationCue.RocketClusterDetonation,
            out WeaponPresentationCueData clusterDetonation), Is.True);
        ParticleSystem clusterDebris = clusterDetonation.VfxPrefab.transform
            .Find("Sparks and Mesh Debris")
            .GetComponent<ParticleSystem>();
        Assert.That(clusterDebris.shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Sphere),
            "The active casing still needs a radial cue because its child rockets fan out radially.");
        Assert.That(clusterDetonation.VfxPrefab.transform.Find("Forward Mini Explosions"), Is.Null);

        Assert.That(profile.TryGetCueData(
            WeaponPresentationCue.RocketFragmentChildImpact,
            out WeaponPresentationCueData childImpact), Is.True);
        Assert.That(childImpact.VfxPrefab, Is.SameAs(fragmentImpact.VfxPrefab));
        Assert.That(childImpact.MinReplayInterval, Is.Zero,
            "Simultaneous ability child impacts must not suppress one another.");
        Assert.That(childImpact.MaxSimultaneous, Is.EqualTo(20));
        Assert.That(childImpact.AudioClips, Is.Empty,
            "The dedicated child cue keeps every visual without stacking twenty impact sounds.");
        Assert.That(childImpact.CameraPositionImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(childImpact.CameraRotationImpulse, Is.EqualTo(Vector3.zero));
        Assert.That(childImpact.CameraFovKick, Is.Zero);
    }

    [Test]
    public void WorldSpaceVfx_DetachesFromMovingPresentationRootAndStaysAtImpact()
    {
        GameObject movingPresentationRoot = new("Moving Player Presentation Root");
        GameObject prefab = new("World Impact VFX");
        prefab.AddComponent<PooledWeaponVfx>();
        WeaponPresentationCueData cue = new()
        {
            Cue = WeaponPresentationCue.RocketFragmentImpact,
            VfxPrefab = prefab,
            Duration = 0.5f,
            PrewarmCount = 1,
            MaxSimultaneous = 1
        };

        try
        {
            WeaponVfxPool pool = new(cue, movingPresentationRoot.transform);
            Vector3 impactPosition = new(4f, 0f, 6f);
            WeaponPresentationContext context = new(
                cue.Cue,
                null,
                impactPosition,
                Vector3.forward);

            Assert.That(pool.TryPlay(in context, 0f, false, out PooledWeaponVfx instance), Is.True);
            Assert.That(instance.transform.parent, Is.Null);
            Assert.That(instance.transform.position, Is.EqualTo(impactPosition));

            movingPresentationRoot.transform.position = new Vector3(20f, 0f, 20f);
            Assert.That(instance.transform.position, Is.EqualTo(impactPosition),
                "World impacts must remain at the collision point when the player moves.");

            pool.ReleaseAll();
            Assert.That(instance.transform.parent, Is.SameAs(movingPresentationRoot.transform));
        }
        finally
        {
            Object.DestroyImmediate(movingPresentationRoot);
            Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void FragmentationAbility_AllTwentyChildImpactsCanPresentSimultaneously()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        GameObject runtimeRoot = new("Ability Child Impact Presentation Root");
        CombatFeedbackDirector director = null;
        try
        {
            GameFeelRuntimeOptions options = new()
            {
                AudioEnabled = false,
                CameraFeedbackEnabled = false,
                HitStopEnabled = false,
                EnemyReactionEnabled = false
            };
            director = new CombatFeedbackDirector(
                profile,
                runtimeRoot.transform,
                null,
                null,
                options,
                new CameraFeedbackController(),
                new HitStopController(),
                1,
                1f);

            for (int i = 0; i < 20; i++)
            {
                WeaponPresentationContext impact = new(
                    WeaponPresentationCue.RocketFragmentChildImpact,
                    null,
                    new Vector3(i, 0f, 4f),
                    Vector3.forward,
                    isAbility: true,
                    explosionRadius: 1f);
                Assert.That(director.EmitLegacy(in impact, 1f, 10f), Is.True,
                    $"Ability child impact {i + 1} was suppressed.");
            }

            Assert.That(director.ActiveVfxCount, Is.EqualTo(20));
        }
        finally
        {
            director?.StopAll();
            Object.DestroyImmediate(runtimeRoot);
        }
    }

    [Test]
    public void RocketFeedbackBindings_DistinguishBaseKineticAndFragmentation()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");
        WeaponInstance instance = new() { Data = data, Level = 6, State = WeaponState.Manual };

        instance.SelectedPath = WeaponUpgradePath.None;
        AssertResolved(profile, WeaponFeedbackEvent.ProjectileImpact, instance, WeaponPresentationCue.RocketImpact);
        instance.SelectedPath = WeaponUpgradePath.PathA;
        AssertResolved(profile, WeaponFeedbackEvent.ProjectileImpact, instance, WeaponPresentationCue.RocketKineticImpact);
        instance.SelectedPath = WeaponUpgradePath.PathB;
        AssertResolved(profile, WeaponFeedbackEvent.ProjectileImpact, instance, WeaponPresentationCue.RocketFragmentImpact);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, instance, WeaponPresentationCue.RocketManualLaunch);
    }

    [Test]
    public void RepeatLockMarker_UsesDistinctCountAndBracketGeometry()
    {
        GameObject target = new("Elite Lock Target");
        RocketTargetMarkerVfx marker = null;
        try
        {
            marker = RocketTargetMarkerVfx.Create(target.transform, 0.7f, 3);
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.LockCount, Is.EqualTo(3));
            Assert.That(marker.GetComponentsInChildren<LineRenderer>(true), Has.Length.EqualTo(5));
            marker.SetLockCount(1);
            Assert.That(marker.LockCount, Is.EqualTo(1));
        }
        finally
        {
            if (marker != null)
                Object.DestroyImmediate(marker.gameObject);
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void AutomaticRocketVolley_UsesSeparatedLaunchPoints()
    {
        MethodInfo method = typeof(RocketLauncherWeapon).GetMethod(
            "GetVolleyLaunchOffset",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        Vector3 first = (Vector3)method.Invoke(null, new object[] { 0, 4, Vector3.forward });
        Vector3 second = (Vector3)method.Invoke(null, new object[] { 1, 4, Vector3.forward });
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(first.magnitude, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(second.magnitude, Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void AuthoredRocketExplosion_EmitsImpactDamageAndKineticStatusAtDetonation()
    {
        GameObject projectileObject = new("Semantic Rocket");
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        try
        {
            projectileObject.AddComponent<Rigidbody>();
            projectileObject.AddComponent<SphereCollider>();
            Projectile projectile = projectileObject.AddComponent<Projectile>();
            target.transform.position = Vector3.right;
            TestDamageable damageable = target.AddComponent<TestDamageable>();
            RecordingFeedbackSink sink = new();
            Vector3 launchDirection = new Vector3(0f, 1f, 1f).normalized;
            WeaponFeedbackContext context = new(
                null,
                WeaponFeedbackMode.Manual,
                0f,
                projectileObject.transform.position,
                launchDirection,
                explosionRadius: 3f);

            projectile.ConfigurePooled(2f, 12, 0f);
            projectile.Launch(launchDirection);
            projectile.ConfigureExplosion(3f, 0.5f);
            projectile.ConfigureFragmentCone(45f, 12f, 0.5f);
            projectile.ConfigureDamageAmplifierOnExplosion(1.2f, 5f);
            projectile.ConfigureFeedback(sink, in context, replaceExplosionVfx: true);
            Physics.SyncTransforms();

            MethodInfo applyExplosion = typeof(Projectile).GetMethod(
                "ApplyExplosionDamage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            applyExplosion.Invoke(projectile, null);

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
            Assert.That(sink.ImpactCount, Is.EqualTo(1));
            Assert.That(sink.DamageCount, Is.EqualTo(1));
            Assert.That(sink.StatusCount, Is.EqualTo(1));
            Assert.That(sink.LastImpact.ImpactPosition, Is.EqualTo(projectileObject.transform.position));
            Assert.That(sink.LastImpact.Direction.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Vector3.Angle(sink.LastImpact.Direction, Vector3.forward), Is.LessThan(0.01f),
                "Fragment feedback must use the same ground-plane forward direction as cone damage.");
        }
        finally
        {
            Object.DestroyImmediate(projectileObject);
            Object.DestroyImmediate(target);
        }
    }

    private static void AssertResolved(
        WeaponPresentationProfile profile,
        WeaponFeedbackEvent feedbackEvent,
        WeaponInstance instance,
        WeaponPresentationCue expected)
    {
        WeaponFeedbackContext context = new(
            instance,
            WeaponFeedbackMode.Manual,
            0f,
            Vector3.zero,
            Vector3.forward,
            explosionRadius: 2f);
        Assert.That(profile.TryResolveCue(feedbackEvent, in context, out WeaponPresentationCueData cue), Is.True);
        Assert.That(cue.Cue, Is.EqualTo(expected));
    }

    private sealed class TestDamageable : MonoBehaviour, IAuthoritativeDamageable
    {
        private int _health = 100000;

        public int TotalDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            DamageRequest request = new(amount, amount, DamageChannel.Direct);
            return ApplyDamage(in request).Applied;
        }

        public DamageApplicationResult ApplyDamage(in DamageRequest request)
        {
            int before = _health;
            int after = Mathf.Max(0, before - request.ModifiedDamage);
            _health = after;
            TotalDamage += before - after;
            return DamageApplicationResult.FromHealthDelta(in request, before, after);
        }
    }

    private sealed class RecordingFeedbackSink : IWeaponFeedbackSink
    {
        public int ImpactCount { get; private set; }
        public int DamageCount { get; private set; }
        public int StatusCount { get; private set; }
        public WeaponFeedbackContext LastImpact { get; private set; }

        public void Emit(in WeaponPresentationContext context) { }
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context) => default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context) { }
        public void OnChargeStarted(in WeaponFeedbackContext context) { }
        public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress) { }
        public void OnChargeCancelled(in WeaponFeedbackContext context) { }
        public void OnShotFired(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
        public void OnProjectileImpact(in WeaponFeedbackContext context)
        {
            ImpactCount++;
            LastImpact = context;
        }
        public void OnDamageConfirmed(in WeaponFeedbackContext context) => DamageCount++;
        public void OnStatusApplied(in WeaponFeedbackContext context) => StatusCount++;
        public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
        public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold) { }
        public void ConfigureProjectile(
            Projectile projectile,
            ProjectilePresentationArchetypeId archetype,
            in WeaponFeedbackContext context) { }
    }
}
