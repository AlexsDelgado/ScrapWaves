using System;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int _maxHealth = 100;
    [SerializeField] private PlayerStats _playerStats;

    [SerializeField, Min(0f), Tooltip("Tras recibir daño, no se puede volver a dañar hasta pasados estos segundos (i-frames globales).")]
    private float _hitInvulnerabilitySeconds = 1.5f;

    [SerializeField]private int _currentHealth;
    [SerializeField, Min(0f), Tooltip("Segundos sin recibir dano antes de regenerar vida.")]
    private float _regenerationDelaySeconds = 5f;

    private float _invulnerableUntil;
    private float _lastDamageTime;
    private float _regenerationRemainder;
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

    public bool IsBurning => _burnRemaining > 0f;

    /// <summary>Cambios de vida actuales/máxima (daño, subida de max, etc.).</summary>
    public event Action OnHealthChanged;

    /// <summary>Daño directo (no burn); para flash de impacto en HUD.</summary>
    public event Action OnHitDamageTaken;

    public void GrantInvulnerability(float seconds)
    {
        if (seconds <= 0f)
            return;

        _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + seconds);
    }

    private PlayerStats Stats
    {
        get
        {
            if (_playerStats == null)
                _playerStats = GetComponent<PlayerStats>();
            return _playerStats;
        }
    }

    private void Awake()
    {
        if (_playerStats == null)
            _playerStats = GetComponent<PlayerStats>();

        if (_currentHealth <= 0)
            _currentHealth = _maxHealth;
    }

    /// <summary>Suma a vida maxima y cura la misma cantidad (mejoras de MaxHealth).</summary>
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
        _lastDamageTime = Time.time - _regenerationDelaySeconds;
        _regenerationRemainder = 0f;
        ClearBurn();
        OnHealthChanged?.Invoke();
    }

    private void Update()
    {
        if (_isDead)
            return;

        UpdateBurn(Time.deltaTime);
        ApplyRegeneration(Time.deltaTime, Time.time);
    }

    private void UpdateBurn(float deltaTime)
    {
        if (_burnRemaining <= 0f)
            return;

        _burnRemaining -= deltaTime;
        _burnTickTimer -= deltaTime;

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

        int finalAmount = PlayerStatMath.ApplyDamageResistance(Stats, amount);
        _currentHealth -= finalAmount;
        if (_currentHealth < 0)
            _currentHealth = 0;

        RegisterDamageTaken(Time.time);
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
        _lastDamageTime = Time.time - _regenerationDelaySeconds;
        _regenerationRemainder = 0f;
        ClearBurn();
        OnHealthChanged?.Invoke();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || _currentHealth <= 0 || _isDead)
            return;

        if (Time.time < _invulnerableUntil)
            return;

        int finalAmount = PlayerStatMath.ApplyDamageResistance(Stats, amount);
        _currentHealth -= finalAmount;
        if (_currentHealth < 0)
            _currentHealth = 0;

        _invulnerableUntil = Time.time + _hitInvulnerabilitySeconds;
        RegisterDamageTaken(Time.time);

        AudioManager.TryPlayPlayerHurt();
        OnHitDamageTaken?.Invoke();
        OnHealthChanged?.Invoke();

        if (_currentHealth <= 0 && !_isDead)
        {
            _isDead = true;
            OnPlayerDied?.Invoke();
        }
    }

    private void ApplyRegeneration(float deltaTime, float currentTime)
    {
        if (deltaTime <= 0f || _currentHealth <= 0 || _currentHealth >= _maxHealth)
            return;

        if (currentTime - _lastDamageTime < _regenerationDelaySeconds)
            return;

        float regeneration = PlayerStatMath.GetHealthRegenerationPerSecond(Stats);
        if (regeneration <= 0f)
            return;

        float healFloat = regeneration * deltaTime + _regenerationRemainder;
        int healAmount = Mathf.FloorToInt(healFloat);
        _regenerationRemainder = healFloat - healAmount;

        if (healAmount <= 0)
            return;

        Heal(healAmount);
        if (_currentHealth >= _maxHealth)
            _regenerationRemainder = 0f;
    }

    private void RegisterDamageTaken(float currentTime)
    {
        _lastDamageTime = currentTime;
        _regenerationRemainder = 0f;
    }
}
