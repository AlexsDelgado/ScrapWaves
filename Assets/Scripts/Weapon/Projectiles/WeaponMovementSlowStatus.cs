using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponMovementSlowStatus : MonoBehaviour
{
    private float _speedMultiplier = 1f;
    private float _remainingDuration;
    private int _rampStacks;

    public float SpeedMultiplier => _remainingDuration > 0f ? Mathf.Clamp(_speedMultiplier, 0f, 1f) : 1f;

    public void Refresh(float speedMultiplier, float duration, string label)
    {
        if (duration <= 0f)
            return;
        if (_remainingDuration <= 0f)
        {
            _speedMultiplier = 1f;
            _rampStacks = 0;
        }
        _speedMultiplier = Mathf.Min(_speedMultiplier, Mathf.Clamp01(speedMultiplier));
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        TryApplyDummyStatus(label, duration);
        EnemyStatusFeedback.ApplyOrRefresh(transform, WeaponStatusKind.Slow, _remainingDuration, 1f - _speedMultiplier);
    }

    public void RefreshRamp(float startMultiplier, float endMultiplier, int ticksToFull, float duration, string label)
    {
        if (duration <= 0f)
            return;
        if (_remainingDuration <= 0f)
        {
            _speedMultiplier = 1f;
            _rampStacks = 0;
        }
        int maxTicks = Mathf.Max(1, ticksToFull);
        _rampStacks = Mathf.Clamp(_rampStacks + 1, 1, maxTicks);
        float t = maxTicks <= 1 ? 1f : (_rampStacks - 1) / (float)(maxTicks - 1);
        _speedMultiplier = Mathf.Min(_speedMultiplier, Mathf.Clamp01(Mathf.Lerp(startMultiplier, endMultiplier, t)));
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        TryApplyDummyStatus(label, duration);
        EnemyStatusFeedback.ApplyOrRefresh(transform, WeaponStatusKind.Slow, _remainingDuration, 1f - _speedMultiplier);
    }

    public static void Apply(Transform target, float speedMultiplier, float duration, string label)
    {
        WeaponMovementSlowStatus status = GetOrCreate(target);
        if (status != null)
            status.Refresh(speedMultiplier, duration, label);
    }

    public static void ApplyRamp(Transform target, float startMultiplier, float endMultiplier, int ticksToFull, float duration, string label)
    {
        WeaponMovementSlowStatus status = GetOrCreate(target);
        if (status != null)
            status.RefreshRamp(startMultiplier, endMultiplier, ticksToFull, duration, label);
    }

    public static float GetSpeedMultiplier(Transform target)
    {
        if (target == null)
            return 1f;
        Transform root = ResolveTargetRoot(target);
        WeaponMovementSlowStatus status = root.GetComponent<WeaponMovementSlowStatus>();
        return status != null ? status.SpeedMultiplier : 1f;
    }

    private static WeaponMovementSlowStatus GetOrCreate(Transform target)
    {
        if (target == null)
            return null;
        Transform root = ResolveTargetRoot(target);
        WeaponMovementSlowStatus status = root.GetComponent<WeaponMovementSlowStatus>();
        if (status == null)
            status = root.gameObject.AddComponent<WeaponMovementSlowStatus>();
        return status;
    }

    private static Transform ResolveTargetRoot(Transform target)
    {
        EnemyHealth health = target.GetComponentInParent<EnemyHealth>();
        if (health != null)
            return health.transform;
        WeaponDummyEnemy dummy = target.GetComponentInParent<WeaponDummyEnemy>();
        if (dummy != null)
            return dummy.transform;
        return target.root != null ? target.root : target;
    }

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        if (_remainingDuration <= 0f)
            Destroy(this);
    }

    private void TryApplyDummyStatus(string label, float duration)
    {
        WeaponDummyEnemy dummy = GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
            dummy.ApplyStatus(string.IsNullOrWhiteSpace(label) ? "Slow" : label, duration);
    }

    private void OnDisable() => EnemyStatusFeedback.Remove(transform, WeaponStatusKind.Slow);
    private void OnDestroy() => EnemyStatusFeedback.Remove(transform, WeaponStatusKind.Slow);
}
