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
    private static readonly Color ExplosionGizmoColor = new(1f, 0.42f, 0.05f, 0.85f);

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        SphereCollider sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        _activeMaxLifetime = _maxLifetime;
        _activeSpeed = _speed;
    }

    public void ConfigurePooled(float maxLifetimeSeconds)
    {
        _activeMaxLifetime = Mathf.Max(0.05f, maxLifetimeSeconds);
        _knockback = 0f;
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

    // Launches projectile and resets runtime damage mode state.
    public void Launch(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude > 0.0001f)
            _direction = worldDirection.normalized;

        _elapsed = 0f;
        _consumed = false;

        _rigidbody.position = transform.position;
        _rigidbody.rotation = transform.rotation;
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

    private void FixedUpdate()
    {
        if (_consumed)
            return;

        Vector3 delta = _direction * (_activeSpeed * Time.fixedDeltaTime);
        _rigidbody.MovePosition(_rigidbody.position + delta);

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
            if (_useExplosion)
                ConsumeAtCurrentPosition(detonate: true);
            else
                DespawnOrDestroy();

            return;
        }

        if (IsIgnoredCollision(other))
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
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
        ExplosionRadiusVfx.Spawn(transform.position, _explosionRadius);

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
                WeaponDamageAmplifierStatus.Apply(damageable, _damageAmplifierMultiplier, _damageAmplifierDuration);
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
                EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback * falloffScale);
        }

        ApplyFragmentConeDamage();
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

            Vector3 forward = _direction;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
                forward = transform.forward;

            float angle = Vector3.Angle(forward.normalized, toTarget.normalized);
            if (angle > _fragmentConeAngle * 0.5f)
                continue;

            int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * _fragmentDamageScale));
            WeaponDamageApplier.TryApplyDamage(damageable, damage);
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
