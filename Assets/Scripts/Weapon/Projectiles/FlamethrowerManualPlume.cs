using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One bounded world-space particle renderer, driven only by the flame simulation clock.</summary>
internal sealed class FlamethrowerManualPlume
{
    public const int MaximumParticles = 768;
    public const int WispsPerArea = 24;
    private readonly ParticleSystem _system;
    private readonly ParticleSystemRenderer _renderer;
    private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[MaximumParticles];
    private readonly MaterialPropertyBlock _properties = new();
    private int _count;
    private float _time;

    public int ParticleCount => _count;

    public FlamethrowerManualPlume(Transform parent, Material material)
    {
        var root = new GameObject("Manual flame wisps");
        root.layer = 2;
        root.transform.SetParent(parent, false);
        _system = root.AddComponent<ParticleSystem>();
        var main = _system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = MaximumParticles;
        main.gravityModifier = 0f;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = _system.emission;
        emission.enabled = false;
        var shape = _system.shape;
        shape.enabled = false;
        var collision = _system.collision;
        collision.enabled = false;
        _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _system.Pause();
        _renderer = root.GetComponent<ParticleSystemRenderer>();
        _renderer.sharedMaterial = material;
        _renderer.renderMode = ParticleSystemRenderMode.Billboard;
        _renderer.alignment = ParticleSystemRenderSpace.View;
        _renderer.sortMode = ParticleSystemSortMode.Distance;
        _renderer.minParticleSize = 0f;
        _renderer.maxParticleSize = 1f;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
    }

    public void Begin(float time)
    {
        _time = time;
        _count = 0;
    }

    public void Add(Vector3 center, Vector3 direction, Vector3[] path, int pathCount,
        float radius, float life, int seed, FlamethrowerStreamStyle style, float heat)
    {
        Vector3 axis = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(side, axis).normalized;
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, life));
        float cooling = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.85f, life));
        Color body = style == FlamethrowerStreamStyle.JellifiedFuel ? new Color(.23f, .63f, .035f)
            : style == FlamethrowerStreamStyle.LiquidNitrogen ? new Color(.25f, .57f, .8f) : new Color(1f, .22f, .012f);
        Color hot = style == FlamethrowerStreamStyle.JellifiedFuel ? new Color(.86f, .95f, .34f)
            : style == FlamethrowerStreamStyle.LiquidNitrogen ? new Color(.85f, .98f, 1f) : new Color(1f, .87f, .36f);
        for (int i = 0; i < WispsPerArea && _count < MaximumParticles; i++)
        {
            float random = Hash(seed * 43 + i * 17);
            float phase = Hash(seed * 71 + i * 29) * Mathf.PI * 2f;
            float along = (i + random * .75f) / WispsPerArea;
            float sample = along * Mathf.Max(0, pathCount - 1);
            int index = Mathf.Min(Mathf.FloorToInt(sample), Mathf.Max(0, pathCount - 1));
            Vector3 position = pathCount > 1 ? Vector3.Lerp(path[index], path[Mathf.Min(index + 1, pathCount - 1)], sample - index) : center;
            float curl = _time * (2.1f + random * 1.4f) + phase;
            bool core = i % 6 == 0;
            float spread = radius * (.12f + .34f * random) * (core ? .35f : 1f);
            // Independent layers across both cross-section axes reveal depth when viewed from the side.
            position += side * (Mathf.Sin(curl) * spread) + up * (Mathf.Cos(curl * .83f + phase) * spread);
            position += Vector3.up * (radius * life * .22f + Mathf.Sin(curl * .61f) * radius * .08f);
            float size = radius * (.74f + random * .75f) * (1f + .12f * Mathf.Sin(curl * 1.23f));
            if (core) size *= .62f;
            Color color = core ? hot : Color.Lerp(body, hot, random * .35f + (1f - life) * .12f);
            color *= .85f + random * .3f + heat * .08f;
            color = Color.Lerp(color, new Color(.075f, .064f, .053f), cooling);
            // Back samples taper away so adjacent packets merge without a hard connector strip.
            color.a = (core ? .48f + random * .12f : .18f + random * .15f) * fade * Mathf.Lerp(.35f, 1f, along);
            _particles[_count++] = new ParticleSystem.Particle
            {
                position = position,
                startSize3D = new Vector3(size, size * (1.1f + random * .65f), size),
                startColor = color,
                rotation = phase * Mathf.Rad2Deg + Mathf.Sin(curl * .4f) * 22f,
                startLifetime = 2f,
                remainingLifetime = 2f,
                randomSeed = (uint)(seed * WispsPerArea + i + 1)
            };
        }
    }

    public void End()
    {
        _system.SetParticles(_particles, _count);
        _properties.SetFloat("_CloudAge", _time);
        _renderer.SetPropertyBlock(_properties);
    }

    public void Clear()
    {
        _count = 0;
        _system.SetParticles(_particles, 0);
    }

    private static float Hash(int value)
    {
        uint bits = unchecked((uint)value);
        bits ^= bits >> 16;
        bits *= 0x7feb352d;
        bits ^= bits >> 15;
        bits *= 0x846ca68b;
        bits ^= bits >> 16;
        return (bits & 0xffffff) / 16777216f;
    }
}
