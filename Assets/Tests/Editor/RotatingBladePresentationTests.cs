using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class RotatingBladePresentationTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/WeaponPresentation/RotatingBladePresentation.asset";

    [Test]
    public void ScrapVfxShader_ExposesParticleMainTextureProperty()
    {
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        Assert.That(shader, Is.Not.Null);
        Material material = new(shader);
        try
        {
            Assert.That(material.HasProperty("_MainTex"), Is.True,
                "ParticleSystemRenderer queries Material.mainTexture and must not emit warnings.");
            Assert.That(material.mainTexture, Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(material);
        }
    }

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
    public void ManualSlash_MaterializesAsFilledLeftToRightSweepInsideGameplayRange()
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
            float nearest = float.MaxValue;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                Vector3 vertex = mesh.vertices[i];
                float radial = new Vector2(vertex.x, vertex.z).magnitude;
                furthest = Mathf.Max(furthest, radial);
                nearest = Mathf.Min(nearest, radial);
            }
            Assert.That(furthest, Is.EqualTo(range).Within(range * 0.05f));
            Assert.That(furthest, Is.LessThanOrEqualTo(range + 0.001f), "Presentation must stay inside the damage range.");
            Assert.That(nearest, Is.InRange(range * 0.06f, range * 0.12f), "The fan should reach inward but stop short of the player.");
            Assert.That(mesh.vertexCount, Is.GreaterThan((96 + 1) * 2), "The filled fan needs radial samples for its inward fade.");

            float minimumAngle = float.MaxValue;
            float maximumAngle = float.MinValue;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                Vector3 vertex = mesh.vertices[i];
                float angle = Mathf.Atan2(vertex.x, vertex.z) * Mathf.Rad2Deg;
                minimumAngle = Mathf.Min(minimumAngle, angle);
                maximumAngle = Mathf.Max(maximumAngle, angle);
            }
            Assert.That(maximumAngle - minimumAngle, Is.LessThan(3f),
                "The slash must begin at the left edge and reveal across the arc over time.");

            LineRenderer sweep = vfx.transform.Find("Blade Slash 1").GetComponent<LineRenderer>();
            Assert.That(sweep.positionCount, Is.EqualTo(24));
            Assert.That(sweep.widthCurve.Evaluate(0f), Is.LessThan(sweep.widthCurve.Evaluate(0.75f) * 0.15f));
            MeshRenderer slashRenderer = vfx.transform.Find("Blade Slash Surface 1").GetComponent<MeshRenderer>();
            Assert.That(slashRenderer.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/GameFeel/Flowing Slash"));
            Assert.That(slashRenderer.sharedMaterial.GetColor("_BaseColor").a, Is.GreaterThanOrEqualTo(0.85f),
                "The outer slash retains its authored opacity; the fan fades through vertex alpha.");
        }
        finally
        {
            if (vfx != null)
                Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [TestCase(1f, 45f)]
    [TestCase(3f, 85f)]
    [TestCase(6f, 150f)]
    public void ManualSlash_FillsSectorWithRapidInwardFadeAndBrightRim(float range, float coneAngle)
    {
        Mesh mesh = new();
        try
        {
            RebuildSlash(mesh, range, coneAngle, 1f);
            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;
            var centerSamples = new SortedDictionary<float, float>();
            float halfAngle = coneAngle * 0.5f;
            for (int i = 0; i < vertices.Length; i++)
            {
                float radius = vertices[i].magnitude;
                float angle = Mathf.Atan2(vertices[i].x, vertices[i].z) * Mathf.Rad2Deg;
                Assert.That(radius, Is.LessThanOrEqualTo(range + 0.001f), "Fill must stay inside the damage range.");
                Assert.That(Mathf.Abs(angle), Is.LessThanOrEqualTo(halfAngle + 0.001f), "Fill must stay inside the damage cone.");
                Assert.That(colors[i].a, Is.InRange(0f, 1f));
                if (radius < range * 0.12f)
                    Assert.That(colors[i].a, Is.Zero.Within(0.0001f), "The fan must disappear near the player.");
                if (Mathf.Abs(angle) < 0.001f)
                    centerSamples[radius] = colors[i].a;
            }

            Assert.That(centerSamples.Count, Is.GreaterThanOrEqualTo(4), "A nonlinear inward fade needs multiple radial samples.");
            float previousAlpha = 0f;
            foreach (var sample in centerSamples)
            {
                Assert.That(sample.Value, Is.GreaterThanOrEqualTo(previousAlpha - 0.0001f), "Opacity should rise smoothly toward the outer rim.");
                previousAlpha = sample.Value;
            }
            Assert.That(previousAlpha, Is.GreaterThan(0.9f), "The outer slash rim must remain readable.");

            // Sample rendered triangles, rather than only checking for unused interior vertices.
            float nearAlpha = SampleSlashAlpha(mesh, Vector3.forward * (range * 0.2f));
            float middleAlpha = SampleSlashAlpha(mesh, Vector3.forward * (range * 0.5f));
            float rimAlpha = SampleSlashAlpha(mesh, Vector3.forward * (range * 0.95f));
            Assert.That(nearAlpha, Is.LessThan(0.04f));
            Assert.That(middleAlpha, Is.GreaterThan(0.05f), "The interior should be visible, not an empty crescent.");
            Assert.That(middleAlpha, Is.LessThan(rimAlpha * 0.45f), "The fan must lose opacity quickly toward the player.");
            Assert.That(rimAlpha, Is.GreaterThan(0.8f));
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 point = Quaternion.AngleAxis(side * halfAngle * 0.55f, Vector3.up) * Vector3.forward * (range * 0.5f);
                Assert.That(SampleSlashAlpha(mesh, point), Is.GreaterThan(0.02f), "The fill must cover both halves of the swept sector.");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void ManualSlash_FillRevealsAcrossTheExistingSweep()
    {
        Mesh mesh = new();
        try
        {
            const float coneAngle = 85f;
            float previousEnd = -coneAngle;
            foreach (float reveal in new[] { 0f, 0.25f, 0.5f, 1f })
            {
                RebuildSlash(mesh, 3f, coneAngle, reveal);
                float start = float.MaxValue;
                float end = float.MinValue;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    float angle = Mathf.Atan2(vertex.x, vertex.z) * Mathf.Rad2Deg;
                    start = Mathf.Min(start, angle);
                    end = Mathf.Max(end, angle);
                }
                Assert.That(start, Is.EqualTo(-coneAngle * 0.5f).Within(0.001f));
                Assert.That(end, Is.GreaterThan(previousEnd));
                if (reveal == 0f) Assert.That(end - start, Is.LessThan(3f));
                if (reveal == 0.5f) Assert.That(end, Is.InRange(-2f, 2f));
                if (reveal == 1f) Assert.That(end, Is.EqualTo(coneAngle * 0.5f).Within(0.001f));
                previousEnd = end;
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [TestCase(0f)]
    [TestCase(45f)]
    [TestCase(-45f)]
    [TestCase(90f)]
    [TestCase(-90f)]
    public void ManualSlash_FilledSectorFollowsPitchedAim(float pitch)
    {
        RotatingBladeVfx vfx = RotatingBladeVfx.Create();
        try
        {
            Vector3 origin = new(3f, 4f, 5f);
            Vector3 direction = Quaternion.AngleAxis(-pitch, Vector3.right) * Vector3.forward;
            const float range = 3f, coneAngle = 85f;
            vfx.ShowSlash(origin, direction, range, coneAngle, 0.2f);
            Transform surface = vfx.transform.Find("Blade Slash Surface 1");
            Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
            RebuildSlash(mesh, range, coneAngle, 1f);
            Assert.That(Vector3.Distance(surface.position, origin), Is.LessThan(0.001f));
            Assert.That(Vector3.Angle(surface.forward, direction), Is.LessThan(0.02f));
            foreach (Vector3 local in mesh.vertices)
            {
                Vector3 delta = surface.TransformPoint(local) - origin;
                Assert.That(delta.magnitude, Is.LessThanOrEqualTo(range + 0.001f));
                Assert.That(Vector3.Angle(direction, delta), Is.LessThanOrEqualTo(coneAngle * 0.5f + 0.02f));
            }
        }
        finally { Object.DestroyImmediate(vfx.gameObject); }
    }

    private static void RebuildSlash(Mesh mesh, float range, float coneAngle, float reveal)
    {
        typeof(RotatingBladeVfx).GetMethod("BuildSlashSurface", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { mesh, range, coneAngle, reveal });
    }

    private static float SampleSlashAlpha(Mesh mesh, Vector3 point)
    {
        Vector3[] vertices = mesh.vertices;
        Color[] colors = mesh.colors;
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 edge0 = vertices[b] - vertices[a];
            Vector3 edge1 = vertices[c] - vertices[a];
            Vector3 offset = point - vertices[a];
            float d00 = Vector3.Dot(edge0, edge0), d01 = Vector3.Dot(edge0, edge1), d11 = Vector3.Dot(edge1, edge1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 0.0000000001f) continue;
            float u = (d11 * Vector3.Dot(offset, edge0) - d01 * Vector3.Dot(offset, edge1)) / denominator;
            float v = (d00 * Vector3.Dot(offset, edge1) - d01 * Vector3.Dot(offset, edge0)) / denominator;
            if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
            return colors[a].a * (1f - u - v) + colors[b].a * u + colors[c].a * v;
        }
        Assert.Fail($"No slash triangle covers interior point {point}.");
        return 0f;
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
