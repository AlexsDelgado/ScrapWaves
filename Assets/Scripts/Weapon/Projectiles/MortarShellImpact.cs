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
    private static readonly Dictionary<int, List<MortarShellImpact>> s_authoredPools = new();
    private static Transform s_poolRoot;

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
    private readonly RaycastHit[] _predictionHits = new RaycastHit[32];
    private readonly RaycastHit[] _presentationSupportHits = new RaycastHit[16];
    private readonly List<IDamageable> _damagedThisExplosion = new();
    private MortarUpgradePayload _payload = MortarUpgradePayload.None;
    private bool _useWeaponDamageContext;
    private WeaponDamageContext _weaponDamageContext;
    private bool _useGrapeshotVfx;
    private float _grapeshotAirburstNormalizedTime = -1f;
    private int _remainingRepeatExplosions;
    private float _repeatExplosionTimer;
    private bool _detonated;
    private MortarShellVfx _authoredVfx;
    private GameObject _authoredPrefab;
    private int _poolCapacity;
    private bool _usesAuthoredPool;
    private bool _detailedPresentation = true;
    private bool _showLandingIndicator = true;
    private IWeaponPresentationSink _presentationSink = NullWeaponPresentationSink.Instance;
    private IWeaponFeedbackSink _feedbackSink = NullWeaponPresentationSink.Instance;
    private WeaponFeedbackContext _feedbackTemplate;
    private bool _hasFeedbackTemplate;
    private Vector3 _impactNormal = Vector3.up;
    private Collider _impactCollider;
    private Vector3 _presentationImpactPoint;
    private Vector3 _presentationImpactNormal = Vector3.up;
    private bool _hasPresentationImpactSurface;
    private int _repeatExplosionIndex;
    private bool _completesDamageSequenceContributor;
    private bool _damageSequenceContributorCompleted;

    public bool HasPredictedPresentationCollision { get; private set; }
    public Vector3 PredictedPresentationCollisionPoint { get; private set; }

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
        return LaunchAuthored(
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
            useGrapeshotVfx,
            damageContext,
            null,
            0,
            true,
            true,
            null,
            default);
    }

    public static MortarShellImpact LaunchAuthored(
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
        WeaponDamageContext damageContext,
        GameObject authoredPrefab,
        int poolCapacity,
        bool detailedPresentation,
        bool showLandingIndicator,
        IWeaponPresentationSink presentationSink,
        WeaponFeedbackContext feedbackTemplate,
        bool completesSequenceContributor = false)
    {
        MortarShellImpact shell = Acquire(authoredPrefab, poolCapacity);
        shell._payload = payload;
        shell._useGrapeshotVfx = useGrapeshotVfx || payload.UseGrapeshot;
        shell._weaponDamageContext = damageContext;
        shell._useWeaponDamageContext = damageContext.IsValid;
        shell._completesDamageSequenceContributor =
            completesSequenceContributor && ResolveSequenceId(in damageContext, in feedbackTemplate) != 0;
        shell._damageSequenceContributorCompleted = false;
        shell._detailedPresentation = detailedPresentation;
        shell._showLandingIndicator = showLandingIndicator;
        shell._presentationSink = presentationSink ?? NullWeaponPresentationSink.Instance;
        shell._feedbackSink = WeaponFeedbackEmitter.Resolve(shell._presentationSink);
        shell._feedbackTemplate = feedbackTemplate;
        shell._hasFeedbackTemplate = feedbackTemplate.Weapon != null;
        shell.Configure(start, target, travelTime, arcHeight, damage, explosionRadius, falloff, knockback, collisionRadius, ignoredRoot);
        return shell;
    }

    public static void Prewarm(GameObject authoredPrefab, int count, int poolCapacity)
    {
        if (authoredPrefab == null || count <= 0)
            return;
        int key = authoredPrefab.GetInstanceID();
        List<MortarShellImpact> pool = GetOrCreatePool(key);
        PrunePool(pool);
        int targetCount = Mathf.Min(Mathf.Max(0, count), Mathf.Max(1, poolCapacity));
        while (pool.Count < targetCount)
        {
            MortarShellImpact shell = CreateAuthored(authoredPrefab, poolCapacity);
            pool.Add(shell);
            shell.ReturnToPool();
        }
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
        _elapsed = 0f;
        _remainingRepeatExplosions = 0;
        _repeatExplosionTimer = 0f;
        _repeatExplosionIndex = 0;
        _detonated = false;
        _impactNormal = Vector3.up;
        _impactCollider = null;
        _presentationImpactPoint = default;
        _presentationImpactNormal = Vector3.up;
        _hasPresentationImpactSurface = false;
        HasPredictedPresentationCollision = false;
        PredictedPresentationCollisionPoint = default;
        _grapeshotAirburstNormalizedTime = _payload.UseGrapeshot
            ? CalculateGrapeshotAirburstNormalizedTime()
            : -1f;
        transform.position = _start;
        _authoredVfx ??= GetComponent<MortarShellVfx>();
        if (_authoredVfx != null)
        {
            MortarShellVisualStyle style = UsesRepeatExplosionVisuals()
                ? MortarShellVisualStyle.MultiCharged
                : UsesGrapeshotVisuals()
                    ? MortarShellVisualStyle.Grapeshot
                    : MortarShellVisualStyle.Base;
            RaycastHit predictedHit = default;
            bool showPredictedLanding = _showLandingIndicator
                && !_payload.UseGrapeshot
                && MortarTrajectory.TryPredictTerrainCollision(
                    _start,
                    _target,
                    _arcHeight,
                    _travelTime,
                    _collisionRadius,
                    _ignoredRoot,
                    _predictionHits,
                    out predictedHit);
            Vector3 presentationTarget = _target;
            Vector3 presentationNormal = Vector3.up;
            if (showPredictedLanding)
            {
                MortarPresentationSurface.Resolve(
                    predictedHit,
                    _explosionRadius,
                    _ignoredRoot,
                    _presentationSupportHits,
                    out presentationTarget,
                    out presentationNormal);
            }
            HasPredictedPresentationCollision = showPredictedLanding;
            PredictedPresentationCollisionPoint = showPredictedLanding ? presentationTarget : default;
            _authoredVfx.Configure(
                style,
                presentationTarget,
                presentationNormal,
                _explosionRadius,
                _travelTime,
                _feedbackTemplate.NormalizedHeat,
                _detailedPresentation,
                showPredictedLanding);
        }
        else
        {
            BuildLineRenderer();
            BuildShellVisual();
            UpdateArcVisual();
        }
    }

    private void Update()
    {
        TouchDamageSequence();

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
        if (_authoredVfx != null)
            _authoredVfx.UpdateFlight(nextPosition - previousPosition, Mathf.Clamp01(t));
        if (t >= MortarTrajectory.GetMaximumNormalizedTime(_travelTime))
            ReleaseShell();
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
        _impactNormal = closestHit.normal.sqrMagnitude > 0.0001f ? closestHit.normal.normalized : Vector3.up;
        _impactCollider = closestHit.collider;
        MortarPresentationSurface.Resolve(
            closestHit,
            _explosionRadius,
            _ignoredRoot,
            _presentationSupportHits,
            out _presentationImpactPoint,
            out _presentationImpactNormal);
        _hasPresentationImpactSurface = true;
        _authoredVfx?.SetImpactPoint(_presentationImpactPoint, _presentationImpactNormal);
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
        // Compatibility fallback for isolated tests or data without a production
        // profile. Normal gameplay always enters through the authored shell pool.
        GameObject visual = new("Mortar Shell Visual");
        _shellVisual = visual;
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * Mathf.Max(0.12f, _collisionRadius * 2f);
        MeshFilter filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
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
        Vector3 presentationPoint = _hasPresentationImpactSurface
            ? _presentationImpactPoint
            : explosionCenter;
        Vector3 presentationNormal = _hasPresentationImpactSurface
            ? _presentationImpactNormal
            : _payload.UseGrapeshot ? Vector3.up : _impactNormal;
        _authoredVfx?.SetImpactPoint(presentationPoint, presentationNormal);
        HideFlightVisuals(keepShellVisual: !_payload.UseGrapeshot && _payload.RepeatExplosionCount > 1);
        if (_payload.UseGrapeshot)
        {
            EmitImpactCue(WeaponPresentationCue.MortarGrapeshotAirburst, explosionCenter, Mathf.Max(0.8f, _explosionRadius), 1.15f);
            SpawnGrapeshot(explosionCenter);
        }
        else
            ApplyExplosionDamageAt(explosionCenter);

        if (!_detonated)
        {
            _detonated = true;
            _remainingRepeatExplosions = Mathf.Max(1, _payload.RepeatExplosionCount) - 1;
            _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
            if (_remainingRepeatExplosions > 0)
            {
                if (_authoredVfx == null)
                    WeaponUpgradeVfx.SpawnRing(explosionCenter, _explosionRadius * 1.25f, RepeatExplosionVfxColor, _repeatExplosionTimer, 1.5f, null);
                _authoredVfx?.BeginRepeatCountdown(_repeatExplosionTimer, _remainingRepeatExplosions);
            }
        }

        if (_remainingRepeatExplosions <= 0)
            ReleaseShell();
    }

    private void TickRepeatExplosions()
    {
        if (_remainingRepeatExplosions <= 0)
        {
            ReleaseShell();
            return;
        }

        _repeatExplosionTimer -= Time.deltaTime;
        _authoredVfx?.UpdateRepeatCountdown(_repeatExplosionTimer);
        if (_repeatExplosionTimer > 0f)
            return;

        _remainingRepeatExplosions--;
        _repeatExplosionIndex++;
        _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
        ApplyExplosionDamageAt(_target);
        _authoredVfx?.PulseRepeat(_remainingRepeatExplosions);
    }

    private void ApplyExplosionDamageAt(Vector3 explosionCenter)
    {
        WeaponPresentationCue impactCue = UsesGrapeshotVisuals()
            ? WeaponPresentationCue.MortarGrapeshotImpact
            : UsesRepeatExplosionVisuals()
                ? (_repeatExplosionIndex > 0
                    ? WeaponPresentationCue.MortarMultiChargedRepeat
                    : WeaponPresentationCue.MortarMultiChargedImpact)
                : WeaponPresentationCue.MortarImpact;
        bool authoredImpact = EmitImpactCue(
            impactCue,
            explosionCenter,
            _explosionRadius,
            UsesRepeatExplosionVisuals() ? 1.2f + _repeatExplosionIndex * 0.15f : 1f);

        if (!authoredImpact)
        {
            if (UsesGrapeshotVisuals())
                ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius, GrapeshotVfxColor);
            else if (UsesRepeatExplosionVisuals())
                ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius, RepeatExplosionVfxColor);
            else
                ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius);
        }

        if (_payload.RepeatExplosionCount > 1 && _authoredVfx == null)
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
            DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(damageable, finalDamage);
            if (result.Applied)
            {
                EnemyKnockbackReceiver.TryApply(damageable, explosionCenter, ResolveKnockback(finalDamage, falloffScale));
                EmitDamageFeedback(damageable, hits[i], explosionCenter, in result);
            }
        }
    }

    private void SpawnGrapeshot(Vector3 center)
    {
        if (!_payload.UseGrapeshot || _payload.GrapeshotCount <= 0)
            return;

        float grapeshotDamageScale = Mathf.Max(0f, _payload.GrapeshotDamageScale);
        int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * grapeshotDamageScale));
        WeaponDamageContext grapeshotContext = CreateScaledDamageContext(grapeshotDamageScale, 0.2f);
        float subShellCollisionRadius = Mathf.Max(0.04f, _collisionRadius * 0.75f);
        float subShellExplosionRadius = Mathf.Max(0.08f, subShellCollisionRadius * 1.25f);
        float subShellTravelTime = Mathf.Clamp(_travelTime * 0.45f, 0.22f, 0.6f);
        float spreadRadius = Mathf.Max(0.75f, _explosionRadius * 1.5f);
        float fallDistance = Mathf.Max(1.5f, _explosionRadius * 1.2f);

        WeaponUpgradeVfx.SpawnRing(center, Mathf.Max(0.35f, _explosionRadius * 0.45f), GrapeshotVfxColor, 0.35f, 1.1f, null);

        for (int i = 0; i < _payload.GrapeshotCount; i++)
        {
            Vector2 offset = Random.insideUnitCircle * spreadRadius;
            Vector3 target = new(center.x + offset.x, center.y - fallDistance, center.z + offset.y);

            WeaponFeedbackContext childFeedback = new(
                _feedbackTemplate.Weapon,
                _feedbackTemplate.Mode,
                _feedbackTemplate.NormalizedHeat,
                center,
                target - center,
                impactPosition: target,
                isAbilityDamage: _feedbackTemplate.IsAbilityDamage,
                explosionRadius: subShellExplosionRadius,
                eventIntensity: 0.55f,
                referenceDamage: grapeshotContext.ReferenceDamage,
                actionSequenceId: grapeshotContext.ActionSequenceId,
                damageKind: DamageFeedbackKind.Fragment);
            MortarShellImpact child = MortarShellImpact.LaunchAuthored(
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
                grapeshotContext,
                _authoredPrefab,
                _poolCapacity,
                _detailedPresentation && i < 6,
                showLandingIndicator: false,
                _presentationSink,
                childFeedback,
                completesSequenceContributor: grapeshotContext.ActionSequenceId != 0);
            if (child == null && grapeshotContext.ActionSequenceId != 0)
                DamageFeedbackSequenceRuntime.CompleteContributor(grapeshotContext.ActionSequenceId);
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
        return _weaponDamageContext.WithScales(
            damageScale,
            knockbackScale,
            DamageFeedbackKind.Fragment);
    }

    private static Transform GetDamageTarget(IDamageable damageable, Collider hitCollider)
    {
        if (damageable is Component component)
            return component.transform;

        return hitCollider != null ? hitCollider.transform : null;
    }

    private bool UsesGrapeshotVisuals() => _useGrapeshotVfx || _payload.UseGrapeshot;
    private bool UsesRepeatExplosionVisuals() => _payload.RepeatExplosionCount > 1;

    private void HideFlightVisuals(bool keepShellVisual = false)
    {
        if (_authoredVfx != null)
        {
            _authoredVfx.ShowImpact(keepShellVisual);
            return;
        }

        if (_line != null)
            _line.enabled = false;

        if (_shellVisual != null)
        {
            if (keepShellVisual)
            {
                transform.position = _target;
                _shellVisual.transform.localPosition = Vector3.zero;
                _shellVisual.SetActive(true);
            }
            else
            {
                _shellVisual.SetActive(false);
            }
        }
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

    private bool EmitImpactCue(
        WeaponPresentationCue cue,
        Vector3 position,
        float explosionRadius,
        float intensity)
    {
        if (!_hasFeedbackTemplate || _presentationSink == null || cue == WeaponPresentationCue.None)
            return false;

        Vector3 direction = _target - _start;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.down;
        Vector3 presentationPosition = _hasPresentationImpactSurface
            ? _presentationImpactPoint
            : position;
        Vector3 presentationNormal = _hasPresentationImpactSurface
            ? _presentationImpactNormal
            : _payload.UseGrapeshot ? Vector3.up : _impactNormal;
        WeaponPresentationContext context = new(
            cue,
            _feedbackTemplate.Weapon,
            presentationPosition,
            direction,
            intensity,
            isAbility: _feedbackTemplate.IsAbilityDamage,
            mode: _feedbackTemplate.Mode,
            upgradePath: _feedbackTemplate.UpgradePath,
            weaponLevel: _feedbackTemplate.WeaponLevel,
            normalizedHeat: _feedbackTemplate.NormalizedHeat,
            impactNormal: presentationNormal,
            damageAmount: _damage,
            surfaceType: ImpactSurfaceResolver.Resolve(_impactCollider, null),
            explosionRadius: explosionRadius);
        _presentationSink.Emit(in context);
        return true;
    }

    private void EmitDamageFeedback(
        IDamageable damageable,
        Collider hitCollider,
        Vector3 explosionCenter,
        in DamageApplicationResult result)
    {
        if (!_hasFeedbackTemplate || !result.IsAuthoritative || result.AppliedDamage <= 0)
            return;
        Transform target = damageable is Component component
            ? component.transform
            : hitCollider != null ? hitCollider.transform : null;
        Vector3 impactPosition = hitCollider != null
            ? hitCollider.ClosestPoint(explosionCenter)
            : target != null ? target.position : explosionCenter;
        WeaponFeedbackContext feedback = _feedbackTemplate.WithImpact(
            impactPosition,
            impactPosition - explosionCenter,
            result.AppliedDamage,
            _useWeaponDamageContext && _weaponDamageContext.IsCritical,
            false,
            result.Killed,
            target,
            WeaponEnemyClassifier.GetKind(target),
            ImpactSurfaceResolver.Resolve(hitCollider, damageable));
        if (_useWeaponDamageContext)
        {
            feedback = feedback.WithDamageMetadata(
                _weaponDamageContext.ReferenceDamage,
                _weaponDamageContext.ActionSequenceId,
                _weaponDamageContext.DamageKind,
                _weaponDamageContext.StatusInstanceId,
                _weaponDamageContext.StatusKind,
                _weaponDamageContext.SegmentIndex);
        }
        _feedbackSink.OnDamageConfirmed(in feedback);
    }

    private static int GetRemainingHealth(IDamageable damageable)
    {
        if (damageable is EnemyHealth enemyHealth)
            return enemyHealth.CurrentHealth;
        if (damageable is WeaponDummyEnemy dummy)
            return dummy.CurrentHealth;
        if (damageable is Component component)
        {
            EnemyHealth parent = component.GetComponentInParent<EnemyHealth>();
            if (parent != null)
                return parent.CurrentHealth;
            WeaponDummyEnemy parentDummy = component.GetComponentInParent<WeaponDummyEnemy>();
            if (parentDummy != null)
                return parentDummy.CurrentHealth;
        }
        return -1;
    }

    private static MortarShellImpact Acquire(GameObject authoredPrefab, int poolCapacity)
    {
        if (authoredPrefab == null)
        {
            GameObject go = new("MortarShellImpact");
            return go.AddComponent<MortarShellImpact>();
        }

        int key = authoredPrefab.GetInstanceID();
        List<MortarShellImpact> pool = GetOrCreatePool(key);
        PrunePool(pool);
        for (int i = 0; i < pool.Count; i++)
        {
            MortarShellImpact candidate = pool[i];
            if (candidate == null || candidate.gameObject.activeSelf)
                continue;
            candidate.transform.SetParent(null, false);
            candidate.gameObject.SetActive(true);
            candidate._usesAuthoredPool = true;
            candidate._authoredPrefab = authoredPrefab;
            candidate._poolCapacity = Mathf.Max(1, poolCapacity);
            return candidate;
        }

        MortarShellImpact created = CreateAuthored(authoredPrefab, poolCapacity);
        pool.Add(created);
        return created;
    }

    private static MortarShellImpact CreateAuthored(GameObject authoredPrefab, int poolCapacity)
    {
        GameObject go = Instantiate(authoredPrefab);
        go.name = "MortarShellImpact_Authored";
        MortarShellImpact shell = go.GetComponent<MortarShellImpact>();
        if (shell == null)
            shell = go.AddComponent<MortarShellImpact>();
        shell._usesAuthoredPool = true;
        shell._authoredPrefab = authoredPrefab;
        shell._poolCapacity = Mathf.Max(1, poolCapacity);
        shell._authoredVfx = go.GetComponent<MortarShellVfx>();
        return shell;
    }

    private static List<MortarShellImpact> GetOrCreatePool(int key)
    {
        if (!s_authoredPools.TryGetValue(key, out List<MortarShellImpact> pool))
        {
            pool = new List<MortarShellImpact>();
            s_authoredPools.Add(key, pool);
        }
        return pool;
    }

    private static void PrunePool(List<MortarShellImpact> pool)
    {
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            if (pool[i] == null)
                pool.RemoveAt(i);
        }
    }

    private void ReleaseShell()
    {
        CompleteDamageSequenceContributor();

        if (!_usesAuthoredPool || _authoredPrefab == null)
        {
            DestroyUnityObject(gameObject);
            return;
        }
        ReturnToPool();
    }

    private void ReturnToPool()
    {
        CompleteDamageSequenceContributor();
        _authoredVfx?.ResetVisuals();
        _damagedThisExplosion.Clear();
        _presentationSink = NullWeaponPresentationSink.Instance;
        _feedbackSink = NullWeaponPresentationSink.Instance;
        _hasFeedbackTemplate = false;
        _completesDamageSequenceContributor = false;
        _damageSequenceContributorCompleted = false;
        int key = _authoredPrefab != null ? _authoredPrefab.GetInstanceID() : 0;
        if (key != 0 && s_authoredPools.TryGetValue(key, out List<MortarShellImpact> pool))
        {
            PrunePool(pool);
            if (pool.Count > Mathf.Max(1, _poolCapacity))
            {
                pool.Remove(this);
                DestroyUnityObject(gameObject);
                return;
            }
        }
        EnsurePoolRoot();
        transform.SetParent(s_poolRoot, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        gameObject.SetActive(false);
    }

    private void TouchDamageSequence()
    {
        if (!_completesDamageSequenceContributor || _damageSequenceContributorCompleted)
            return;

        int sequenceId = ResolveSequenceId(in _weaponDamageContext, in _feedbackTemplate);
        if (sequenceId != 0)
            DamageFeedbackSequenceRuntime.TouchSequence(sequenceId);
    }

    private void CompleteDamageSequenceContributor()
    {
        if (!_completesDamageSequenceContributor || _damageSequenceContributorCompleted)
            return;

        int sequenceId = ResolveSequenceId(in _weaponDamageContext, in _feedbackTemplate);
        if (sequenceId != 0)
            DamageFeedbackSequenceRuntime.CompleteContributor(sequenceId);
        _damageSequenceContributorCompleted = true;
    }

    private static int ResolveSequenceId(
        in WeaponDamageContext damageContext,
        in WeaponFeedbackContext feedbackTemplate)
    {
        return damageContext.ActionSequenceId != 0
            ? damageContext.ActionSequenceId
            : feedbackTemplate.ActionSequenceId;
    }

    private void OnDisable() => CompleteDamageSequenceContributor();
    private void OnDestroy() => CompleteDamageSequenceContributor();

    private static void EnsurePoolRoot()
    {
        if (s_poolRoot != null)
            return;
        GameObject root = new("[MortarShellPool]") { hideFlags = HideFlags.HideAndDontSave };
        s_poolRoot = root.transform;
    }

    private void OnDrawGizmos()
    {
        if (_explosionRadius <= 0f)
            return;

        Gizmos.color = new Color(1f, 0.62f, 0.05f, 0.8f);
        Gizmos.DrawWireSphere(_target, _explosionRadius);
    }
}
