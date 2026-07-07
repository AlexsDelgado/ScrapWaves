using UnityEngine;

public sealed class ExplosionRadiusVfx : MonoBehaviour
{
    private const int SegmentCount = 72;
    private const float HeightOffset = 0.06f;
    private static readonly Color DefaultColor = new(1f, 0.42f, 0.05f, 0.9f);

    private static Material s_lineMaterial;

    private LineRenderer _outerRing;
    private LineRenderer _shockwaveRing;
    private Color _baseColor = DefaultColor;
    private float _radius;
    private float _duration;
    private float _elapsed;
    private ExplosionRadiusVfxPool _pool;

    public static void Spawn(Vector3 position, float radius)
    {
        Spawn(position, radius, DefaultColor);
    }

    public static void Spawn(Vector3 position, float radius, Color color)
    {
        ExplosionRadiusVfxPool.TrySpawn(position, radius, color);
    }

    internal static void SpawnRuntime(Vector3 position, float radius, float duration)
    {
        SpawnRuntime(position, radius, duration, DefaultColor);
    }

    internal static void SpawnRuntime(Vector3 position, float radius, float duration, Color color)
    {
        if (radius <= 0f)
            return;

        GameObject go = new("[ExplosionRadiusVfx]");
        go.transform.position = position + Vector3.up * HeightOffset;

        ExplosionRadiusVfx vfx = go.AddComponent<ExplosionRadiusVfx>();
        vfx.Initialize(radius, duration, color);
    }

    public void PrepareForPool()
    {
        _outerRing = CreateRing("Explosion Radius", 0.075f);
        _shockwaveRing = CreateRing("Explosion Shockwave", 0.16f);
    }

    public void ActivateFromPool(Vector3 position, float radius, float duration, Color color, ExplosionRadiusVfxPool pool)
    {
        _pool = pool;
        _elapsed = 0f;
        transform.position = position + Vector3.up * HeightOffset;
        Initialize(radius, duration, color);
        gameObject.SetActive(true);
    }

    private void Initialize(float radius, float duration, Color color)
    {
        _baseColor = color;
        _radius = Mathf.Max(0.01f, radius);
        _duration = Mathf.Max(0.05f, duration);

        if (_outerRing == null || _shockwaveRing == null)
            PrepareForPool();

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
        {
            if (_pool != null)
            {
                _pool.Release(this);
                _pool = null;
            }
            else
            {
                EnemyPoolProfiler.RegisterDestroy();
                Destroy(gameObject);
            }
        }
    }

    private void UpdateRings(float t)
    {
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float alpha = 1f - t;

        DrawRing(_outerRing, _radius, new Color(_baseColor.r, _baseColor.g, _baseColor.b, _baseColor.a * alpha * 0.7f), Mathf.Lerp(0.08f, 0.025f, t));
        DrawRing(_shockwaveRing, Mathf.Lerp(_radius * 0.18f, _radius, eased), new Color(_baseColor.r, _baseColor.g, _baseColor.b, _baseColor.a * alpha), Mathf.Lerp(0.18f, 0.035f, t));
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
