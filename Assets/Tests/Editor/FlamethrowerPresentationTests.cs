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
            WeaponPresentationCue.FlamethrowerJellifiedStatus,
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
        Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true), Has.Length.GreaterThanOrEqualTo(3));
        Assert.That(stream.ParticleLayerCount, Is.EqualTo(2));
        Assert.That(stream.MaximumSegments, Is.EqualTo(48));
        Assert.That(stream.BodyRadialSides, Is.GreaterThanOrEqualTo(6), "The flame body must be a volume, not a plane.");
        Assert.That(stream.CoreRadialSides, Is.GreaterThanOrEqualTo(4));
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
    public void ActiveRadiusRing_IsHorizontalAndMatchesGameplayScale()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.FlamethrowerActiveBurst, out WeaponPresentationCueData cue), Is.True);
        Transform visual = cue.VfxPrefab.transform.Find("Animated Visual");
        Assert.That(visual, Is.Not.Null);
        Transform radius = visual.Find("Damage Radius");
        Assert.That(radius, Is.Not.Null);
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(radius.localEulerAngles.x, 90f)), Is.LessThan(0.01f));
        Assert.That(radius.localScale, Is.EqualTo(Vector3.one * 2f));
    }

    [Test]
    public void Profile_PreservesSfxAndStatusVfxWithoutHandLoopVisuals()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.FlamethrowerManualLoop, out WeaponPresentationCueData loop), Is.True);
        Assert.That(loop.Loop, Is.True);
        Assert.That(loop.AudioClips, Is.Not.Empty);
        Assert.That(loop.VfxPrefab, Is.Null, "The removed repeating hand explosion must not return with the SFX loop.");
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.FlamethrowerActiveBurst, out WeaponPresentationCueData active), Is.True);
        Assert.That(active.AudioClips, Is.Not.Empty);

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
