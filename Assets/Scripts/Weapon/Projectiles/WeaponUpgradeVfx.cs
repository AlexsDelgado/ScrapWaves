using System.Collections.Generic;
using UnityEngine;

public sealed class WeaponUpgradeVfx : MonoBehaviour
{
    private const int RingSegments = 72;
    private const float HeightOffset = 0.18f;

    private static Material s_lineMaterial;

    private readonly List<LineRenderer> _lines = new();
    private readonly List<float> _baseWidths = new();

    private Transform _followTarget;
    private TextMesh _label;
    private Color _color;
    private float _duration = 0.4f;
    private float _elapsed;
    private bool _isRing;
    private float _ringRadius;

    public static WeaponUpgradeVfx SpawnRing(
        Vector3 center,
        float radius,
        Color color,
        float duration,
        float widthMultiplier,
        string label)
    {
        if (radius <= 0f)
            return null;

        WeaponUpgradeVfx vfx = Create("Ring", Lift(center), color, duration, label);
        vfx._isRing = true;
        vfx._ringRadius = Mathf.Max(0.01f, radius);
        LineRenderer ring = vfx.CreateLine("Upgrade Ring", Mathf.Max(0.02f, 0.08f * widthMultiplier), useWorldSpace: false, loop: true);
        ring.positionCount = RingSegments;
        vfx.DrawRing(ring, vfx._ringRadius);
        return vfx;
    }

    public static WeaponUpgradeVfx SpawnBeam(
        Vector3 start,
        Vector3 end,
        Color color,
        float duration,
        float width,
        string label)
    {
        if ((end - start).sqrMagnitude <= 0.0001f)
            return null;

        start = Lift(start);
        end = Lift(end);
        WeaponUpgradeVfx vfx = Create("Beam", (start + end) * 0.5f, color, duration, label);
        LineRenderer beam = vfx.CreateLine("Upgrade Beam", Mathf.Max(0.01f, width), useWorldSpace: true, loop: false);
        beam.positionCount = 2;
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);
        return vfx;
    }

    public static WeaponUpgradeVfx SpawnCone(
        Vector3 origin,
        Vector3 direction,
        float range,
        float angle,
        Color color,
        float duration,
        int rays,
        string label)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f || range <= 0f || angle <= 0f)
            return null;

        direction.Normalize();
        origin = Lift(origin);
        WeaponUpgradeVfx vfx = Create("Cone", origin, color, duration, label);
        int rayCount = Mathf.Clamp(rays, 3, 15);
        float halfAngle = Mathf.Clamp(angle, 1f, 180f) * 0.5f;
        for (int i = 0; i < rayCount; i++)
        {
            float t = rayCount == 1 ? 0.5f : i / (float)(rayCount - 1);
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 rayDirection = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            LineRenderer ray = vfx.CreateLine("Upgrade Cone Ray", 0.045f, useWorldSpace: true, loop: false);
            ray.positionCount = 2;
            ray.SetPosition(0, origin + Vector3.up * 0.12f);
            ray.SetPosition(1, origin + Vector3.up * 0.12f + rayDirection * range);
        }

        return vfx;
    }

    private static Vector3 Lift(Vector3 position)
    {
        position.y += HeightOffset;
        return position;
    }

    public static WeaponUpgradeVfx SpawnTargetPulse(Transform target, Color color, float duration, string label)
    {
        if (target == null)
            return null;

        WeaponUpgradeVfx vfx = SpawnRing(target.position + Vector3.up * 0.08f, 0.75f, color, duration, 1.4f, label);
        if (vfx != null)
            vfx._followTarget = target;
        return vfx;
    }

    private static WeaponUpgradeVfx Create(string kind, Vector3 position, Color color, float duration, string label)
    {
        GameObject go = new($"[WeaponUpgradeVfx] {kind}");
        go.transform.position = position;
        WeaponUpgradeVfx vfx = go.AddComponent<WeaponUpgradeVfx>();
        vfx._color = color;
        vfx._duration = Mathf.Max(0.05f, duration);
        vfx.CreateLabel(label);
        return vfx;
    }

    private LineRenderer CreateLine(string childName, float width, bool useWorldSpace, bool loop)
    {
        GameObject child = new(childName);
        child.transform.SetParent(transform, false);

        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = useWorldSpace;
        line.loop = loop;
        line.material = GetLineMaterial();
        line.widthMultiplier = width;
        line.numCornerVertices = 3;
        line.numCapVertices = 3;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        SetLineColor(line, _color);

        _lines.Add(line);
        _baseWidths.Add(width);
        return line;
    }

    private void CreateLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        GameObject labelGo = new("Upgrade VFX Label");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = Vector3.up * 1.1f;
        _label = labelGo.AddComponent<TextMesh>();
        _label.text = label;
        _label.fontSize = 24;
        _label.characterSize = 0.08f;
        _label.anchor = TextAnchor.MiddleCenter;
        _label.alignment = TextAlignment.Center;
        _label.color = _color;
    }

    private void Update()
    {
        if (_followTarget != null)
            transform.position = Lift(_followTarget.position + Vector3.up * 0.08f);

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        float alpha = 1f - t;

        for (int i = 0; i < _lines.Count; i++)
        {
            LineRenderer line = _lines[i];
            if (line == null)
                continue;

            Color faded = _color;
            faded.a *= alpha;
            SetLineColor(line, faded);
            line.widthMultiplier = Mathf.Lerp(_baseWidths[i], _baseWidths[i] * 0.25f, t);

            if (_isRing)
                DrawRing(line, Mathf.Lerp(_ringRadius * 0.65f, _ringRadius * 1.2f, t));
        }

        if (_label != null)
        {
            Color labelColor = _color;
            labelColor.a *= alpha;
            _label.color = labelColor;
            if (Camera.main != null)
                _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - Camera.main.transform.position);
        }

        if (t >= 1f)
            DestroySelf();
    }

    private void DrawRing(LineRenderer line, float radius)
    {
        if (line == null)
            return;

        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (i / (float)RingSegments) * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private void DestroySelf()
    {
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
    }

    private static void SetLineColor(LineRenderer line, Color color)
    {
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;
    }

    private static Material GetLineMaterial()
    {
        if (s_lineMaterial != null)
            return s_lineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        s_lineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_lineMaterial;
    }
}
