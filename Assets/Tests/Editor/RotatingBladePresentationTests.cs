using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class RotatingBladePresentationTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/RotatingBladePresentation.asset";

    [Test]
    public void ProductionRotatingBlade_ReferencesCompleteAuthoredProfile()
    {
        WeaponData production = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset");
        WeaponData sandbox = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Scripts/Weapon/Testing/SO/Sandbox_RotatingBlade.asset");
        Assert.That(production, Is.Not.Null);
        Assert.That(sandbox, Is.Not.Null);
        Assert.That(production.PresentationProfile, Is.Not.Null);
        Assert.That(sandbox.PresentationProfile, Is.SameAs(production.PresentationProfile));
        Assert.That(production.PresentationProfile.WeaponType, Is.EqualTo(WeaponType.RotatingBlade));
        Assert.That(production.PresentationProfile.HasDuplicateCues, Is.False);

        WeaponPresentationCue[] required =
        {
            WeaponPresentationCue.RotatingBladeManualSlash,
            WeaponPresentationCue.RotatingBladeActiveThrust,
            WeaponPresentationCue.RotatingBladeMultiSlash,
            WeaponPresentationCue.RotatingBladeMultiThrust,
            WeaponPresentationCue.RotatingBladeAtomicSlash,
            WeaponPresentationCue.RotatingBladeAtomicDash,
            WeaponPresentationCue.RotatingBladeContactImpact,
            WeaponPresentationCue.RotatingBladeMultiFinalImpact,
            WeaponPresentationCue.RotatingBladeAtomicSliceImpact
        };
        for (int i = 0; i < required.Length; i++)
            Assert.That(production.PresentationProfile.TryGetCueData(required[i], out _), Is.True, required[i].ToString());
    }

    [Test]
    public void RuntimePrefab_ContainsPhysicalScrapBladeAndPersistentTrail()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        GameObject prefab = profile.RotatingBlade.RuntimeVfxPrefab;
        Assert.That(prefab, Is.Not.Null);
        RotatingBladeVfx vfx = prefab.GetComponent<RotatingBladeVfx>();
        Assert.That(vfx, Is.Not.Null);
        Assert.That(vfx.AuthoredBladeMeshCount, Is.GreaterThanOrEqualTo(3));
        Assert.That(prefab.GetComponentsInChildren<TrailRenderer>(true), Has.Length.EqualTo(1));
        Assert.That(profile.RotatingBlade.MaximumOrbitingBlades, Is.EqualTo(6));
    }

    [Test]
    public void AuthoredOrbit_CreatesOnePhysicalBladePerGameplayBlade()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        RotatingBladeVfx vfx = null;
        try
        {
            vfx = RotatingBladeVfx.Create(profile.RotatingBlade.RuntimeVfxPrefab);
            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * 360f;
                Vector3 center = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * 2.2f;
                vfx.ShowOrbit(Vector3.zero, center, 0.6f, 0.2f, new Color(1f, 0.68f, 0.48f), 0.8f);
            }
            Assert.That(vfx.VisiblePhysicalBladeCount, Is.EqualTo(6));
            Transform first = vfx.transform.Find("Physical Orbiting Blade 1");
            Assert.That(first, Is.Not.Null);
            Assert.That(first.GetComponentsInChildren<MeshRenderer>(true), Has.Length.GreaterThanOrEqualTo(3));
            TrailRenderer trail = first.GetComponentInChildren<TrailRenderer>(true);
            Assert.That(trail, Is.Not.Null);
            Assert.That(trail.emitting, Is.True);
            Assert.That(trail.time, Is.GreaterThan(0.13f), "Heat should extend the persistent trail.");
        }
        finally
        {
            if (vfx != null)
                Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void ManualSlash_UsesCurvedSurfaceInsideGameplayRange()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        RotatingBladeVfx vfx = null;
        try
        {
            vfx = RotatingBladeVfx.Create(profile.RotatingBlade.RuntimeVfxPrefab);
            const float range = 3f;
            vfx.ShowSlash(Vector3.zero, Vector3.forward, range, 85f, 0.2f);
            Assert.That(vfx.ActiveSlashSurfaceCount, Is.EqualTo(1));
            Mesh mesh = vfx.transform.Find("Blade Slash Surface 1").GetComponent<MeshFilter>().sharedMesh;
            float furthest = 0f;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                Vector3 vertex = mesh.vertices[i];
                furthest = Mathf.Max(furthest, new Vector2(vertex.x, vertex.z).magnitude);
            }
            Assert.That(furthest, Is.EqualTo(range).Within(range * 0.05f));
        }
        finally
        {
            if (vfx != null)
                Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void ActiveThrust_UsesTaperedRibbonThatReachesDamageRange()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        RotatingBladeVfx vfx = null;
        try
        {
            vfx = RotatingBladeVfx.Create(profile.RotatingBlade.RuntimeVfxPrefab);
            const float range = 12f;
            const float width = 0.8f;
            vfx.ShowThrust(Vector3.zero, Vector3.forward, range, width, 0.2f);
            Assert.That(vfx.ActiveThrustSurfaceCount, Is.EqualTo(1));
            Mesh mesh = vfx.transform.Find("Blade Thrust Ribbon 1").GetComponent<MeshFilter>().sharedMesh;
            float furthest = 0f;
            float widest = 0f;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                furthest = Mathf.Max(furthest, mesh.vertices[i].z);
                widest = Mathf.Max(widest, Mathf.Abs(mesh.vertices[i].x));
            }
            Assert.That(furthest, Is.EqualTo(range).Within(0.001f));
            Assert.That(widest, Is.LessThanOrEqualTo(width * 0.5f + 0.001f));
        }
        finally
        {
            if (vfx != null)
                Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void AtomicDash_UsesThinRibbonAndSixBladeAfterimages()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        RotatingBladeVfx vfx = null;
        try
        {
            vfx = RotatingBladeVfx.Create(profile.RotatingBlade.RuntimeVfxPrefab);
            vfx.ShowDash(Vector3.zero, Vector3.forward, 9f, 0.8f, 0.5f, new Color(0.36f, 0.04f, 0.55f));
            Assert.That(vfx.ActiveThrustSurfaceCount, Is.EqualTo(1));
            Assert.That(vfx.ActiveDashAfterimageCount, Is.EqualTo(6));
        }
        finally
        {
            if (vfx != null)
                Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void ImpactPrefabs_SeparateFrictionFinalAndAtomicGrammar()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        AssertCueStyle(profile, WeaponPresentationCue.RotatingBladeContactImpact, RotatingBladeCueStyle.ContactSparks, minimumParticles: 1);
        AssertCueStyle(profile, WeaponPresentationCue.RotatingBladeMultiFinalImpact, RotatingBladeCueStyle.MultiBladeFinalImpact, minimumParticles: 1);
        AssertCueStyle(profile, WeaponPresentationCue.RotatingBladeAtomicSliceImpact, RotatingBladeCueStyle.AtomicSliceImpact, minimumParticles: 1);
    }

    [Test]
    public void Profile_RoutesBaseMultiBladeAndAtomicModes()
    {
        WeaponPresentationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>(ProfilePath);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, WeaponFeedbackMode.Manual, WeaponUpgradePath.None, WeaponPresentationCue.RotatingBladeManualSlash);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, WeaponFeedbackMode.Active, WeaponUpgradePath.None, WeaponPresentationCue.RotatingBladeActiveThrust);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, WeaponFeedbackMode.Manual, WeaponUpgradePath.PathA, WeaponPresentationCue.RotatingBladeMultiSlash);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, WeaponFeedbackMode.Active, WeaponUpgradePath.PathA, WeaponPresentationCue.RotatingBladeMultiThrust);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, WeaponFeedbackMode.Manual, WeaponUpgradePath.PathB, WeaponPresentationCue.RotatingBladeAtomicSlash);
        AssertResolved(profile, WeaponFeedbackEvent.ShotFired, WeaponFeedbackMode.Active, WeaponUpgradePath.PathB, WeaponPresentationCue.RotatingBladeAtomicDash);
        AssertResolved(profile, WeaponFeedbackEvent.ProjectileImpact, WeaponFeedbackMode.Automatic, WeaponUpgradePath.PathB, WeaponPresentationCue.RotatingBladeAtomicSliceImpact);
    }

    private static void AssertCueStyle(
        WeaponPresentationProfile profile,
        WeaponPresentationCue cue,
        RotatingBladeCueStyle style,
        int minimumParticles)
    {
        Assert.That(profile.TryGetCueData(cue, out WeaponPresentationCueData data), Is.True);
        Assert.That(data.VfxPrefab, Is.Not.Null);
        RotatingBladeCueVfx vfx = data.VfxPrefab.GetComponent<RotatingBladeCueVfx>();
        Assert.That(vfx, Is.Not.Null);
        Assert.That(vfx.Style, Is.EqualTo(style));
        Assert.That(vfx.RuntimeParticleSystemCount, Is.GreaterThanOrEqualTo(minimumParticles));
    }

    private static void AssertResolved(
        WeaponPresentationProfile profile,
        WeaponFeedbackEvent feedbackEvent,
        WeaponFeedbackMode mode,
        WeaponUpgradePath path,
        WeaponPresentationCue expected)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset");
        WeaponInstance instance = new()
        {
            Data = data,
            Level = path == WeaponUpgradePath.None ? 1 : 6,
            SelectedPath = path
        };
        WeaponFeedbackContext context = new(instance, mode, 0.5f, Vector3.zero, Vector3.forward);
        Assert.That(profile.TryResolveCue(feedbackEvent, in context, out WeaponPresentationCueData resolved), Is.True);
        Assert.That(resolved.Cue, Is.EqualTo(expected));
    }
}
