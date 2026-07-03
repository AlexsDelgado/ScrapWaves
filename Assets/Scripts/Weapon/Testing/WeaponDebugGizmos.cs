using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponDebugGizmos : MonoBehaviour
{
    public bool ShowRuntimeVisuals = true;
    public bool ShowTargetingCone = true;
    public bool ShowProjectilePaths = true;
    public bool ShowExplosionRadius = true;
    public bool ShowDamageNumbers = true;
    public bool ShowKnockbackVectors = true;
    public bool ShowWeaponHitboxes = true;
    public bool ShowStatusEffectIcons = true;
    public bool ShowDpsWindow = true;

    private const int CircleSegments = 72;
    private const int ArcSegments = 24;
    private const float RuntimeLineWidth = 0.055f;
    private static Material s_runtimeMaterial;

    private readonly List<LineRenderer> _runtimeLines = new();
    private WeaponTestingSandboxManager _sandbox;
    private Transform _runtimeRoot;
    private int _runtimeLineIndex;

    public void Bind(WeaponTestingSandboxManager sandbox)
    {
        _sandbox = sandbox;
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (!ShowRuntimeVisuals || _sandbox == null || _sandbox.PlayerTransform == null)
        {
            HideRuntimeLines();
            return;
        }

        WeaponInstance weapon = _sandbox.CurrentManualWeapon;
        if (weapon?.Data == null)
        {
            HideRuntimeLines();
            return;
        }

        Transform player = _sandbox.PlayerTransform;
        Vector3 origin = _sandbox.ProjectileSpawn != null ? _sandbox.ProjectileSpawn.position : player.position + Vector3.up;
        Vector3 forward = _sandbox.CurrentAimDirection.sqrMagnitude > 0.0001f ? _sandbox.CurrentAimDirection.normalized : player.forward;

        BeginRuntimeFrame();
        DrawRuntimeTargeting(weapon, origin, forward);
        DrawRuntimeWeaponSpecific(weapon, origin, forward);
        EndRuntimeFrame();
    }

    private void OnDisable()
    {
        HideRuntimeLines();
    }

    private void OnDrawGizmos()
    {
        if (_sandbox == null || _sandbox.PlayerTransform == null)
            return;

        WeaponInstance weapon = _sandbox.CurrentManualWeapon;
        if (weapon?.Data == null)
            return;

        Transform player = _sandbox.PlayerTransform;
        Vector3 origin = _sandbox.ProjectileSpawn != null ? _sandbox.ProjectileSpawn.position : player.position + Vector3.up;
        Vector3 forward = _sandbox.CurrentAimDirection.sqrMagnitude > 0.0001f ? _sandbox.CurrentAimDirection.normalized : player.forward;

        DrawTargeting(weapon, origin, forward);
        DrawWeaponSpecific(weapon, origin, forward);
    }

    private void DrawRuntimeTargeting(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        if (!ShowTargetingCone || weapon.Data.WeaponType == WeaponType.RotatingBlade)
            return;

        Color color = new(0.1f, 0.65f, 1f, 0.9f);
        if (weapon.Data.WeaponType == WeaponType.Flamethrower)
            DrawRuntimeHose(origin, forward, weapon.Data.BaseRange, weapon.Data.Flamethrower.FlameHoseRadius, color);
        else
            DrawRuntimeCone(origin, forward, weapon.Data.BaseRange, 90f, color);
    }

    private void DrawRuntimeWeaponSpecific(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        switch (weapon.Data.WeaponType)
        {
            case WeaponType.Flamethrower:
                DrawRuntimeFlamethrower(weapon, origin, forward);
                break;
            case WeaponType.RocketLauncher:
                DrawRuntimeRocketLauncher(weapon, origin, forward);
                break;
            case WeaponType.Mortar:
                DrawRuntimeMortar(weapon, origin, forward);
                break;
            case WeaponType.AutomaticCannon:
                DrawRuntimeAutomaticCannon(weapon, origin, forward);
                break;
            case WeaponType.RotatingBlade:
                DrawRuntimeRotatingBlade(weapon, origin, forward);
                break;
        }
    }

    private void DrawRuntimeFlamethrower(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        FlamethrowerTuning tuning = weapon.Data.Flamethrower;
        float size = GetAreaSize();
        Color guideColor = GetFlamethrowerGuideColor(weapon, 0.9f);
        if (ShowWeaponHitboxes)
            DrawRuntimeHose(origin, forward, weapon.Data.BaseRange * size, tuning.FlameHoseRadius * size, guideColor);

        if (ShowExplosionRadius && !IsJellifiedFuelPath(weapon))
            DrawRuntimeSphere(_sandbox.PlayerTransform.position, tuning.FlameActiveRadius * size, GetFlamethrowerGuideColor(weapon, 0.75f));
    }

    private void DrawRuntimeRocketLauncher(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        RocketLauncherTuning tuning = weapon.Data.RocketLauncher;
        float size = GetAreaSize();
        Vector3 target = origin + forward.normalized * weapon.Data.BaseRange;

        if (ShowProjectilePaths)
            DrawRuntimeLine(origin, target, new Color(1f, 0.9f, 0.05f, 0.95f), RuntimeLineWidth);

        if (ShowExplosionRadius)
            DrawRuntimeSphere(target, tuning.RocketManualExplosionRadius * size, new Color(1f, 0.42f, 0.02f, 0.95f));
    }

    private void DrawRuntimeMortar(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        MortarTuning tuning = weapon.Data.Mortar;
        float size = GetAreaSize();
        Vector3 target = origin + forward.normalized * weapon.Data.BaseRange;

        if (ShowProjectilePaths)
        {
            DrawRuntimeArc(origin, target, tuning.MortarArcHeight, new Color(1f, 0.86f, 0.05f, 0.95f));
            Vector3 dropStart = target + Vector3.up * tuning.MortarActiveDropHeight;
            DrawRuntimeLine(dropStart, target, new Color(1f, 0.25f, 0.04f, 0.95f), RuntimeLineWidth * 1.5f);
            DrawRuntimeSphere(dropStart, Mathf.Max(0.08f, tuning.MortarShellCollisionRadius * size), new Color(1f, 0.55f, 0.08f, 0.9f));
        }

        if (ShowExplosionRadius)
        {
            DrawRuntimeSphere(target, tuning.MortarManualExplosionRadius * size, new Color(1f, 0.45f, 0.04f, 0.95f));
            DrawRuntimeSphere(target, tuning.MortarActiveExplosionRadius * size, new Color(1f, 0.12f, 0.02f, 0.8f));
            DrawRuntimeCircle(target, Vector3.up, tuning.MortarManualAccuracyRadius, new Color(1f, 1f, 1f, 0.65f), RuntimeLineWidth);
        }

        if (ShowWeaponHitboxes)
            DrawRuntimeCircle(target, Vector3.up, tuning.MortarBarrageRadius * size, new Color(1f, 0.82f, 0.05f, 0.75f), RuntimeLineWidth);
    }

    private void DrawRuntimeAutomaticCannon(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        AutomaticCannonTuning tuning = weapon.Data.AutomaticCannon;
        Vector3 direction = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        if (ShowProjectilePaths)
        {
            Color pathColor = new(0.78f, 1f, 0.16f, 0.95f);
            int count = Mathf.Max(1, tuning.CannonManualBurstCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 start = origin + direction * (tuning.CannonManualLineSpacing * i);
                DrawRuntimeLine(start, start + direction * weapon.Data.BaseRange, pathColor, RuntimeLineWidth);
            }
        }

        if (ShowWeaponHitboxes)
            DrawRuntimeCone(origin, direction, weapon.Data.BaseRange, tuning.CannonAbilityScatterRadius, new Color(1f, 1f, 1f, 0.55f));
    }

    private void DrawRuntimeRotatingBlade(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        RotatingBladeTuning tuning = weapon.Data.RotatingBlade;
        float size = GetAreaSize();
        Vector3 playerPosition = _sandbox.PlayerTransform.position;

        if (ShowWeaponHitboxes)
        {
            float orbitRadius = tuning.BladeOrbitRadius * size;
            float hitRadius = tuning.BladeHitRadius * size;
            DrawRuntimeCircle(playerPosition, Vector3.up, orbitRadius, new Color(0.45f, 0.95f, 1f, 0.9f), RuntimeLineWidth);

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude <= 0.0001f)
                flatForward = _sandbox.PlayerTransform.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude > 0.0001f ? flatForward.normalized : Vector3.forward;

            DrawRuntimeSphere(playerPosition + flatForward * orbitRadius, hitRadius, new Color(0.2f, 1f, 1f, 0.95f));
            DrawRuntimeCone(origin, forward, tuning.BladeManualRange * size, tuning.BladeManualConeAngle, new Color(0.4f, 1f, 0.55f, 0.8f));
        }

        if (ShowProjectilePaths)
        {
            float activeRange = tuning.BladeManualRange * size * GetBladeActiveRangeMultiplier(tuning);
            float activeRadius = tuning.BladeActiveLineWidth * size * 0.5f;
            DrawRuntimeHose(origin, forward, activeRange, activeRadius, new Color(0.18f, 1f, 1f, 0.95f));
        }
    }

    private void DrawTargeting(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        if (!ShowTargetingCone)
            return;

        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.65f);
        float range = weapon.Data.BaseRange;
        if (weapon.Data.WeaponType == WeaponType.RotatingBlade)
            return;
        if (weapon.Data.WeaponType == WeaponType.Flamethrower)
            DrawHose(origin, forward, range, weapon.Data.Flamethrower.FlameHoseRadius);
        else
            DrawCone(origin, forward, range, 90f);
    }

    private void DrawWeaponSpecific(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        switch (weapon.Data.WeaponType)
        {
            case WeaponType.Flamethrower:
                DrawFlamethrower(weapon, origin, forward);
                break;
            case WeaponType.RocketLauncher:
                DrawRocketLauncher(weapon, origin, forward);
                break;
            case WeaponType.Mortar:
                DrawMortar(weapon, origin, forward);
                break;
            case WeaponType.AutomaticCannon:
                DrawAutomaticCannon(weapon, origin, forward);
                break;
            case WeaponType.RotatingBlade:
                DrawRotatingBlade(weapon, origin, forward);
                break;
        }
    }

    private void DrawFlamethrower(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        FlamethrowerTuning tuning = weapon.Data.Flamethrower;
        if (ShowWeaponHitboxes)
        {
            Gizmos.color = GetFlamethrowerGuideColor(weapon, 0.85f);
            DrawHose(origin, forward, weapon.Data.BaseRange, tuning.FlameHoseRadius);
        }

        if (ShowExplosionRadius && !IsJellifiedFuelPath(weapon))
        {
            Gizmos.color = GetFlamethrowerGuideColor(weapon, 0.7f);
            Gizmos.DrawWireSphere(_sandbox.PlayerTransform.position, tuning.FlameActiveRadius);
        }
    }

    private static bool IsJellifiedFuelPath(WeaponInstance weapon) =>
        weapon != null && weapon.HasAdvancedPath && weapon.SelectedPath == WeaponUpgradePath.PathA;

    private static bool IsLiquidNitrogenPath(WeaponInstance weapon) =>
        weapon != null && weapon.HasAdvancedPath && weapon.SelectedPath == WeaponUpgradePath.PathB;

    private static Color GetFlamethrowerGuideColor(WeaponInstance weapon, float alpha)
    {
        if (IsJellifiedFuelPath(weapon))
            return new Color(0.03f, 0.28f, 0.06f, alpha);
        if (IsLiquidNitrogenPath(weapon))
            return new Color(0.55f, 0.9f, 1f, alpha);
        return new Color(1f, 0.28f, 0.02f, alpha);
    }

    private void DrawRocketLauncher(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        RocketLauncherTuning tuning = weapon.Data.RocketLauncher;
        Vector3 target = origin + forward * weapon.Data.BaseRange;

        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.8f);
            Gizmos.DrawLine(origin, target);
        }

        if (ShowExplosionRadius)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.75f);
            Gizmos.DrawWireSphere(target, tuning.RocketManualExplosionRadius);
        }
    }

    private void DrawMortar(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        MortarTuning tuning = weapon.Data.Mortar;
        Vector3 target = origin + forward * weapon.Data.BaseRange;
        float size = _sandbox.StatOverride != null ? _sandbox.StatOverride.ProjectileAreaSizeMultiplier : 1f;

        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.05f, 0.8f);
            DrawArc(origin, target, tuning.MortarArcHeight);

            Gizmos.color = new Color(1f, 0.25f, 0.05f, 0.9f);
            Vector3 dropStart = target + Vector3.up * tuning.MortarActiveDropHeight;
            Gizmos.DrawLine(dropStart, target);
            Gizmos.DrawWireSphere(dropStart, Mathf.Max(0.08f, tuning.MortarShellCollisionRadius * size));
        }

        if (ShowExplosionRadius)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.75f);
            Gizmos.DrawWireSphere(target, tuning.MortarManualExplosionRadius * size);
            Gizmos.color = new Color(1f, 0.2f, 0.05f, 0.45f);
            Gizmos.DrawWireSphere(target, tuning.MortarActiveExplosionRadius * size);
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(target, tuning.MortarManualAccuracyRadius);
        }

        if (ShowWeaponHitboxes)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.05f, 0.45f);
            Gizmos.DrawWireSphere(target, tuning.MortarBarrageRadius * size);
        }
    }

    private void DrawAutomaticCannon(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        AutomaticCannonTuning tuning = weapon.Data.AutomaticCannon;
        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(0.8f, 1f, 0.2f, 0.8f);
            for (int i = 0; i < tuning.CannonManualBurstCount; i++)
            {
                Vector3 start = origin + forward * (tuning.CannonManualLineSpacing * i);
                Gizmos.DrawLine(start, start + forward * weapon.Data.BaseRange);
            }
        }

        if (ShowWeaponHitboxes)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            DrawCone(origin, forward, weapon.Data.BaseRange, tuning.CannonAbilityScatterRadius);
        }
    }

    private void DrawRotatingBlade(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        RotatingBladeTuning tuning = weapon.Data.RotatingBlade;
        float size = _sandbox.StatOverride != null ? _sandbox.StatOverride.ProjectileAreaSizeMultiplier : 1f;

        if (ShowWeaponHitboxes)
        {
            Gizmos.color = new Color(0.75f, 0.95f, 1f, 0.85f);
            float orbitRadius = tuning.BladeOrbitRadius * size;
            float hitRadius = tuning.BladeHitRadius * size;
            Gizmos.DrawWireSphere(_sandbox.PlayerTransform.position, orbitRadius);

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude <= 0.0001f)
                flatForward = _sandbox.PlayerTransform.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude > 0.0001f ? flatForward.normalized : Vector3.forward;

            Gizmos.color = new Color(0.45f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(_sandbox.PlayerTransform.position + flatForward * orbitRadius, hitRadius);

            Gizmos.color = new Color(0.6f, 1f, 0.65f, 0.85f);
            DrawCone(origin, forward, tuning.BladeManualRange * size, tuning.BladeManualConeAngle);
        }

        if (ShowProjectilePaths)
        {
            float activeRange = tuning.BladeManualRange * size * GetBladeActiveRangeMultiplier(tuning);
            float activeRadius = tuning.BladeActiveLineWidth * size * 0.5f;
            Gizmos.color = new Color(0.2f, 1f, 1f, 0.85f);
            DrawHose(origin, forward, activeRange, activeRadius);
        }
    }

    private float GetBladeActiveRangeMultiplier(RotatingBladeTuning tuning)
    {
        float heatPercent = _sandbox.HeatOverride != null ? _sandbox.HeatOverride.NormalizedHeat * 100f : 0f;
        float stepPercent = Mathf.Max(0.01f, tuning.BladeActiveHeatStepPercent);
        float bonusSteps = Mathf.Floor(heatPercent / stepPercent);
        return Mathf.Min(
            Mathf.Max(1f, tuning.BladeActiveBaseRangeMultiplier) + bonusSteps,
            Mathf.Max(tuning.BladeActiveBaseRangeMultiplier, tuning.BladeActiveMaxRangeMultiplier));
    }

    private float GetAreaSize()
    {
        return _sandbox.StatOverride != null ? Mathf.Max(0.01f, _sandbox.StatOverride.ProjectileAreaSizeMultiplier) : 1f;
    }

    private void BeginRuntimeFrame()
    {
        EnsureRuntimeRoot();
        _runtimeLineIndex = 0;
    }

    private void EndRuntimeFrame()
    {
        for (int i = _runtimeLineIndex; i < _runtimeLines.Count; i++)
            _runtimeLines[i].enabled = false;
    }

    private void HideRuntimeLines()
    {
        for (int i = 0; i < _runtimeLines.Count; i++)
        {
            if (_runtimeLines[i] != null)
                _runtimeLines[i].enabled = false;
        }
    }

    private void EnsureRuntimeRoot()
    {
        if (_runtimeRoot != null)
            return;

        GameObject root = new("[WeaponDebugRuntimeVisuals]");
        root.transform.SetParent(transform, false);
        _runtimeRoot = root.transform;
    }

    private void DrawRuntimeLine(Vector3 start, Vector3 end, Color color, float width)
    {
        DrawRuntimePolyline(new[] { start, end }, color, width, loop: false);
    }

    private void DrawRuntimePolyline(Vector3[] points, Color color, float width, bool loop)
    {
        if (points == null || points.Length <= 1)
            return;

        LineRenderer line = GetRuntimeLine();
        line.loop = loop;
        line.positionCount = points.Length;
        line.widthMultiplier = Mathf.Max(0.01f, width);
        line.startColor = color;
        line.endColor = color;
        line.SetPositions(points);
    }

    private LineRenderer GetRuntimeLine()
    {
        EnsureRuntimeRoot();

        if (_runtimeLineIndex >= _runtimeLines.Count)
        {
            GameObject go = new($"Runtime Debug Line {_runtimeLineIndex:00}");
            go.transform.SetParent(_runtimeRoot, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = GetRuntimeMaterial();
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            _runtimeLines.Add(line);
        }

        LineRenderer result = _runtimeLines[_runtimeLineIndex];
        result.enabled = true;
        _runtimeLineIndex++;
        return result;
    }

    private void DrawRuntimeCone(Vector3 origin, Vector3 forward, float range, float angle, Color color)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float clampedAngle = Mathf.Clamp(angle, 1f, 360f);
        float halfAngle = clampedAngle * 0.5f;
        Vector3 leftPoint = origin + Quaternion.AngleAxis(-halfAngle, Vector3.up) * flatForward * range;
        Vector3 rightPoint = origin + Quaternion.AngleAxis(halfAngle, Vector3.up) * flatForward * range;

        DrawRuntimeLine(origin, leftPoint, color, RuntimeLineWidth);
        DrawRuntimeLine(origin, rightPoint, color, RuntimeLineWidth);

        Vector3[] arc = new Vector3[ArcSegments + 1];
        for (int i = 0; i <= ArcSegments; i++)
        {
            float t = i / (float)ArcSegments;
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            arc[i] = origin + Quaternion.AngleAxis(yaw, Vector3.up) * flatForward * range;
        }

        DrawRuntimePolyline(arc, color, RuntimeLineWidth, loop: clampedAngle >= 359.9f);
    }

    private void DrawRuntimeHose(Vector3 origin, Vector3 forward, float range, float radius, Color color)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        range = Mathf.Max(0f, range);
        radius = Mathf.Max(0.01f, radius);

        GetPlaneBasis(forward, out Vector3 side, out Vector3 vertical);
        Vector3 end = origin + forward * range;

        DrawRuntimeLine(origin, end, color, RuntimeLineWidth);
        DrawRuntimeLine(origin + side * radius, end + side * radius, color, RuntimeLineWidth);
        DrawRuntimeLine(origin - side * radius, end - side * radius, color, RuntimeLineWidth);
        DrawRuntimeLine(origin + vertical * radius, end + vertical * radius, color, RuntimeLineWidth);
        DrawRuntimeLine(origin - vertical * radius, end - vertical * radius, color, RuntimeLineWidth);

        int rings = 6;
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            DrawRuntimeCircle(origin + forward * (range * t), forward, radius * Mathf.Lerp(1f, 0.65f, t), color, RuntimeLineWidth);
        }
    }

    private void DrawRuntimeArc(Vector3 start, Vector3 end, float height, Color color)
    {
        Vector3[] points = new Vector3[ArcSegments + 1];
        for (int i = 0; i <= ArcSegments; i++)
        {
            float t = i / (float)ArcSegments;
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y += 4f * height * t * (1f - t);
            points[i] = point;
        }

        DrawRuntimePolyline(points, color, RuntimeLineWidth, loop: false);
    }

    private void DrawRuntimeSphere(Vector3 center, float radius, Color color)
    {
        radius = Mathf.Max(0.01f, radius);
        DrawRuntimeCircle(center, Vector3.up, radius, color, RuntimeLineWidth);
        DrawRuntimeCircle(center, Vector3.right, radius, color, RuntimeLineWidth);
        DrawRuntimeCircle(center, Vector3.forward, radius, color, RuntimeLineWidth);
    }

    private void DrawRuntimeCircle(Vector3 center, Vector3 normal, float radius, Color color, float width)
    {
        if (radius <= 0f)
            return;

        GetPlaneBasis(normal, out Vector3 axisA, out Vector3 axisB);
        Vector3[] points = new Vector3[CircleSegments];
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i / (float)CircleSegments * Mathf.PI * 2f;
            points[i] = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
        }

        DrawRuntimePolyline(points, color, width, loop: true);
    }

    private static void GetPlaneBasis(Vector3 normal, out Vector3 axisA, out Vector3 axisB)
    {
        if (normal.sqrMagnitude <= 0.0001f)
            normal = Vector3.up;

        normal.Normalize();
        Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        axisA = Vector3.Cross(normal, reference).normalized;
        axisB = Vector3.Cross(normal, axisA).normalized;
    }

    private static Material GetRuntimeMaterial()
    {
        if (s_runtimeMaterial != null)
            return s_runtimeMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        s_runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_runtimeMaterial;
    }

    private static void DrawCone(Vector3 origin, Vector3 forward, float range, float angle)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Quaternion left = Quaternion.AngleAxis(-angle * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(angle * 0.5f, Vector3.up);
        Vector3 leftPoint = origin + left * flatForward * range;
        Vector3 rightPoint = origin + right * flatForward * range;

        Gizmos.DrawLine(origin, leftPoint);
        Gizmos.DrawLine(origin, rightPoint);
        int segments = 20;
        Vector3 previous = leftPoint;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float yaw = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, t);
            Vector3 next = origin + Quaternion.AngleAxis(yaw, Vector3.up) * flatForward * range;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private static void DrawHose(Vector3 origin, Vector3 forward, float range, float radius)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        range = Mathf.Max(0f, range);
        radius = Mathf.Max(0.01f, radius);

        Vector3 side = Vector3.Cross(Vector3.up, forward);
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.Cross(Vector3.forward, forward);
        side.Normalize();

        Vector3 vertical = Vector3.Cross(forward, side).normalized;
        Vector3 end = origin + forward * range;

        Gizmos.DrawLine(origin, end);
        Gizmos.DrawLine(origin + side * radius, end + side * radius);
        Gizmos.DrawLine(origin - side * radius, end - side * radius);
        Gizmos.DrawLine(origin + vertical * radius, end + vertical * radius);
        Gizmos.DrawLine(origin - vertical * radius, end - vertical * radius);

        int rings = 6;
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            Gizmos.DrawWireSphere(origin + forward * (range * t), radius * Mathf.Lerp(1f, 0.65f, t));
        }
    }

    private static void DrawArc(Vector3 start, Vector3 end, float height)
    {
        Vector3 previous = start;
        for (int i = 1; i <= 24; i++)
        {
            float t = i / 24f;
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y += 4f * height * t * (1f - t);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
