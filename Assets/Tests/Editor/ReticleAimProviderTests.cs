using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ReticleAimProviderTests
{
    [Test]
    public void TryGetAimDirection_WhenReticleHitsDamageableAndAimPointIsPreferred_AimsAtTargetAimPoint()
    {
        GameObject cameraGo = new("AimCamera");
        GameObject providerGo = new("AimProvider");
        GameObject targetGo = new("DamageableTarget");

        try
        {
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            BoxCollider body = targetGo.AddComponent<BoxCollider>();
            targetGo.AddComponent<ReticleAimTestDamageable>();
            targetGo.transform.position = new Vector3(0f, 1f, 10f);
            body.size = new Vector3(2f, 2f, 2f);
            Physics.SyncTransforms();

            ReticleAimProvider provider = providerGo.AddComponent<ReticleAimProvider>();
            SetPrivateField(provider, "_aimCamera", camera);

            Vector3 muzzle = new Vector3(1.5f, 1f, 0f);
            Vector3 expectedDirection = EnemyRegistry.GetAimPoint(targetGo.transform) - muzzle;

            bool hasAim = provider.TryGetAimDirection(muzzle, 20f, true, out Vector3 direction);

            Assert.That(hasAim, Is.True);
            Assert.That(Vector3.Distance(direction, expectedDirection), Is.LessThan(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(targetGo);
            Object.DestroyImmediate(providerGo);
            Object.DestroyImmediate(cameraGo);
        }
    }

    [Test]
    public void TryGetAimDirection_WithoutHit_ConvergesAtRequestedFallbackDistance()
    {
        GameObject cameraGo = new("AimCamera");
        GameObject providerGo = new("AimProvider");

        try
        {
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(-1f, 1.6f, -4f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.35f, 1f).normalized, Vector3.up);

            ReticleAimProvider provider = providerGo.AddComponent<ReticleAimProvider>();
            SetPrivateField(provider, "_aimCamera", camera);
            SetPrivateField(provider, "_aimMask", new LayerMask { value = 0 });

            Vector3 muzzle = new Vector3(0.7f, 1.2f, -0.5f);
            float fallbackDistance = 20f;

            bool hasAim = provider.TryGetAimDirection(muzzle, fallbackDistance, out Vector3 direction);

            Assert.That(hasAim, Is.True);
            Vector3 endpoint = muzzle + direction.normalized * fallbackDistance;
            Ray reticleRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float endpointDistanceFromReticleRay = Vector3.Cross(endpoint - reticleRay.origin, reticleRay.direction).magnitude;
            Assert.That(endpointDistanceFromReticleRay, Is.LessThan(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(providerGo);
            Object.DestroyImmediate(cameraGo);
        }
    }

    private static void SetPrivateField<T>(object target, string name, T value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private sealed class ReticleAimTestDamageable : MonoBehaviour, IDamageable
    {
        public bool ApplyDamage(int amount) => true;
    }
}
