using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class Projectile : MonoBehaviour
{
    [SerializeField, Min(1)] private int _damage = 2;

    [SerializeField, Tooltip("Unidades por segundo (movimiento en FixedUpdate con Rigidbody kinematic).")]
    private float _speed = 18f;

    [SerializeField, Tooltip("Segundos de vida por defecto (el pool puede sobrescribir en ConfigurePooled).")]
    private float _maxLifetime = 4f;

    private float _activeMaxLifetime;
    private Vector3 _direction = Vector3.forward;
    private float _elapsed;
    private bool _consumed;
    private Rigidbody _rigidbody;
    private SphereCollider _sphereCollider;
    private Vector3 _baseScale;
    private bool _useExplosion;
    private float _explosionRadius;
    private float _explosionFalloff;
    private float _knockback;
    private float _activeSpeed;
    private Vector3 _launchPosition;
    private float _maxTravelDistance;
    private bool _explodeOnMaxTravel;
    private bool _applyDamageAmplifierOnExplosion;
    private float _damageAmplifierMultiplier = 1f;
    private float _damageAmplifierDuration;
    private bool _useFragmentCone;
    private float _fragmentConeAngle;
    private float _fragmentConeRange;
    private float _fragmentDamageScale;
    private bool _useExplosionCluster;
    private bool _visualOnly;
    private ProjectilePool _clusterPool;
    private int _clusterProjectileCount;
    private int _clusterDamage;
    private float _clusterExplosionRadius;
    private float _clusterFalloff;
    private float _clusterKnockback;
    private float _clusterSpeedMultiplier;
    private float _clusterTravelDistance;
    private float _clusterFragmentConeAngle;
    private float _clusterFragmentConeRange;
    private float _clusterFragmentDamageScale;
    private static readonly Color ExplosionGizmoColor = new(1f, 0.42f, 0.05f, 0.85f);
    private static readonly Color AmplifierVfxColor = new(1f, 0.1f, 0.72f, 0.95f);
    private static readonly Color FragmentVfxColor = new(0.55f, 0.02f, 0.02f, 0.95f);
    private static readonly Color ClusterVfxColor = new(0.7f, 0.03f, 0.02f, 0.95f);
    private readonly RaycastHit[] _sweepHits = new RaycastHit[12];

    private void Awake()
    {
        _baseScale = transform.localScale;
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        _sphereCollider = GetComponent<SphereCollider>();
        _sphereCollider.isTrigger = true;
        _activeMaxLifetime = _maxLifetime;
        _activeSpeed = _speed;
    }

    public void ConfigurePooled(float maxLifetimeSeconds)
    {
        _activeMaxLifetime = Mathf.Max(0.05f, maxLifetimeSeconds);
        _knockback = 0f;
        _visualOnly = false;
    }

    public void ConfigurePooled(float maxLifetimeSeconds, int damageForThisShot)
    {
        ConfigurePooled(maxLifetimeSeconds, damageForThisShot, 0f);
    }

    public void ConfigurePooled(float maxLifetimeSeconds, int damageForThisShot, float knockbackForThisShot)
    {
        ConfigurePooled(maxLifetimeSeconds);
        _damage = Mathf.Max(1, damageForThisShot);
        _knockback = Mathf.Max(0f, knockbackForThisShot);
    }

    public void ConfigureVisualOnly(float maxLifetimeSeconds)
    {
        _activeMaxLifetime = Mathf.Max(0.05f, maxLifetimeSeconds);
        _knockback = 0f;
        _visualOnly = true;
    }

    // Launches projectile and resets runtime damage mode state.
    public void Launch(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude > 0.0001f)
            _direction = worldDirection.normalized;

        _elapsed = 0f;
        _consumed = false;

        _rigidbody.position = transform.position;
        _rigidbody.rotation = transform.rotation;
        transform.localScale = _baseScale;
        _launchPosition = transform.position;
        _activeSpeed = _speed;
        _useExplosion = false;
        _explosionRadius = 0f;
        _explosionFalloff = 0f;
        _maxTravelDistance = 0f;
        _explodeOnMaxTravel = false;
        _applyDamageAmplifierOnExplosion = false;
        _damageAmplifierMultiplier = 1f;
        _damageAmplifierDuration = 0f;
        _useFragmentCone = false;
        _fragmentConeAngle = 0f;
        _fragmentConeRange = 0f;
        _fragmentDamageScale = 0f;
        _useExplosionCluster = false;
        _clusterPool = null;
        _clusterProjectileCount = 0;
        _clusterDamage = 0;
        _clusterExplosionRadius = 0f;
        _clusterFalloff = 0f;
        _clusterKnockback = 0f;
        _clusterSpeedMultiplier = 1f;
        _clusterTravelDistance = 0f;
        _clusterFragmentConeAngle = 0f;
        _clusterFragmentConeRange = 0f;
        _clusterFragmentDamageScale = 0f;
    }


    // Configures radial explosion damage behavior for this shot.
    public void ConfigureExplosion(float radius, float falloff)
    {
        _useExplosion = radius > 0f;
        _explosionRadius = Mathf.Max(0f, radius);
        _explosionFalloff = Mathf.Clamp01(falloff);
    }

    // Overrides projectile speed for one shot.
    public void ConfigureSpeedMultiplier(float multiplier)
    {
        _activeSpeed = _speed * Mathf.Max(0.01f, multiplier);
    }

    public void ConfigureVisualScale(float multiplier)
    {
        transform.localScale = _baseScale * Mathf.Max(0.01f, multiplier);
    }

    // Configures detonation once the projectile travels its weapon range.
    public void ConfigureMaxTravel(float maxDistance, bool explodeOnMaxTravel)
    {
        _maxTravelDistance = Mathf.Max(0f, maxDistance);
        _explodeOnMaxTravel = explodeOnMaxTravel;
    }

    // Applies a temporary damage vulnerability to targets caught in the explosion.
    public void ConfigureDamageAmplifierOnExplosion(float multiplier, float duration)
    {
        _applyDamageAmplifierOnExplosion = duration > 0f && multiplier > 1f;
        _damageAmplifierMultiplier = Mathf.Max(1f, multiplier);
        _damageAmplifierDuration = Mathf.Max(0f, duration);
    }

    // Adds forward cone shrapnel damage after the main explosion resolves.
    public void ConfigureFragmentCone(float angle, float range, float damageScale)
    {
        _useFragmentCone = angle > 0f && range > 0f && damageScale > 0f;
        _fragmentConeAngle = Mathf.Clamp(angle, 1f, 180f);
        _fragmentConeRange = Mathf.Max(0f, range);
        _fragmentDamageScale = Mathf.Max(0f, damageScale);
    }

    public void ConfigureExplosionCluster(
        ProjectilePool pool,
        int projectileCount,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback,
        float speedMultiplier,
        float travelDistance,
        float fragmentConeAngle,
        float fragmentConeRange,
        float fragmentDamageScale)
    {
        _useExplosionCluster = pool != null && projectileCount > 0 && damage > 0;
        _clusterPool = pool;
        _clusterProjectileCount = Mathf.Max(0, projectileCount);
        _clusterDamage = Mathf.Max(1, damage);
        _clusterExplosionRadius = Mathf.Max(0.05f, explosionRadius);
        _clusterFalloff = Mathf.Clamp01(falloff);
        _clusterKnockback = Mathf.Max(0f, knockback);
        _clusterSpeedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        _clusterTravelDistance = Mathf.Max(0.1f, travelDistance);
        _clusterFragmentConeAngle = Mathf.Max(0f, fragmentConeAngle);
        _clusterFragmentConeRange = Mathf.Max(0f, fragmentConeRange);
        _clusterFragmentDamageScale = Mathf.Max(0f, fragmentDamageScale);
    }

    private void FixedUpdate()
    {
        if (_consumed)
            return;

        Vector3 delta = _direction * (_activeSpeed * Time.fixedDeltaTime);
        Vector3 currentPosition = _rigidbody.position;
        if (TryConsumeSweptWorldCollision(currentPosition, delta))
            return;

        _rigidbody.MovePosition(currentPosition + delta);

        if (_maxTravelDistance > 0f && Vector3.Distance(_launchPosition, _rigidbody.position) >= _maxTravelDistance)
            ConsumeAtCurrentPosition(_explodeOnMaxTravel);
    }

    private void Update()
    {
        if (_consumed)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _activeMaxLifetime)
            ConsumeAtCurrentPosition(_explodeOnMaxTravel);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_consumed)
            return;

        // World impact: explosive shots detonate, normal bullets despawn.
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        bool hitTerrain = terrainLayer >= 0 && other.gameObject.layer == terrainLayer;
        if (hitTerrain)
        {
            if (_visualOnly)
                DespawnOrDestroy();
            else if (_useExplosion)
                ConsumeAtCurrentPosition(detonate: true);
            else
                DespawnOrDestroy();

            return;
        }

        if (IsIgnoredCollision(other))
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (_visualOnly)
        {
            if (damageable != null || IsSolidWorldCollider(other))
                DespawnOrDestroy();

            return;
        }

        if (_useExplosion)
        {
            if (damageable != null || IsSolidWorldCollider(other))
                ConsumeAtCurrentPosition(detonate: true);

            return;
        }

        if (damageable != null)
        {
            if (WeaponDamageApplier.TryApplyDamage(damageable, _damage))
                EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback);
            DespawnOrDestroy();
            return;
        }

        if (IsSolidWorldCollider(other))
            DespawnOrDestroy();
    }

    // Keeps projectiles from detonating on the player or non-target trigger volumes.
    private bool IsIgnoredCollision(Collider other)
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0 && other.gameObject.layer == playerLayer)
            return true;

        if (other.GetComponentInParent<PlayerHealth>() != null)
            return true;

        return false;
    }

    // Treats non-trigger colliders as world geometry for impact detonation.
    private bool IsSolidWorldCollider(Collider other)
    {
        return !other.isTrigger;
    }

    // Kinematic trigger projectiles can tunnel through thin floor tiles at high speed.
    private bool TryConsumeSweptWorldCollision(Vector3 currentPosition, Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return false;

        float distance = delta.magnitude;
        Vector3 direction = delta / distance;
        Vector3 centerOffset = GetSphereCenterOffset();
        Vector3 sweepStart = currentPosition + centerOffset;
        float sweepRadius = GetScaledSphereRadius();
        int hitCount = Physics.SphereCastNonAlloc(
            sweepStart,
            sweepRadius,
            direction,
            _sweepHits,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        Collider closestCollider = null;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _sweepHits[i].collider;
            if (hitCollider == null || hitCollider == _sphereCollider)
                continue;

            if (IsIgnoredCollision(hitCollider))
                continue;

            bool damageableHit = hitCollider.GetComponentInParent<IDamageable>() != null;
            if (!damageableHit && !IsSolidWorldCollider(hitCollider))
                continue;

            if (_sweepHits[i].distance >= closestDistance)
                continue;

            closestDistance = _sweepHits[i].distance;
            closestCollider = hitCollider;
        }

        if (closestCollider == null)
            return false;

        Vector3 impactPosition = sweepStart + direction * closestDistance - centerOffset;
        _rigidbody.position = impactPosition;
        transform.position = impactPosition;

        IDamageable damageable = closestCollider.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            if (_visualOnly)
                DespawnOrDestroy();
            else if (_useExplosion)
                ConsumeAtCurrentPosition(detonate: true);
            else
            {
                if (WeaponDamageApplier.TryApplyDamage(damageable, _damage))
                    EnemyKnockbackReceiver.TryApply(damageable, impactPosition, _knockback);
                DespawnOrDestroy();
            }

            return true;
        }

        if (_visualOnly)
            DespawnOrDestroy();
        else if (_useExplosion)
            ConsumeAtCurrentPosition(detonate: true);
        else
            DespawnOrDestroy();

        return true;
    }

    private Vector3 GetSphereCenterOffset()
    {
        if (_sphereCollider == null)
            return Vector3.zero;

        return transform.TransformVector(_sphereCollider.center);
    }

    private float GetScaledSphereRadius()
    {
        if (_sphereCollider == null)
            return 0.05f;

        Vector3 scale = _sphereCollider.transform.lossyScale;
        float largestAxis = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return Mathf.Max(0.01f, _sphereCollider.radius * largestAxis);
    }

    // Applies optional explosion before removing this projectile.
    private void ConsumeAtCurrentPosition(bool detonate)
    {
        if (_consumed)
            return;

        if (_useExplosion && detonate)
            ApplyExplosionDamage();

        DespawnOrDestroy();
    }

    // Applies area damage around impact point with distance-based falloff.
    private void ApplyExplosionDamage()
    {
        if (_applyDamageAmplifierOnExplosion)
            ExplosionRadiusVfx.Spawn(transform.position, _explosionRadius, AmplifierVfxColor);
        else if (_useFragmentCone || _useExplosionCluster)
            ExplosionRadiusVfx.Spawn(transform.position, _explosionRadius, FragmentVfxColor);
        else
            ExplosionRadiusVfx.Spawn(transform.position, _explosionRadius);
        if (_applyDamageAmplifierOnExplosion)
            WeaponUpgradeVfx.SpawnRing(transform.position, _explosionRadius * 1.15f, AmplifierVfxColor, 0.65f, 2f, "KINETIC");
        if (_useFragmentCone)
            WeaponUpgradeVfx.SpawnCone(transform.position, GetFragmentForward(), _fragmentConeRange, _fragmentConeAngle, FragmentVfxColor, 0.55f, 7, "FRAG");

        Collider[] hits = Physics.OverlapSphere(transform.position, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            float distance = Vector3.Distance(transform.position, hits[i].transform.position);
            float t = _explosionRadius <= 0f ? 1f : Mathf.Clamp01(distance / _explosionRadius);
            float falloffScale = Mathf.Lerp(1f, 1f - _explosionFalloff, t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(_damage * falloffScale));
            if (_applyDamageAmplifierOnExplosion)
            {
                WeaponDamageAmplifierStatus.Apply(damageable, _damageAmplifierMultiplier, _damageAmplifierDuration);
                WeaponUpgradeVfx.SpawnTargetPulse(hits[i].transform, AmplifierVfxColor, 0.45f, "VULN");
            }
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
                EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback * falloffScale);
        }

        ApplyFragmentConeDamage();
        SpawnExplosionCluster();
    }

    private void ApplyFragmentConeDamage()
    {
        if (!_useFragmentCone)
            return;

        Collider[] hits = Physics.OverlapSphere(transform.position, _fragmentConeRange);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            Vector3 toTarget = hits[i].transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
                continue;

            Vector3 forward = GetFragmentForward();
            float angle = Vector3.Angle(forward.normalized, toTarget.normalized);
            if (angle > _fragmentConeAngle * 0.5f)
                continue;

            int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * _fragmentDamageScale));
            WeaponDamageApplier.TryApplyDamage(damageable, damage);
        }
    }

    private Vector3 GetFragmentForward()
    {
        Vector3 forward = _direction;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private void SpawnExplosionCluster()
    {
        if (!_useExplosionCluster || _clusterPool == null || _clusterProjectileCount <= 0)
            return;

        WeaponUpgradeVfx.SpawnRing(transform.position, _explosionRadius * 1.25f, ClusterVfxColor, 0.55f, 1.4f, "CLUSTER");
        for (int i = 0; i < _clusterProjectileCount; i++)
        {
            float angle = i / (float)_clusterProjectileCount * Mathf.PI * 2f;
            Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
            _clusterPool.TrySpawnExplosiveProjectileWithAmplifier(
                transform.position + direction * 0.15f + Vector3.up * 0.15f,
                rotation,
                direction,
                _clusterDamage,
                _clusterExplosionRadius,
                _clusterFalloff,
                _clusterKnockback,
                _clusterSpeedMultiplier,
                _clusterTravelDistance,
                true,
                1f,
                0f,
                _clusterFragmentConeAngle,
                _clusterFragmentConeRange,
                _clusterFragmentDamageScale);
        }
    }

    private void OnDrawGizmos()
    {
        if (!_useExplosion || _explosionRadius <= 0f)
            return;

        Gizmos.color = ExplosionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, _explosionRadius);
    }

    // Returns projectile to pool or destroys when pooling unavailable.
    private void DespawnOrDestroy()
    {
        if (_consumed)
            return;

        _consumed = true;

        if (TryGetComponent(out ProjectilePoolMember poolMember))
            poolMember.Despawn();
        else
            Destroy(gameObject);
    }
}
