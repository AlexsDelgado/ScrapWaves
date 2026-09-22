using UnityEngine;

/// <summary>
/// Escala la dificultad con el tiempo de partida. Curva Y = intensidad 0–1 sobre el eje X (minutos tras el retraso inicial).
/// Controla CUÁNTOS enemigos spawnean y qué tan fuertes son (vida, velocidad, daño).
/// La FRECUENCIA de spawn ya no depende del tiempo: la maneja <see cref="HeatManager"/> según el % de heat.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-45)]
public class DifficultyManager : MonoBehaviour
{
    public static DifficultyManager Instance { get; private set; }

    [SerializeField, Tooltip("Si está asignado, sus valores mandan y los campos de abajo se ignoran. Normalmente lo inyecta el GameObject BalanceTuning.")]
    private SpawnBalanceProfile _profile;

    [Header("Fallback (se usa solo si no hay profile)")]
    [SerializeField, Min(0f), Tooltip("Segundos desde el inicio de la partida antes de que empiece a subir la dificultad.")]
    private float _scalingStartDelaySeconds = 30f;

    [SerializeField, Tooltip("Intensidad 0–1 en función de minutos transcurridos desde que empezó el escalado (X=0 es el primer frame tras el retraso).")]
    private AnimationCurve _intensityOverMinutesAfterStart = DefaultIntensityCurve();

    [SerializeField, Min(0.1f), Tooltip("Velocidad a la que se recorre la curva de intensidad. 3.5 alcanza cada punto 3.5 veces antes.")]
    private float _difficultyRampSpeedMultiplier = 1f;

    [SerializeField, Min(1f), Tooltip("Multiplicador de enemigos por oleada cuando la intensidad es 1.")]
    private float _maxSpawnCountMultiplier = 2.5f;

    [SerializeField, Tooltip("Si está activo, escala la vida máxima al spawnear enemigos del pool.")]
    private bool _scaleEnemyHealth = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de vida máxima del enemigo cuando la intensidad es 1.")]
    private float _maxEnemyHealthMultiplier = 2f;

    [SerializeField, Tooltip("Si está activo, escala la velocidad de movimiento al spawnear.")]
    private bool _scaleEnemyMoveSpeed = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de velocidad cuando la intensidad es 1.")]
    private float _maxEnemySpeedMultiplier = 1.35f;

    [SerializeField, Tooltip("Si está activo, escala el daño al jugador al spawnear.")]
    private bool _scaleEnemyDamage = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de daño enemigo cuando la intensidad es 1.")]
    private float _maxEnemyDamageMultiplier = 1.35f;

    private float _runStartTime;

    /// <summary>Lo llama <see cref="BalanceTuningHub"/> antes de que nadie lea valores.</summary>
    public void SetProfile(SpawnBalanceProfile profile) => _profile = profile;

    public SpawnBalanceProfile Profile => _profile;

    private float ScalingStartDelaySeconds =>
        _profile != null ? _profile.ScalingStartDelaySeconds : _scalingStartDelaySeconds;

    private AnimationCurve IntensityCurve =>
        _profile != null ? _profile.IntensityOverMinutesAfterStart : _intensityOverMinutesAfterStart;

    private float RampSpeedMultiplier =>
        _profile != null ? _profile.DifficultyRampSpeedMultiplier : _difficultyRampSpeedMultiplier;

    private float MaxSpawnCountMultiplier =>
        _profile != null ? _profile.MaxSpawnCountMultiplier : _maxSpawnCountMultiplier;

    private bool ScaleEnemyHealth => _profile != null ? _profile.ScaleEnemyHealth : _scaleEnemyHealth;

    private float MaxEnemyHealthMultiplier =>
        _profile != null ? _profile.MaxEnemyHealthMultiplier : _maxEnemyHealthMultiplier;

    private bool ScaleEnemyMoveSpeed => _profile != null ? _profile.ScaleEnemyMoveSpeed : _scaleEnemyMoveSpeed;

    private float MaxEnemySpeedMultiplier =>
        _profile != null ? _profile.MaxEnemySpeedMultiplier : _maxEnemySpeedMultiplier;

    private bool ScaleEnemyDamage => _profile != null ? _profile.ScaleEnemyDamage : _scaleEnemyDamage;

    private float MaxEnemyDamageMultiplier =>
        _profile != null ? _profile.MaxEnemyDamageMultiplier : _maxEnemyDamageMultiplier;

    private void Awake()
    {
        _runStartTime = Time.timeSinceLevelLoad;
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Minutos desde que empezó el escalado (0 si aún no ha empezado).</summary>
    public float MinutesSinceScalingStarted
    {
        get
        {
            float elapsed = Time.timeSinceLevelLoad - ScalingStartDelaySeconds;
            if (elapsed <= 0f)
                return 0f;
            return elapsed / 60f;
        }
    }

    /// <summary>Intensidad actual 0–1 según la curva.</summary>
    public float CurrentIntensity
    {
        get
        {
            if (Time.timeSinceLevelLoad < ScalingStartDelaySeconds)
                return 0f;

            AnimationCurve curve = IntensityCurve;
            if (curve == null)
                return 0f;

            float minutes = MinutesSinceScalingStarted * RampSpeedMultiplier;
            float lastKey = curve.length > 0
                ? curve.keys[curve.length - 1].time
                : 30f;
            float t = Mathf.Max(0f, minutes);
            return Mathf.Clamp01(curve.Evaluate(Mathf.Min(t, lastKey)));
        }
    }

    public float GetSpawnCountMultiplier()
    {
        float i = CurrentIntensity;
        return Mathf.Lerp(1f, MaxSpawnCountMultiplier, i);
    }

    public float GetEnemyHealthMultiplier()
    {
        if (!ScaleEnemyHealth)
            return 1f;
        return Mathf.Lerp(1f, MaxEnemyHealthMultiplier, CurrentIntensity);
    }

    public float GetEnemyMoveSpeedMultiplier()
    {
        if (!ScaleEnemyMoveSpeed)
            return 1f;
        return Mathf.Lerp(1f, MaxEnemySpeedMultiplier, CurrentIntensity);
    }

    public float GetEnemyDamageMultiplier()
    {
        if (!ScaleEnemyDamage)
            return 1f;
        return Mathf.Lerp(1f, MaxEnemyDamageMultiplier, CurrentIntensity);
    }

    /// <summary>
    /// Vida extra permanente por overheats ya terminados. Si no hay <see cref="HeatManager"/>, es 1.
    /// </summary>
    private static float CompletedCycleHealthScale()
    {
        HeatManager heat = HeatManager.GetInstance();
        return heat != null ? heat.GetCompletedCycleHealthScale() : 1f;
    }

    /// <summary>Aplica vida, velocidad y daño según dificultad (enemigos del pool tras <see cref="SwarmEnemyPool.TryGet"/>).</summary>
    public void ApplySpawnModifiers(GameObject enemy)
    {
        if (enemy == null)
            return;

        float h = GetEnemyHealthMultiplier() * CompletedCycleHealthScale();
        float s = GetEnemyMoveSpeedMultiplier();
        float d = GetEnemyDamageMultiplier();

        if (enemy.TryGetComponent(out EnemyHealth health))
            health.ConfigureDifficultyForSpawn(h);

        if (enemy.TryGetComponent(out EnemyFollow follow))
            follow.ConfigureDifficultyForSpawn(s);

        if (enemy.TryGetComponent(out SimpleFollow simpleFollow))
            simpleFollow.ConfigureDifficultyForSpawn(s);

        EnemyOutgoingDamageScale damageScale = enemy.GetComponent<EnemyOutgoingDamageScale>();
        if (damageScale == null)
            damageScale = enemy.AddComponent<EnemyOutgoingDamageScale>();
        damageScale.ConfigureForSpawn(d);
    }

    private static AnimationCurve DefaultIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(5f, 0.35f),
            new Keyframe(15f, 0.7f),
            new Keyframe(30f, 1f));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_maxSpawnCountMultiplier < 1f)
            _maxSpawnCountMultiplier = 1f;
        if (_difficultyRampSpeedMultiplier < 0.1f)
            _difficultyRampSpeedMultiplier = 0.1f;
        if (_maxEnemyHealthMultiplier < 1f)
            _maxEnemyHealthMultiplier = 1f;
        if (_maxEnemySpeedMultiplier < 1f)
            _maxEnemySpeedMultiplier = 1f;
        if (_maxEnemyDamageMultiplier < 1f)
            _maxEnemyDamageMultiplier = 1f;
    }
#endif
}
