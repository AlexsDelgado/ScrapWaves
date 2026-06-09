using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int _maxHealth = 100;

    [SerializeField, Min(0f), Tooltip("Tras recibir daño, no se puede volver a dañar hasta pasados estos segundos (i-frames globales).")]
    private float _hitInvulnerabilitySeconds = 1.5f;

    [SerializeField]private int _currentHealth;
    private float _invulnerableUntil;
    private bool _isDead;

    private const float BurnTickInterval = 0.5f;
    private float _burnRemaining;
    private float _burnTickTimer;
    private int _burnDps;

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
        ClearBurn();
        OnHealthChanged?.Invoke();
    }

    private void Update()
    {
        if (_burnRemaining <= 0f || _isDead)
            return;

        _burnRemaining -= Time.deltaTime;
        _burnTickTimer -= Time.deltaTime;

        if (_burnTickTimer <= 0f)
        {
            _burnTickTimer += BurnTickInterval;
            int tickDamage = Mathf.Max(1, Mathf.RoundToInt(_burnDps * BurnTickInterval));
            ApplyBurnTick(tickDamage);
        }

        if (_burnRemaining <= 0f)
            ClearBurn();
    }

    /// <summary>
    /// Aplica una quemadura (DoT) por <paramref name="seconds"/> a <paramref name="dps"/> de daño
    /// por segundo. IGNORA los i-frames globales (canal aparte de <see cref="TakeDamage"/>).
    /// Refresca la duración (no apila) y conserva el dps más alto.
    /// </summary>
    public void ApplyBurn(float seconds, int dps)
    {
        if (seconds <= 0f || dps <= 0 || _isDead || _currentHealth <= 0)
            return;

        _burnRemaining = Mathf.Max(_burnRemaining, seconds);
        _burnDps = Mathf.Max(_burnDps, dps);
        if (_burnTickTimer <= 0f)
            _burnTickTimer = BurnTickInterval;
    }

    private void ApplyBurnTick(int amount)
    {
        if (amount <= 0 || _currentHealth <= 0 || _isDead)
            return;

        _currentHealth -= amount;
        if (_currentHealth < 0)
            _currentHealth = 0;

        OnHealthChanged?.Invoke();

        if (_currentHealth <= 0 && !_isDead)
        {
            _isDead = true;
            ClearBurn();
            OnPlayerDied?.Invoke();
        }
    }

    private void ClearBurn()
    {
        _burnRemaining = 0f;
        _burnTickTimer = 0f;
        _burnDps = 0;
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
        ClearBurn();
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
