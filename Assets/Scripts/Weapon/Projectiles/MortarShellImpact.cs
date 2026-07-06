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
    private const int AirburstProbeSegments = 32;
    private static readonly Color GrapeshotVfxColor = new(1f, 0.88f, 0.22f, 0.9f);
    private static readonly Color RepeatExplosionVfxColor = new(0.68f, 0.35f, 1f, 0.9f);
    private static Material s_shellMaterial;
    private static Material s_grapeshotShellMaterial;
    private static Material s_repeatShellMaterial;

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
    private GameObject _shellVisual;
    private readonly Vector3[] _arcPoints = new Vector3[ArcSegments + 1];
    private readonly RaycastHit[] _collisionHits = new RaycastHit[CollisionBufferSize];
    private readonly List<IDamageable> _damagedThisExplosion = new();
    private MortarUpgradePayload _payload = MortarUpgradePayload.None;
    private bool _useWeaponDamageContext;
    private WeaponDamageContext _weaponDamageContext;
    private bool _useGrapeshotVfx;
    private float _grapeshotAirburstNormalizedTime = -1f;
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
            payload,
            useGrapeshotVfx: false);
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
        MortarUpgradePayload payload,
        bool useGrapeshotVfx,
        WeaponDamageContext damageContext = default)
    {
        GameObject go = new GameObject("MortarShellImpact");
        MortarShellImpact shell = go.AddComponent<MortarShellImpact>();
        shell._payload = payload;
        shell._useGrapeshotVfx = useGrapeshotVfx || payload.UseGrapeshot;
        shell._weaponDamageContext = damageContext;
        shell._useWeaponDamageContext = damageContext.IsValid;
        shell.Configure(start, target, travelTime, arcHeight, damage, explosionRadius, falloff, knockback, collisionRadius, ignoredRoot);
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
        _grapeshotAirburstNormalizedTime = _payload.UseGrapeshot
            ? CalculateGrapeshotAirburstNormalizedTime()
            : -1f;
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

        if (_payload.UseGrapeshot && t >= _grapeshotAirburstNormalizedTime)
        {
            Vector3 airburstPoint = GetArcPoint(_grapeshotAirburstNormalizedTime);
            transform.position = airburstPoint;
            Detonate(airburstPoint);
            return;
        }

        if (TryGetCollision(previousPosition, nextPosition, out Vector3 collisionPoint))
        {
            transform.position = collisionPoint;
            Detonate(collisionPoint);
            return;
        }

        transform.position = nextPosition;
        if (t >= MortarTrajectory.GetMaximumNormalizedTime(_travelTime))
            DestroyUnityObject(gameObject);
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

        if (_payload.UseGrapeshot)
            return true;

        if (_payload.RepeatExplosionCount > 1
            && hit.collider != null
            && hit.collider.GetComponentInParent<IDamageable>() != null)
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
        if (UsesGrapeshotVisuals())
        {
            _line.startColor = GrapeshotVfxColor;
            _line.endColor = new Color(1f, 0.72f, 0.06f, 0.3f);
        }
        else if (UsesRepeatExplosionVisuals())
        {
            _line.startColor = RepeatExplosionVfxColor;
            _line.endColor = new Color(0.54f, 0.16f, 1f, 0.3f);
        }
        else
        {
            _line.startColor = new Color(1f, 0.85f, 0.2f, 0.9f);
            _line.endColor = new Color(1f, 0.35f, 0.05f, 0.25f);
        }
    }

    private void BuildShellVisual()
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _shellVisual = visual;
        visual.name = "Mortar Shell Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * Mathf.Max(0.12f, _collisionRadius * 2f);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
            DestroyUnityObject(visualCollider);

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetShellMaterial(UsesGrapeshotVisuals(), UsesRepeatExplosionVisuals());
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

    private static Material GetShellMaterial(bool grapeshot, bool repeat)
    {
        if (grapeshot && s_grapeshotShellMaterial != null)
            return s_grapeshotShellMaterial;

        if (repeat && s_repeatShellMaterial != null)
            return s_repeatShellMaterial;

        if (!grapeshot && !repeat && s_shellMaterial != null)
            return s_shellMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.color = grapeshot
            ? new Color(1f, 0.86f, 0.08f, 1f)
            : repeat
                ? new Color(0.68f, 0.35f, 1f, 1f)
            : new Color(1f, 0.42f, 0.04f, 1f);

        if (grapeshot)
            s_grapeshotShellMaterial = material;
        else if (repeat)
            s_repeatShellMaterial = material;
        else
            s_shellMaterial = material;

        return material;
    }

    private void Detonate()
    {
        Detonate(_target);
    }

    private void Detonate(Vector3 explosionCenter)
    {
        _target = explosionCenter;
        HideFlightVisuals();
        if (_payload.UseGrapeshot)
            SpawnGrapeshot(explosionCenter);
        else
            ApplyExplosionDamageAt(explosionCenter);

        if (!_detonated)
        {
            _detonated = true;
            _remainingRepeatExplosions = Mathf.Max(1, _payload.RepeatExplosionCount) - 1;
            _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
            if (_remainingRepeatExplosions > 0)
                WeaponUpgradeVfx.SpawnRing(explosionCenter, _explosionRadius * 1.25f, RepeatExplosionVfxColor, _repeatExplosionTimer, 1.5f, null);
        }

        if (_remainingRepeatExplosions <= 0)
            DestroyUnityObject(gameObject);
    }

    private void TickRepeatExplosions()
    {
        if (_remainingRepeatExplosions <= 0)
        {
            DestroyUnityObject(gameObject);
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
        if (UsesGrapeshotVisuals())
            ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius, GrapeshotVfxColor);
        else if (UsesRepeatExplosionVisuals())
            ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius, RepeatExplosionVfxColor);
        else
            ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius);

        if (_payload.RepeatExplosionCount > 1)
            WeaponUpgradeVfx.SpawnRing(explosionCenter, _explosionRadius * 1.15f, RepeatExplosionVfxColor, 0.55f, 1.8f, null);

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
            int finalDamage = ResolveDamage(damageable, hits[i], falloffScale);
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
                EnemyKnockbackReceiver.TryApply(damageable, explosionCenter, ResolveKnockback(finalDamage, falloffScale));
        }
    }

    private void SpawnGrapeshot(Vector3 center)
    {
        if (!_payload.UseGrapeshot || _payload.GrapeshotCount <= 0)
            return;

        Vector3 forward = _target - _start;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        Quaternion baseRotation = Quaternion.LookRotation(forward, Vector3.up);
        float grapeshotDamageScale = Mathf.Max(0f, _payload.GrapeshotDamageScale);
        int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * grapeshotDamageScale));
        WeaponDamageContext grapeshotContext = CreateScaledDamageContext(grapeshotDamageScale, 0.35f);
        float subShellExplosionRadius = Mathf.Max(0.25f, _explosionRadius * 0.35f);
        float subShellCollisionRadius = Mathf.Max(0.04f, _collisionRadius * 0.75f);
        float subShellTravelTime = Mathf.Clamp(_travelTime * 0.45f, 0.22f, 0.6f);
        float spreadDistance = Mathf.Max(0.75f, _explosionRadius);
        float fallDistance = Mathf.Max(1.5f, _explosionRadius * 1.2f);

        WeaponUpgradeVfx.SpawnRing(center, Mathf.Max(0.35f, _explosionRadius * 0.45f), GrapeshotVfxColor, 0.35f, 1.1f, null);

        for (int i = 0; i < _payload.GrapeshotCount; i++)
        {
            float yaw = Random.Range(-_payload.GrapeshotConeAngle * 0.5f, _payload.GrapeshotConeAngle * 0.5f);
            Vector3 direction = baseRotation * Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 target = center
                + direction.normalized * spreadDistance
                + Vector3.down * fallDistance;

            MortarShellImpact.Launch(
                center,
                target,
                subShellTravelTime,
                0f,
                damage,
                subShellExplosionRadius,
                0.2f,
                _knockback * 0.35f,
                subShellCollisionRadius,
                _ignoredRoot,
                MortarUpgradePayload.None,
                useGrapeshotVfx: true,
                grapeshotContext);
        }
    }

    private int ResolveDamage(IDamageable damageable, Collider hitCollider, float additionalScale = 1f)
    {
        if (!_useWeaponDamageContext)
            return Mathf.Max(1, Mathf.RoundToInt(_damage * Mathf.Max(0f, additionalScale)));

        return _weaponDamageContext.CalculateDamage(GetDamageTarget(damageable, hitCollider), additionalScale);
    }

    private float ResolveKnockback(int damage, float falloffScale = 1f)
    {
        if (!_useWeaponDamageContext)
            return _knockback * Mathf.Max(0f, falloffScale);

        return _weaponDamageContext.CalculateKnockback(damage, falloffScale);
    }

    private WeaponDamageContext CreateScaledDamageContext(float damageScale, float knockbackScale)
    {
        if (!_useWeaponDamageContext)
            return default;

        return new WeaponDamageContext(
            _weaponDamageContext.Stats,
            _weaponDamageContext.Weapon,
            _weaponDamageContext.CanCrit,
            _weaponDamageContext.CritMultiplierOverride,
            _weaponDamageContext.DamageScale * Mathf.Max(0f, damageScale),
            _weaponDamageContext.IsAbilityDamage,
            _weaponDamageContext.KnockbackScale * Mathf.Max(0f, knockbackScale));
    }

    private static Transform GetDamageTarget(IDamageable damageable, Collider hitCollider)
    {
        if (damageable is Component component)
            return component.transform;

        return hitCollider != null ? hitCollider.transform : null;
    }

    private bool UsesGrapeshotVisuals() => _useGrapeshotVfx || _payload.UseGrapeshot;
    private bool UsesRepeatExplosionVisuals() => _payload.RepeatExplosionCount > 1;

    private void HideFlightVisuals()
    {
        if (_line != null)
            _line.enabled = false;

        if (_shellVisual != null)
            _shellVisual.SetActive(false);
    }

    private float GetGrapeshotAirburstNormalizedTime()
    {
        if (_grapeshotAirburstNormalizedTime >= 0f)
            return _grapeshotAirburstNormalizedTime;

        return CalculateGrapeshotAirburstNormalizedTime();
    }

    private float CalculateGrapeshotAirburstNormalizedTime()
    {
        float apexTime = GetApexNormalizedTime();
        float impactTime = GetTrajectoryWorldImpactNormalizedTime(apexTime);
        if (impactTime <= apexTime)
            impactTime = 1f;

        return Mathf.Clamp01(Mathf.Lerp(apexTime, impactTime, 0.5f));
    }

    private float GetApexNormalizedTime()
    {
        if (_arcHeight <= 0.0001f)
            return 0.5f;

        float verticalDelta = _target.y - _start.y;
        return Mathf.Clamp01(0.5f + verticalDelta / (8f * _arcHeight));
    }

    private float GetTrajectoryWorldImpactNormalizedTime(float startTime)
    {
        float previousT = Mathf.Clamp01(startTime);
        Vector3 previousPoint = GetArcPoint(previousT);

        for (int i = 1; i <= AirburstProbeSegments; i++)
        {
            float t = Mathf.Lerp(previousT, 1f, i / (float)AirburstProbeSegments);
            Vector3 point = GetArcPoint(t);
            if (TryGetAirburstProbeCollision(previousPoint, point, out float segmentAlpha))
                return Mathf.Lerp(previousT, t, segmentAlpha);

            previousT = t;
            previousPoint = point;
        }

        return 1f;
    }

    private bool TryGetAirburstProbeCollision(Vector3 start, Vector3 end, out float segmentAlpha)
    {
        segmentAlpha = 1f;
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

        float closestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _collisionHits[i];
            if (ShouldIgnoreAirburstProbeCollision(hit))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            found = true;
        }

        if (!found)
            return false;

        segmentAlpha = Mathf.Clamp01(closestDistance / distance);
        return true;
    }

    private bool ShouldIgnoreAirburstProbeCollision(RaycastHit hit)
    {
        Collider hitCollider = hit.collider;
        Transform hitTransform = hit.transform;
        if (hitCollider == null || hitTransform == null)
            return true;

        if (hitTransform == transform || hitTransform.IsChildOf(transform))
            return true;

        if (hitCollider.GetComponentInParent<IDamageable>() != null)
            return true;

        if (_ignoredRoot == null)
            return false;

        if (hitTransform == _ignoredRoot || hitTransform.IsChildOf(_ignoredRoot))
            return true;

        Rigidbody body = hit.rigidbody;
        return body != null && (body.transform == _ignoredRoot || body.transform.IsChildOf(_ignoredRoot));
    }

    private static void DestroyUnityObject(Object target)
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
