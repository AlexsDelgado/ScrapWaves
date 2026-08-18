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
public class DestroyerMouthWeakPoint : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1)] private int _maxHealth = 80;

    private int _currentHealth;

    /// <summary>Se dispara una vez al llegar a 0 HP, antes de desactivarse.</summary>
    public event Action OnWeakPointDestroyed;

    private void OnEnable()
    {
        _currentHealth = _maxHealth;
    }

    public bool ApplyDamage(int amount)
    {
        if (amount <= 0 || _currentHealth <= 0)
            return false;

        _currentHealth -= amount;
        if (_currentHealth > 0)
            return true;

        _currentHealth = 0;
        OnWeakPointDestroyed?.Invoke();
        gameObject.SetActive(false);
        return true;
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
