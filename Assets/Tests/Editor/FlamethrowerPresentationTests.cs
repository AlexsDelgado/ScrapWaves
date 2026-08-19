using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class FlamethrowerPresentationTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/FlamethrowerPresentation.asset";

    [Test]
    public void ProductionFlamethrower_ReferencesCompleteAuthoredProfile()
    {
        WeaponData production = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        WeaponData sandbox = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Scripts/Weapon/Testing/SO/Sandbox_Flamethrower.asset");
        Assert.That(production, Is.Not.Null);
        Assert.That(sandbox, Is.Not.Null);
        Assert.That(production.PresentationProfile, Is.Not.Null);
        Assert.That(sandbox.PresentationProfile, Is.SameAs(production.PresentationProfile));
        Assert.That(production.PresentationProfile.WeaponType, Is.EqualTo(WeaponType.Flamethrower));
        Assert.That(production.PresentationProfile.HasDuplicateCues, Is.False);

        WeaponPresentationCue[] required =
        {
            WeaponPresentationCue.FlamethrowerAutomaticLoop,
            WeaponPresentationCue.FlamethrowerManualLoop,
            WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop,
            WeaponPresentationCue.FlamethrowerJellifiedManualLoop,
            WeaponPresentationCue.FlamethrowerNitrogenAutomaticLoop,
            WeaponPresentationCue.FlamethrowerNitrogenManualLoop,
            WeaponPresentationCue.FlamethrowerActiveBurst,
            WeaponPresentationCue.FlamethrowerJellifiedActive,
            WeaponPresentationCue.FlamethrowerNitrogenActive,
            WeaponPresentationCue.FlamethrowerBurnStatus,
            WeaponPresentationCue.FlamethrowerNitrogenSlow,
            WeaponPresentationCue.FlamethrowerNitrogenFreeze
        };
        for (int i = 0; i < required.Length; i++)
            Assert.That(production.PresentationProfile.TryGetCueData(required[i], out _), Is.True, required[i].ToString());
    }

    [Test]
    public void ProductionStream_UsesLayeredProceduralMeshWithoutLineRenderers()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        GameObject prefab = profile.Flamethrower.StreamPrefab;
        Assert.That(prefab, Is.Not.Null);
        FlamethrowerStreamVfx stream = prefab.GetComponent<FlamethrowerStreamVfx>();
        Assert.That(stream, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<LineRenderer>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true), Has.Length.GreaterThanOrEqualTo(4));
        Assert.That(stream.ParticleLayerCount, Is.EqualTo(2));
        Assert.That(stream.MaximumSegments, Is.EqualTo(48));
        Assert.That(stream.BodyRadialSides, Is.GreaterThanOrEqualTo(6), "The flame body must be a volume, not a plane.");
        Assert.That(stream.CoreRadialSides, Is.GreaterThanOrEqualTo(4));
        Transform billows = prefab.transform.Find("Rolling Flame Billows");
        Assert.That(billows, Is.Not.Null);
        MeshRenderer billowRenderer = billows.GetComponent<MeshRenderer>();
        Assert.That(billowRenderer.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/GameFeel/Flamethrower Plume"),
            "Both flamethrower modes need the soft turbulent plume shader instead of the hard-edged generic VFX surface.");
        Assert.That(profile.Flamethrower.MaximumStreamSegments, Is.EqualTo(48));
    }

    [Test]
    public void ManualRibbon_CentersEverySegmentOnAuthoritativeHosePoints()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        FlamethrowerStreamVfx stream = null;
        try
        {
            stream = FlamethrowerStreamVfx.Create(profile.Flamethrower.StreamPrefab, 48);
            Vector3[] points =
            {
                new(2f, 0.4f, 3f),
                new(2.3f, 0.5f, 4.5f),
                new(2.7f, 0.42f, 6f),
                new(3.2f, 0.3f, 7.4f)
            };
            stream.ShowHose(points, points.Length, 0.7f, 0.2f);
            Vector3[] vertices = stream.BodyMesh.vertices;
            int sides = stream.BodyRadialSides;
            Assert.That(vertices, Has.Length.EqualTo(points.Length * sides));
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 localCenter = Vector3.zero;
                for (int side = 0; side < sides; side++)
                    localCenter += vertices[i * sides + side];
                localCenter /= sides;
                Vector3 worldCenter = stream.transform.TransformPoint(localCenter);
                Assert.That(Vector3.Distance(worldCenter, points[i]), Is.LessThan(0.0002f), $"Segment {i}");
            }
        }
        finally
        {
            if (stream != null)
                Object.DestroyImmediate(stream.gameObject);
        }
    }

    [Test]
    public void AutomaticRibbon_RemainsInsideItsDamageConeAndReachesRange()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        FlamethrowerStreamVfx stream = null;
        try
        {
            stream = FlamethrowerStreamVfx.Create(profile.Flamethrower.StreamPrefab, 48);
            const float range = 10f;
            const float coneAngle = 45f;
            stream.ShowCone(Vector3.zero, Vector3.forward, range, coneAngle, 0.2f);
            Vector3[] vertices = stream.BodyMesh.vertices;
            float furthest = 0f;
            float tallest = 0f;
            float widest = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = stream.transform.TransformPoint(vertices[i]);
                tallest = Mathf.Max(tallest, Mathf.Abs(world.y));
                widest = Mathf.Max(widest, Mathf.Abs(world.x));
                world.y = 0f;
                float distance = world.magnitude;
                furthest = Mathf.Max(furthest, distance);
                Assert.That(distance, Is.LessThanOrEqualTo(range + 0.001f), $"Vertex {i} exceeds damage range.");
                if (distance > 0.001f)
                    Assert.That(Vector3.Angle(Vector3.forward, world), Is.LessThanOrEqualTo(coneAngle * 0.5f + 0.001f), $"Vertex {i} exceeds damage angle.");
            }
            Assert.That(furthest, Is.EqualTo(range).Within(0.001f));
            Assert.That(stream.AutomaticWidthMultiplier, Is.EqualTo(0.52f).Within(0.001f));
            Assert.That(stream.AutomaticHeightMultiplier, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(widest, Is.LessThan(range * 0.24f), "Automatic flame should not fill most of the camera horizontally.");
            Assert.That(tallest, Is.LessThan(range * 0.11f), "Automatic flame should stay low enough to preserve visibility.");
            Assert.That(stream.BillowVertexCount, Is.GreaterThanOrEqualTo(stream.AutomaticBillowCount * 96),
                "Automatic fire should use overlapping flame billows instead of a solid cone surface alone.");
            Vector3[] billowVertices = stream.BillowMesh.vertices;
            for (int i = 0; i < billowVertices.Length; i++)
            {
                Vector3 world = stream.transform.TransformPoint(billowVertices[i]);
                world.y = 0f;
                float distance = world.magnitude;
                Assert.That(distance, Is.LessThanOrEqualTo(range + 0.001f), $"Billow vertex {i} exceeds automatic damage range.");
                if (distance > 0.001f)
                    Assert.That(Vector3.Angle(Vector3.forward, world), Is.LessThanOrEqualTo(coneAngle * 0.5f + 0.001f), $"Billow vertex {i} exceeds automatic damage angle.");
            }
            Assert.That(stream.BodyShaderName, Is.EqualTo("ScrapWaves/GameFeel/Flamethrower Plume"));
            Assert.That(stream.CoreVisible, Is.False);
            Assert.That(stream.NozzleGlowVisible, Is.False);

            stream.ReleaseAutomatic();
            Assert.That(stream.IsAutomaticReleasing, Is.True);
            Assert.That(stream.BodyVisible, Is.True,
                "Stopping automatic fire should leave its final cone visible while it dissipates.");
            Assert.That(stream.AutomaticReleaseDuration, Is.GreaterThanOrEqualTo(0.35f));
        }
        finally
        {
            if (stream != null)
                Object.DestroyImmediate(stream.gameObject);
        }
    }

    [Test]
    public void JellifiedFuelPuddle_IsAuthoredLayeredAndPooled()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        GameObject prefab = profile.Flamethrower.FuelPuddlePrefab;
        Assert.That(prefab, Is.Not.Null);
        FlamethrowerFuelPuddle puddle = prefab.GetComponent<FlamethrowerFuelPuddle>();
        Assert.That(puddle, Is.Not.Null);
        Assert.That(puddle.MeshLayerCount, Is.EqualTo(2));
        Assert.That(puddle.ParticleLayerCount, Is.EqualTo(2));
        Assert.That(prefab.GetComponentsInChildren<LineRenderer>(true), Is.Empty);
        Assert.That(profile.Flamethrower.FuelPuddlePrewarmCount, Is.GreaterThan(0));
        Assert.That(profile.Flamethrower.FuelPuddlePoolCapacity, Is.GreaterThan(profile.Flamethrower.FuelPuddlePrewarmCount));
    }

    [Test]
    public void JellifiedFuelPuddle_ParticlesCoverLargeAbilityRadius()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        FlamethrowerFuelPuddle puddle = null;
        try
        {
            puddle = FlamethrowerFuelPuddle.SpawnAuthored(
                profile.Flamethrower.FuelPuddlePrefab,
                0,
                1,
                Vector3.zero,
                4f,
                1,
                2f,
                0.5f,
                default);
            Assert.That(puddle, Is.Not.Null);
            ParticleSystem bubbles = puddle.transform.Find("Fuel Bubbles").GetComponent<ParticleSystem>();
            ParticleSystem smoke = puddle.transform.Find("Dark Fuel Smoke").GetComponent<ParticleSystem>();
            Assert.That(bubbles.shape.radius, Is.EqualTo(3.6f).Within(0.001f));
            Assert.That(bubbles.shape.radiusThickness, Is.EqualTo(1f).Within(0.001f));
            Assert.That(smoke.shape.radius, Is.EqualTo(3.28f).Within(0.001f));
            Vector3 bubbleNormal = Quaternion.Euler(bubbles.shape.rotation) * Vector3.forward;
            Vector3 smokeNormal = Quaternion.Euler(smoke.shape.rotation) * Vector3.forward;
            Assert.That(Vector3.Dot(bubbleNormal, Vector3.up), Is.GreaterThan(0.999f), "Bubbles must emit across the ground XZ plane and rise upward.");
            Assert.That(Vector3.Dot(smokeNormal, Vector3.up), Is.GreaterThan(0.999f), "Smoke must emit across the ground XZ plane and rise upward.");
            Assert.That(bubbles.sizeOverLifetime.enabled, Is.True);
            Assert.That(smoke.sizeOverLifetime.enabled, Is.True);
            Assert.That(bubbles.sizeOverLifetime.size.curve.Evaluate(1f), Is.LessThan(0.05f), "Bubbles should shrink away instead of popping.");
            Assert.That(bubbles.colorOverLifetime.color.gradient.alphaKeys[^1].alpha, Is.EqualTo(0f).Within(0.001f), "Bubbles should finish fully transparent.");
            float longestParticleLife = Mathf.Max(
                bubbles.main.startLifetime.constantMax / bubbles.main.simulationSpeed,
                smoke.main.startLifetime.constantMax / smoke.main.simulationSpeed);
            Assert.That(puddle.VisualFadeDuration, Is.GreaterThan(longestParticleLife), "Puddle fade must outlast its final particles.");
        }
        finally
        {
            if (puddle != null)
                Object.DestroyImmediate(puddle.gameObject);
        }
    }

    [Test]
    public void ActiveRadialPlume_IsHorizontalAndMatchesGameplayScaleForEveryPath()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponPresentationCue[] activeCues =
        {
            WeaponPresentationCue.FlamethrowerActiveBurst,
            WeaponPresentationCue.FlamethrowerJellifiedActive,
            WeaponPresentationCue.FlamethrowerNitrogenActive
        };

        for (int cueIndex = 0; cueIndex < activeCues.Length; cueIndex++)
        {
            Assert.That(profile.TryGetCueData(activeCues[cueIndex], out WeaponPresentationCueData cue), Is.True);
            GameObject instance = Object.Instantiate(cue.VfxPrefab);
            try
            {
                FlamethrowerCueVfx vfx = instance.GetComponent<FlamethrowerCueVfx>();
                vfx.Prewarm();
                Transform visual = instance.transform.Find("Animated Visual");
                Assert.That(visual, Is.Not.Null);
                Transform radius = visual.Find("Damage Radius");
                Assert.That(radius, Is.Not.Null);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(radius.localEulerAngles.x, 90f)), Is.LessThan(0.01f));
                Assert.That(radius.localScale, Is.EqualTo(Vector3.one * 2f));
                Assert.That(radius.GetComponent<MeshRenderer>().enabled, Is.False,
                    "The old solid radius ring should only act as a horizontal scaling carrier.");
                Assert.That(vfx.UsesActiveRadialPlume, Is.True);
                Assert.That(radius.childCount, Is.GreaterThanOrEqualTo(20),
                    "The ability should cover its radial footprint with overlapping turbulent billows.");
                for (int child = 0; child < radius.childCount; child++)
                {
                    MeshRenderer renderer = radius.GetChild(child).GetComponent<MeshRenderer>();
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/GameFeel/Flamethrower Plume"));
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void Profile_RestoresPreOverhaulSilenceAndPreservesStatusVfx()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.FlamethrowerManualLoop, out WeaponPresentationCueData loop), Is.True);
        Assert.That(loop.Loop, Is.True);
        Assert.That(loop.AudioClips, Is.Empty);
        Assert.That(loop.VfxPrefab, Is.Null, "The removed repeating hand explosion must not return.");
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.FlamethrowerActiveBurst, out WeaponPresentationCueData active), Is.True);
        Assert.That(active.AudioClips, Is.Empty);

        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        WeaponInstance instance = new() { Data = data, Level = 6, State = WeaponState.Manual, SelectedPath = WeaponUpgradePath.PathB };
        WeaponFeedbackContext context = new(instance, WeaponFeedbackMode.Manual, 0.5f, Vector3.zero, Vector3.forward);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.SustainedFireStarted, in context, out WeaponPresentationCueData sustained), Is.True);
        Assert.That(sustained.VfxPrefab, Is.Null);
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.StatusApplied, in context, out WeaponPresentationCueData status), Is.True);
        Assert.That(status.Cue, Is.EqualTo(WeaponPresentationCue.FlamethrowerNitrogenSlow));
        Assert.That(status.VfxPrefab, Is.Not.Null);
    }

    [Test]
    public void ActiveFeedbackBindings_DistinguishUpgradePaths()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        WeaponInstance instance = new() { Data = data, Level = 6, State = WeaponState.Manual };

        instance.SelectedPath = WeaponUpgradePath.None;
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, instance, WeaponFeedbackMode.Active, WeaponPresentationCue.FlamethrowerActiveBurst);
        instance.SelectedPath = WeaponUpgradePath.PathA;
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, instance, WeaponFeedbackMode.Active, WeaponPresentationCue.FlamethrowerJellifiedActive);
        instance.SelectedPath = WeaponUpgradePath.PathB;
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, instance, WeaponFeedbackMode.Active, WeaponPresentationCue.FlamethrowerNitrogenActive);
    }

    [Test]
    public void ManualPlume_GrowsFromNozzleIntoStableHoseWithRollingBillowVolume()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        FlamethrowerStreamVfx stream = null;
        try
        {
            stream = FlamethrowerStreamVfx.Create(profile.Flamethrower.StreamPrefab, 48);
            const int pointCount = 12;
            const float radius = 0.65f;
            Vector3[] points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
                points[i] = Vector3.forward * (i * 0.7f);

            stream.ShowHose(points, points.Length, radius, 0.2f);

            ParticleSystem[] streamParticles = stream.GetComponentsInChildren<ParticleSystem>(true);
            for (int particleIndex = 0; particleIndex < streamParticles.Length; particleIndex++)
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = streamParticles[particleIndex].velocityOverLifetime;
                Assert.That(velocity.enabled, Is.False,
                    "Stream particles must not rewrite velocity curves at runtime; authored start speed drives their motion.");
                Assert.That(velocity.x.mode, Is.EqualTo(velocity.y.mode),
                    "Particle velocity curves must use one shared mode to avoid Unity runtime errors.");
                Assert.That(velocity.z.mode, Is.EqualTo(velocity.y.mode),
                    "Particle velocity curves must use one shared mode to avoid Unity runtime errors.");
            }

            Vector3[] body = stream.BodyMesh.vertices;
            float[] averageRingRadii = new float[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                Vector3 center = stream.transform.InverseTransformPoint(points[i]);
                for (int side = 0; side < stream.BodyRadialSides; side++)
                    averageRingRadii[i] += Vector3.Distance(center, body[i * stream.BodyRadialSides + side]);
                averageRingRadii[i] /= stream.BodyRadialSides;
            }

            Assert.That(averageRingRadii[0], Is.LessThan(averageRingRadii[3] * 0.35f),
                "The hose should leave the nozzle compact and begin growing gradually.");
            Assert.That(averageRingRadii[3], Is.LessThan(averageRingRadii[6] * 0.7f),
                "The fire should keep building volume through roughly the first half of its reach.");
            float minimumEstablishedRadius = float.MaxValue;
            float maximumEstablishedRadius = 0f;
            for (int i = 7; i <= 9; i++)
            {
                minimumEstablishedRadius = Mathf.Min(minimumEstablishedRadius, averageRingRadii[i]);
                maximumEstablishedRadius = Mathf.Max(maximumEstablishedRadius, averageRingRadii[i]);
            }
            Assert.That(maximumEstablishedRadius - minimumEstablishedRadius, Is.LessThan(radius * 0.12f),
                "Once established, the hose must stop continuously widening so it cannot become a cone.");
            Assert.That(maximumEstablishedRadius - minimumEstablishedRadius, Is.GreaterThan(radius * 0.015f),
                "The established hose should retain gentle authored thickness changes instead of becoming a cylinder.");
            Assert.That(stream.ManualBillowCount, Is.GreaterThanOrEqualTo(11),
                "The manual plume needs dense overlap so its fire reads as one turbulent mass.");
            Assert.That(stream.BillowVertexCount, Is.GreaterThanOrEqualTo(stream.ManualBillowCount * 96));
            Assert.That(stream.BillowMesh.uv2, Has.Length.EqualTo(stream.BillowVertexCount),
                "The plume shader needs per-vertex heat and distance data for its hot-to-cool transition.");
            Assert.That(stream.ManualTubeWidthMultiplier, Is.InRange(0.45f, 0.65f),
                "The connective body should stay subordinate to the rolling fire mass.");
            Assert.That(stream.ManualBodyOpacity, Is.InRange(0.18f, 0.35f));
            Assert.That(stream.CoreVisible, Is.False,
                "Manual fire must not render a central core line; its heat should come from the turbulent plume.");
            Assert.That(stream.NozzleGlowVisible, Is.False,
                "Manual fire must not leave a separate bright mesh attached to the player's hand.");
            Assert.That(stream.BodyShaderName, Is.EqualTo("ScrapWaves/GameFeel/Flamethrower Plume"),
                "Manual fire should use the eroded turbulent plume shader instead of the solid beam material.");

            Vector3[] billows = stream.BillowMesh.vertices;
            int verticesPerBillow = billows.Length / stream.ManualBillowCount;
            float minimumBillowCenterX = float.MaxValue;
            float maximumBillowCenterX = float.MinValue;
            float minimumBillowCenterY = float.MaxValue;
            float maximumBillowCenterY = float.MinValue;
            for (int billow = 0; billow < stream.ManualBillowCount; billow++)
            {
                Vector3 center = Vector3.zero;
                for (int vertex = 0; vertex < verticesPerBillow; vertex++)
                    center += billows[billow * verticesPerBillow + vertex];
                center /= verticesPerBillow;
                minimumBillowCenterX = Mathf.Min(minimumBillowCenterX, center.x);
                maximumBillowCenterX = Mathf.Max(maximumBillowCenterX, center.x);
                minimumBillowCenterY = Mathf.Min(minimumBillowCenterY, center.y);
                maximumBillowCenterY = Mathf.Max(maximumBillowCenterY, center.y);
            }

            Assert.That(maximumBillowCenterX - minimumBillowCenterX, Is.GreaterThan(radius * 0.05f),
                "The connected plume still needs subtle lateral turbulence.");
            Assert.That(maximumBillowCenterY - minimumBillowCenterY, Is.GreaterThan(radius * 0.04f),
                "The connected plume should roll upward by varying amounts.");

            stream.ReleaseManual();
            Assert.That(stream.IsManualReleasing, Is.True);
            Assert.That(stream.BodyVisible, Is.True,
                "Releasing the trigger should keep the already-fired flame visible while it dissipates.");
            Assert.That(stream.ManualReleaseDuration, Is.GreaterThanOrEqualTo(0.45f),
                "The release tail must be long enough to read as a natural flame fade rather than a pop.");

            stream.ShowCone(Vector3.zero, Vector3.forward, 8f, 40f, 0.2f);
            Assert.That(stream.BillowVertexCount, Is.GreaterThanOrEqualTo(stream.AutomaticBillowCount * 96),
                "Automatic fire should rebuild the turbulent billows as a cone.");
            Assert.That(stream.IsManualReleasing, Is.False);
            Assert.That(stream.BodyShaderName, Is.EqualTo("ScrapWaves/GameFeel/Flamethrower Plume"),
                "Automatic fire should share the turbulent material while retaining cone geometry.");
            Assert.That(stream.CoreVisible, Is.False,
                "Automatic fire should not reintroduce the solid central ray.");
            Assert.That(stream.NozzleGlowVisible, Is.False,
                "Automatic fire should not reintroduce the separate bright nozzle object.");
        }
        finally
        {
            if (stream != null)
                Object.DestroyImmediate(stream.gameObject);
        }
    }

    [Test]
    public void JellifiedFuelStatusFeedback_UsesRegularBurnCue()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        WeaponInstance instance = new()
        {
            Data = data,
            Level = 6,
            State = WeaponState.Manual,
            SelectedPath = WeaponUpgradePath.PathA
        };

        AssertResolved(
            profile,
            WeaponFeedbackEvent.StatusApplied,
            instance,
            WeaponFeedbackMode.Manual,
            WeaponPresentationCue.FlamethrowerBurnStatus);
    }

    private static void AssertResolved(
        WeaponPresentationProfile profile,
        WeaponFeedbackEvent feedbackEvent,
        WeaponInstance instance,
        WeaponFeedbackMode mode,
        WeaponPresentationCue expected)
    {
        WeaponFeedbackContext context = new(instance, mode, 0.5f, Vector3.zero, Vector3.forward, explosionRadius: 6f);
        Assert.That(profile.TryResolveCue(feedbackEvent, in context, out WeaponPresentationCueData cue), Is.True);
        Assert.That(cue.Cue, Is.EqualTo(expected));
    }
}
