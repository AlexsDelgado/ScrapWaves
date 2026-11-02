using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponDebugGizmos : MonoBehaviour
{
    public bool ShowRuntimeVisuals;
    public bool ShowTargetingCone;
    public bool ShowProjectilePaths;
    public bool ShowExplosionRadius;
    public bool ShowDamageNumbers;
    public bool ShowKnockbackVectors;
    public bool ShowWeaponHitboxes;
    public bool ShowStatusEffectIcons;
    public bool ShowDpsWindow;

    private const int CircleSegments = 72;
    private const int ArcSegments = 24;
    private const float RuntimeLineWidth = 0.055f;
    private static Material s_runtimeMaterial;
    private readonly List<LineRenderer> _runtimeLines = new();
    private WeaponTestingSandboxManager _sandbox;
    private Transform _runtimeRoot;
    private int _runtimeLineIndex;
    private int _geometryFrame = -1;
    private BasicProjectileWeapon _geometryWeapon;
    private int _geometryLevel;
    private WeaponUpgradePath _geometryPath;
    private WeaponGeometrySnapshot _geometry;

    public void Bind(WeaponTestingSandboxManager sandbox)
    {
        _sandbox = sandbox;
        _geometryFrame = -1;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (!ShowRuntimeVisuals || !TryGetWeapon(out BasicProjectileWeapon weapon))
        {
            HideRuntimeLines();
            return;
        }
        BeginRuntimeFrame();
        DrawResolvedGeometry(weapon, true);
        EndRuntimeFrame();
    }

    private void OnDisable() => HideRuntimeLines();

    private void OnDrawGizmos()
    {
        if (TryGetWeapon(out BasicProjectileWeapon weapon)) DrawResolvedGeometry(weapon, false);
    }

    private bool TryGetWeapon(out BasicProjectileWeapon weapon)
    {
        weapon = _sandbox != null ? _sandbox.CurrentManualBehaviour as BasicProjectileWeapon : null;
        return _sandbox != null && _sandbox.PlayerTransform != null && weapon?.Runtime?.Data != null;
    }

    private WeaponGeometrySnapshot ReadGeometry(BasicProjectileWeapon weapon)
    {
        if (!Application.isPlaying || _geometryFrame != Time.frameCount || _geometryWeapon != weapon
            || _geometryLevel != weapon.Runtime.Level || _geometryPath != weapon.Runtime.SelectedPath)
        {
            _geometry = weapon.CaptureGeometry();
            _geometryFrame = Time.frameCount;
            _geometryWeapon = weapon;
            _geometryLevel = weapon.Runtime.Level;
            _geometryPath = weapon.Runtime.SelectedPath;
        }
        return _geometry;
    }

    private void DrawResolvedGeometry(BasicProjectileWeapon weapon, bool runtime)
    {
        Transform player = _sandbox.PlayerTransform;
        Vector3 muzzle = _sandbox.ProjectileSpawn != null ? _sandbox.ProjectileSpawn.position : player.position + Vector3.up;
        Vector3 aim = _sandbox.CurrentAimDirection.sqrMagnitude > 0.0001f
            ? _sandbox.CurrentAimDirection.normalized : player.forward;
        WeaponGeometrySnapshot snapshot = ReadGeometry(weapon);
        if (snapshot != null)
        {
            foreach (WeaponGeometryGuide guide in snapshot.Guides)
            {
                if (!IsVisible(guide.Purpose)) continue;
                Vector3 direction = guide.Direction == WeaponGeometryDirection.OwnerForward ? player.forward : aim;
                if (guide.Direction == WeaponGeometryDirection.HorizontalAim)
                {
                    direction.y = 0f;
                    if (direction.sqrMagnitude <= 0.0001f) { direction = player.forward; direction.y = 0f; }
                }
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
                Vector3 anchor = guide.Anchor == WeaponGeometryAnchor.Owner ? player.position : muzzle;
                DrawGuide(in guide, anchor, direction, runtime);
            }
        }
        // These are already the actual moving damage areas, including their current sizes.
        if (ShowWeaponHitboxes && weapon is FlamethrowerWeapon flame && flame.ManualAreas != null)
        {
            FlamethrowerManualAreas areas = flame.ManualAreas;
            for (int i = 0; i < areas.ActiveCount; i++)
                DrawSphere(areas.GetCenter(i), areas.GetRadius(i), new Color(1f, 0.28f, 0.02f, 0.85f), runtime);
        }
    }

    private bool IsVisible(WeaponGeometryPurpose purpose) => purpose switch
    {
        WeaponGeometryPurpose.Targeting => ShowTargetingCone,
        WeaponGeometryPurpose.ProjectilePath => ShowProjectilePaths,
        WeaponGeometryPurpose.Hitbox => ShowWeaponHitboxes,
        WeaponGeometryPurpose.Explosion => ShowExplosionRadius,
        _ => false
    };

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void DrawGuide(in WeaponGeometryGuide guide, Vector3 anchor, Vector3 forward, bool runtime)
    {
        if (!Finite(guide.ForwardOffsetMeters) || !Finite(guide.RangeMeters) || !Finite(guide.RadiusMeters)
            || !Finite(guide.FullAngleDegrees) || !Finite(guide.ArcHeightMeters)) return;
        if (guide.RangeMeters < 0f || guide.RadiusMeters < 0f) return;
        Vector3 origin = anchor + forward * guide.ForwardOffsetMeters;
        Vector3 end = origin + forward * guide.RangeMeters;
        Color color = guide.Mode == WeaponStatMode.Automatic ? new Color(0.1f, 0.65f, 1f, 0.9f)
            : guide.Mode == WeaponStatMode.Ability ? new Color(1f, 0.42f, 0.02f, 0.9f)
            : new Color(0.4f, 1f, 0.55f, 0.9f);
        if (!runtime) Gizmos.color = color;
        switch (guide.Shape)
        {
            case WeaponGeometryShape.Line:
                if (guide.RangeMeters > 0f) DrawLine(origin, end, color, runtime);
                break;
            case WeaponGeometryShape.Circle:
                DrawCircle(origin, Vector3.up, guide.RadiusMeters, color, runtime);
                break;
            case WeaponGeometryShape.Sphere:
                DrawSphere(origin, guide.RadiusMeters, color, runtime);
                break;
            case WeaponGeometryShape.PlanarCone:
                if (guide.RangeMeters <= 0f || guide.FullAngleDegrees <= 0f) break;
                if (runtime) DrawRuntimeCone(origin, forward, guide.RangeMeters, guide.FullAngleDegrees, color);
                else DrawCone(origin, forward, guide.RangeMeters, guide.FullAngleDegrees);
                break;
            case WeaponGeometryShape.SphericalCone:
                if (guide.RangeMeters > 0f && guide.FullAngleDegrees > 0f)
                    DrawSwordCone(origin, forward, guide.RangeMeters, guide.FullAngleDegrees, runtime);
                break;
            case WeaponGeometryShape.Capsule:
                DrawCapsule(origin, end, guide.RadiusMeters, color, runtime);
                break;
            case WeaponGeometryShape.SweptCircle:
                DrawSweptCircle(origin, guide.RangeMeters, guide.RadiusMeters, color, runtime);
                break;
            case WeaponGeometryShape.Arc:
                if (guide.RangeMeters <= 0f) break;
                if (runtime) DrawRuntimeArc(origin, end, guide.ArcHeightMeters, color);
                else DrawArc(origin, end, guide.ArcHeightMeters);
                break;
        }
    }

    private void DrawLine(Vector3 start, Vector3 end, Color color, bool runtime)
    {
        if (runtime) DrawRuntimeLine(start, end, color, RuntimeLineWidth);
        else { Gizmos.color = color; Gizmos.DrawLine(start, end); }
    }

    private void DrawSphere(Vector3 center, float radius, Color color, bool runtime)
    {
        if (!Finite(radius) || radius <= 0f) return;
        if (runtime) DrawRuntimeSphere(center, radius, color);
        else { Gizmos.color = color; Gizmos.DrawWireSphere(center, radius); }
    }

    private void DrawCircle(Vector3 center, Vector3 normal, float radius, Color color, bool runtime)
    {
        if (!Finite(radius) || radius <= 0f) return;
        if (runtime) { DrawRuntimeCircle(center, normal, radius, color, RuntimeLineWidth); return; }
        GetPlaneBasis(normal, out Vector3 x, out Vector3 y);
        Vector3 previous = center + x * radius;
        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / CircleSegments;
            Vector3 next = center + (x * Mathf.Cos(angle) + y * Mathf.Sin(angle)) * radius;
            DrawLine(previous, next, color, false);
            previous = next;
        }
    }

    private void DrawCapsule(Vector3 start, Vector3 end, float radius, Color color, bool runtime)
    {
        if (!Finite(radius) || radius < 0f) return;
        if (radius == 0f) { DrawLine(start, end, color, runtime); return; }
        Vector3 direction = (end - start).normalized;
        GetPlaneBasis(direction, out Vector3 x, out Vector3 y);
        DrawSphere(start, radius, color, runtime);
        DrawSphere(end, radius, color, runtime);
        foreach (Vector3 radial in new[] { x, -x, y, -y })
            DrawLine(start + radial * radius, end + radial * radius, color, runtime);
    }

    private void DrawSweptCircle(Vector3 center, float orbitRadius, float contactRadius, Color color, bool runtime)
    {
        DrawCircle(center, Vector3.up, orbitRadius + contactRadius, color, runtime);
        if (orbitRadius > contactRadius) DrawCircle(center, Vector3.up, orbitRadius - contactRadius, color, runtime);
        DrawCircle(center + Vector3.up * contactRadius, Vector3.up, orbitRadius, color, runtime);
        DrawCircle(center - Vector3.up * contactRadius, Vector3.up, orbitRadius, color, runtime);
        foreach (Vector3 offset in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            DrawCircle(center + offset * orbitRadius, Vector3.Cross(Vector3.up, offset), contactRadius, color, runtime);
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

    // Draw the same 3D angular/range volume used by the manual sword, including
    // the player apex and the spherical outer edge traced by the slash.
    private void DrawSwordCone(Vector3 origin, Vector3 forward, float range, float angle, bool runtime)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Quaternion frame = Quaternion.LookRotation(forward,
            Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up);
        float halfAngle = Mathf.Clamp(angle, 0f, 180f) * 0.5f * Mathf.Deg2Rad;
        Color color = new(0.4f, 1f, 0.55f, 0.8f);
        void Line(Vector3 a, Vector3 b)
        {
            if (runtime) DrawRuntimeLine(a, b, color, RuntimeLineWidth);
            else { Gizmos.color = color; Gizmos.DrawLine(a, b); }
        }
        Vector3 previous = Vector3.zero;
        for (int i = 0; i <= ArcSegments; i++)
        {
            float azimuth = i * 2f * Mathf.PI / ArcSegments;
            Vector3 edge = origin + frame * new Vector3(
                Mathf.Sin(halfAngle) * Mathf.Cos(azimuth),
                Mathf.Sin(halfAngle) * Mathf.Sin(azimuth), Mathf.Cos(halfAngle)) * range;
            if (i > 0) Line(previous, edge);
            if (i < ArcSegments && i % (ArcSegments / 4) == 0) Line(origin, edge);
            previous = edge;
        }
        for (int plane = 0; plane < 2; plane++)
        {
            for (int i = 0; i <= ArcSegments; i++)
            {
                float sweep = Mathf.Lerp(-halfAngle, halfAngle, i / (float)ArcSegments);
                Vector3 local = plane == 0
                    ? new Vector3(Mathf.Sin(sweep), 0f, Mathf.Cos(sweep))
                    : new Vector3(0f, Mathf.Sin(sweep), Mathf.Cos(sweep));
                Vector3 edge = origin + frame * local * range;
                if (i > 0) Line(previous, edge);
                previous = edge;
            }
        }
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
        if (!Finite(radius) || radius <= 0f) return;

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
            Vector3 point = MortarTrajectory.Evaluate(start, end, height, t);
            points[i] = point;
        }

        DrawRuntimePolyline(points, color, RuntimeLineWidth, loop: false);
    }

    private void DrawRuntimeSphere(Vector3 center, float radius, Color color)
    {
        if (!Finite(radius) || radius <= 0f) return;
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
        if (!Finite(radius) || radius <= 0f) return;

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
            Vector3 point = MortarTrajectory.Evaluate(start, end, height, t);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
