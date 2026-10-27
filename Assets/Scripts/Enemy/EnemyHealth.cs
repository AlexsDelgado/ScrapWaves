using UnityEngine;

public class EnemyHealth : MonoBehaviour, IAuthoritativeDamageable
{
    [SerializeField, Min(1)] private int _maxHealth = 12;
    [SerializeField, Min(0f)] private float _damageTakenMultiplier = 1f;

    private int _prefabMaxHealth;
    private float _spawnHealthMultiplier = 1f;
    private float _difficultyHealthMultiplier = 1f;
    private int _currentHealth;
    private bool _isInvincible;
    private bool _blockDotWhileInvincible;
    private string _lastDamagingWeaponId;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => _maxHealth;

    /// <summary>WeaponId del último hit de arma del jugador (crédito de kill para challenges).</summary>
    public string LastDamagingWeaponId => _lastDamagingWeaponId;

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
        _spawnHealthMultiplier = _difficultyHealthMultiplier = 1f;
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
        // Re-enabling the same randomized life must not reset its health or variation.
        if (TryGetComponent(out EnemyStatRandomizer variation) && variation.HasSampledForSpawn)
            return;
        _spawnHealthMultiplier = _difficultyHealthMultiplier = 1f;
        _maxHealth = _prefabMaxHealth;
        _currentHealth = _maxHealth;
        _isInvincible = false;
        _blockDotWhileInvincible = false;
        _lastDamagingWeaponId = null;
    }

    /// <summary>Tras salir del pool; <see cref="DifficultyManager"/> ajusta vida según la partida.</summary>
    public void ConfigureDifficultyForSpawn(float healthMultiplier)
    {
        _difficultyHealthMultiplier = healthMultiplier;
        RefillScaledHealth();
    }

    /// <summary>Reset de estado al reutilizar desde pool (antes de modifiers de dificultad).</summary>
    public void PrepareForPoolSpawn()
    {
        _spawnHealthMultiplier = _difficultyHealthMultiplier = 1f;
        _maxHealth = _prefabMaxHealth;
        _currentHealth = _maxHealth;
        _isInvincible = false;
        _blockDotWhileInvincible = false;
        _lastDamagingWeaponId = null;
    }

    /// <summary>Spawn-only variation; leaves the authored HP and difficulty factor intact.</summary>
    public void SetSpawnHealthMultiplier(float multiplier)
    {
        _spawnHealthMultiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f
            ? 1f : multiplier;
        RefillScaledHealth();
    }

    private void RefillScaledHealth()
    {
        _maxHealth = Mathf.Max(1, Mathf.RoundToInt(_prefabMaxHealth * _difficultyHealthMultiplier * _spawnHealthMultiplier));
        _currentHealth = _maxHealth;
    }

    public void NotifyDamagedByWeapon(string weaponId)
    {
        if (!string.IsNullOrEmpty(weaponId) && !EnemyEmergence.BlocksGameplay(this))
            _lastDamagingWeaponId = weaponId;
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
        return ApplyDamageInternal(in request, _damageTakenMultiplier);
    }

    /// <summary>
    /// Daño desde una zona de hit (p. ej. cabeza). Usa <paramref name="zoneMultiplier"/>
    /// en lugar de <see cref="_damageTakenMultiplier"/>.
    /// </summary>
    public DamageApplicationResult ApplyHitZoneDamage(in DamageRequest request, float zoneMultiplier)
    {
        return ApplyDamageInternal(in request, zoneMultiplier);
    }

    private DamageApplicationResult ApplyDamageInternal(in DamageRequest request, float damageMultiplier)
    {
        int healthBefore = Mathf.Max(0, _currentHealth);
        if (request.ModifiedDamage <= 0 || healthBefore <= 0)
            return DamageApplicationResult.Rejected(in request, healthBefore);

        // Emergence is an independent all-channel immunity, including hit zones and DoT.
        // Do not modify the invincibility flags owned by enemy combat behaviors.
        if (EnemyEmergence.BlocksGameplay(this))
            return DamageApplicationResult.BlockedResult(in request, healthBefore);

        bool blocked = request.Channel == DamageChannel.Direct
            ? _isInvincible
            : _isInvincible && _blockDotWhileInvincible;
        if (blocked)
            return DamageApplicationResult.BlockedResult(in request, healthBefore);

        int scaledDamage = Mathf.RoundToInt(request.ModifiedDamage * Mathf.Max(0f, damageMultiplier));
        if (scaledDamage <= 0)
            return DamageApplicationResult.Rejected(in request, healthBefore);

        int healthAfter = Mathf.Max(0, healthBefore - scaledDamage);
        _currentHealth = healthAfter;
        if (!string.IsNullOrEmpty(request.SourceWeaponId))
            _lastDamagingWeaponId = request.SourceWeaponId;

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

    /// <summary>Muerte forzada (caída al vacío). Ignora invencibilidad para no dejar el objetivo colgado.</summary>
    public void ForceKill()
    {
        if (_currentHealth <= 0)
            return;

        _currentHealth = 0;
        CompleteDeath();
    }

    private void CompleteDeath()
    {
        AudioManager.TryPlayEnemyDeath();
        ChallengeProgressTracker.NotifyEnemyKilled(this);
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
