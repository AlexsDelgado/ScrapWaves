using System.Collections.Generic;
using UnityEngine;

public readonly struct MortarUpgradePayload
{
    public readonly bool UseGrapeshot;
    public readonly int GrapeshotCount;
    public readonly float GrapeshotConeAngle;
    public readonly float GrapeshotDamageScale;
    public readonly int RepeatExplosionCount;
    public readonly float RepeatExplosionDelay;

    public MortarUpgradePayload(
        bool useGrapeshot,
        int grapeshotCount,
        float grapeshotConeAngle,
        float grapeshotDamageScale,
        int repeatExplosionCount,
        float repeatExplosionDelay)
    {
        UseGrapeshot = useGrapeshot;
        GrapeshotCount = grapeshotCount;
        GrapeshotConeAngle = grapeshotConeAngle;
        GrapeshotDamageScale = grapeshotDamageScale;
        RepeatExplosionCount = repeatExplosionCount;
        RepeatExplosionDelay = repeatExplosionDelay;
    }

    public static MortarUpgradePayload None => new(false, 0, 0f, 0f, 1, 0f);
}

[DisallowMultipleComponent]
public sealed class MortarShellImpact : MonoBehaviour
{
    private const int ArcSegments = 18;
    private const int CollisionBufferSize = 16;
    private static readonly Color GrapeshotVfxColor = new(1f, 0.88f, 0.22f, 0.9f);
    private static readonly Color RepeatExplosionVfxColor = new(0.68f, 0.35f, 1f, 0.9f);
    private static Material s_shellMaterial;

    private Vector3 _start;
    private Vector3 _target;
    private float _travelTime;
    private float _elapsed;
    private float _arcHeight;
    private float _explosionRadius;
    private float _falloff;
    private int _damage;
    private float _knockback;
    private float _collisionRadius;
    private Transform _ignoredRoot;
    private LineRenderer _line;
    private readonly Vector3[] _arcPoints = new Vector3[ArcSegments + 1];
    private readonly RaycastHit[] _collisionHits = new RaycastHit[CollisionBufferSize];
    private readonly List<IDamageable> _damagedThisExplosion = new();
    private MortarUpgradePayload _payload = MortarUpgradePayload.None;
    private int _remainingRepeatExplosions;
    private float _repeatExplosionTimer;
    private bool _detonated;

    public static MortarShellImpact Launch(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback,
        float collisionRadius,
        Transform ignoredRoot)
    {
        return Launch(
            start,
            target,
            travelTime,
            arcHeight,
            damage,
            explosionRadius,
            falloff,
            knockback,
            collisionRadius,
            ignoredRoot,
            MortarUpgradePayload.None);
    }

    public static MortarShellImpact Launch(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback,
        float collisionRadius,
        Transform ignoredRoot,
        MortarUpgradePayload payload)
    {
        GameObject go = new GameObject("MortarShellImpact");
        MortarShellImpact shell = go.AddComponent<MortarShellImpact>();
        shell.Configure(start, target, travelTime, arcHeight, damage, explosionRadius, falloff, knockback, collisionRadius, ignoredRoot);
        shell._payload = payload;
        return shell;
    }

    private void Configure(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback,
        float collisionRadius,
        Transform ignoredRoot)
    {
        _start = start;
        _target = target;
        _travelTime = Mathf.Max(0.05f, travelTime);
        _arcHeight = Mathf.Max(0f, arcHeight);
        _damage = Mathf.Max(1, damage);
        _explosionRadius = Mathf.Max(0f, explosionRadius);
        _falloff = Mathf.Clamp01(falloff);
        _knockback = Mathf.Max(0f, knockback);
        _collisionRadius = Mathf.Max(0.01f, collisionRadius);
        _ignoredRoot = ignoredRoot;
        transform.position = _start;
        BuildLineRenderer();
        BuildShellVisual();
        UpdateArcVisual();
    }

    private void Update()
    {
        if (_detonated)
        {
            TickRepeatExplosions();
            return;
        }

        _elapsed += Time.deltaTime;
        float t = _elapsed / _travelTime;
        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = GetArcPoint(t);

        if (TryGetCollision(previousPosition, nextPosition, out Vector3 collisionPoint))
        {
            transform.position = collisionPoint;
            Detonate(collisionPoint);
            return;
        }

        transform.position = nextPosition;
        if (t >= MortarTrajectory.GetMaximumNormalizedTime(_travelTime))
            DestroyObject(gameObject);
    }

    // Sweeps the shell between frames so fast projectiles cannot tunnel through map geometry.
    private bool TryGetCollision(Vector3 start, Vector3 end, out Vector3 collisionPoint)
    {
        collisionPoint = end;
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return false;

        Vector3 direction = displacement / distance;
        int hitCount = Physics.SphereCastNonAlloc(
            start,
            _collisionRadius,
            direction,
            _collisionHits,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        RaycastHit closestHit = default;
        float closestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _collisionHits[i];
            if (ShouldIgnoreCollision(hit))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestHit = hit;
            closestDistance = hit.distance;
            found = true;
        }

        if (!found)
            return false;

        collisionPoint = closestHit.point;
        return true;
    }

    private bool ShouldIgnoreCollision(RaycastHit hit)
    {
        Transform hitTransform = hit.transform;
        if (hitTransform == null)
            return true;

        if (hitTransform == transform || hitTransform.IsChildOf(transform))
            return true;

        if (_ignoredRoot == null)
            return false;

        if (hitTransform == _ignoredRoot || hitTransform.IsChildOf(_ignoredRoot))
            return true;

        Rigidbody body = hit.rigidbody;
        return body != null && (body.transform == _ignoredRoot || body.transform.IsChildOf(_ignoredRoot));
    }

    private void BuildLineRenderer()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.widthMultiplier = 0.06f;
        _line.positionCount = _arcPoints.Length;
        _line.material = new Material(Shader.Find("Sprites/Default"));
        _line.startColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        _line.endColor = new Color(1f, 0.35f, 0.05f, 0.25f);
    }

    private void BuildShellVisual()
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Mortar Shell Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * Mathf.Max(0.12f, _collisionRadius * 2f);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
            DestroyObject(visualCollider);

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetShellMaterial();
    }

    private void UpdateArcVisual()
    {
        for (int i = 0; i < _arcPoints.Length; i++)
        {
            float t = i / (float)(_arcPoints.Length - 1);
            _arcPoints[i] = GetArcPoint(t);
        }

        _line.SetPositions(_arcPoints);
    }

    private Vector3 GetArcPoint(float t)
    {
        return MortarTrajectory.Evaluate(_start, _target, _arcHeight, t);
    }

    private static Material GetShellMaterial()
    {
        if (s_shellMaterial != null)
            return s_shellMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_shellMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        s_shellMaterial.color = new Color(1f, 0.42f, 0.04f, 1f);
        return s_shellMaterial;
    }

    private void Detonate()
    {
        Detonate(_target);
    }

    private void Detonate(Vector3 explosionCenter)
    {
        _target = explosionCenter;
        ApplyExplosionDamageAt(explosionCenter);

        if (!_detonated)
        {
            _detonated = true;
            _remainingRepeatExplosions = Mathf.Max(1, _payload.RepeatExplosionCount) - 1;
            _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
            SpawnGrapeshot(explosionCenter);
            if (_remainingRepeatExplosions > 0)
                WeaponUpgradeVfx.SpawnRing(explosionCenter, _explosionRadius * 1.25f, RepeatExplosionVfxColor, _repeatExplosionTimer, 1.5f, "REPEAT");
        }

        if (_remainingRepeatExplosions <= 0)
            DestroyObject(gameObject);
    }

    private void TickRepeatExplosions()
    {
        if (_remainingRepeatExplosions <= 0)
        {
            DestroyObject(gameObject);
            return;
        }

        _repeatExplosionTimer -= Time.deltaTime;
        if (_repeatExplosionTimer > 0f)
            return;

        _remainingRepeatExplosions--;
        _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
        ApplyExplosionDamageAt(_target);
    }

    private void ApplyExplosionDamageAt(Vector3 explosionCenter)
    {
        ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius);
        if (_payload.RepeatExplosionCount > 1)
            WeaponUpgradeVfx.SpawnRing(explosionCenter, _explosionRadius * 1.15f, RepeatExplosionVfxColor, 0.55f, 1.8f, "BOOM");

        _damagedThisExplosion.Clear();
        Collider[] hits = Physics.OverlapSphere(explosionCenter, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            if (_damagedThisExplosion.Contains(damageable))
                continue;

            _damagedThisExplosion.Add(damageable);
            float distance = Vector3.Distance(explosionCenter, hits[i].transform.position);
            float t = _explosionRadius <= 0f ? 1f : Mathf.Clamp01(distance / _explosionRadius);
            float falloffScale = Mathf.Lerp(1f, 1f - _falloff, t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(_damage * falloffScale));
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
                EnemyKnockbackReceiver.TryApply(damageable, explosionCenter, _knockback * falloffScale);
        }
    }

    private void SpawnGrapeshot(Vector3 center)
    {
        if (!_payload.UseGrapeshot || _payload.GrapeshotCount <= 0)
            return;

        Vector3 forward = (_target - _start).sqrMagnitude > 0.0001f ? (_target - _start).normalized : Vector3.forward;
        Quaternion baseRotation = Quaternion.LookRotation(forward, Vector3.up);
        for (int i = 0; i < _payload.GrapeshotCount; i++)
        {
            float yaw = Random.Range(-_payload.GrapeshotConeAngle * 0.5f, _payload.GrapeshotConeAngle * 0.5f);
            Vector3 direction = baseRotation * Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 hitCenter = center + direction.normalized * Mathf.Max(0.5f, _explosionRadius);
            int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * Mathf.Max(0f, _payload.GrapeshotDamageScale)));
            WeaponUpgradeVfx.SpawnBeam(center + Vector3.up * 0.1f, hitCenter + Vector3.up * 0.1f, GrapeshotVfxColor, 0.35f, 0.06f, i == 0 ? "SHOT" : null);
            WeaponUpgradeVfx.SpawnRing(hitCenter, Mathf.Max(0.25f, _explosionRadius * 0.35f), GrapeshotVfxColor, 0.35f, 0.8f, null);
            WeaponRadialDamage.Apply(hitCenter, Mathf.Max(0.25f, _explosionRadius * 0.35f), damage, 0.2f, _knockback * 0.35f, 16);
        }
    }

    private static void DestroyObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private void OnDrawGizmos()
    {
        if (_explosionRadius <= 0f)
            return;

        Gizmos.color = new Color(1f, 0.62f, 0.05f, 0.8f);
        Gizmos.DrawWireSphere(_target, _explosionRadius);
    }
}
