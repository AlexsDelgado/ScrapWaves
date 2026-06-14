using UnityEngine;

public sealed class RocketTargetMarkerVfx : MonoBehaviour
{
    private const int SegmentCount = 48;
    private static readonly Color MarkerColor = new(1f, 0.03f, 0.02f, 0.95f);
    private static Material s_lineMaterial;

    private Transform _target;
    private LineRenderer _ring;
    private float _radius;
    private float _heightOffset;

    public Transform Target => _target;

    public static RocketTargetMarkerVfx Create(Transform target, float radius)
    {
        if (target == null)
            return null;

        GameObject go = new("[RocketTargetMarker]");
        RocketTargetMarkerVfx marker = go.AddComponent<RocketTargetMarkerVfx>();
        marker.Initialize(target, radius);
        return marker;
    }

    private void Initialize(Transform target, float radius)
    {
        _target = target;
        _radius = Mathf.Max(0.05f, radius);
        _heightOffset = CalculateHeightOffset(target);

        _ring = gameObject.AddComponent<LineRenderer>();
        _ring.useWorldSpace = true;
        _ring.loop = true;
        _ring.positionCount = SegmentCount;
        _ring.widthMultiplier = 0.08f;
        _ring.material = GetLineMaterial();
        _ring.numCornerVertices = 3;
        _ring.numCapVertices = 3;
        _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring.receiveShadows = false;
        DrawRing();
    }

    private void LateUpdate()
    {
        if (_target == null || !_target.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        DrawRing();
    }

    private void DrawRing()
    {
        if (_ring == null || _target == null)
            return;

        Vector3 center = _target.position + Vector3.up * _heightOffset;
        float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.08f;
        float radius = _radius * pulse;
        Color color = MarkerColor;
        color.a *= 0.82f + Mathf.Sin(Time.time * 8f) * 0.15f;
        _ring.startColor = color;
        _ring.endColor = color;

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i / (float)SegmentCount * Mathf.PI * 2f;
            _ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private static float CalculateHeightOffset(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return 2.2f;

        float highest = target.position.y;
        for (int i = 0; i < renderers.Length; i++)
            highest = Mathf.Max(highest, renderers[i].bounds.max.y);

        return Mathf.Max(0.5f, highest - target.position.y + 0.3f);
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
