using UnityEngine;

public class EnemyHealth : MonoBehaviour, IAuthoritativeDamageable
{
    [SerializeField, Min(1)] private int _maxHealth = 12;

    private int _prefabMaxHealth;
    private int _currentHealth;
    private bool _isInvincible;
    private bool _blockDotWhileInvincible;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => _maxHealth;

    /// <summary>Mientras sea true, <see cref="ApplyDamage"/> ignora el dano (p. ej. Hellfire al lanzarse).</summary>
    public bool IsInvincible => _isInvincible;

    /// <summary>
    /// Activa/desactiva la invencibilidad temporal del enemigo. Por defecto el DoT (<see cref="ApplyDotDamage"/>)
    /// sigue pasando (comportamiento histórico); pasar <paramref name="blockDot"/> en true para inmunidad total
    /// (p. ej. Destroyer durante la succión).
    /// </summary>
    public void SetInvincible(bool invincible, bool blockDot = false)
    {
        _isInvincible = invincible;
        _blockDotWhileInvincible = invincible && blockDot;
    }

    /// <summary>Fija vida máxima y rellena al máximo (p. ej. boss spawneado por <see cref="BossManager"/>).</summary>
    public void ApplyConfiguredMaxHealth(int maxHealth)
    {
        _prefabMaxHealth = Mathf.Max(1, maxHealth);
        _maxHealth = _prefabMaxHealth;
        _currentHealth = _maxHealth;
    }

    /// <summary>Se invoca una vez al pasar a 0 HP, antes del despawn / desactivar el objeto.</summary>
    public event System.Action OnDied;

    private void Awake()
    {
        _prefabMaxHealth = _maxHealth;
    }

    private void OnEnable()
    {
        _maxHealth = _prefabMaxHealth;
        _currentHealth = _maxHealth;
        _isInvincible = false;
        _blockDotWhileInvincible = false;
    }

    /// <summary>Tras salir del pool; <see cref="DifficultyManager"/> ajusta vida según la partida.</summary>
    public void ConfigureDifficultyForSpawn(float healthMultiplier)
    {
        int newMax = Mathf.Max(1, Mathf.RoundToInt(_prefabMaxHealth * healthMultiplier));
        _maxHealth = newMax;
        _currentHealth = newMax;
    }

    /// <summary>Reset de estado al reutilizar desde pool (antes de modifiers de dificultad).</summary>
    public void PrepareForPoolSpawn()
    {
        _maxHealth = _prefabMaxHealth;
        _currentHealth = _maxHealth;
        _isInvincible = false;
        _blockDotWhileInvincible = false;
    }

    /// <summary>Cura al enemigo, clamp a la vida máxima actual (Destroyer: comer enemigos / tragar al jugador).</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || _currentHealth <= 0)
            return;

        _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);
    }

    /// <summary>
    /// Daño por estado (DoT). Por defecto ignora invencibilidad; la respeta si esta fue activada con
    /// <c>blockDot: true</c> (inmunidad total).
    /// </summary>
    public bool ApplyDotDamage(int amount)
    {
        DamageRequest request = new(
            amount,
            amount,
            DamageChannel.Status,
            WeaponStatusKind.Burn,
            canTriggerLifesteal: false);
        return ApplyDamage(in request).Applied;
    }

    public bool ApplyDamage(int amount)
    {
        DamageRequest request = new(amount, amount, DamageChannel.Direct);
        return ApplyDamage(in request).Applied;
    }

    public DamageApplicationResult ApplyDamage(in DamageRequest request)
    {
        int healthBefore = Mathf.Max(0, _currentHealth);
        if (request.ModifiedDamage <= 0 || healthBefore <= 0)
            return DamageApplicationResult.Rejected(in request, healthBefore);

        bool blocked = request.Channel == DamageChannel.Direct
            ? _isInvincible
            : _isInvincible && _blockDotWhileInvincible;
        if (blocked)
            return DamageApplicationResult.BlockedResult(in request, healthBefore);

        int healthAfter = Mathf.Max(0, healthBefore - request.ModifiedDamage);
        _currentHealth = healthAfter;
        DamageApplicationResult result = DamageApplicationResult.FromHealthDelta(
            in request,
            healthBefore,
            healthAfter);

        if (result.Killed)
        {
            CompleteDeath();
        }
        else if (request.Channel == DamageChannel.Direct)
        {
            AudioManager.TryPlayEnemyHit();
        }

        return result;
    }

    private void CompleteDeath()
    {
        AudioManager.TryPlayEnemyDeath();
        OnDied?.Invoke();
        RunCombatStats.RegisterEnemyEliminated();
        FinalizeDeath();
    }

    private void FinalizeDeath()
    {
        if (TryGetComponent(out SwarmPooledEnemy pooled) && pooled.IsBound)
        {
            pooled.Despawn();
            return;
        }

        EnemyPoolProfiler.RegisterDestroy();
        if (Application.isPlaying)
            Destroy(gameObject);
    }
}
