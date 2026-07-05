using System.Collections.Generic;
using UnityEngine;

public sealed class WeaponStatusShardVfx : MonoBehaviour
{
    private static Material s_lineMaterial;

    private readonly List<LineRenderer> _shards = new();

    private Transform _target;
    private Color _coreColor;
    private Color _edgeColor;
    private float _duration;
    private float _elapsed;
    private float _radius;
    private float _height;
    private float _shardLength;
    private float _seed;

    public static WeaponStatusShardVfx SpawnIceShards(Transform target, Color coreColor, Color edgeColor, float duration, bool frozen)
    {
        if (target == null || duration <= 0f)
            return null;

        GameObject go = new("[WeaponStatusShardVfx] Ice Shards");
        WeaponStatusShardVfx vfx = go.AddComponent<WeaponStatusShardVfx>();
        vfx.Configure(
            target,
            coreColor,
            edgeColor,
            duration,
            frozen ? 11 : 7,
            frozen ? 0.9f : 0.68f,
            frozen ? 1.35f : 1.15f,
            frozen ? 0.48f : 0.34f);
        return vfx;
    }

    private void Configure(
        Transform target,
        Color coreColor,
        Color edgeColor,
        float duration,
        int shardCount,
        float radius,
        float height,
        float shardLength)
    {
        _target = target;
        _coreColor = coreColor;
        _edgeColor = edgeColor;
        _duration = Mathf.Max(0.05f, duration);
        _radius = Mathf.Max(0.1f, radius);
        _height = Mathf.Max(0.2f, height);
        _shardLength = Mathf.Max(0.08f, shardLength);
        _seed = Mathf.Abs(target.position.x * 31.17f + target.position.z * 19.71f + Time.time * 3.13f);

        int count = Mathf.Clamp(shardCount, 3, 16);
        for (int i = 0; i < count; i++)
            _shards.Add(CreateShard($"Ice Shard {i}", i));
    }

    private LineRenderer CreateShard(string name, int index)
    {
        GameObject shardGo = new(name);
        shardGo.transform.SetParent(transform, false);

        LineRenderer shard = shardGo.AddComponent<LineRenderer>();
        shard.useWorldSpace = false;
        shard.positionCount = 2;
        shard.material = GetLineMaterial();
        shard.widthMultiplier = Mathf.Lerp(0.025f, 0.045f, index % 3 / 2f);
        shard.numCornerVertices = 1;
        shard.numCapVertices = 2;
        shard.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shard.receiveShadows = false;
        return shard;
    }

    private void Update()
    {
        if (_target == null)
        {
            DestroySelf();
            return;
        }

        transform.position = _target.position;
        _elapsed += Time.deltaTime;

        float t = Mathf.Clamp01(_elapsed / _duration);
        float alpha = Mathf.SmoothStep(1f, 0f, t);
        float orbit = Time.time * 1.8f + _seed;

        for (int i = 0; i < _shards.Count; i++)
        {
            LineRenderer shard = _shards[i];
            if (shard == null)
                continue;

            float shardT = i / (float)Mathf.Max(1, _shards.Count);
            float angle = shardT * Mathf.PI * 2f + orbit * Mathf.Lerp(0.45f, 0.9f, shardT);
            float bob = Mathf.Sin(Time.time * 4.2f + i * 1.7f + _seed) * 0.16f;
            float radius = _radius * (0.82f + Mathf.Sin(Time.time * 2.1f + i) * 0.08f);

            Vector3 center = new Vector3(Mathf.Cos(angle) * radius, _height + bob, Mathf.Sin(angle) * radius);
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0.35f + Mathf.Sin(i + _seed) * 0.22f, Mathf.Cos(angle)).normalized;
            Vector3 start = center - tangent * (_shardLength * 0.5f);
            Vector3 end = center + tangent * (_shardLength * 0.5f);

            Color core = _coreColor;
            Color edge = _edgeColor;
            core.a *= alpha;
            edge.a *= alpha * 0.75f;
            shard.startColor = core;
            shard.endColor = edge;
            shard.SetPosition(0, start);
            shard.SetPosition(1, end);
        }

        if (t >= 1f)
            DestroySelf();
    }

    private void DestroySelf()
    {
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
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

public sealed class WeaponStatusAuraVfx : MonoBehaviour
{
    private const int RingSegments = 72;
    private static readonly Color VulnerableCoreColor = new(1f, 0.12f, 0.72f, 0.95f);
    private static readonly Color VulnerableEdgeColor = new(1f, 0.52f, 0.92f, 0.65f);

    private static Material s_lineMaterial;

    private readonly List<LineRenderer> _wisps = new();

    private Transform _target;
    private Color _coreColor;
    private Color _edgeColor;
    private float _remainingDuration;
    private float _radius;
    private float _height;
    private float _seed;
    private LineRenderer _lowRing;
    private LineRenderer _highRing;

    public static WeaponStatusAuraVfx SpawnVulnerableAura(Transform target, float duration)
    {
        if (target == null || duration <= 0f)
            return null;

        GameObject go = new("[WeaponStatusAuraVfx] Vulnerable Aura");
        WeaponStatusAuraVfx vfx = go.AddComponent<WeaponStatusAuraVfx>();
        vfx.Configure(target, duration, VulnerableCoreColor, VulnerableEdgeColor, 0.82f, 1.25f);
        return vfx;
    }

    public void Refresh(float duration)
    {
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
    }

    public void Dismiss()
    {
        DestroySelf();
    }

    private void Configure(Transform target, float duration, Color coreColor, Color edgeColor, float radius, float height)
    {
        _target = target;
        _coreColor = coreColor;
        _edgeColor = edgeColor;
        _remainingDuration = Mathf.Max(0.05f, duration);
        _radius = Mathf.Max(0.1f, radius);
        _height = Mathf.Max(0.2f, height);
        _seed = Mathf.Abs(target.position.x * 23.31f + target.position.z * 37.17f + Time.time * 5.11f);
        transform.position = target.position;

        _lowRing = CreateRing("Vulnerable Low Ring", 0.045f);
        _highRing = CreateRing("Vulnerable High Ring", 0.035f);
        for (int i = 0; i < 8; i++)
            _wisps.Add(CreateWisp($"Vulnerable Wisp {i}", i));
    }

    private LineRenderer CreateRing(string childName, float width)
    {
        GameObject ringGo = new(childName);
        ringGo.transform.SetParent(transform, false);

        LineRenderer ring = ringGo.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = RingSegments;
        ring.material = GetLineMaterial();
        ring.widthMultiplier = width;
        ring.numCornerVertices = 2;
        ring.numCapVertices = 2;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        return ring;
    }

    private LineRenderer CreateWisp(string childName, int index)
    {
        GameObject wispGo = new(childName);
        wispGo.transform.SetParent(transform, false);

        LineRenderer wisp = wispGo.AddComponent<LineRenderer>();
        wisp.useWorldSpace = false;
        wisp.positionCount = 2;
        wisp.material = GetLineMaterial();
        wisp.widthMultiplier = Mathf.Lerp(0.025f, 0.055f, index % 3 / 2f);
        wisp.numCornerVertices = 1;
        wisp.numCapVertices = 2;
        wisp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wisp.receiveShadows = false;
        return wisp;
    }

    private void Update()
    {
        if (_target == null)
        {
            DestroySelf();
            return;
        }

        transform.position = _target.position;
        _remainingDuration -= Time.deltaTime;

        float alpha = Mathf.Clamp01(_remainingDuration / 0.35f);
        float pulse = 1f + Mathf.Sin(Time.time * 6.5f + _seed) * 0.06f;
        DrawRing(_lowRing, _radius * pulse, 0.22f, _coreColor, alpha);
        DrawRing(_highRing, _radius * 0.72f * pulse, _height, _edgeColor, alpha * 0.8f);
        UpdateWisps(alpha);

        if (_remainingDuration <= 0f)
            DestroySelf();
    }

    private void DrawRing(LineRenderer line, float radius, float y, Color color, float alpha)
    {
        if (line == null)
            return;

        color.a *= alpha;
        line.startColor = color;
        line.endColor = color;

        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (i / (float)RingSegments) * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
        }
    }

    private void UpdateWisps(float alpha)
    {
        float orbit = Time.time * 2.25f + _seed;
        for (int i = 0; i < _wisps.Count; i++)
        {
            LineRenderer wisp = _wisps[i];
            if (wisp == null)
                continue;

            float t = i / (float)Mathf.Max(1, _wisps.Count);
            float angle = t * Mathf.PI * 2f + orbit * Mathf.Lerp(0.55f, 1.1f, t);
            float height = Mathf.Lerp(0.35f, _height, Mathf.PingPong(Time.time * 0.7f + t, 1f));
            float radius = _radius * (0.85f + Mathf.Sin(Time.time * 2.4f + i) * 0.08f);
            Vector3 center = new(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            Vector3 vertical = Vector3.up * Mathf.Lerp(0.22f, 0.42f, i % 3 / 2f);

            Color start = _coreColor;
            Color end = _edgeColor;
            start.a *= alpha;
            end.a *= alpha * 0.7f;
            wisp.startColor = start;
            wisp.endColor = end;
            wisp.SetPosition(0, center - vertical * 0.5f);
            wisp.SetPosition(1, center + vertical * 0.5f);
        }
    }

    private void DestroySelf()
    {
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
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
