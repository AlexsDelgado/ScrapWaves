using UnityEngine;

/// <summary>
/// Anillo de telegraph en suelo (emergencia del Giga Worm u otros bosses).
/// </summary>
public sealed class AttackTelegraphVfx : MonoBehaviour
{
    private const int SegmentCount = 72;
    private const float HeightOffset = 0.06f;

    private static Material s_lineMaterial;

    private LineRenderer _outerRing;
    private LineRenderer _innerRing;
    private float _radius;
    private float _duration;
    private float _elapsed;
    private Color _baseColor;

    public static AttackTelegraphVfx Spawn(Vector3 position, float radius, float duration, Color? color = null)
    {
        if (radius <= 0f || duration <= 0f)
            return null;

        GameObject go = new("[AttackTelegraphVfx]");
        go.transform.position = position + Vector3.up * HeightOffset;

        AttackTelegraphVfx vfx = go.AddComponent<AttackTelegraphVfx>();
        vfx.Initialize(radius, duration, color ?? new Color(0.35f, 0.95f, 0.15f, 0.85f));
        return vfx;
    }

    private void Initialize(float radius, float duration, Color baseColor)
    {
        _radius = Mathf.Max(0.01f, radius);
        _duration = Mathf.Max(0.05f, duration);
        _baseColor = baseColor;
        _outerRing = CreateRing("Telegraph Outer", 0.08f);
        _innerRing = CreateRing("Telegraph Inner", 0.04f);
        UpdateRings(0f);
    }

    private LineRenderer CreateRing(string childName, float width)
    {
        GameObject ring = new(childName);
        ring.transform.SetParent(transform, false);

        LineRenderer line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.widthMultiplier = width;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.material = GetLineMaterial();
        return line;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        UpdateRings(t);

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void UpdateRings(float t)
    {
        float pulse = 0.85f + 0.15f * Mathf.Sin(t * Mathf.PI * 6f);
        float alpha = Mathf.Lerp(0.95f, 0.15f, t);
        Color color = new(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
        DrawRing(_outerRing, _radius * pulse, color, Mathf.Lerp(0.1f, 0.04f, t));
        DrawRing(_innerRing, _radius * 0.55f * pulse, color, Mathf.Lerp(0.05f, 0.025f, t));
    }

    private static void DrawRing(LineRenderer line, float radius, Color color, float width)
    {
        if (line == null)
            return;

        line.widthMultiplier = width;
        line.startColor = color;
        line.endColor = color;

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = (i / (float)SegmentCount) * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
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
