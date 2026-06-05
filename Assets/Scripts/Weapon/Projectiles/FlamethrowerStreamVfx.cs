using UnityEngine;

public sealed class FlamethrowerStreamVfx : MonoBehaviour
{
    private const int BeamCount = 5;
    private const int RingSegmentCount = 72;
    private static readonly Color FlameCore = new(1f, 0.75f, 0.15f, 0.95f);
    private static readonly Color FlameEdge = new(1f, 0.18f, 0.02f, 0.75f);

    private static Material s_lineMaterial;

    private readonly LineRenderer[] _beams = new LineRenderer[BeamCount];
    private LineRenderer _ring;
    private float _visibleTimer;
    private float _visibleDuration;

    public static FlamethrowerStreamVfx Create()
    {
        GameObject go = new("[FlamethrowerStreamVfx]");
        FlamethrowerStreamVfx vfx = go.AddComponent<FlamethrowerStreamVfx>();
        vfx.InitializeCone();
        return vfx;
    }

    public static void SpawnRing(Vector3 center, float radius, float duration)
    {
        if (radius <= 0f)
            return;

        GameObject go = new("[FlamethrowerRingVfx]");
        go.transform.position = center + Vector3.up * 0.08f;

        FlamethrowerStreamVfx vfx = go.AddComponent<FlamethrowerStreamVfx>();
        vfx.InitializeRing(radius, duration);
    }

    // Updates the reusable cone stream for this frame or tick.
    public void ShowCone(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration)
    {
        if (_beams[0] == null)
            InitializeCone();

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;

        direction.Normalize();
        range = Mathf.Max(0.01f, range);
        float halfAngle = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;

        _visibleDuration = Mathf.Max(0.01f, duration);
        _visibleTimer = _visibleDuration;
        Quaternion rotation = Quaternion.LookRotation(direction, GetStableUp(direction));

        for (int i = 0; i < BeamCount; i++)
        {
            float t = BeamCount == 1 ? 0.5f : i / (float)(BeamCount - 1);
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 beamDirection = rotation * (Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward);
            float beamRange = range * Mathf.Lerp(0.92f, 1f, Mathf.Sin(t * Mathf.PI));

            _beams[i].enabled = true;
            _beams[i].positionCount = 2;
            _beams[i].SetPosition(0, origin);
            _beams[i].SetPosition(1, origin + beamDirection * beamRange);
        }

        SetConeColor(1f);
    }

    // Updates the stream to match the simulated hose path used by damage.
    public void ShowHose(Vector3[] points, int pointCount, float radius, float duration)
    {
        if (points == null || pointCount <= 1)
            return;

        if (_beams[0] == null)
            InitializeCone();

        pointCount = Mathf.Min(pointCount, points.Length);
        if (pointCount <= 1)
            return;

        radius = Mathf.Max(0.03f, radius);
        _visibleDuration = Mathf.Max(0.01f, duration);
        _visibleTimer = _visibleDuration;

        for (int i = 0; i < BeamCount; i++)
        {
            LineRenderer beam = _beams[i];
            beam.enabled = true;
            beam.useWorldSpace = true;
            beam.positionCount = pointCount;
            beam.widthMultiplier = radius * (i == BeamCount / 2 ? 0.42f : 0.18f);

            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                beam.SetPosition(pointIndex, points[pointIndex] + GetHoseOffset(points, pointIndex, pointCount, i, radius));
        }

        SetConeColor(1f);
    }

    private void InitializeCone()
    {
        for (int i = 0; i < BeamCount; i++)
        {
            _beams[i] = CreateLine($"Flame Beam {i}", i == BeamCount / 2 ? 0.22f : 0.13f, useWorldSpace: true);
            _beams[i].positionCount = 2;
            _beams[i].enabled = false;
        }
    }

    private void InitializeRing(float radius, float duration)
    {
        _visibleDuration = Mathf.Max(0.01f, duration);
        _visibleTimer = _visibleDuration;
        _ring = CreateLine("Flame Ring", 0.2f, useWorldSpace: true);
        _ring.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        _ring.loop = true;
        _ring.positionCount = RingSegmentCount;
        DrawRing(radius, 0f);
    }

    private LineRenderer CreateLine(string childName, float width, bool useWorldSpace)
    {
        GameObject lineObject = new(childName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = useWorldSpace;
        line.material = GetLineMaterial();
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.72f, 0.72f),
            new Keyframe(1f, 0.15f));
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    // Fades stream and ring visuals without needing authored particle prefabs.
    private void Update()
    {
        if (_visibleTimer <= 0f)
        {
            HideCone();
            if (_ring != null)
                Destroy(gameObject);
            return;
        }

        _visibleTimer -= Time.deltaTime;
        float alpha = Mathf.Clamp01(_visibleTimer / _visibleDuration);
        SetConeColor(alpha);

        if (_ring != null)
            DrawRing(GetCurrentRingRadius(), 1f - alpha);
    }

    private void HideCone()
    {
        for (int i = 0; i < _beams.Length; i++)
        {
            if (_beams[i] != null)
                _beams[i].enabled = false;
        }
    }

    private void SetConeColor(float alpha)
    {
        for (int i = 0; i < _beams.Length; i++)
        {
            if (_beams[i] == null)
                continue;

            Color color = i == BeamCount / 2 ? FlameCore : FlameEdge;
            color.a *= alpha;
            _beams[i].startColor = color;
            _beams[i].endColor = new Color(color.r, color.g * 0.65f, color.b * 0.45f, 0f);
        }
    }

    private float _ringRadius;

    private void DrawRing(float radius, float expansionT)
    {
        _ringRadius = radius;
        if (_ring == null)
            return;

        float alpha = 1f - expansionT;
        Color color = new(FlameEdge.r, FlameEdge.g, FlameEdge.b, alpha);
        _ring.startColor = color;
        _ring.endColor = color;
        _ring.widthMultiplier = Mathf.Lerp(0.22f, 0.04f, expansionT);

        float drawRadius = Mathf.Lerp(radius * 0.15f, radius, expansionT);
        for (int i = 0; i < RingSegmentCount; i++)
        {
            float angle = i / (float)RingSegmentCount * Mathf.PI * 2f;
            _ring.SetPosition(i, transform.position + new Vector3(Mathf.Cos(angle) * drawRadius, 0f, Mathf.Sin(angle) * drawRadius));
        }
    }

    private float GetCurrentRingRadius() => Mathf.Max(0.01f, _ringRadius);

    private static Vector3 GetHoseOffset(Vector3[] points, int pointIndex, int pointCount, int beamIndex, float radius)
    {
        if (beamIndex == BeamCount / 2)
            return Vector3.zero;

        Vector3 direction = GetHoseDirection(points, pointIndex, pointCount);
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.Cross(Vector3.forward, direction);
        side.Normalize();

        Vector3 vertical = Vector3.Cross(direction, side).normalized;
        float angle = GetBeamOffsetAngle(beamIndex);
        float t = pointCount == 1 ? 0f : pointIndex / (float)(pointCount - 1);
        float offsetRadius = radius * 0.32f * Mathf.Lerp(1f, 0.4f, t);
        return (side * Mathf.Cos(angle) + vertical * Mathf.Sin(angle)) * offsetRadius;
    }

    private static Vector3 GetHoseDirection(Vector3[] points, int pointIndex, int pointCount)
    {
        Vector3 direction;
        if (pointIndex <= 0)
            direction = points[1] - points[0];
        else if (pointIndex >= pointCount - 1)
            direction = points[pointCount - 1] - points[pointCount - 2];
        else
            direction = points[pointIndex + 1] - points[pointIndex - 1];

        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private static float GetBeamOffsetAngle(int beamIndex)
    {
        return beamIndex switch
        {
            0 => 0f,
            1 => Mathf.PI * 0.5f,
            3 => Mathf.PI,
            4 => Mathf.PI * 1.5f,
            _ => 0f
        };
    }

    private static Vector3 GetStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
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

        s_lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_lineMaterial;
    }
}
