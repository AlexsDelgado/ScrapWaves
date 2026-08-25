using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PresentationAccessibilityConsumerTests
{
    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions
        {
            EnemyReactionEnabled = true,
            Quality = GameFeelQualityLevel.High
        });
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions
        {
            EnemyReactionEnabled = true,
            Quality = GameFeelQualityLevel.High
        });
    }

    [Test]
    public void EnemyReactionRuntime_MirrorsReducedMotion()
    {
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions
        {
            EnemyReactionEnabled = true,
            ReducedMotion = true
        });

        Assert.That(EnemyReactionRuntime.ReducedMotion, Is.True);
    }

    [Test]
    public void EnemyHitFeedback_ReducedMotionShrinksDisplacementAndSquash()
    {
        GameObject root = new("Reduced motion enemy");
        root.SetActive(false);
        GameObject visual = new("Cosmetic body");
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>();
        visual.AddComponent<MeshRenderer>();
        EnemyReactionProfile profile = ScriptableObject.CreateInstance<EnemyReactionProfile>();
        EnemyHitFeedback feedback = root.AddComponent<EnemyHitFeedback>();
        SetPrivateField(feedback, "_profile", profile);
        SetPrivateField(feedback, "_visualRoot", visual.transform);
        root.SetActive(true);

        try
        {
            WeaponFeedbackContext context = new(
                weapon: null,
                mode: WeaponFeedbackMode.Manual,
                normalizedHeat: 0f,
                origin: Vector3.back,
                direction: Vector3.forward,
                damageAmount: 20,
                isCritical: true,
                target: root.transform);

            feedback.Play(in context, reducedFlash: false, reducedMotion: false);
            float fullDisplacement = visual.transform.localPosition.magnitude;
            float fullSquash = Mathf.Abs(1f - visual.transform.localScale.y);

            feedback.enabled = false;
            feedback.enabled = true;
            feedback.Play(in context, reducedFlash: false, reducedMotion: true);

            Assert.That(visual.transform.localPosition.magnitude, Is.LessThan(fullDisplacement * 0.5f));
            Assert.That(Mathf.Abs(1f - visual.transform.localScale.y), Is.LessThan(fullSquash * 0.5f));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void CameraFeedback_ReducedMotionScalesTravelAndRemovesFovKick()
    {
        GameObject cameraObject = new("Reduced motion camera");
        cameraObject.AddComponent<Camera>();
        ThirdPersonCamera camera = cameraObject.AddComponent<ThirdPersonCamera>();
        CameraFeedbackController controller = new();
        controller.Bind(camera);
        WeaponPresentationCueData cue = new()
        {
            CameraPositionImpulse = new Vector3(0.1f, 0f, 0f),
            CameraRotationImpulse = new Vector3(0f, 0.2f, 0f),
            CameraFovKick = 4f
        };
        WeaponFeedbackContext context = new(
            weapon: null,
            mode: WeaponFeedbackMode.Manual,
            normalizedHeat: 0f,
            origin: Vector3.zero,
            direction: Vector3.forward);

        try
        {
            Assert.That(
                controller.Request(cue, in context, null, enabled: true, reducedShake: false, reducedMotion: true, now: 1f),
                Is.True);
            Assert.That(camera.CurrentPresentationPositionImpulse.x, Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(camera.CurrentPresentationRotationImpulse.y, Is.EqualTo(0.07f).Within(0.0001f));
            Assert.That(camera.CurrentPresentationFovKick, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void WeaponRecoil_ReducedMotionScalesCosmeticTravelOnly()
    {
        GameObject root = new("Reduced motion recoil");
        root.SetActive(false);
        GameObject recoilRoot = new("Cosmetic recoil root");
        recoilRoot.transform.SetParent(root.transform, false);
        WeaponRecoilFeedback recoil = root.AddComponent<WeaponRecoilFeedback>();
        SetPrivateField(recoil, "_recoilRoot", recoilRoot.transform);
        root.SetActive(true);
        WeaponFeedbackContext context = new(
            weapon: null,
            mode: WeaponFeedbackMode.Manual,
            normalizedHeat: 0f,
            origin: Vector3.zero,
            direction: Vector3.forward);

        try
        {
            recoil.Request(in context, null, heatEnabled: false, reducedMotion: false);
            float full = GetPrivateField<float>(recoil, "_targetRecoil");
            recoil.enabled = false;
            recoil.enabled = true;
            recoil.Request(in context, null, heatEnabled: false, reducedMotion: true);
            float reduced = GetPrivateField<float>(recoil, "_targetRecoil");

            Assert.That(reduced, Is.EqualTo(full * 0.2f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void HitStop_ReducedMotionSuppressesRoutineAndCapsImportantEvents()
    {
        HitStopController hitStop = new();
        try
        {
            Assert.That(
                hitStop.Request(0.08f, priority: 0, enabled: true, reducedFeedback: false, reducedMotion: true, now: 0f),
                Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            Assert.That(
                hitStop.Request(0.08f, priority: 3, enabled: true, reducedFeedback: false, reducedMotion: true, now: 0f),
                Is.True);
            Assert.That(hitStop.RemainingDuration, Is.EqualTo(0.015f).Within(0.0001f));
            Assert.That(Time.timeScale, Is.Zero);
        }
        finally
        {
            hitStop.Restore();
        }
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (T)field.GetValue(instance);
    }
}
