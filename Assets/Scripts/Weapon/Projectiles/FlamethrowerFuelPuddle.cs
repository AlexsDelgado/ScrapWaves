using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class FlamethrowerFuelPuddle : MonoBehaviour, IWeaponRadialDamageObserver
{
    private const int PuddleSegments = 28;
    private const float GroundProbeHeight = 4f;
    private const float GroundProbeDistance = 10f;
    private const float SurfaceOffset = 0.045f;
    private const float MinimumVisualFadeDuration = 0.25f;
    private const float ParticleFadePadding = 0.08f;
    private const float MaximumTallySegmentDuration = 3.25f;
    private const int GroundHitCapacity = 32;
    private static readonly Color FuelFillColor = new(0.015f, 0.2f, 0.045f, 0.92f);
    private static readonly Color FuelEdgeColor = new(0.55f, 0.78f, 0.08f, 0.78f);
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly RaycastHit[] GroundHits = new RaycastHit[GroundHitCapacity];

    private sealed class PuddlePool
    {
        public GameObject Prefab;
        public int Capacity;
        public int Created;
        public readonly Queue<FlamethrowerFuelPuddle> Available = new();
    }

    private static readonly Dictionary<int, PuddlePool> Pools = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPools() => Pools.Clear();

    [Header("Authored visual layers")]
    [SerializeField] private MeshFilter _fillFilter;
    [SerializeField] private MeshRenderer _fillRenderer;
    [SerializeField] private MeshFilter _edgeFilter;
    [SerializeField] private MeshRenderer _edgeRenderer;
    [SerializeField] private ParticleSystem _bubbles;
    [SerializeField] private ParticleSystem _darkSmoke;
    [SerializeField, Range(0f, 0.25f)] private float _edgeThickness = 0.07f;
    [SerializeField, Min(0f)] private float _viscousPulseSpeed = 1.6f;

    private Vector3 _center;
    private float _radius;
    private int _damagePerTick;
    private float _initialDuration;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;
    private float _shapeSeed;
    private float _visualFadeDuration;
    private bool _bubbleEmissionStopped;
    private bool _smokeEmissionStopped;
    private bool _useDamageContext;
    private WeaponDamageContext _damageContext;
    private IWeaponFeedbackSink _feedbackSink;
    private WeaponFeedbackMode _feedbackMode;
    private int _statusInstanceId;
    private int _statusSegmentIndex;
    private float _statusSegmentElapsed;
    private bool _statusSegmentClosureNotified;
    private Mesh _fillMesh;
    private Mesh _edgeMesh;
    private Vector3[] _fillVertices;
    private int[] _fillTriangles;
    private Vector2[] _fillUvs;
    private Vector3[] _edgeVertices;
    private int[] _edgeTriangles;
    private Vector2[] _edgeUvs;
    private MaterialPropertyBlock _propertyBlock;
    private PuddlePool _pool;
    private bool _visualReady;

    public float GameplayRadius => _radius;
    public int MeshLayerCount => (_fillRenderer != null ? 1 : 0) + (_edgeRenderer != null ? 1 : 0);
    public int ParticleLayerCount => (_bubbles != null ? 1 : 0) + (_darkSmoke != null ? 1 : 0);
    public float VisualFadeDuration => _visualFadeDuration;

    public static FlamethrowerFuelPuddle Spawn(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        FlamethrowerFuelPuddle puddle = CreateFallback();
        puddle.Configure(center, radius, damagePerTick, duration, tickInterval, default, null, default);
        return puddle;
    }

    public static FlamethrowerFuelPuddle SpawnWithContext(
        Vector3 center,
        float radius,
        int damagePerTick,
        float duration,
        float tickInterval,
        WeaponDamageContext damageContext,
        IWeaponFeedbackSink feedbackSink = null,
        WeaponFeedbackMode feedbackMode = WeaponFeedbackMode.Automatic)
    {
        FlamethrowerFuelPuddle puddle = CreateFallback();
        puddle.Configure(
            center,
            radius,
            damagePerTick,
            duration,
            tickInterval,
            damageContext,
            feedbackSink,
            feedbackMode);
        return puddle;
    }

    public static FlamethrowerFuelPuddle SpawnAuthored(
        GameObject prefab,
        int prewarmCount,
        int poolCapacity,
        Vector3 center,
        float radius,
        int damagePerTick,
        float duration,
        float tickInterval,
        WeaponDamageContext damageContext,
        IWeaponFeedbackSink feedbackSink = null,
        WeaponFeedbackMode feedbackMode = WeaponFeedbackMode.Automatic)
    {
        if (prefab == null)
        {
            return SpawnWithContext(
                center,
                radius,
                damagePerTick,
                duration,
                tickInterval,
                damageContext,
                feedbackSink,
                feedbackMode);
        }

        int key = prefab.GetInstanceID();
        if (!Pools.TryGetValue(key, out PuddlePool pool) || pool.Prefab != prefab)
        {
            pool = new PuddlePool
            {
                Prefab = prefab,
                Capacity = Mathf.Max(1, poolCapacity)
            };
            Pools[key] = pool;
            int count = Mathf.Clamp(prewarmCount, 0, pool.Capacity);
            for (int i = 0; i < count; i++)
            {
                FlamethrowerFuelPuddle prewarmed = CreatePooled(pool);
                prewarmed.gameObject.SetActive(false);
                pool.Available.Enqueue(prewarmed);
            }
        }

        FlamethrowerFuelPuddle puddle = null;
        while (pool.Available.Count > 0 && puddle == null)
            puddle = pool.Available.Dequeue();
        if (puddle == null && pool.Created < pool.Capacity)
            puddle = CreatePooled(pool);
        if (puddle == null)
            return null;

        puddle.gameObject.SetActive(true);
        puddle.Configure(
            center,
            radius,
            damagePerTick,
            duration,
            tickInterval,
            damageContext,
            feedbackSink,
            feedbackMode);
        return puddle;
    }

    private static FlamethrowerFuelPuddle CreateFallback()
    {
        GameObject go = new("FlamethrowerFuelPuddle");
        return go.AddComponent<FlamethrowerFuelPuddle>();
    }

    private static FlamethrowerFuelPuddle CreatePooled(PuddlePool pool)
    {
        GameObject instance = Instantiate(pool.Prefab);
        instance.name = "FlamethrowerFuelPuddle (Pooled)";
        FlamethrowerFuelPuddle puddle = instance.GetComponent<FlamethrowerFuelPuddle>();
        if (puddle == null)
            puddle = instance.AddComponent<FlamethrowerFuelPuddle>();
        puddle._pool = pool;
        pool.Created++;
        return puddle;
    }

    private void Configure(
        Vector3 center,
        float radius,
        int damagePerTick,
        float duration,
        float tickInterval,
        WeaponDamageContext damageContext,
        IWeaponFeedbackSink feedbackSink,
        WeaponFeedbackMode feedbackMode)
    {
        EnsureVisual();
        _radius = Mathf.Max(0.1f, radius);
        _damagePerTick = Mathf.Max(1, damagePerTick);
        _initialDuration = Mathf.Max(0.1f, duration);
        _remainingDuration = _initialDuration;
        _tickInterval = Mathf.Max(0.05f, tickInterval);
        _tickTimer = 0f;
        _damageContext = damageContext;
        _useDamageContext = damageContext.IsValid;
        _feedbackSink = feedbackSink;
        _feedbackMode = feedbackMode;
        _statusInstanceId = _useDamageContext && feedbackSink != null
            ? StatusDamageInstanceRuntime.Next()
            : 0;
        _statusSegmentIndex = 0;
        _statusSegmentElapsed = 0f;
        _statusSegmentClosureNotified = false;
        _center = ResolveGroundPosition(center);
        _shapeSeed = Mathf.Abs(center.x * 12.9898f + center.z * 78.233f);
        transform.SetPositionAndRotation(_center, Quaternion.identity);
        BuildPuddleShape(1f);
        UpdateParticleCoverage();
        _bubbleEmissionStopped = false;
        _smokeEmissionStopped = false;
        _visualFadeDuration = Mathf.Max(
            MinimumVisualFadeDuration,
            Mathf.Max(GetParticleWindDownDuration(_bubbles), GetParticleWindDownDuration(_darkSmoke)));
        SetParticlesActive(true);
        UpdatePuddleVisual();
    }

    private void Awake() => EnsureVisual();

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        _tickTimer -= Time.deltaTime;
        _statusSegmentElapsed += Time.deltaTime;
        if (_tickTimer <= 0f && _remainingDuration > 0f)
        {
            if (_statusSegmentElapsed >= MaximumTallySegmentDuration)
            {
                CloseCurrentStatusSegment();
                _statusSegmentIndex++;
                _statusSegmentElapsed = 0f;
                _statusSegmentClosureNotified = false;
            }

            if (_useDamageContext)
            {
                WeaponRadialDamage.Apply(
                    _center,
                    _radius,
                    _damageContext,
                    falloff: 0f,
                    maxTargets: 64,
                    showVfx: false,
                    observer: this,
                    channel: DamageChannel.Status,
                    statusKind: _damageContext.StatusKind);
            }
            else
                WeaponRadialDamage.Apply(_center, _radius, _damagePerTick, falloff: 0f, knockback: 0f, maxTargets: 64, showVfx: false);
            // Never stack several damage ticks in a single frame after a hitch.
            // Besides feeling unfair, a burst of catch-up ticks would not match the
            // single visual pulse presented by the puddle that frame.
            _tickTimer = _tickInterval;
        }

        UpdateParticleWindDown();
        UpdatePuddleVisual();
        if (_remainingDuration <= 0f)
            Release();
    }

    private void OnDestroy()
    {
        CloseCurrentStatusSegment();
        if (_fillMesh != null)
            DestroyRuntimeObject(_fillMesh);
        if (_edgeMesh != null)
            DestroyRuntimeObject(_edgeMesh);
        if (_pool != null)
            _pool.Created = Mathf.Max(0, _pool.Created - 1);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = FuelEdgeColor;
        Gizmos.DrawWireSphere(transform.position, _radius);
    }

    public void OnRadialDamageApplied(
        Collider hitCollider,
        IDamageable damageable,
        Transform target,
        Vector3 impactPosition,
        in DamageApplicationResult result,
        in WeaponDamageContext damageContext)
    {
        if (_feedbackSink == null || target == null || _statusInstanceId <= 0 ||
            !result.IsAuthoritative || result.AppliedDamage <= 0)
        {
            return;
        }

        WeaponStatusKind statusKind = damageContext.StatusKind;
        DamageFeedbackKind damageKind = statusKind == WeaponStatusKind.JellifiedBurn
            ? DamageFeedbackKind.JellifiedBurn
            : DamageFeedbackKind.Burn;
        Vector3 direction = impactPosition - _center;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.up;
        WeaponFeedbackContext feedback = new(
            damageContext.Weapon,
            _feedbackMode,
            normalizedHeat: 0f,
            origin: _center,
            direction: direction,
            impactPosition: impactPosition,
            impactNormal: -direction.normalized,
            damageAmount: result.AppliedDamage,
            isCritical: damageContext.IsCritical,
            isKill: result.Killed,
            isAbilityDamage: damageContext.IsAbilityDamage,
            targetClass: WeaponEnemyClassifier.GetKind(target),
            surfaceType: ImpactSurfaceResolver.Resolve(hitCollider, damageable),
            explosionRadius: _radius,
            eventIntensity: 0.8f,
            target: target,
            anchor: target,
            referenceDamage: damageContext.ReferenceDamage,
            actionSequenceId: damageContext.ActionSequenceId,
            damageKind: damageKind,
            statusInstanceId: _statusInstanceId,
            statusKind: statusKind,
            segmentIndex: _statusSegmentIndex);
        _feedbackSink.OnDamageConfirmed(in feedback);
    }

    private void EnsureVisual()
    {
        if (_visualReady)
            return;
        _visualReady = true;
        _propertyBlock = new MaterialPropertyBlock();
        EnsureMeshLayer(ref _fillFilter, ref _fillRenderer, "Viscous Fuel Fill", FuelFillColor);
        EnsureMeshLayer(ref _edgeFilter, ref _edgeRenderer, "Irregular Fuel Edge", FuelEdgeColor);
        _fillMesh = new Mesh { name = "Jellified Fuel Fill", hideFlags = HideFlags.DontSave };
        _edgeMesh = new Mesh { name = "Jellified Fuel Edge", hideFlags = HideFlags.DontSave };
        _fillMesh.MarkDynamic();
        _edgeMesh.MarkDynamic();
        _fillFilter.sharedMesh = _fillMesh;
        _edgeFilter.sharedMesh = _edgeMesh;
        _fillVertices = new Vector3[PuddleSegments + 1];
        _fillTriangles = new int[PuddleSegments * 3];
        _fillUvs = new Vector2[PuddleSegments + 1];
        _edgeVertices = new Vector3[PuddleSegments * 2];
        _edgeTriangles = new int[PuddleSegments * 6];
        _edgeUvs = new Vector2[PuddleSegments * 2];
    }

    private void EnsureMeshLayer(ref MeshFilter filter, ref MeshRenderer renderer, string childName, Color fallbackColor)
    {
        if (filter == null)
        {
            GameObject child = new(childName);
            child.transform.SetParent(transform, false);
            filter = child.AddComponent<MeshFilter>();
            renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateFallbackMaterial(fallbackColor);
        }
        else if (renderer == null)
            renderer = filter.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private void UpdatePuddleVisual()
    {
        if (!_visualReady)
            return;
        // Stay visually solid during gameplay, then dissolve only while the
        // already-emitted smoke and bubbles perform their final fade.
        float fadeDuration = Mathf.Min(_initialDuration, Mathf.Max(MinimumVisualFadeDuration, _visualFadeDuration));
        float life = Mathf.Clamp01(_remainingDuration / Mathf.Max(0.01f, fadeDuration));
        ApplyRenderer(_fillRenderer, FuelFillColor, life, 1.4f);
        ApplyRenderer(_edgeRenderer, FuelEdgeColor, life, 2.1f);
    }

    private void BuildPuddleShape(float scale)
    {
        _fillVertices[0] = Vector3.zero;
        _fillUvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < PuddleSegments; i++)
        {
            float t = i / (float)PuddleSegments;
            float angle = t * Mathf.PI * 2f;
            Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float outerRadius = _radius * GetEdgeScale(angle) * scale;
            float innerRadius = outerRadius * (1f - Mathf.Clamp(_edgeThickness, 0.01f, 0.25f));
            Vector3 outer = radial * outerRadius;
            _fillVertices[i + 1] = outer;
            _fillUvs[i + 1] = new Vector2(radial.x, radial.z) * 0.5f + Vector2.one * 0.5f;
            _edgeVertices[i * 2] = radial * innerRadius + Vector3.up * 0.012f;
            _edgeVertices[i * 2 + 1] = outer + Vector3.up * 0.012f;
            _edgeUvs[i * 2] = new Vector2(0f, t);
            _edgeUvs[i * 2 + 1] = new Vector2(1f, t);

            int nextFill = i == PuddleSegments - 1 ? 1 : i + 2;
            int fillTriangle = i * 3;
            _fillTriangles[fillTriangle] = 0;
            _fillTriangles[fillTriangle + 1] = nextFill;
            _fillTriangles[fillTriangle + 2] = i + 1;

            int nextEdge = ((i + 1) % PuddleSegments) * 2;
            int edge = i * 2;
            int edgeTriangle = i * 6;
            _edgeTriangles[edgeTriangle] = edge;
            _edgeTriangles[edgeTriangle + 1] = nextEdge;
            _edgeTriangles[edgeTriangle + 2] = edge + 1;
            _edgeTriangles[edgeTriangle + 3] = edge + 1;
            _edgeTriangles[edgeTriangle + 4] = nextEdge;
            _edgeTriangles[edgeTriangle + 5] = nextEdge + 1;
        }

        AssignMesh(_fillMesh, _fillVertices, _fillUvs, _fillTriangles);
        AssignMesh(_edgeMesh, _edgeVertices, _edgeUvs, _edgeTriangles);
    }

    private static void AssignMesh(Mesh mesh, Vector3[] vertices, Vector2[] uvs, int[] triangles)
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void ApplyRenderer(Renderer renderer, Color color, float life, float emission)
    {
        if (renderer == null)
            return;
        Color faded = color;
        faded.a *= Mathf.SmoothStep(0f, 1f, life);
        renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(BaseColorId, faded);
        _propertyBlock.SetColor(EmissionColorId, color * emission);
        _propertyBlock.SetFloat(EmissionIntensityId, emission);
        _propertyBlock.SetFloat(PulseId, 0.55f + Mathf.Sin(Time.time * _viscousPulseSpeed + _shapeSeed) * 0.15f);
        _propertyBlock.SetFloat(DissolveId, Mathf.Clamp01(1f - life));
        renderer.SetPropertyBlock(_propertyBlock);
    }

    private float GetEdgeScale(float angle)
    {
        float slowWave = Mathf.Sin(angle * 3.1f + _shapeSeed) * 0.16f;
        float fastWave = Mathf.Sin(angle * 7.4f + _shapeSeed * 0.37f) * 0.08f;
        return Mathf.Clamp(0.88f + slowWave + fastWave, 0.68f, 1.14f);
    }

    private void SetParticlesActive(bool active)
    {
        SetParticleState(_bubbles, active);
        SetParticleState(_darkSmoke, active);
    }

    private void UpdateParticleWindDown()
    {
        StopEmissionForWindDown(_bubbles, ref _bubbleEmissionStopped);
        StopEmissionForWindDown(_darkSmoke, ref _smokeEmissionStopped);
    }

    private void StopEmissionForWindDown(ParticleSystem particles, ref bool emissionStopped)
    {
        if (particles == null || emissionStopped || _remainingDuration > GetParticleWindDownDuration(particles))
            return;

        // Preserve living particles so their authored alpha/size-over-lifetime
        // curves can finish instead of popping out with the puddle.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        emissionStopped = true;
    }

    private static float GetParticleWindDownDuration(ParticleSystem particles)
    {
        if (particles == null)
            return 0f;
        ParticleSystem.MainModule main = particles.main;
        return main.startLifetime.constantMax / Mathf.Max(0.01f, main.simulationSpeed) + ParticleFadePadding;
    }

    private void UpdateParticleCoverage()
    {
        // Authored particle radii are suitable for the small on-hit puddle, but the
        // active ability can create a much larger pool. Fill each particle shape
        // from the actual damage radius so bubbles and smoke cover the whole pool.
        SetParticleCoverage(_bubbles, _radius * 0.9f, 1f);
        SetParticleCoverage(_darkSmoke, _radius * 0.82f, 0.86f);
    }

    private static void SetParticleCoverage(ParticleSystem particles, float radius, float radiusThickness)
    {
        if (particles == null)
            return;
        ParticleSystem.ShapeModule shape = particles.shape;
        // Unity's Circle shape is authored in its local XY plane. Rotate it onto
        // the ground so emission covers both world X and Z and travels upward.
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.radius = Mathf.Max(0.05f, radius);
        shape.radiusThickness = Mathf.Clamp01(radiusThickness);
    }

    private static void SetParticleState(ParticleSystem particles, bool active)
    {
        if (particles == null)
            return;
        if (active)
        {
            particles.Clear();
            particles.Play();
        }
        else
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Release()
    {
        CloseCurrentStatusSegment();
        _feedbackSink = null;
        _statusInstanceId = 0;
        _statusSegmentIndex = 0;
        _statusSegmentElapsed = 0f;
        _statusSegmentClosureNotified = false;
        SetParticlesActive(false);
        if (_pool == null)
        {
            Destroy(gameObject);
            return;
        }
        gameObject.SetActive(false);
        _pool.Available.Enqueue(this);
    }

    private void CloseCurrentStatusSegment()
    {
        if (_statusSegmentClosureNotified || _statusInstanceId <= 0 ||
            _feedbackSink is not ICombatTextStatusLifecycleSink lifecycleSink)
        {
            return;
        }

        _statusSegmentClosureNotified = true;
        // A puddle can affect many targets, so a null target closes every tally
        // carrying this puddle's exact status-instance/segment identity.
        lifecycleSink.OnStatusSegmentClosed(
            null,
            _damageContext.StatusKind,
            _statusInstanceId,
            _statusSegmentIndex);
    }

    private static Vector3 ResolveGroundPosition(Vector3 center)
    {
        Vector3 rayOrigin = center + Vector3.up * GroundProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            GroundHits,
            GroundProbeHeight + GroundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);
        float nearestDistance = float.MaxValue;
        Vector3 nearestPoint = default;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = GroundHits[i];
            if (hit.distance < nearestDistance && IsGroundSurface(hit.collider))
            {
                nearestDistance = hit.distance;
                nearestPoint = hit.point;
            }
        }
        if (nearestDistance < float.MaxValue)
            return nearestPoint + Vector3.up * SurfaceOffset;
        if (center.y > 0.5f)
            center.y = 0f;
        return center + Vector3.up * SurfaceOffset;
    }

    private static bool IsGroundSurface(Collider collider) =>
        collider != null &&
        collider.GetComponentInParent<PlayerStats>() == null &&
        collider.GetComponentInParent<EnemyRegistryMember>() == null &&
        collider.GetComponentInParent<IDamageable>() == null;

    private static Material CreateFallbackMaterial(Color color)
    {
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);
        return material;
    }

    private static void DestroyRuntimeObject(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}
