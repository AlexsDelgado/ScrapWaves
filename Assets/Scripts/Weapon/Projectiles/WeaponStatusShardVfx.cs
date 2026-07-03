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
