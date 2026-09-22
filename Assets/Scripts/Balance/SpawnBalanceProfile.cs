using UnityEngine;

/// <summary>
/// Perillas de balance de spawn en un solo asset, para que el game designer las tunee sin
/// saltar entre GameObjects y sin perder los cambios al salir de Play Mode.
/// Lo consumen <see cref="DifficultyManager"/> (escalado por tiempo), <see cref="HeatManager"/>
/// (escalado por heat y por ciclos de Overheat terminados) y <see cref="OrbitalSpawner"/> (cadencia base).
/// Si un manager no tiene profile asignado usa sus propios campos de fallback.
/// </summary>
[CreateAssetMenu(fileName = "SpawnBalanceProfile", menuName = "ScrapWaves/Balance/Spawn Balance Profile", order = 0)]
public class SpawnBalanceProfile : ScriptableObject
{
    [Header("Escalado por tiempo — ritmo")]
    [SerializeField, Min(0f), Tooltip("Segundos desde el inicio de la partida antes de que empiece a subir la dificultad.")]
    private float _scalingStartDelaySeconds = 30f;

    [SerializeField, Tooltip("Intensidad 0–1 en función de minutos transcurridos desde que empezó el escalado (X=0 es el primer frame tras el retraso).")]
    private AnimationCurve _intensityOverMinutesAfterStart = DefaultIntensityCurve();

    [SerializeField, Min(0.1f), Tooltip("Velocidad a la que se recorre la curva de intensidad. 3.5 alcanza cada punto 3.5 veces antes.")]
    private float _difficultyRampSpeedMultiplier = 1f;

    [Header("Escalado por tiempo — efectos")]
    [SerializeField, Min(1f), Tooltip("Multiplicador de enemigos por oleada cuando la intensidad de tiempo es 1. El tiempo YA NO afecta la frecuencia de spawn, solo la cantidad.")]
    private float _maxSpawnCountMultiplier = 4f;

    [SerializeField, Tooltip("Si está activo, escala la vida máxima al spawnear enemigos del pool.")]
    private bool _scaleEnemyHealth = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de vida máxima del enemigo cuando la intensidad es 1.")]
    private float _maxEnemyHealthMultiplier = 10f;

    [SerializeField, Tooltip("Si está activo, escala la velocidad de movimiento al spawnear.")]
    private bool _scaleEnemyMoveSpeed = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de velocidad cuando la intensidad es 1.")]
    private float _maxEnemySpeedMultiplier = 4f;

    [SerializeField, Tooltip("Si está activo, escala el daño al jugador al spawnear.")]
    private bool _scaleEnemyDamage = true;

    [SerializeField, Min(1f), Tooltip("Multiplicador de daño enemigo cuando la intensidad es 1.")]
    private float _maxEnemyDamageMultiplier = 1.35f;

    [Header("Escalado por heat")]
    [SerializeField, Tooltip("Intensidad 0–1 en función del % LINEAL de heat (X = CurrentHeat / TotalHeatCapacity). Ojo: X=0.5 es el 80% de la barra visible, porque los dos tramos de la barra cuestan los mismos puntos.")]
    private AnimationCurve _spawnScalingOverHeatRatio = DefaultHeatScalingCurve();

    [SerializeField, Min(1f), Tooltip("Multiplicador del tamaño de los grupos cuando la curva de heat vale 1.")]
    private float _maxSpawnCountMultiplierAtFullHeat = 2f;

    [SerializeField, Range(0.15f, 1f), Tooltip("Cuando la curva de heat vale 1, el intervalo de spawn se multiplica por este valor (menor a 1 = spawns más frecuentes).")]
    private float _spawnIntervalScaleAtFullHeat = 0.45f;

    [Header("Escalado por ciclos de Overheat terminados")]
    [SerializeField, Min(0f), Tooltip("Cuánto baja el multiplicador de intervalo por cada Overheat ya terminado. 0.1 da 0.9, 0.8, 0.7… Se multiplica encima del escalado por heat actual.")]
    private float _completedCycleIntervalStep = 0.1f;

    [SerializeField, Range(0.05f, 1f), Tooltip("Piso del multiplicador de intervalo por ciclos. Con paso 0.1 se alcanza en el ciclo 7.")]
    private float _completedCycleIntervalFloor = 0.3f;

    [SerializeField, Min(0f), Tooltip("Cuánto sube el multiplicador de batch por cada Overheat ya terminado. 0.1 da 1.1, 1.2, 1.3…")]
    private float _completedCycleBatchStep = 0.1f;

    [SerializeField, Min(1f), Tooltip("Techo del multiplicador de batch por ciclos. Con paso 0.1 se alcanza en el ciclo 10.")]
    private float _completedCycleBatchCeiling = 2f;

    [SerializeField, Min(0f), Tooltip("Cuánto sube el multiplicador de vida por cada Overheat ya terminado. 0.1 da 1.1, 1.2, 1.3…")]
    private float _completedCycleHealthStep = 0.1f;

    [SerializeField, Min(1f), Tooltip("Techo del multiplicador de vida por ciclos. Con paso 0.1 se alcanza en el ciclo 10.")]
    private float _completedCycleHealthCeiling = 2f;

    [Header("Heat (medidor)")]
    [SerializeField, Min(0.01f), Tooltip("Puntos de heat para llenar la barra de 0% a 80%.")]
    private float _pointsToReachDisplay80 = 100f;

    [SerializeField, Min(0.01f), Tooltip("Puntos de heat para ir de 80% a 100% (Overheat).")]
    private float _pointsFromDisplay80To100 = 100f;

    [SerializeField, Min(1.001f), Tooltip("Multiplicador aplicado al requisito total después de cada Overheat completado.")]
    private float _escalationPerOverheatCycle = 1.5f;

    [SerializeField, Min(0f), Tooltip("Heat que otorga cada kill.")]
    private float _heatPerKill = 5f;

    [SerializeField, Min(0f), Tooltip("Decay de heat por segundo tras Overheat (durante la pausa de spawn).")]
    private float _postOverheatDecayPerSecond = 12f;

    [Header("Spawner")]
    [SerializeField, Min(0.05f), Tooltip("Intervalo base entre oleadas, antes de aplicar el escalado por heat.")]
    private float _spawnInterval = 5f;

    [SerializeField, Min(1), Tooltip("Techo duro de enemigos activos simultáneos.")]
    private int _maxActiveEnemies = 300;

    public float ScalingStartDelaySeconds => _scalingStartDelaySeconds;
    public AnimationCurve IntensityOverMinutesAfterStart => _intensityOverMinutesAfterStart;
    public float DifficultyRampSpeedMultiplier => _difficultyRampSpeedMultiplier;
    public float MaxSpawnCountMultiplier => _maxSpawnCountMultiplier;
    public bool ScaleEnemyHealth => _scaleEnemyHealth;
    public float MaxEnemyHealthMultiplier => _maxEnemyHealthMultiplier;
    public bool ScaleEnemyMoveSpeed => _scaleEnemyMoveSpeed;
    public float MaxEnemySpeedMultiplier => _maxEnemySpeedMultiplier;
    public bool ScaleEnemyDamage => _scaleEnemyDamage;
    public float MaxEnemyDamageMultiplier => _maxEnemyDamageMultiplier;

    public AnimationCurve SpawnScalingOverHeatRatio => _spawnScalingOverHeatRatio;
    public float MaxSpawnCountMultiplierAtFullHeat => _maxSpawnCountMultiplierAtFullHeat;
    public float SpawnIntervalScaleAtFullHeat => _spawnIntervalScaleAtFullHeat;

    public float CompletedCycleIntervalStep => _completedCycleIntervalStep;
    public float CompletedCycleIntervalFloor => _completedCycleIntervalFloor;
    public float CompletedCycleBatchStep => _completedCycleBatchStep;
    public float CompletedCycleBatchCeiling => _completedCycleBatchCeiling;
    public float CompletedCycleHealthStep => _completedCycleHealthStep;
    public float CompletedCycleHealthCeiling => _completedCycleHealthCeiling;

    public float PointsToReachDisplay80 => _pointsToReachDisplay80;
    public float PointsFromDisplay80To100 => _pointsFromDisplay80To100;
    public float EscalationPerOverheatCycle => _escalationPerOverheatCycle;
    public float HeatPerKill => _heatPerKill;
    public float PostOverheatDecayPerSecond => _postOverheatDecayPerSecond;

    public float SpawnInterval => _spawnInterval;
    public int MaxActiveEnemies => _maxActiveEnemies;

    /// <summary>Misma forma que la curva serializada en GameplayScene: intensidad máxima a ~19.46 min.</summary>
    private static AnimationCurve DefaultIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(5f, 0.212854f),
            new Keyframe(9.997177f, 0.39984435f),
            new Keyframe(19.460941f, 1f));
    }

    /// <summary>X = ratio lineal de heat (0.5 es el codo del 80% de barra), Y = intensidad de spawn.</summary>
    private static AnimationCurve DefaultHeatScalingCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 0.35f),
            new Keyframe(1f, 1f));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_difficultyRampSpeedMultiplier < 0.1f)
            _difficultyRampSpeedMultiplier = 0.1f;
        if (_maxSpawnCountMultiplier < 1f)
            _maxSpawnCountMultiplier = 1f;
        if (_maxEnemyHealthMultiplier < 1f)
            _maxEnemyHealthMultiplier = 1f;
        if (_maxEnemySpeedMultiplier < 1f)
            _maxEnemySpeedMultiplier = 1f;
        if (_maxEnemyDamageMultiplier < 1f)
            _maxEnemyDamageMultiplier = 1f;
        if (_maxSpawnCountMultiplierAtFullHeat < 1f)
            _maxSpawnCountMultiplierAtFullHeat = 1f;
        if (_spawnIntervalScaleAtFullHeat < 0.15f)
            _spawnIntervalScaleAtFullHeat = 0.15f;
        if (_completedCycleIntervalStep < 0f)
            _completedCycleIntervalStep = 0f;
        if (_completedCycleIntervalFloor < 0.05f)
            _completedCycleIntervalFloor = 0.05f;
        if (_completedCycleIntervalFloor > 1f)
            _completedCycleIntervalFloor = 1f;
        if (_completedCycleBatchStep < 0f)
            _completedCycleBatchStep = 0f;
        if (_completedCycleBatchCeiling < 1f)
            _completedCycleBatchCeiling = 1f;
        if (_completedCycleHealthStep < 0f)
            _completedCycleHealthStep = 0f;
        if (_completedCycleHealthCeiling < 1f)
            _completedCycleHealthCeiling = 1f;
        if (_escalationPerOverheatCycle < 1.001f)
            _escalationPerOverheatCycle = 1.001f;
        if (_spawnInterval < 0.05f)
            _spawnInterval = 0.05f;
        if (_maxActiveEnemies < 1)
            _maxActiveEnemies = 1;
    }
#endif
}
