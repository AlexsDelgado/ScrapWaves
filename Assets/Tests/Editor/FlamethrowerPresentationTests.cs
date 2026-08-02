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
            WeaponPresentationCue.FlamethrowerSustainedStop,
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
            Assert.That(vertices, Has.Length.EqualTo(points.Length * 2));
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 localCenter = (vertices[i * 2] + vertices[i * 2 + 1]) * 0.5f;
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
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = stream.transform.TransformPoint(vertices[i]);
                world.y = 0f;
                float distance = world.magnitude;
                furthest = Mathf.Max(furthest, distance);
                Assert.That(distance, Is.LessThanOrEqualTo(range + 0.001f), $"Vertex {i} exceeds damage range.");
                if (distance > 0.001f)
                    Assert.That(Vector3.Angle(Vector3.forward, world), Is.LessThanOrEqualTo(coneAngle * 0.5f + 0.001f), $"Vertex {i} exceeds damage angle.");
            }
            Assert.That(furthest, Is.EqualTo(range).Within(0.001f));
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
    public void LiquidNitrogenFreeze_UsesFrostShardsVaporAndCrackGeometry()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        Assert.That(profile.TryGetCueData(WeaponPresentationCue.FlamethrowerNitrogenFreeze, out WeaponPresentationCueData cue), Is.True);
        Transform visual = cue.VfxPrefab.transform.Find("Animated Visual");
        Assert.That(visual, Is.Not.Null);
        Assert.That(visual.Find("Frost Overlay"), Is.Not.Null);
        Assert.That(visual.Find("Freeze Crack Pulse"), Is.Not.Null);
        for (int i = 1; i <= 6; i++)
            Assert.That(visual.Find("Ice Shard " + i), Is.Not.Null);
        Assert.That(visual.Find("Dense Cold Vapor"), Is.Not.Null);
        FlamethrowerCueVfx vfx = cue.VfxPrefab.GetComponent<FlamethrowerCueVfx>();
        Assert.That(vfx.RuntimeMeshLayerCount, Is.GreaterThanOrEqualTo(8));
        Assert.That(vfx.RuntimeParticleSystemCount, Is.EqualTo(2));
    }

    [Test]
    public void FeedbackBindings_DistinguishModePathAndDeepFreeze()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        WeaponInstance instance = new() { Data = data, Level = 6, State = WeaponState.Manual };

        instance.SelectedPath = WeaponUpgradePath.None;
        AssertResolved(profile, WeaponFeedbackEvent.SustainedFireStarted, instance, WeaponFeedbackMode.Manual, WeaponPresentationCue.FlamethrowerManualLoop);
        instance.SelectedPath = WeaponUpgradePath.PathA;
        AssertResolved(profile, WeaponFeedbackEvent.SustainedFireStarted, instance, WeaponFeedbackMode.Automatic, WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, instance, WeaponFeedbackMode.Active, WeaponPresentationCue.FlamethrowerJellifiedActive);
        instance.SelectedPath = WeaponUpgradePath.PathB;
        AssertResolved(profile, WeaponFeedbackEvent.StatusApplied, instance, WeaponFeedbackMode.Manual, WeaponPresentationCue.FlamethrowerNitrogenSlow);
        AssertResolved(profile, WeaponFeedbackEvent.StatusApplied, instance, WeaponFeedbackMode.Active, WeaponPresentationCue.FlamethrowerNitrogenFreeze);
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
