using UnityEngine;

/// <summary>
/// Zona de hit que reenvía daño a <see cref="EnemyHealth"/> del padre con un multiplicador propio
/// (p. ej. cabeza del Stalker al 120%). No usar el nombre "WeakPoint" si no se quiere el bonus de Head Hunter.
/// </summary>
public sealed class EnemyDamageHitZone : MonoBehaviour, IAuthoritativeDamageable
{
    [SerializeField, Min(0f)] private float _damageMultiplier = 1.2f;

    private EnemyHealth _health;

    private void Awake()
    {
        _health = GetComponentInParent<EnemyHealth>();
    }

    public bool ApplyDamage(int amount)
    {
        DamageRequest request = new(amount, amount, DamageChannel.Direct);
        return ApplyDamage(in request).Applied;
    }

    public DamageApplicationResult ApplyDamage(in DamageRequest request)
    {
        if (_health == null)
            _health = GetComponentInParent<EnemyHealth>();

        return _health != null
            ? _health.ApplyHitZoneDamage(in request, _damageMultiplier)
            : DamageApplicationResult.Rejected(in request, health: 0);
    }
}
