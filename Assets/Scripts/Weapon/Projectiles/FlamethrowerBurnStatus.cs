using UnityEngine;

public sealed class FlamethrowerBurnStatus : MonoBehaviour
{
    private IDamageable _target;
    private int _damagePerTick;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;

    // Refreshes burn duration and keeps the strongest active burn damage.
    public void Refresh(IDamageable target, int damagePerTick, float duration, float tickInterval)
    {
        _target = target;
        _damagePerTick = Mathf.Max(_damagePerTick, damagePerTick);
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        _tickInterval = Mathf.Max(0.01f, tickInterval);

        if (_tickTimer <= 0f)
            _tickTimer = _tickInterval;
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
            if (_target is EnemyHealth enemyHealth)
                enemyHealth.ApplyDotDamage(_damagePerTick);
            else
                _target.ApplyDamage(_damagePerTick);

            _tickTimer += _tickInterval;
        }

        if (_remainingDuration <= 0f)
            Destroy(this);
    }
}
