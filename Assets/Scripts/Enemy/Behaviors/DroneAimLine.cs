using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reusable, depth-tested attack warning. Owns no gameplay state or material instances.</summary>
internal sealed class DroneAimLine
{
    public const float WarningSeconds = .5f;
    private const float DistancePastTarget = 12f;
    private readonly LineRenderer _line;

    public static bool TryGetWarningProgress(float remaining, float attackDuration, out float progress)
    {
        float window = Mathf.Min(WarningSeconds, Mathf.Max(0f, attackDuration));
        progress = 0f;
        if (window <= 0f || float.IsNaN(remaining) || remaining <= 0f || remaining > window) return false;
        progress = Mathf.Clamp01(1f - remaining / window);
        return true;
    }

    public DroneAimLine(Transform owner)
    {
        Material material = Resources.Load<Material>("VFX/DroneAimLine");
        if (material == null || material.shader == null || !material.shader.isSupported)
        {
            Debug.LogWarning("Drone aim line material unavailable; attack behavior is unchanged.", owner);
            return;
        }
        var child = new GameObject("Locking aim line");
        child.transform.SetParent(owner, false);
        child.layer = owner.gameObject.layer;
        _line = child.AddComponent<LineRenderer>();
        _line.enabled = false;
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.numCapVertices = 3;
        _line.alignment = LineAlignment.View;
        _line.shadowCastingMode = ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.lightProbeUsage = LightProbeUsage.Off;
        _line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _line.sharedMaterial = material;
    }

    public void Show(Vector3 origin, Vector3 target, float progress, float width, float lockDuration)
    {
        if (_line == null) return;
        if (!Finite(origin) || !Finite(target) || (target - origin).sqrMagnitude < .0001f)
        {
            Hide();
            return;
        }
        var preferences = PresentationAccessibilityRuntime.Current;
        var settings = UserSettingsService.Instance;
        bool steady = preferences.ReducedMotion || preferences.ReducedFlash ||
            (settings != null && (settings.ReducedMotion || !settings.ScreenFlash));
        // Charge reads through thickness and hue; accessibility keeps a steady warning.
        float charge = steady ? .65f : Mathf.Clamp01(progress);
        Color color = Color.Lerp(new Color(1f, .65f, .16f, .6f), new Color(1f, .23f, .08f, .92f), charge);
        if (!steady)
        {
            // Integrate a frequency ramp (0.8 -> 5.5 Hz). Multiplying an animated
            // frequency by wall-clock time would jump phase between attacks.
            float elapsed = charge * Mathf.Max(0f, lockDuration);
            float cycles = elapsed * (.8f + .5f * (5.5f - .8f) * charge);
            float wave = .5f + .5f * Mathf.Cos(cycles * Mathf.PI * 2f);
            color.a *= Mathf.Lerp(.18f, 1f, Mathf.SmoothStep(0f, 1f, wave));
        }
        _line.startColor = color;
        _line.endColor = color;
        _line.widthMultiplier = Mathf.Max(.005f, width) * Mathf.Lerp(.8f, 1.4f, charge);
        _line.SetPosition(0, origin);
        _line.SetPosition(1, target + (target - origin).normalized * DistancePastTarget);
        _line.enabled = true;
    }

    public void Hide()
    {
        if (_line != null) _line.enabled = false;
    }

    public void Dispose()
    {
        if (_line == null) return;
        Hide();
        if (Application.isPlaying) Object.Destroy(_line.gameObject);
        else Object.DestroyImmediate(_line.gameObject);
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
