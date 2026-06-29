using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponMovementFreezeStatus : MonoBehaviour
{
    private SimpleFollow _simpleFollow;
    private EnemyFollow _enemyFollow;
    private bool _simpleWasEnabled;
    private bool _enemyWasEnabled;
    private bool _hasCachedState;
    private float _remainingDuration;

    public void Refresh(float duration)
    {
        if (duration <= 0f)
            return;

        CacheState();
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        SetMovement(false);
        TryApplyDummyStatus(duration);
    }

    public static void Apply(Transform target, float duration)
    {
        if (target == null || duration <= 0f)
            return;

        Transform root = target.root != null ? target.root : target;
        WeaponMovementFreezeStatus status = root.GetComponent<WeaponMovementFreezeStatus>();
        if (status == null)
            status = root.gameObject.AddComponent<WeaponMovementFreezeStatus>();

        status.Refresh(duration);
    }

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        if (_remainingDuration > 0f)
            return;

        SetMovement(true);
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (_hasCachedState)
            SetMovement(true);
    }

    private void CacheState()
    {
        if (_hasCachedState)
            return;

        _simpleFollow = GetComponent<SimpleFollow>();
        _enemyFollow = GetComponent<EnemyFollow>();
        _simpleWasEnabled = _simpleFollow != null && _simpleFollow.enabled;
        _enemyWasEnabled = _enemyFollow != null && _enemyFollow.enabled;
        _hasCachedState = true;
    }

    private void SetMovement(bool enabled)
    {
        if (_simpleFollow != null)
            _simpleFollow.enabled = enabled && _simpleWasEnabled;
        if (_enemyFollow != null)
            _enemyFollow.enabled = enabled && _enemyWasEnabled;
    }

    private void TryApplyDummyStatus(float duration)
    {
        WeaponDummyEnemy dummy = GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
            dummy.ApplyStatus("Freeze", duration);
    }
}
