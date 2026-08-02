using System.Collections.Generic;
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
    private bool _initialized;
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
    private bool _visualOnlyIgnoresCollisions;
    private bool _useWeaponDamageContext;
    private WeaponDamageContext _weaponDamageContext;
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
    private WeaponDamageContext _clusterDamageContext;
    private Collider _detonationCollider;
    private IWeaponPresentationSink _presentationSink;
    private WeaponInstance _presentationWeapon;
    private WeaponPresentationCue _impactCue;
    private WeaponPresentationCue _criticalImpactCue;
    private WeaponPresentationCue _weakPointImpactCue;
    private bool _presentationIsAbility;
    private bool _presentationAllowWeakPoint;
    private bool _presentationImpactEmitted;
    private IWeaponFeedbackSink _feedbackSink;
    private WeaponFeedbackContext _feedbackTemplate;
    private bool _feedbackImpactEmitted;
    private bool _feedbackAllowWeakPoint;
    private bool _replaceExplosionVfx;
    private WeaponPresentationCue _feedbackImpactCueOverride;
    private ProjectileVisualController _visualController;
    private readonly HashSet<IDamageable> _explosionDamageables = new();
    private readonly HashSet<IDamageable> _fragmentConeDamageables = new();
    private static readonly Color ExplosionGizmoColor = new(1f, 0.42f, 0.05f, 0.85f);
    private static readonly Color AmplifierVfxColor = new(1f, 0.1f, 0.72f, 0.95f);
    private static readonly Color FragmentVfxColor = new(0.55f, 0.02f, 0.02f, 0.95f);
    private static readonly Color ClusterVfxColor = new(0.7f, 0.03f, 0.02f, 0.95f);
    private readonly RaycastHit[] _sweepHits = new RaycastHit[12];

    public float ActiveSpeed => _activeSpeed;
    public bool HasPresentationContext => _presentationSink != null;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _baseScale = transform.localScale;
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        _sphereCollider = GetComponent<SphereCollider>();
        if (_sphereCollider != null)
            _sphereCollider.isTrigger = true;

        _activeMaxLifetime = _maxLifetime;
        _activeSpeed = _speed;
        _initialized = true;
    }

    public void ConfigurePooled(float maxLifetimeSeconds)
    {
        ClearPresentation();
        _activeMaxLifetime = Mathf.Max(0.05f, maxLifetimeSeconds);
        _knockback = 0f;
        _visualOnly = false;
        _visualOnlyIgnoresCollisions = false;
        _useWeaponDamageContext = false;
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
        ConfigureVisualOnly(maxLifetimeSeconds, ignoreCollisions: false);
    }

    public void ConfigureVisualOnly(float maxLifetimeSeconds, bool ignoreCollisions)
    {
        ClearPresentation();
        _activeMaxLifetime = Mathf.Max(0.05f, maxLifetimeSeconds);
        _knockback = 0f;
        _visualOnly = true;
        _visualOnlyIgnoresCollisions = ignoreCollisions;
        _useWeaponDamageContext = false;
    }

    public void ConfigureWeaponDamage(WeaponDamageContext context)
    {
        _weaponDamageContext = context;
        _useWeaponDamageContext = context.IsValid;
    }

    public void ConfigurePresentation(
        IWeaponPresentationSink presentationSink,
        WeaponInstance weapon,
        WeaponPresentationCue impactCue,
        WeaponPresentationCue criticalImpactCue,
        WeaponPresentationCue weakPointImpactCue,
        bool isAbility,
        bool allowWeakPoint)
    {
        _presentationSink = presentationSink ?? NullWeaponPresentationSink.Instance;
        _presentationWeapon = weapon;
        _impactCue = impactCue;
        _criticalImpactCue = criticalImpactCue;
        _weakPointImpactCue = weakPointImpactCue;
        _presentationIsAbility = isAbility;
        _presentationAllowWeakPoint = allowWeakPoint;
        _presentationImpactEmitted = false;
    }

    public void ConfigureFeedback(
        IWeaponFeedbackSink feedbackSink,
        in WeaponFeedbackContext template,
        bool allowWeakPoint = false,
        bool replaceExplosionVfx = false,
        WeaponPresentationCue impactCueOverride = WeaponPresentationCue.None)
    {
        _feedbackSink = feedbackSink ?? NullWeaponPresentationSink.Instance;
        _feedbackTemplate = template;
        _feedbackImpactEmitted = false;
        _feedbackAllowWeakPoint = allowWeakPoint;
        _replaceExplosionVfx = replaceExplosionVfx;
        _feedbackImpactCueOverride = impactCueOverride;
    }

    public void ClearPresentation()
    {
        _presentationSink = null;
        _presentationWeapon = null;
        _impactCue = WeaponPresentationCue.None;
        _criticalImpactCue = WeaponPresentationCue.None;
        _weakPointImpactCue = WeaponPresentationCue.None;
        _presentationIsAbility = false;
        _presentationAllowWeakPoint = false;
        _presentationImpactEmitted = false;
        _feedbackSink = null;
        _feedbackTemplate = default;
        _feedbackImpactEmitted = false;
        _feedbackAllowWeakPoint = false;
        _replaceExplosionVfx = false;
        _feedbackImpactCueOverride = WeaponPresentationCue.None;
        if (_visualController == null)
            _visualController = GetComponent<ProjectileVisualController>();
        _visualController?.ResetVisual();
    }

    // Launches projectile and resets runtime damage mode state.
    public void Launch(Vector3 worldDirection)
    {
        EnsureInitialized();

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
        _clusterDamageContext = default;
        _detonationCollider = null;
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
        float fragmentDamageScale,
        WeaponDamageContext damageContext = default)
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
        _clusterDamageContext = damageContext;
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

        Vector3 impactPosition = ResolveImpactPosition(other);

        // World impact: explosive shots detonate, normal bullets despawn.
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        bool hitTerrain = terrainLayer >= 0 && other.gameObject.layer == terrainLayer;
        if (hitTerrain)
        {
            if (_visualOnly && _visualOnlyIgnoresCollisions)
                return;

            if (_visualOnly)
                DespawnOrDestroy();
            else if (_useExplosion)
            {
                MoveToImpactPoint(impactPosition);
                _detonationCollider = other;
                ConsumeAtCurrentPosition(detonate: true);
            }
            else
            {
                EmitFeedbackImpact(other, damageable: null, worldImpact: true, impactPosition, 0, false, false);
                EmitPresentationImpact(other, damageable: null, worldImpact: true, impactPosition);
                DespawnOrDestroy();
            }

            return;
        }

        if (IsIgnoredCollision(other))
            return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (_visualOnly)
        {
            if (_visualOnlyIgnoresCollisions)
                return;

            if (damageable != null || IsSolidWorldCollider(other))
                DespawnOrDestroy();

            return;
        }

        if (_useExplosion)
        {
            if (damageable != null || IsSolidWorldCollider(other))
            {
                MoveToImpactPoint(impactPosition);
                _detonationCollider = other;
                ConsumeAtCurrentPosition(detonate: true);
            }

            return;
        }

        if (damageable != null)
        {
            int finalDamage = ResolveDamage(damageable, other);
            int healthBefore = GetRemainingHealth(damageable);
            bool damageApplied = WeaponDamageApplier.TryApplyDamage(damageable, finalDamage);
            if (damageApplied)
            {
                EnemyKnockbackReceiver.TryApply(damageable, impactPosition, ResolveKnockback(finalDamage));
                bool kill = healthBefore > 0 && GetRemainingHealth(damageable) <= 0;
                EmitFeedbackImpact(other, damageable, worldImpact: false, impactPosition, finalDamage, true, kill);
                EmitPresentationImpact(other, damageable, worldImpact: false, impactPosition);
            }
            DespawnOrDestroy();
            return;
        }

        if (IsSolidWorldCollider(other))
        {
            EmitFeedbackImpact(other, damageable: null, worldImpact: true, impactPosition, 0, false, false);
            EmitPresentationImpact(other, damageable: null, worldImpact: true, impactPosition);
            DespawnOrDestroy();
        }
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
        Vector3 closestImpactPoint = default;
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
            closestImpactPoint = _sweepHits[i].point;
        }

        if (closestCollider == null)
            return false;

        if (_visualOnly && _visualOnlyIgnoresCollisions)
            return false;

        Vector3 impactPosition = sweepStart + direction * closestDistance - centerOffset;
        _rigidbody.position = impactPosition;
        transform.position = impactPosition;

        IDamageable damageable = closestCollider.GetComponentInParent<IDamageable>();
        _detonationCollider = closestCollider;
        if (damageable != null)
        {
            if (_visualOnly)
                DespawnOrDestroy();
            else if (_useExplosion)
                ConsumeAtCurrentPosition(detonate: true);
            else
            {
                int finalDamage = ResolveDamage(damageable, closestCollider);
                int healthBefore = GetRemainingHealth(damageable);
                bool damageApplied = WeaponDamageApplier.TryApplyDamage(damageable, finalDamage);
                if (damageApplied)
                {
                    EnemyKnockbackReceiver.TryApply(damageable, impactPosition, ResolveKnockback(finalDamage));
                    bool kill = healthBefore > 0 && GetRemainingHealth(damageable) <= 0;
                    EmitFeedbackImpact(closestCollider, damageable, worldImpact: false, closestImpactPoint, finalDamage, true, kill);
                    EmitPresentationImpact(closestCollider, damageable, worldImpact: false, closestImpactPoint);
                }
                DespawnOrDestroy();
            }

            return true;
        }

        if (_visualOnly)
            DespawnOrDestroy();
        else if (_useExplosion)
            ConsumeAtCurrentPosition(detonate: true);
        else
        {
            EmitFeedbackImpact(closestCollider, damageable: null, worldImpact: true, closestImpactPoint, 0, false, false);
            EmitPresentationImpact(closestCollider, damageable: null, worldImpact: true, closestImpactPoint);
            DespawnOrDestroy();
        }

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
        if (!_replaceExplosionVfx)
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
        }

        _explosionDamageables.Clear();
        int totalDamage = 0;
        bool anyDamageApplied = false;
        bool anyKill = false;
        IDamageable feedbackDamageable = null;
        Collider feedbackCollider = null;
        Collider[] hits = Physics.OverlapSphere(transform.position, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = GetAreaDamageable(hits[i]);
            if (damageable == null)
                continue;

            if (!_explosionDamageables.Add(damageable))
                continue;

            Transform targetTransform = GetDamageableTransform(damageable, hits[i]);
            float distance = Vector3.Distance(transform.position, targetTransform.position);
            float t = _explosionRadius <= 0f ? 1f : Mathf.Clamp01(distance / _explosionRadius);
            float falloffScale = Mathf.Lerp(1f, 1f - _explosionFalloff, t);
            if (_applyDamageAmplifierOnExplosion)
            {
                WeaponDamageAmplifierStatus.Apply(damageable, _damageAmplifierMultiplier, _damageAmplifierDuration);
                if (!_replaceExplosionVfx)
                    WeaponUpgradeVfx.SpawnTargetPulse(targetTransform, AmplifierVfxColor, 0.45f, "VULN");
            }
            int finalDamage = ResolveDamage(damageable, hits[i], falloffScale);
            int healthBefore = GetRemainingHealth(damageable);
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
            {
                EnemyKnockbackReceiver.TryApply(damageable, transform.position, ResolveKnockback(finalDamage, falloffScale));
                totalDamage += finalDamage;
                anyDamageApplied = true;
                anyKill |= healthBefore > 0 && GetRemainingHealth(damageable) <= 0;
                feedbackDamageable ??= damageable;
                feedbackCollider ??= hits[i];
            }
        }

        ApplyFragmentConeDamage();
        EmitExplosionFeedback(_detonationCollider != null ? _detonationCollider : feedbackCollider, feedbackDamageable, totalDamage, anyDamageApplied, anyKill);
        SpawnExplosionCluster();
    }

    private void EmitExplosionFeedback(
        Collider hitCollider,
        IDamageable damageable,
        int totalDamage,
        bool damageApplied,
        bool kill)
    {
        if (_feedbackSink == null || _feedbackImpactEmitted)
            return;

        Transform target = damageable is Component component
            ? component.transform
            : hitCollider != null ? hitCollider.transform : null;
        ImpactSurfaceType surface = ImpactSurfaceResolver.Resolve(hitCollider, damageable);
        WeaponFeedbackContext impact = _feedbackTemplate.WithImpact(
            transform.position,
            -_direction,
            totalDamage,
            _useWeaponDamageContext && _weaponDamageContext.IsCritical,
            false,
            kill,
            target,
            WeaponEnemyClassifier.GetKind(target),
            surface);
        if (_useFragmentCone)
            impact = impact.WithDirection(GetFragmentForward());

        if (_feedbackImpactCueOverride != WeaponPresentationCue.None)
        {
            WeaponPresentationContext explicitImpact = new(
                _feedbackImpactCueOverride,
                impact.Weapon,
                impact.ImpactPosition,
                impact.Direction,
                impact.EventIntensity,
                impact.Target,
                impact.IsAbilityDamage,
                impact.IsCritical,
                impact.IsWeakPoint,
                mode: impact.Mode,
                upgradePath: impact.UpgradePath,
                weaponLevel: impact.WeaponLevel,
                normalizedHeat: impact.NormalizedHeat,
                impactNormal: impact.ImpactNormal,
                damageAmount: impact.DamageAmount,
                isKill: impact.IsKill,
                targetClass: impact.TargetClass,
                surfaceType: impact.SurfaceType,
                explosionRadius: impact.ExplosionRadius);
            _feedbackSink.Emit(in explicitImpact);
        }
        else
            _feedbackSink.OnProjectileImpact(in impact);
        if (damageApplied)
            _feedbackSink.OnDamageConfirmed(in impact);
        if (_applyDamageAmplifierOnExplosion && damageApplied)
            _feedbackSink.OnStatusApplied(in impact);
        _feedbackImpactEmitted = true;
    }

    private void ApplyFragmentConeDamage()
    {
        if (!_useFragmentCone)
            return;

        _fragmentConeDamageables.Clear();
        Vector3 forward = GetFragmentForward();
        Collider[] hits = Physics.OverlapSphere(transform.position, _fragmentConeRange);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = GetAreaDamageable(hits[i]);
            if (damageable == null)
                continue;

            Vector3 conePoint = GetConeTestPoint(hits[i], forward);
            Vector3 toTarget = conePoint - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
                continue;

            float angle = Vector3.Angle(forward.normalized, toTarget.normalized);
            if (angle > _fragmentConeAngle * 0.5f)
                continue;

            if (!_fragmentConeDamageables.Add(damageable))
                continue;

            int damage = ResolveDamage(damageable, hits[i], _fragmentDamageScale);
            WeaponDamageApplier.TryApplyDamage(damageable, damage);
        }
    }

    private static IDamageable GetAreaDamageable(Collider hitCollider)
    {
        if (hitCollider == null)
            return null;

        if (hitCollider.transform.parent != null)
        {
            IDamageable parentDamageable = hitCollider.transform.parent.GetComponentInParent<IDamageable>();
            if (parentDamageable != null)
                return parentDamageable;
        }

        return hitCollider.GetComponentInParent<IDamageable>();
    }

    private static Transform GetDamageableTransform(IDamageable damageable, Collider fallbackCollider)
    {
        if (damageable is Component component)
            return component.transform;

        return fallbackCollider != null ? fallbackCollider.transform : null;
    }

    private Vector3 GetConeTestPoint(Collider hitCollider, Vector3 forward)
    {
        if (hitCollider == null)
            return transform.position;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = Vector3.forward;
        else
            flatForward.Normalize();

        Vector3 boundsCenter = hitCollider.bounds.center;
        Vector3 toCenter = boundsCenter - transform.position;
        toCenter.y = 0f;
        float axisDistance = Mathf.Clamp(Vector3.Dot(toCenter, flatForward), 0f, _fragmentConeRange);
        Vector3 closestAxisPoint = transform.position + flatForward * axisDistance;
        closestAxisPoint.y = boundsCenter.y;

        Vector3 closestPoint = hitCollider.ClosestPoint(closestAxisPoint);
        return (closestPoint - transform.position).sqrMagnitude > 0.000001f
            ? closestPoint
            : hitCollider.bounds.center;
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

    private static Transform GetDamageTarget(IDamageable damageable, Collider hitCollider)
    {
        if (damageable is Component component)
            return component.transform;

        return hitCollider != null ? hitCollider.transform : null;
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

    private void EmitPresentationImpact(
        Collider hitCollider,
        IDamageable damageable,
        bool worldImpact,
        Vector3 impactPosition)
    {
        if (_presentationSink == null || _presentationImpactEmitted)
            return;

        bool weakPoint = !worldImpact &&
            _presentationAllowWeakPoint &&
            IsWeakPointCollider(hitCollider);
        bool critical = !worldImpact &&
            _useWeaponDamageContext &&
            _weaponDamageContext.IsCritical;

        WeaponPresentationCue cue = weakPoint && _weakPointImpactCue != WeaponPresentationCue.None
            ? _weakPointImpactCue
            : critical && _criticalImpactCue != WeaponPresentationCue.None
                ? _criticalImpactCue
                : _impactCue;
        if (cue == WeaponPresentationCue.None)
            return;

        Transform target = damageable is Component component
            ? component.transform
            : hitCollider != null ? hitCollider.transform : null;
        WeaponPresentationContext context = new(
            cue,
            _presentationWeapon,
            impactPosition,
            _direction,
            target: target,
            isAbility: _presentationIsAbility,
            isCritical: critical,
            isWeakPoint: weakPoint);
        _presentationSink.Emit(in context);
        _presentationImpactEmitted = true;
    }

    private void EmitFeedbackImpact(
        Collider hitCollider,
        IDamageable damageable,
        bool worldImpact,
        Vector3 impactPosition,
        int damageAmount,
        bool damageApplied,
        bool kill)
    {
        if (_feedbackSink == null || _feedbackImpactEmitted)
            return;

        bool weakPoint = !worldImpact && _feedbackAllowWeakPoint && IsWeakPointCollider(hitCollider);
        bool critical = !worldImpact && _useWeaponDamageContext && _weaponDamageContext.IsCritical;
        Transform target = damageable is Component component
            ? component.transform
            : hitCollider != null ? hitCollider.transform : null;
        WeaponEnemyKind targetClass = worldImpact
            ? WeaponEnemyKind.Normal
            : WeaponEnemyClassifier.GetKind(target);
        ImpactSurfaceType surface = ImpactSurfaceResolver.Resolve(hitCollider, damageable);
        WeaponFeedbackContext impact = _feedbackTemplate.WithImpact(
            impactPosition,
            -_direction,
            damageAmount,
            critical,
            weakPoint,
            kill,
            target,
            targetClass,
            surface);

        _feedbackSink.OnProjectileImpact(in impact);
        if (damageApplied)
            _feedbackSink.OnDamageConfirmed(in impact);
        _feedbackImpactEmitted = true;
    }

    private Vector3 ResolveImpactPosition(Collider hitCollider)
    {
        return hitCollider != null
            ? hitCollider.ClosestPoint(transform.position)
            : transform.position;
    }

    private void MoveToImpactPoint(Vector3 impactPosition)
    {
        transform.position = impactPosition;
        if (_rigidbody != null)
            _rigidbody.position = impactPosition;
    }

    private static int GetRemainingHealth(IDamageable damageable)
    {
        if (damageable is EnemyHealth enemyHealth)
            return enemyHealth.CurrentHealth;
        if (damageable is WeaponDummyEnemy dummy)
            return dummy.CurrentHealth;
        if (damageable is Component component)
        {
            EnemyHealth parentHealth = component.GetComponentInParent<EnemyHealth>();
            if (parentHealth != null)
                return parentHealth.CurrentHealth;
            WeaponDummyEnemy parentDummy = component.GetComponentInParent<WeaponDummyEnemy>();
            if (parentDummy != null)
                return parentDummy.CurrentHealth;
        }

        return -1;
    }

    private static bool IsWeakPointCollider(Collider collider)
    {
        if (collider == null)
            return false;

        return collider.name.Contains("WeakPoint", System.StringComparison.OrdinalIgnoreCase)
            || collider.transform.name.Contains("WeakPoint", System.StringComparison.OrdinalIgnoreCase);
    }

    private void SpawnExplosionCluster()
    {
        if (!_useExplosionCluster || _clusterPool == null || _clusterProjectileCount <= 0)
            return;

        if (!_replaceExplosionVfx)
            WeaponUpgradeVfx.SpawnRing(transform.position, _explosionRadius * 1.25f, ClusterVfxColor, 0.55f, 1.4f, "CLUSTER");
        for (int i = 0; i < _clusterProjectileCount; i++)
        {
            float angle = i / (float)_clusterProjectileCount * Mathf.PI * 2f;
            Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);
            bool spawned = _clusterPool.TrySpawnExplosiveProjectileWithAmplifier(
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
                _clusterFragmentDamageScale,
                _clusterDamageContext,
                out Projectile clusterProjectile);
            if (!spawned || clusterProjectile == null || _feedbackSink == null)
                continue;

            WeaponFeedbackContext clusterFeedback = _feedbackTemplate.WithExplosionRadius(_clusterExplosionRadius);
            clusterProjectile.ConfigureFeedback(
                _feedbackSink,
                in clusterFeedback,
                allowWeakPoint: false,
                replaceExplosionVfx: _replaceExplosionVfx,
                impactCueOverride: WeaponPresentationCue.RocketFragmentChildImpact);
            _feedbackSink.ConfigureProjectile(
                clusterProjectile,
                ProjectilePresentationArchetypeId.FragmentRocket,
                in clusterFeedback);
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
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(gameObject);
            else
                Destroy(gameObject);
#else
            Destroy(gameObject);
#endif
        }
    }
}
