using UnityEngine;

public sealed class FlamethrowerBurnStatus : MonoBehaviour
{
    private const float MaximumTallySegmentDuration = 3.25f;

    private IDamageable _target;
    private int _damagePerTick;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;
    private WeaponStatusKind _statusKind = WeaponStatusKind.Burn;
    private StatusDamageSource _source;
    private WeaponEnemyKind _targetClass;
    private int _statusInstanceId;
    private int _segmentIndex;
    private float _segmentElapsed;
    private bool _hasFeedbackSource;
    private bool _segmentClosureNotified;

    public int StatusInstanceId => _statusInstanceId;
    public int TallySegmentIndex => _segmentIndex;

    // Refreshes burn duration and keeps the strongest active burn damage.
    public void Refresh(IDamageable target, int damagePerTick, float duration, float tickInterval,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn)
    {
        RefreshInternal(target, damagePerTick, duration, tickInterval, statusKind, default);
    }

    public void Refresh(
        IDamageable target,
        int damagePerTick,
        float duration,
        float tickInterval,
        WeaponStatusKind statusKind,
        in StatusDamageSource source)
    {
        RefreshInternal(target, damagePerTick, duration, tickInterval, statusKind, in source);
    }

    private void RefreshInternal(
        IDamageable target,
        int damagePerTick,
        float duration,
        float tickInterval,
        WeaponStatusKind statusKind,
        in StatusDamageSource source)
    {
        bool hasIncomingSource = source.FeedbackSink != null && source.Weapon != null;
        bool sourceChanged = _hasFeedbackSource != hasIncomingSource ||
            (_hasFeedbackSource && (!ReferenceEquals(_source.Weapon, source.Weapon) ||
                                    !ReferenceEquals(_source.FeedbackSink, source.FeedbackSink) ||
                                    _source.StatusKind != statusKind ||
                                    _source.Mode != source.Mode ||
                                    _source.IsAbilityDamage != source.IsAbilityDamage ||
                                    (source.StatusInstanceId > 0 && source.StatusInstanceId != _statusInstanceId)));
        bool kindChanged = _statusKind != statusKind;

        if ((sourceChanged || kindChanged) && _statusInstanceId > 0)
            CloseCurrentSegment();

        if (kindChanged)
            EnemyStatusFeedback.Remove(transform, _statusKind);

        if (sourceChanged || kindChanged || _statusInstanceId <= 0)
        {
            _statusInstanceId = source.StatusInstanceId > 0
                ? source.StatusInstanceId
                : StatusDamageInstanceRuntime.Next();
            _segmentIndex = 0;
            _segmentElapsed = 0f;
            _segmentClosureNotified = false;

            if (sourceChanged || kindChanged)
            {
                _damagePerTick = 0;
                _remainingDuration = 0f;
                _tickTimer = 0f;
            }
        }

        _target = target;
        _statusKind = statusKind;
        _hasFeedbackSource = hasIncomingSource;
        if (hasIncomingSource)
        {
            _source = new StatusDamageSource(
                source.Weapon,
                source.FeedbackSink,
                source.Mode,
                source.UpgradePath,
                source.ReferenceDamage,
                _statusInstanceId,
                statusKind,
                source.IsAbilityDamage);
            _targetClass = WeaponEnemyClassifier.GetKind(transform);
        }
        else
        {
            _source = default;
            _targetClass = WeaponEnemyKind.Normal;
        }

        _damagePerTick = Mathf.Max(_damagePerTick, damagePerTick);
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        _tickInterval = Mathf.Max(0.01f, tickInterval);

        if (_tickTimer <= 0f)
            _tickTimer = _tickInterval;

        EnemyStatusFeedback.ApplyOrRefresh(transform, _statusKind, _remainingDuration,
            Mathf.Clamp01(_damagePerTick / 20f));
    }

    // Applies repeated fire damage until the status expires or the target disappears.
    private void Update() => Tick(Time.deltaTime);

    /// <summary>Advances status damage using caller-provided time for deterministic simulation and tests.</summary>
    public void Tick(float deltaTime)
    {
        if (_target == null)
        {
            RemoveSelf();
            return;
        }

        float delta = Mathf.Max(0f, deltaTime);
        _remainingDuration -= delta;
        _tickTimer -= delta;
        _segmentElapsed += delta;

        while (_remainingDuration > 0f && _segmentElapsed >= MaximumTallySegmentDuration)
        {
            CloseCurrentSegment();
            _segmentIndex++;
            _segmentElapsed -= MaximumTallySegmentDuration;
            _segmentClosureNotified = false;
        }

        while (_tickTimer <= 0f && _remainingDuration > 0f)
        {
            Vector3 impactPosition = transform.position;
            DamageApplicationResult result = WeaponDamageApplier.ApplyDamage(
                _target,
                _damagePerTick,
                DamageChannel.Status,
                _statusKind);
            if (result.AppliedDamage > 0)
            {
                EnemyStatusFeedback.Pulse(transform, _statusKind, 0.75f);
                EmitDamageFeedback(in result, impactPosition);
            }
            _tickTimer += _tickInterval;

            if (result.Killed)
            {
                _remainingDuration = 0f;
                break;
            }
        }

        if (_remainingDuration <= 0f)
            RemoveSelf();
    }

    private void OnDisable() => StopStatusAndClear();

    private void OnDestroy() => StopStatusAndClear();

    private void EmitDamageFeedback(in DamageApplicationResult result, Vector3 impactPosition)
    {
        if (!_hasFeedbackSource || !_source.IsValid || !result.IsAuthoritative || result.AppliedDamage <= 0)
            return;

        WeaponFeedbackContext feedback = new(
            _source.Weapon,
            _source.Mode,
            normalizedHeat: 0f,
            origin: impactPosition,
            direction: Vector3.up,
            impactPosition: impactPosition,
            impactNormal: Vector3.down,
            damageAmount: result.AppliedDamage,
            isKill: result.Killed,
            isAbilityDamage: _source.IsAbilityDamage,
            targetClass: _targetClass,
            surfaceType: ImpactSurfaceType.EnemyOrganic,
            eventIntensity: 1f,
            target: transform,
            anchor: transform,
            referenceDamage: _source.ReferenceDamage,
            damageKind: _source.DamageKind,
            statusInstanceId: _statusInstanceId,
            statusKind: _statusKind,
            segmentIndex: _segmentIndex);
        _source.FeedbackSink.OnDamageConfirmed(in feedback);
    }

    private void RemoveSelf()
    {
        StopStatusAndClear();
        if (Application.isPlaying)
            Destroy(this);
        else
            DestroyImmediate(this);
    }

    private void CloseCurrentSegment()
    {
        if (_segmentClosureNotified || _statusInstanceId <= 0 || !_hasFeedbackSource)
            return;
        if (_source.FeedbackSink is not ICombatTextStatusLifecycleSink lifecycleSink)
            return;

        _segmentClosureNotified = true;
        lifecycleSink.OnStatusSegmentClosed(
            transform,
            _statusKind,
            _statusInstanceId,
            _segmentIndex);
    }

    private void StopStatusAndClear()
    {
        bool hadStatus = _statusInstanceId > 0 || _target != null || _remainingDuration > 0f;
        WeaponStatusKind endingKind = _statusKind;
        CloseCurrentSegment();
        if (hadStatus)
            EnemyStatusFeedback.Remove(transform, endingKind);

        _target = null;
        _damagePerTick = 0;
        _remainingDuration = 0f;
        _tickInterval = 0f;
        _tickTimer = 0f;
        _statusKind = WeaponStatusKind.Burn;
        _source = default;
        _targetClass = WeaponEnemyKind.Normal;
        _statusInstanceId = 0;
        _segmentIndex = 0;
        _segmentElapsed = 0f;
        _hasFeedbackSource = false;
        _segmentClosureNotified = false;
    }
}
