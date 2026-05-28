using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyKnockbackReceiver : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _drag = 14f;
    [SerializeField, Min(0f)] private float _maxPlanarSpeed = 18f;

    private Vector3 _planarVelocity;

    // Applies a horizontal impulse away from the impact point.
    public void ApplyKnockback(Vector3 impactOrigin, float strength)
    {
        if (strength <= 0f)
            return;

        Vector3 direction = transform.position - impactOrigin;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = transform.forward;

        _planarVelocity += direction.normalized * strength;
        _planarVelocity = Vector3.ClampMagnitude(_planarVelocity, _maxPlanarSpeed);
    }

    // Movement controllers consume displacement so knockback does not fight their normal steering.
    public Vector3 ConsumeDisplacement(float deltaTime)
    {
        if (_planarVelocity.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        Vector3 displacement = _planarVelocity * deltaTime;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, Vector3.zero, _drag * deltaTime);
        return displacement;
    }

    // Lazily adds a receiver so existing enemy prefabs do not need manual setup.
    public static void TryApply(IDamageable damageable, Vector3 impactOrigin, float strength)
    {
        if (strength <= 0f || damageable is not Component damageComponent)
            return;

        if (!damageComponent.gameObject.activeInHierarchy)
            return;

        EnemyKnockbackReceiver receiver = damageComponent.GetComponent<EnemyKnockbackReceiver>();
        if (receiver == null)
            receiver = damageComponent.gameObject.AddComponent<EnemyKnockbackReceiver>();

        receiver.ApplyKnockback(impactOrigin, strength);
    }
}
