using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class EnemyReactionTests
{
    [SetUp]
    public void EnableReactions()
    {
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions
        {
            EnemyReactionEnabled = true,
            ReducedFlash = false,
            Quality = GameFeelQualityLevel.High
        });
    }

    [Test]
    public void EnemyReactionProfile_ResolvesLightHeavyCriticalWeakPointAndKillTiers()
    {
        EnemyReactionProfile profile = ScriptableObject.CreateInstance<EnemyReactionProfile>();
        try
        {
            Assert.That(profile.ResolveTier(Context(damage: 1), 100), Is.EqualTo(EnemyReactionTier.Light));
            Assert.That(profile.ResolveTier(Context(damage: 20), 100), Is.EqualTo(EnemyReactionTier.Heavy));
            Assert.That(profile.ResolveTier(Context(damage: 1, critical: true), 100), Is.EqualTo(EnemyReactionTier.Critical));
            Assert.That(profile.ResolveTier(Context(damage: 1, weakPoint: true), 100), Is.EqualTo(EnemyReactionTier.WeakPoint));
            Assert.That(profile.ResolveTier(Context(damage: 100, kill: true), 100), Is.EqualTo(EnemyReactionTier.Kill));
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    [TestCase(DamageFeedbackKind.Burn)]
    [TestCase(DamageFeedbackKind.JellifiedBurn)]
    public void CombatFeedbackDirector_BurnFamilyKeepsStatusChannelWithoutOrdinaryHitReaction(
        DamageFeedbackKind damageKind)
    {
        GameObject runtimeRoot = new("Burn Feedback Runtime");
        GameObject target = new("Burn Feedback Target");
        WeaponPresentationProfile profile = ScriptableObject.CreateInstance<WeaponPresentationProfile>();
        CombatFeedbackDirector director = null;
        HitStopController hitStop = new();
        try
        {
            WeaponPresentationCueData killCue = new()
            {
                Cue = WeaponPresentationCue.AutomaticCannonKillImpact,
                HitStopDuration = 0.04f,
                HitStopPriority = 3
            };
            WeaponFeedbackBinding killBinding = new()
            {
                Event = WeaponFeedbackEvent.DamageConfirmed,
                Kill = FeedbackFilter.Required,
                Cue = killCue.Cue
            };
            SetPrivateField(
                profile,
                "_cues",
                new System.Collections.Generic.List<WeaponPresentationCueData> { killCue });
            SetPrivateField(
                profile,
                "_feedbackBindings",
                new System.Collections.Generic.List<WeaponFeedbackBinding> { killBinding });
            profile.RebuildCache();

            EnemyHitFeedback hitFeedback = target.AddComponent<EnemyHitFeedback>();
            GameFeelRuntimeOptions options = new()
            {
                ProductionPresentationEnabled = false,
                AudioEnabled = false,
                CameraFeedbackEnabled = false,
                HitStopEnabled = true,
                EnemyReactionEnabled = true
            };
            director = new CombatFeedbackDirector(
                profile,
                runtimeRoot.transform,
                null,
                null,
                options,
                new CameraFeedbackController(),
                hitStop,
                1,
                0f);
            WeaponFeedbackContext burn = new(
                null,
                WeaponFeedbackMode.Manual,
                0f,
                Vector3.zero,
                Vector3.forward,
                damageAmount: 5,
                isKill: true,
                target: target.transform,
                damageKind: damageKind,
                statusInstanceId: 1,
                statusKind: damageKind == DamageFeedbackKind.JellifiedBurn
                    ? WeaponStatusKind.JellifiedBurn
                    : WeaponStatusKind.Burn);

            director.EmitSemantic(WeaponFeedbackEvent.DamageConfirmed, in burn, 0f, 1f);

            Assert.That(hitFeedback.IsPlaying, Is.False,
                "Burn ticks retain their combat-text/status channels but must not play the ordinary hit flash or displacement.");
            Assert.That(hitStop.IsActive, Is.False,
                "Burn-family confirmations must not request per-tick hit-stop, including a final kill tick.");

            WeaponFeedbackContext direct = new(
                null,
                WeaponFeedbackMode.Manual,
                0f,
                Vector3.zero,
                Vector3.forward,
                damageAmount: 5,
                isKill: true,
                target: target.transform,
                damageKind: DamageFeedbackKind.Direct);
            director.EmitSemantic(WeaponFeedbackEvent.DamageConfirmed, in direct, 0f, 1.1f);
            Assert.That(hitFeedback.IsPlaying, Is.True,
                "The burn gate must not suppress ordinary direct-hit reactions.");
            Assert.That(hitStop.IsActive, Is.True,
                "The burn gate must not suppress an authored direct-hit kill stop.");
        }
        finally
        {
            director?.StopAll();
            hitStop.Restore();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(runtimeRoot);
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void StatusFeedback_UsesJellifiedBurnInsteadOfRegularBurn()
    {
        GameObject target = new("Status Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Burn, 3f);
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.JellifiedBurn, 3f);

            EnemyStatusFeedback feedback = target.GetComponent<EnemyStatusFeedback>();
            Assert.That(feedback, Is.Not.Null);
            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.JellifiedBurn));

            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Burn, 3f);
            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.JellifiedBurn));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void StatusFeedback_KeepsSlowWhileFreezeTemporarilyTakesPriority()
    {
        GameObject target = new("Frozen Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Slow, 5f, 0.7f);
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Freeze, 2f);
            EnemyStatusFeedback feedback = target.GetComponent<EnemyStatusFeedback>();

            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.Slow | WeaponStatusMask.Freeze));
            EnemyStatusFeedback.Remove(target.transform, WeaponStatusKind.Freeze);
            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.Slow));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void AuthoredReactionProfile_IsAvailableThroughResources()
    {
        EnemyReactionProfile profile = AssetDatabase.LoadAssetAtPath<EnemyReactionProfile>(
            "Assets/GameFeel/Resources/EnemyReactionProfile.asset");
        Assert.That(profile, Is.Not.Null);
    }

    [Test]
    public void SlowStatus_StaysOnNestedEnemyInsteadOfLeakingToSceneParent()
    {
        GameObject container = new("Enemy Container");
        GameObject enemy = new("Nested Enemy");
        enemy.transform.SetParent(container.transform);
        enemy.AddComponent<EnemyHealth>();
        try
        {
            WeaponMovementSlowStatus.Apply(enemy.transform, 0.5f, 3f, "Slow");

            Assert.That(enemy.GetComponent<WeaponMovementSlowStatus>(), Is.Not.Null);
            Assert.That(container.GetComponent<WeaponMovementSlowStatus>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(container);
        }
    }

    [Test]
    public void DisablingEnemyReactions_ClearsCosmeticStatusStateOnly()
    {
        GameObject target = new("Disabled Reaction Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Burn, 3f);
            EnemyStatusFeedback feedback = target.GetComponent<EnemyStatusFeedback>();

            EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions { EnemyReactionEnabled = false });

            Assert.That(feedback.ActiveMask, Is.EqualTo(WeaponStatusMask.None));
        }
        finally
        {
            Object.DestroyImmediate(target);
            EnableReactions();
        }
    }

    [TestCase(WeaponStatusKind.Burn, true, 32, 5, true)]
    [TestCase(WeaponStatusKind.JellifiedBurn, true, 12, 10, true)]
    [TestCase(WeaponStatusKind.Slow, true, 24, 8, true)]
    [TestCase(WeaponStatusKind.Freeze, true, 6, 2, false)]
    [TestCase(WeaponStatusKind.Vulnerable, false, 4, 3, true)]
    public void StatusKinds_UseDistinctVisualGrammar(
        WeaponStatusKind kind,
        bool lowerRingEnabled,
        int upperRingPositions,
        int accentPositions,
        bool accentEnabled)
    {
        GameObject target = new("Status Grammar Target");
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, kind, 3f, 1f);
            Transform visual = target.transform.Find($"[Enemy Status] {kind}");
            Assert.That(visual, Is.Not.Null);

            LineRenderer lowerRing = visual.Find("Lower Status Ring").GetComponent<LineRenderer>();
            LineRenderer upperRing = visual.Find("Upper Status Ring").GetComponent<LineRenderer>();
            LineRenderer accent = visual.Find("Status Accent 0").GetComponent<LineRenderer>();
            Assert.That(lowerRing.enabled, Is.EqualTo(lowerRingEnabled));
            Assert.That(upperRing.positionCount, Is.EqualTo(upperRingPositions));
            Assert.That(accent.positionCount, Is.EqualTo(accentPositions));
            Assert.That(accent.enabled, Is.EqualTo(accentEnabled));
            Assert.That(accent.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/GameFeel/Enemy Status Line"));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void FreezeStatus_CreatesFullBodyIceShell()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = "Freeze Body";
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, WeaponStatusKind.Freeze, 3f, 1f);

            Transform shell = target.transform.Find("[Enemy Freeze Shell] Freeze Body");
            Assert.That(shell, Is.Not.Null);
            Renderer renderer = shell.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/GameFeel/Enemy Ice Shell"));
            Transform visual = target.transform.Find("[Enemy Status] Freeze");
            for (int i = 0; i < 6; i++)
                Assert.That(visual.Find($"Status Accent {i}").GetComponent<LineRenderer>().enabled, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void EnemyReactionShaders_AreAuthoredAndBuildIncluded()
    {
        Shader ice = AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameFeel/Shaders/EnemyIceShell.shader");
        Shader disintegration = AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameFeel/Shaders/EnemyDisintegration.shader");
        Shader statusLine = AssetDatabase.LoadAssetAtPath<Shader>("Assets/GameFeel/Shaders/EnemyStatusLine.shader");

        Assert.That(ice, Is.Not.Null);
        Assert.That(disintegration, Is.Not.Null);
        Assert.That(statusLine, Is.Not.Null);
    }

    [Test]
    public void StatusPalette_UsesDistinctHues()
    {
        WeaponStatusKind[] kinds =
        {
            WeaponStatusKind.Burn,
            WeaponStatusKind.JellifiedBurn,
            WeaponStatusKind.Slow,
            WeaponStatusKind.Freeze,
            WeaponStatusKind.Vulnerable
        };
        Color[] colors = new Color[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            GameObject target = new("Palette Target " + kinds[i]);
            try
            {
                EnemyStatusFeedback.ApplyOrRefresh(target.transform, kinds[i], 3f, 1f);
                Transform visual = target.transform.Find($"[Enemy Status] {kinds[i]}");
                string colorSource = kinds[i] == WeaponStatusKind.Freeze ? "Upper Status Ring" : "Status Accent 0";
                colors[i] = visual.Find(colorSource).GetComponent<LineRenderer>().startColor;
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        for (int a = 0; a < colors.Length; a++)
        {
            Color.RGBToHSV(colors[a], out float hueA, out _, out _);
            for (int b = a + 1; b < colors.Length; b++)
            {
                Color.RGBToHSV(colors[b], out float hueB, out _, out _);
                float delta = Mathf.Abs(hueA - hueB);
                float circularDistance = Mathf.Min(delta, 1f - delta);
                Assert.That(circularDistance, Is.GreaterThan(0.12f), $"{kinds[a]} and {kinds[b]} are too similar.");
            }
        }
    }

    [TestCase(WeaponStatusKind.Burn)]
    [TestCase(WeaponStatusKind.JellifiedBurn)]
    [TestCase(WeaponStatusKind.Slow)]
    [TestCase(WeaponStatusKind.Freeze)]
    [TestCase(WeaponStatusKind.Vulnerable)]
    public void StatusGeometry_ClearsDifferentlyScaledBodyBounds(WeaponStatusKind kind)
    {
        GameObject target = new("Scaled Status Target");
        target.transform.localScale = new Vector3(2f, 1.45f, 0.55f);
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Scaled Enemy Body";
        body.transform.SetParent(target.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        body.transform.localScale = new Vector3(1f, 1.6f, 3f);
        try
        {
            EnemyStatusFeedback.ApplyOrRefresh(target.transform, kind, 3f, 1f);
            Transform visual = target.transform.Find($"[Enemy Status] {kind}");
            string geometrySource = kind == WeaponStatusKind.Freeze ? "Upper Status Ring" : "Status Accent 0";
            LineRenderer accent = visual.Find(geometrySource).GetComponent<LineRenderer>();
            Bounds bodyBounds = body.GetComponent<Renderer>().bounds;
            float extentX = Mathf.Max(0.001f, bodyBounds.extents.x);
            float extentZ = Mathf.Max(0.001f, bodyBounds.extents.z);

            for (int i = 0; i < accent.positionCount; i++)
            {
                Vector3 world = accent.transform.TransformPoint(accent.GetPosition(i));
                float normalizedX = (world.x - bodyBounds.center.x) / extentX;
                float normalizedZ = (world.z - bodyBounds.center.z) / extentZ;
                float ellipseDistance = normalizedX * normalizedX + normalizedZ * normalizedZ;
                Assert.That(ellipseDistance, Is.GreaterThan(1f), $"{kind} accent point {i} intersects the scaled body footprint.");
            }
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    private static WeaponFeedbackContext Context(int damage, bool critical = false, bool weakPoint = false, bool kill = false)
    {
        return new WeaponFeedbackContext(
            weapon: null,
            mode: WeaponFeedbackMode.Automatic,
            normalizedHeat: 0f,
            origin: Vector3.zero,
            direction: Vector3.forward,
            damageAmount: damage,
            isCritical: critical,
            isWeakPoint: weakPoint,
            isKill: kill);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        System.Reflection.FieldInfo field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
