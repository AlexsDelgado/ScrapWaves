using UnityEngine;

/// <summary>
/// Escala la dificultad con el tiempo de partida. Curva Y = intensidad 0–1 sobre el eje X (minutos tras el retraso inicial).
/// Expone multiplicadores para <see cref="SwarmSpawner"/> y para vida/velocidad al spawnear enemigos del pool.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-45)]
public class DifficultyManager : MonoBehaviour
{
    public static DifficultyManager Instance { get; private set; }

    [SerializeField, Min(0f), Tooltip("Segundos desde el inicio de la partida antes de que empiece a subir la dificultad.")]
    private float _scalingStartDelaySeconds = 30f;

    [SerializeField, Tooltip("Intensidad 0–1 en función de minutos transcurridos desde que empezó el escalado (X=0 es el primer frame tras el retraso).")]
    private AnimationCurve _intensityOverMinutesAfterStart = DefaultIntensityCurve();

    [SerializeField, Min(0.1f), Tooltip("Velocidad a la que se recorre la curva de intensidad. 3.5 alcanza cada punto 3.5 veces antes.")]
    private float _difficultyRampSpeedMultiplier = 1f;

    [SerializeField, Min(1f), Tooltip("Multiplicador de enemigos por oleada cuando la intensidad es 1.")]
    private float _maxSpawnCountMultiplier = 2.5f;

    [SerializeField, Range(0.15f, 1f), Tooltip("A intensidad 1, el intervalo de spawn se multiplica por este valor (&lt;1 = más rápido).")]
    private float _spawnIntervalScaleAtMaxIntensity = 0.45f;

    [SerializeField, Tooltip("Si está activo, escala la vida máxima al spawnear enemigos del pool.")]
    private bool _scaleEnemyHealth = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de vida máxima del enemigo cuando la intensidad es 1.")]
    private float _maxEnemyHealthMultiplier = 2f;

    [SerializeField, Tooltip("Si está activo, escala la velocidad de movimiento al spawnear.")]
    private bool _scaleEnemyMoveSpeed = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de velocidad cuando la intensidad es 1.")]
    private float _maxEnemySpeedMultiplier = 1.35f;

    private float _runStartTime;

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
            float elapsed = Time.timeSinceLevelLoad - _scalingStartDelaySeconds;
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
            if (Time.timeSinceLevelLoad < _scalingStartDelaySeconds)
                return 0f;

            float minutes = MinutesSinceScalingStarted * _difficultyRampSpeedMultiplier;
            float lastKey = _intensityOverMinutesAfterStart.length > 0
                ? _intensityOverMinutesAfterStart.keys[_intensityOverMinutesAfterStart.length - 1].time
                : 30f;
            float t = Mathf.Max(0f, minutes);
            return Mathf.Clamp01(_intensityOverMinutesAfterStart.Evaluate(Mathf.Min(t, lastKey)));
        }
    }

    public float GetSpawnCountMultiplier()
    {
        float i = CurrentIntensity;
        return Mathf.Lerp(1f, _maxSpawnCountMultiplier, i);
    }

    /// <summary>Multiplicador sobre el intervalo base del spawner (menor = spawns más frecuentes).</summary>
    public float GetSpawnIntervalScale()
    {
        float i = CurrentIntensity;
        return Mathf.Lerp(1f, _spawnIntervalScaleAtMaxIntensity, i);
    }

    public float GetEnemyHealthMultiplier()
    {
        if (!_scaleEnemyHealth)
            return 1f;
        return Mathf.Lerp(1f, _maxEnemyHealthMultiplier, CurrentIntensity);
    }

    public float GetEnemyMoveSpeedMultiplier()
    {
        if (!_scaleEnemyMoveSpeed)
            return 1f;
        return Mathf.Lerp(1f, _maxEnemySpeedMultiplier, CurrentIntensity);
    }

    /// <summary>Aplica vida y velocidad según dificultad (enemigos del pool tras <see cref="SwarmEnemyPool.TryGet"/>).</summary>
    public void ApplySpawnModifiers(GameObject enemy)
    {
        if (enemy == null)
            return;

        float h = GetEnemyHealthMultiplier();
        float s = GetEnemyMoveSpeedMultiplier();

        if (enemy.TryGetComponent(out EnemyHealth health))
            health.ConfigureDifficultyForSpawn(h);

        if (enemy.TryGetComponent(out EnemyFollow follow))
            follow.ConfigureDifficultyForSpawn(s);

        if (enemy.TryGetComponent(out SimpleFollow simpleFollow))
            simpleFollow.ConfigureDifficultyForSpawn(s);
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
        if (_spawnIntervalScaleAtMaxIntensity < 0.15f)
            _spawnIntervalScaleAtMaxIntensity = 0.15f;
        if (_maxEnemyHealthMultiplier < 1f)
            _maxEnemyHealthMultiplier = 1f;
        if (_maxEnemySpeedMultiplier < 1f)
            _maxEnemySpeedMultiplier = 1f;
    }
#endif
}
