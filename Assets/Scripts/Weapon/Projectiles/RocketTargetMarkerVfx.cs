using UnityEngine;

public sealed class RocketTargetMarkerVfx : MonoBehaviour
{
    private static readonly Color MarkerColor = new(1f, 0.03f, 0.02f, 0.95f);
    private static readonly Color RepeatLockColor = new(1f, 0.66f, 0.08f, 0.98f);
    private static Material s_lineMaterial;

    private Transform _target;
    private readonly LineRenderer[] _brackets = new LineRenderer[4];
    private LineRenderer _repeatDiamond;
    private float _radius;
    private float _heightOffset;
    private int _lockCount = 1;

    public Transform Target => _target;
    public int LockCount => _lockCount;

    public static RocketTargetMarkerVfx Create(Transform target, float radius)
    {
        return Create(target, radius, 1);
    }

    public static RocketTargetMarkerVfx Create(Transform target, float radius, int lockCount)
    {
        if (target == null)
            return null;

        GameObject go = new("[RocketTargetMarker]");
        RocketTargetMarkerVfx marker = go.AddComponent<RocketTargetMarkerVfx>();
        marker.Initialize(target, radius, lockCount);
        return marker;
    }

    public void SetLockCount(int lockCount)
    {
        _lockCount = Mathf.Max(1, lockCount);
        DrawMarker();
    }

    private void Initialize(Transform target, float radius, int lockCount)
    {
        _target = target;
        _radius = Mathf.Max(0.05f, radius);
        _heightOffset = CalculateHeightOffset(target);
        _lockCount = Mathf.Max(1, lockCount);

        for (int i = 0; i < _brackets.Length; i++)
            _brackets[i] = CreateLine($"Lock Bracket {i + 1}", 3, false, 0.085f);
        _repeatDiamond = CreateLine("Repeat Lock Diamond", 5, false, 0.055f);
        DrawMarker();
    }

    private void LateUpdate()
    {
        if (_target == null || !_target.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        DrawMarker();
    }

    private LineRenderer CreateLine(string lineName, int positionCount, bool loop, float width)
    {
        GameObject lineObject = new(lineName);
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = positionCount;
        line.widthMultiplier = width;
        line.material = GetLineMaterial();
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void DrawMarker()
    {
        if (_brackets[0] == null || _target == null)
            return;

        Vector3 center = _target.position + Vector3.up * _heightOffset;
        float pulseSpeed = _lockCount > 1 ? 11f : 8f;
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.06f;
        float radius = _radius * pulse;
        float arm = radius * 0.42f;
        Color color = _lockCount > 1 ? RepeatLockColor : MarkerColor;
        color.a *= 0.84f + Mathf.Sin(Time.time * pulseSpeed) * 0.13f;

        for (int i = 0; i < _brackets.Length; i++)
        {
            LineRenderer bracket = _brackets[i];
            Vector3 corner = i switch
            {
                0 => center + new Vector3(-radius, 0f, -radius),
                1 => center + new Vector3(radius, 0f, -radius),
                2 => center + new Vector3(radius, 0f, radius),
                _ => center + new Vector3(-radius, 0f, radius)
            };
            Vector3 horizontal = i == 0 || i == 3 ? Vector3.right : Vector3.left;
            Vector3 vertical = i < 2 ? Vector3.forward : Vector3.back;
            bracket.startColor = color;
            bracket.endColor = color;
            bracket.SetPosition(0, corner + horizontal * arm);
            bracket.SetPosition(1, corner);
            bracket.SetPosition(2, corner + vertical * arm);
        }

        _repeatDiamond.enabled = _lockCount > 1;
        if (!_repeatDiamond.enabled)
            return;

        float diamondRadius = radius * Mathf.Lerp(0.26f, 0.42f, Mathf.InverseLerp(2f, 5f, _lockCount));
        _repeatDiamond.startColor = color;
        _repeatDiamond.endColor = color;
        _repeatDiamond.SetPosition(0, center + Vector3.forward * diamondRadius);
        _repeatDiamond.SetPosition(1, center + Vector3.right * diamondRadius);
        _repeatDiamond.SetPosition(2, center + Vector3.back * diamondRadius);
        _repeatDiamond.SetPosition(3, center + Vector3.left * diamondRadius);
        _repeatDiamond.SetPosition(4, center + Vector3.forward * diamondRadius);
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
