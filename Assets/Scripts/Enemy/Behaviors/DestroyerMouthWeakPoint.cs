using System;
using UnityEngine;

/// <summary>
/// Punto débil de la boca del Destroyer. Solo activo durante la fase de succión. El GameObject
/// debe llamarse "WeakPoint" (mantiene el bonus Head Hunter del Automatic Cannon, que busca ese
/// string) y su collider debe ser NO trigger para que los raycasts de armas puedan alcanzarlo.
/// Al implementar <see cref="IDamageable"/> en este mismo objeto, el daño se resuelve aquí y no
/// en el <see cref="EnemyHealth"/> del cuerpo (los weapons buscan con GetComponentInParent, que
/// revisa primero el propio objeto golpeado).
/// </summary>
public class DestroyerMouthWeakPoint : MonoBehaviour, IAuthoritativeDamageable
{
    [SerializeField, Min(1)] private int _maxHealth = 80;

    private int _currentHealth;
    private bool _initialized;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => _maxHealth;

    /// <summary>Se dispara una vez al llegar a 0 HP, antes de desactivarse.</summary>
    public event Action OnWeakPointDestroyed;

    private void OnEnable()
    {
        _currentHealth = _maxHealth;
        _initialized = true;
    }

    public bool ApplyDamage(int amount)
    {
        DamageRequest request = new(amount, amount, DamageChannel.Direct);
        return ApplyDamage(in request).Applied;
    }

    public DamageApplicationResult ApplyDamage(in DamageRequest request)
    {
        // EditMode construction does not consistently invoke OnEnable for a component
        // added while its GameObject is inactive. Keep the damage contract authoritative
        // in that case without reviving an already-destroyed, initialized weak point.
        if (!_initialized)
        {
            _currentHealth = _maxHealth;
            _initialized = true;
        }

        int healthBefore = Mathf.Max(0, _currentHealth);
        if (request.ModifiedDamage <= 0 || healthBefore <= 0)
            return DamageApplicationResult.Rejected(in request, healthBefore);

        int healthAfter = Mathf.Max(0, healthBefore - request.ModifiedDamage);
        _currentHealth = healthAfter;
        DamageApplicationResult result = DamageApplicationResult.FromHealthDelta(
            in request,
            healthBefore,
            healthAfter);

        if (result.Killed)
        {
            OnWeakPointDestroyed?.Invoke();
            gameObject.SetActive(false);
        }

        return result;
    }

    /// <summary>Activa el weak point con la vida al máximo (inicio de succión).</summary>
    public void Reactivate()
    {
        gameObject.SetActive(true);
    }

    /// <summary>Desactiva el weak point si seguía activo (fin de succión).</summary>
    public void Deactivate()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
