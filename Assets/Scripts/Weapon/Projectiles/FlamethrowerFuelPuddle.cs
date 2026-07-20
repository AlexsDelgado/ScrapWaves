using UnityEngine;

public sealed class FlamethrowerFuelPuddle : MonoBehaviour
{
    private const int PuddleSegments = 28;
    private const float GroundProbeHeight = 4f;
    private const float GroundProbeDistance = 10f;
    private const float SurfaceOffset = 0.045f;
    private const float OutlineOffset = 0.018f;
    private static readonly Color FuelFillColor = new(0.0f, 0.22f, 0.055f, 0.94f);
    private static readonly Color FuelOutlineColor = new(0.02f, 0.36f, 0.075f, 0.86f);

    private static Shader s_puddleShader;

    private Vector3 _center;
    private float _radius;
    private int _damagePerTick;
    private float _initialDuration;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;
    private float _shapeSeed;
    private bool _useDamageContext;
    private WeaponDamageContext _damageContext;
    private Mesh _mesh;
    private MeshRenderer _meshRenderer;
    private LineRenderer _outline;
    private Material _fillMaterial;
    private Material _outlineMaterial;

    public static FlamethrowerFuelPuddle Spawn(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        GameObject go = new("FlamethrowerFuelPuddle");
        FlamethrowerFuelPuddle puddle = go.AddComponent<FlamethrowerFuelPuddle>();
        puddle.Configure(center, radius, damagePerTick, duration, tickInterval);
        return puddle;
    }

    public static FlamethrowerFuelPuddle Spawn(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval, WeaponDamageContext damageContext)
    {
        GameObject go = new("FlamethrowerFuelPuddle");
        FlamethrowerFuelPuddle puddle = go.AddComponent<FlamethrowerFuelPuddle>();
        puddle.Configure(center, radius, damagePerTick, duration, tickInterval, damageContext);
        return puddle;
    }

    private void Configure(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        Configure(center, radius, damagePerTick, duration, tickInterval, default);
    }

    private void Configure(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval, WeaponDamageContext damageContext)
    {
        _center = center;
        _radius = Mathf.Max(0.1f, radius);
        _damagePerTick = Mathf.Max(1, damagePerTick);
        _initialDuration = Mathf.Max(0.1f, duration);
        _remainingDuration = _initialDuration;
        _tickInterval = Mathf.Max(0.05f, tickInterval);
        _tickTimer = 0f;
        _damageContext = damageContext;
        _useDamageContext = damageContext.IsValid;
        _center = ResolveGroundPosition(center);
        _shapeSeed = Mathf.Abs(center.x * 12.9898f + center.z * 78.233f);
        transform.position = _center;

        CreatePuddleVisual();
        UpdatePuddleVisual();
    }

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        _tickTimer -= Time.deltaTime;

        while (_tickTimer <= 0f && _remainingDuration > 0f)
        {
            if (_useDamageContext)
                WeaponRadialDamage.Apply(_center, _radius, _damageContext, falloff: 0f, maxTargets: 64, showVfx: false);
            else
                WeaponRadialDamage.Apply(_center, _radius, _damagePerTick, falloff: 0f, knockback: 0f, maxTargets: 64, showVfx: false);
            _tickTimer += _tickInterval;
        }

        UpdatePuddleVisual();

        if (_remainingDuration <= 0f)
            Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = FuelOutlineColor;
        Gizmos.DrawWireSphere(transform.position, _radius);
    }

    private void CreatePuddleVisual()
    {
        MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
        _meshRenderer = gameObject.AddComponent<MeshRenderer>();
        _fillMaterial = CreatePuddleMaterial(FuelFillColor);
        _meshRenderer.sharedMaterial = _fillMaterial;
        _mesh = new Mesh { name = "Jellified Fuel Puddle Mesh" };
        meshFilter.sharedMesh = _mesh;

        GameObject outlineGo = new("Puddle Outline");
        outlineGo.transform.SetParent(transform, false);
        _outline = outlineGo.AddComponent<LineRenderer>();
        _outline.useWorldSpace = false;
        _outline.loop = true;
        _outline.positionCount = PuddleSegments;
        _outline.widthMultiplier = Mathf.Max(0.035f, _radius * 0.045f);
        _outline.numCornerVertices = 3;
        _outline.numCapVertices = 3;
        _outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _outline.receiveShadows = false;
        _outlineMaterial = CreatePuddleMaterial(FuelOutlineColor);
        _outline.material = _outlineMaterial;

        BuildPuddleShape(1f);
    }

    private void UpdatePuddleVisual()
    {
        float life = Mathf.Clamp01(_remainingDuration / Mathf.Max(0.01f, _initialDuration));
        float pulse = 1f + Mathf.Sin(Time.time * 1.6f + _shapeSeed) * 0.015f;
        BuildPuddleShape(pulse);

        Color fill = FuelFillColor;
        fill.a *= Mathf.SmoothStep(0f, 1f, life);
        Color outline = FuelOutlineColor;
        outline.a *= life;

        if (_fillMaterial != null)
            _fillMaterial.color = fill;
        if (_outlineMaterial != null)
            _outlineMaterial.color = outline;
        if (_outline != null)
        {
            _outline.startColor = outline;
            _outline.endColor = outline;
        }
    }

    private void BuildPuddleShape(float scale)
    {
        if (_mesh == null || _outline == null)
            return;

        Vector3[] vertices = new Vector3[PuddleSegments + 1];
        int[] triangles = new int[PuddleSegments * 6];
        vertices[0] = Vector3.zero;

        for (int i = 0; i < PuddleSegments; i++)
        {
            float angle = i / (float)PuddleSegments * Mathf.PI * 2f;
            float radius = _radius * GetEdgeScale(angle) * scale;
            Vector3 point = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            vertices[i + 1] = point;
            _outline.SetPosition(i, point + Vector3.up * OutlineOffset);
        }

        for (int i = 0; i < PuddleSegments; i++)
        {
            int current = i + 1;
            int next = i == PuddleSegments - 1 ? 1 : current + 1;
            int triangle = i * 6;
            triangles[triangle] = 0;
            triangles[triangle + 1] = next;
            triangles[triangle + 2] = current;
            triangles[triangle + 3] = 0;
            triangles[triangle + 4] = current;
            triangles[triangle + 5] = next;
        }

        _mesh.Clear();
        _mesh.vertices = vertices;
        _mesh.triangles = triangles;
        _mesh.RecalculateBounds();
        _mesh.RecalculateNormals();
    }

    private float GetEdgeScale(float angle)
    {
        float slowWave = Mathf.Sin(angle * 3.1f + _shapeSeed) * 0.16f;
        float fastWave = Mathf.Sin(angle * 7.4f + _shapeSeed * 0.37f) * 0.08f;
        return Mathf.Clamp(0.88f + slowWave + fastWave, 0.68f, 1.14f);
    }

    private static Vector3 ResolveGroundPosition(Vector3 center)
    {
        Vector3 rayOrigin = center + Vector3.up * GroundProbeHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            GroundProbeHeight + GroundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsGroundSurface(hits[i].collider))
                continue;

            return hits[i].point + Vector3.up * SurfaceOffset;
        }

        if (center.y > 0.5f)
            center.y = 0f;
        return center + Vector3.up * SurfaceOffset;
    }

    private static bool IsGroundSurface(Collider collider)
    {
        return collider != null
            && collider.GetComponentInParent<PlayerStats>() == null
            && collider.GetComponentInParent<EnemyRegistryMember>() == null
            && collider.GetComponentInParent<IDamageable>() == null;
    }

    private static Material CreatePuddleMaterial(Color color)
    {
        Shader shader = GetPuddleShader();
        Material material = new(shader)
        {
            color = color,
            hideFlags = HideFlags.HideAndDontSave
        };

        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
        return material;
    }

    private static Shader GetPuddleShader()
    {
        if (s_puddleShader != null)
            return s_puddleShader;

        s_puddleShader = Shader.Find("Sprites/Default");
        if (s_puddleShader == null)
            s_puddleShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (s_puddleShader == null)
            s_puddleShader = Shader.Find("Unlit/Color");

        return s_puddleShader;
    }
}
