using System;
using UnityEngine;

/// <summary>Four reusable mesh-particle buffers for cosmetic cog, bolt, nail and nut debris.</summary>
[DisallowMultipleComponent]
public sealed class StalkerScrapBurstVfx : MonoBehaviour
{
    public const int MaxParticlesPerKind = 24;
    public const int MaxPiecesPerBurst = 24;
    [SerializeField] private ParticleSystem[] _systems = Array.Empty<ParticleSystem>();
    [Header("Underground trail")]
    [SerializeField, Range(0, MaxPiecesPerBurst)] private int _trailPieceCount = 3;
    [SerializeField, Range(0f, 8f)] private float _trailRadius = 1.2f;
    [SerializeField] private Vector2 _trailSize = new(0.55f, 1.05f);
    [SerializeField] private Vector2 _trailLifetime = new(0.8f, 1.3f);
    [SerializeField] private Vector2 _trailUpSpeed = new(4f, 7f);
    [SerializeField] private Vector2 _trailOutSpeed = new(1f, 3f);
    [Header("Head ground-entry burst")]
    [SerializeField, Range(0, MaxPiecesPerBurst)] private int _entryPieceCount = 20;
    [SerializeField, Range(0f, 8f)] private float _entryRadius = 3.5f;
    [SerializeField] private Vector2 _entrySize = new(0.8f, 1.5f);
    [SerializeField] private Vector2 _entryLifetime = new(1.1f, 1.7f);
    [SerializeField] private Vector2 _entryUpSpeed = new(9f, 15f);
    [SerializeField] private Vector2 _entryOutSpeed = new(4f, 9f);
    private System.Random _random;
    private int _kindCursor;

    public int EntryBurstCount { get; private set; }
    public int TrailBurstCount { get; private set; }
    public Vector3 LastEntryPosition { get; private set; }
    public int ActiveParticleCount
    {
        get
        {
            int count = 0;
            foreach (ParticleSystem system in _systems)
                if (system != null) count += system.particleCount;
            return count;
        }
    }

    private void Awake()
    {
        // Cosmetic random choices must not consume the gameplay/spit random stream.
        _random = new System.Random(GetInstanceID());
        foreach (ParticleSystem system in _systems)
        {
            if (system == null) continue;
            var main = system.main;
            main.maxParticles = MaxParticlesPerKind;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void Configure(ParticleSystem[] systems)
    {
        _systems = systems == null ? Array.Empty<ParticleSystem>() : (ParticleSystem[])systems.Clone();
    }

    public void Emit(Vector3 groundPoint, bool entry, int pieceCount = -1, Collider groundSurface = null)
    {
        if (_systems.Length != 4 || !IsFinite(groundPoint)) return;
        int count = Mathf.Clamp(pieceCount < 0 ? (entry ? _entryPieceCount : _trailPieceCount) : pieceCount,
            0, MaxPiecesPerBurst);
        if (count == 0) return;
        if (_random == null) _random = new System.Random(GetInstanceID());
        if (entry) { EntryBurstCount++; LastEntryPosition = groundPoint; }
        else TrailBurstCount++;
        float radius = Mathf.Clamp(entry ? _entryRadius : _trailRadius, 0f, 8f);
        for (int i = 0; i < count; i++)
        {
            ParticleSystem system = _systems[_kindCursor++ % _systems.Length];
            if (system == null) continue;
            if (!system.isPlaying) system.Play(false);
            float angle = Next(0f, Mathf.PI * 2f);
            Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 position = groundPoint + outward * (Mathf.Sqrt(Next(0f, 1f)) * radius) + Vector3.up * 0.1f;
            if (!entry && groundSurface != null)
                position = PlaceOnSurface(position, groundPoint, groundSurface);
            float size = Range(entry ? _entrySize : _trailSize, 0.1f, 2f);
            float lifetime = Range(entry ? _entryLifetime : _trailLifetime, 0.2f, 2.5f);
            Vector3 velocity = outward * Range(entry ? _entryOutSpeed : _trailOutSpeed, 0f, 20f)
                + Vector3.up * Range(entry ? _entryUpSpeed : _trailUpSpeed, 0f, 20f);
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                rotation3D = new Vector3(Next(0f, 360f), Next(0f, 360f), Next(0f, 360f)),
                randomSeed = (uint)_random.Next(1, int.MaxValue)
            };
            system.Emit(emit, 1);
        }
        // Avoid overflow during unusually long sessions without changing the next family.
        _kindCursor %= _systems.Length;
    }

    private static Vector3 PlaceOnSurface(Vector3 candidate, Vector3 groundPoint, Collider surface)
    {
        // Scatter must follow the surface at each piece's XZ, not the center's Y.
        // Query the known support directly so nearby props cannot lift the debris.
        Bounds bounds = surface.bounds;
        Ray ray = new(new Vector3(candidate.x, bounds.max.y + 0.5f, candidate.z), Vector3.down);
        if (surface.Raycast(ray, out RaycastHit hit, bounds.size.y + 1f))
            return hit.point + Vector3.up * 0.1f;
        // At a ledge, keep this piece at the resolved center instead of emitting in air.
        return groundPoint + Vector3.up * 0.1f;
    }

    private float Range(Vector2 limits, float minimum, float maximum)
    {
        float low = Mathf.Clamp(Mathf.Min(limits.x, limits.y), minimum, maximum);
        float high = Mathf.Clamp(Mathf.Max(limits.x, limits.y), minimum, maximum);
        return Next(low, high);
    }
    private float Next(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
