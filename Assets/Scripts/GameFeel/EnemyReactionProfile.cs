using UnityEngine;

public enum EnemyReactionTier
{
    Light,
    Heavy,
    Critical,
    WeakPoint,
    Kill
}

public enum WeaponStatusKind
{
    Burn,
    JellifiedBurn,
    Slow,
    Freeze,
    Vulnerable
}

[System.Flags]
public enum WeaponStatusMask
{
    None = 0,
    Burn = 1 << 0,
    JellifiedBurn = 1 << 1,
    Slow = 1 << 2,
    Freeze = 1 << 3,
    Vulnerable = 1 << 4
}

[CreateAssetMenu(fileName = "EnemyReactionProfile", menuName = "ScrapWaves/Game Feel/Enemy Reaction Profile")]
public sealed class EnemyReactionProfile : ScriptableObject
{
    [Header("Hit tiers")]
    [SerializeField, Range(0f, 1f)] private float _heavyDamageFraction = 0.18f;
    [SerializeField, Min(0.01f)] private float _lightDuration = 0.085f;
    [SerializeField, Min(0.01f)] private float _heavyDuration = 0.14f;
    [SerializeField, Min(0f)] private float _lightDisplacement = 0.055f;
    [SerializeField, Min(0f)] private float _heavyDisplacement = 0.12f;
    [SerializeField, Range(0f, 0.5f)] private float _lightSquash = 0.055f;
    [SerializeField, Range(0f, 0.5f)] private float _heavySquash = 0.12f;
    [SerializeField, Range(0f, 2f)] private float _maximumAccumulatedIntensity = 1.35f;

    [Header("Reduced motion")]
    [SerializeField, Range(0f, 1f), Tooltip("Multiplier for cosmetic hit displacement when Reduced Motion is enabled.")]
    private float _reducedMotionDisplacementScale = 0.2f;
    [SerializeField, Range(0f, 1f), Tooltip("Multiplier for cosmetic hit squash when Reduced Motion is enabled.")]
    private float _reducedMotionSquashScale = 0.25f;
    [SerializeField, Range(0.25f, 1f), Tooltip("Shortens the cosmetic transform response without changing damage acknowledgement.")]
    private float _reducedMotionDurationScale = 0.75f;

    [Header("Enemy classes")]
    [SerializeField, Range(0f, 1f)] private float _eliteScale = 0.75f;
    [SerializeField, Range(0f, 1f)] private float _bossScale = 0.42f;

    [Header("Status and death")]
    [SerializeField, Min(0.05f)] private float _statusFadeDuration = 0.24f;
    [SerializeField, Range(1, 4)] private int _maximumStatusVisualsPerEnemy = 3;
    [SerializeField, Range(8, 96)] private int _maximumGlobalStatusVisuals = 48;
    [SerializeField, Range(8, 128)] private int _deathPoolCapacity = 48;
    [SerializeField, Min(0.05f)] private float _deathDuration = 1.25f;

    [Header("Colors")]
    [SerializeField] private Color _lightColor = new(1f, 0.34f, 0.08f, 0.72f);
    [SerializeField] private Color _criticalColor = new(1f, 0.84f, 0.28f, 0.92f);
    [SerializeField] private Color _weakPointColor = new(1f, 1f, 1f, 0.95f);

    private static EnemyReactionProfile s_default;

    public float StatusFadeDuration => Mathf.Max(0.05f, _statusFadeDuration);
    public int MaximumStatusVisualsPerEnemy => Mathf.Clamp(_maximumStatusVisualsPerEnemy, 1, 4);
    public int MaximumGlobalStatusVisuals => Mathf.Clamp(_maximumGlobalStatusVisuals, 8, 96);
    public int DeathPoolCapacity => Mathf.Clamp(_deathPoolCapacity, 8, 128);
    public float DeathDuration => Mathf.Max(0.05f, _deathDuration);
    public float MaximumAccumulatedIntensity => Mathf.Max(0.1f, _maximumAccumulatedIntensity);
    public float ReducedMotionDisplacementScale => Mathf.Clamp01(_reducedMotionDisplacementScale);
    public float ReducedMotionSquashScale => Mathf.Clamp01(_reducedMotionSquashScale);
    public float ReducedMotionDurationScale => Mathf.Clamp(_reducedMotionDurationScale, 0.25f, 1f);

    public static EnemyReactionProfile Resolve(EnemyReactionProfile authored)
    {
        if (authored != null)
            return authored;
        if (s_default == null)
            s_default = Resources.Load<EnemyReactionProfile>("EnemyReactionProfile");
        if (s_default == null)
        {
            s_default = CreateInstance<EnemyReactionProfile>();
            s_default.hideFlags = HideFlags.HideAndDontSave;
        }
        return s_default;
    }

    public EnemyReactionTier ResolveTier(in WeaponFeedbackContext context, int maximumHealth)
    {
        if (context.IsKill)
            return EnemyReactionTier.Kill;
        if (context.IsWeakPoint)
            return EnemyReactionTier.WeakPoint;
        if (context.IsCritical)
            return EnemyReactionTier.Critical;
        float fraction = maximumHealth > 0 ? context.DamageAmount / (float)maximumHealth : 0f;
        return context.IsAbilityDamage || fraction >= _heavyDamageFraction || context.EventIntensity >= 1.15f
            ? EnemyReactionTier.Heavy
            : EnemyReactionTier.Light;
    }

    public float GetClassScale(WeaponEnemyKind kind)
    {
        return kind switch
        {
            WeaponEnemyKind.Elite => Mathf.Clamp01(_eliteScale),
            WeaponEnemyKind.Boss => Mathf.Clamp01(_bossScale),
            _ => 1f
        };
    }

    public float GetDuration(EnemyReactionTier tier)
    {
        return tier == EnemyReactionTier.Light ? Mathf.Max(0.01f, _lightDuration) : Mathf.Max(0.01f, _heavyDuration);
    }

    public float GetDisplacement(EnemyReactionTier tier)
    {
        return tier == EnemyReactionTier.Light ? Mathf.Max(0f, _lightDisplacement) : Mathf.Max(0f, _heavyDisplacement);
    }

    public float GetSquash(EnemyReactionTier tier)
    {
        return tier == EnemyReactionTier.Light ? Mathf.Clamp(_lightSquash, 0f, 0.5f) : Mathf.Clamp(_heavySquash, 0f, 0.5f);
    }

    public Color GetHitColor(EnemyReactionTier tier)
    {
        return tier switch
        {
            EnemyReactionTier.WeakPoint => _weakPointColor,
            EnemyReactionTier.Critical => _criticalColor,
            EnemyReactionTier.Kill => _criticalColor,
            _ => _lightColor
        };
    }

    private void OnValidate()
    {
        _heavyDamageFraction = Mathf.Clamp01(_heavyDamageFraction);
        _lightDuration = Mathf.Max(0.01f, _lightDuration);
        _heavyDuration = Mathf.Max(_lightDuration, _heavyDuration);
        _lightDisplacement = Mathf.Max(0f, _lightDisplacement);
        _heavyDisplacement = Mathf.Max(_lightDisplacement, _heavyDisplacement);
        _lightSquash = Mathf.Clamp(_lightSquash, 0f, 0.5f);
        _heavySquash = Mathf.Clamp(_heavySquash, _lightSquash, 0.5f);
        _maximumAccumulatedIntensity = Mathf.Max(0.1f, _maximumAccumulatedIntensity);
        _reducedMotionDisplacementScale = Mathf.Clamp01(_reducedMotionDisplacementScale);
        _reducedMotionSquashScale = Mathf.Clamp01(_reducedMotionSquashScale);
        _reducedMotionDurationScale = Mathf.Clamp(_reducedMotionDurationScale, 0.25f, 1f);
        _statusFadeDuration = Mathf.Max(0.05f, _statusFadeDuration);
        _maximumStatusVisualsPerEnemy = Mathf.Clamp(_maximumStatusVisualsPerEnemy, 1, 4);
        _maximumGlobalStatusVisuals = Mathf.Clamp(_maximumGlobalStatusVisuals, 8, 96);
        _deathPoolCapacity = Mathf.Clamp(_deathPoolCapacity, 8, 128);
        _deathDuration = Mathf.Max(0.05f, _deathDuration);
    }
}

public static class EnemyReactionRuntime
{
    public static bool Enabled { get; private set; } = true;
    public static bool ReducedMotion { get; private set; }
    public static bool ReducedFlash { get; private set; }
    public static bool ScreenFlashEnabled { get; private set; } = true;
    public static GameFeelQualityLevel Quality { get; private set; } = GameFeelQualityLevel.High;

    public static void Apply(GameFeelRuntimeOptions options)
    {
        if (options == null)
            return;
        Enabled = options.EnemyReactionEnabled;
        ReducedMotion = options.ReducedMotion;
        ReducedFlash = options.ReducedFlash;
        Quality = options.Quality;
        if (!Enabled)
            EnemyStatusFeedback.ClearAllActive();
    }

    public static void ApplyUserPreferences(bool reducedMotion, bool screenFlash)
    {
        ReducedMotion = reducedMotion;
        ScreenFlashEnabled = screenFlash;
    }
}
