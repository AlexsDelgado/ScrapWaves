using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int _maxHealth = 100;

    [SerializeField, Min(0f), Tooltip("Tras recibir daño, no se puede volver a dañar hasta pasados estos segundos (i-frames globales).")]
    private float _hitInvulnerabilitySeconds = 1.5f;

    [SerializeField]private int _currentHealth;
    private float _invulnerableUntil;
    private bool _isDead;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => _maxHealth;
    public bool IsAlive => _currentHealth > 0 && !_isDead;

    /// <summary>Se dispara una vez al llegar a 0 HP (para <see cref="GameManager"/>).</summary>
    public event System.Action OnPlayerDied;

    public bool IsInvulnerable => Time.time < _invulnerableUntil;

    /// <summary>Cambios de vida actuales/máxima (daño, subida de max, etc.).</summary>
    public event System.Action OnHealthChanged;

    /// <summary>Suma a vida máxima y cura la misma cantidad (mejoras de MaxHealth).</summary>
    public void ApplyMaxHealthIncrease(int delta)
    {
        if (delta <= 0)
            return;

        _maxHealth += delta;
        _currentHealth += delta;
        OnHealthChanged?.Invoke();
    }

    private void OnEnable()
    {
        _isDead = false;
        _currentHealth = _maxHealth;
        _invulnerableUntil = 0f;
        OnHealthChanged?.Invoke();
    }

    /// <summary>Cura al jugador hasta un maximo de <see cref="MaxHealth"/>.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || _isDead)
            return;

        _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);
        OnHealthChanged?.Invoke();
    }

    /// <summary>Restaura la vida al maximo (revive si estaba muerto). Util para QA.</summary>
    public void FullHeal()
    {
        _isDead = false;
        _currentHealth = _maxHealth;
        _invulnerableUntil = 0f;
        OnHealthChanged?.Invoke();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || _currentHealth <= 0 || _isDead)
            return;

        if (Time.time < _invulnerableUntil)
            return;

        _currentHealth -= amount;
        if (_currentHealth < 0)
            _currentHealth = 0;

        _invulnerableUntil = Time.time + _hitInvulnerabilitySeconds;

        AudioManager.TryPlayPlayerHurt();
        OnHealthChanged?.Invoke();

        if (_currentHealth <= 0 && !_isDead)
        {
            _isDead = true;
            OnPlayerDied?.Invoke();
        }
    }
}
