using UnityEngine;

public sealed class FlamethrowerBurnStatus : MonoBehaviour
{
    private IDamageable _target;
    private int _damagePerTick;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;
    private WeaponStatusKind _statusKind = WeaponStatusKind.Burn;

    // Refreshes burn duration and keeps the strongest active burn damage.
    public void Refresh(IDamageable target, int damagePerTick, float duration, float tickInterval,
        WeaponStatusKind statusKind = WeaponStatusKind.Burn)
    {
        if (_statusKind != statusKind)
            EnemyStatusFeedback.Remove(transform, _statusKind);
        _target = target;
        _statusKind = statusKind;
        _damagePerTick = Mathf.Max(_damagePerTick, damagePerTick);
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        _tickInterval = Mathf.Max(0.01f, tickInterval);

        if (_tickTimer <= 0f)
            _tickTimer = _tickInterval;

        EnemyStatusFeedback.ApplyOrRefresh(transform, _statusKind, _remainingDuration,
            Mathf.Clamp01(_damagePerTick / 20f));
    }

    // Applies repeated fire damage until the status expires or the target disappears.
    private void Update()
    {
        if (_target == null)
        {
            Destroy(this);
            return;
        }

        _remainingDuration -= Time.deltaTime;
        _tickTimer -= Time.deltaTime;

        while (_tickTimer <= 0f && _remainingDuration > 0f)
        {
            if (WeaponDamageApplier.TryApplyDamage(_target, _damagePerTick))
                EnemyStatusFeedback.Pulse(transform, _statusKind, 0.75f);
            _tickTimer += _tickInterval;
        }

        if (_remainingDuration <= 0f)
            Destroy(this);
    }

    private void OnDisable() => EnemyStatusFeedback.Remove(transform, _statusKind);

    private void OnDestroy() => EnemyStatusFeedback.Remove(transform, _statusKind);
}
