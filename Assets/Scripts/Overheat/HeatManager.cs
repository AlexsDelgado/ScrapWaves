using UnityEngine;

/// <summary>
/// Point-based Heat: the first segment (base <see cref="_pointsToReachDisplay80"/>) fills 0-80% of the visual bar;
/// the second segment (base <see cref="_pointsFromDisplay80To100"/>) fills 80-100%. Each segment has the same base amount.
/// After each Overheat cycle, <see cref="ApplyEscalationAfterOverheat"/> raises the total requirement.
/// The intermediate phase (bar >= 80% and &lt; 100%) activates <see cref="OverheatSwarmBoost"/> (does not apply to the boss).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-33)]
public class HeatManager : MonoBehaviour
{
    [SerializeField, Tooltip("Si está asignado, sus valores mandan y los campos de abajo se ignoran. Normalmente lo inyecta el GameObject BalanceTuning.")]
    private SpawnBalanceProfile _profile;

    [Header("Fallback (se usa solo si no hay profile) — escalado de spawn por heat")]
    [SerializeField, Tooltip("Intensidad 0–1 en función del % LINEAL de heat (X = CurrentHeat / TotalHeatCapacity). Ojo: X=0.5 es el 80% de la barra visible, porque los dos tramos cuestan los mismos puntos.")]
    private AnimationCurve _spawnScalingOverHeatRatio = DefaultHeatScalingCurve();

    [SerializeField, Min(1f), Tooltip("Multiplicador del tamaño de los grupos cuando la curva de heat vale 1.")]
    private float _maxSpawnCountMultiplierAtFullHeat = 2f;

    [SerializeField, Range(0.15f, 1f), Tooltip("Cuando la curva de heat vale 1, el intervalo de spawn se multiplica por este valor (menor a 1 = spawns más frecuentes).")]
    private float _spawnIntervalScaleAtFullHeat = 0.45f;

    [Header("Fallback (se usa solo si no hay profile) — ciclos de Overheat terminados")]
    [SerializeField, Min(0f), Tooltip("Cuánto baja el multiplicador de intervalo por cada Overheat ya terminado.")]
    private float _completedCycleIntervalStep = 0.1f;

    [SerializeField, Range(0.05f, 1f), Tooltip("Piso del multiplicador de intervalo por ciclos.")]
    private float _completedCycleIntervalFloor = 0.3f;

    [SerializeField, Min(0f), Tooltip("Cuánto sube el multiplicador de batch por cada Overheat ya terminado.")]
    private float _completedCycleBatchStep = 0.1f;

    [SerializeField, Min(1f), Tooltip("Techo del multiplicador de batch por ciclos.")]
    private float _completedCycleBatchCeiling = 2f;

    [SerializeField, Min(0f), Tooltip("Cuánto sube el multiplicador de vida por cada Overheat ya terminado.")]
    private float _completedCycleHealthStep = 0.1f;

    [SerializeField, Min(1f), Tooltip("Techo del multiplicador de vida por ciclos.")]
    private float _completedCycleHealthCeiling = 2f;

    [Header("Fallback (se usa solo si no hay profile) — medidor")]
    [SerializeField, Min(0.01f), Tooltip("Scaled heat points required to fill the bar from 0% to 80%.")]
    private float _pointsToReachDisplay80 = 50f;

    [SerializeField, Min(0.01f), Tooltip("Scaled heat points required to go from 80% to 100% (Overheat). Same base effort as the first segment.")]
    private float _pointsFromDisplay80To100 = 50f;

    [SerializeField, Min(1.001f), Tooltip("Multiplier applied to the total requirement after each completed Overheat.")]
    private float _escalationPerOverheatCycle = 1.12f;

    [SerializeField, Min(0f), Tooltip("Heat granted by each enemy kill (through RegisterKill or AddHeat).")]
    private float _heatPerKill = 5f;

    [SerializeField, Min(0f), Tooltip("Decay de heat por segundo tras Overheat (pausa de spawn). 0 = usar default runtime (12).")]
    private float _postOverheatDecayPerSecond = 12f;

    [Header("Estado en runtime")]
    [SerializeField, Tooltip("Log Overheat events to the console.")]
    private bool _logOverheat;

    [SerializeField, Tooltip("Accumulated scaling (increases when Overheat ends).")]
    private float _heatRequirementEscalation = 1f;

    [SerializeField, Tooltip("Overheats que ya terminaron. No incluye el que está en curso.")]
    private int _completedOverheatCycles;

    [SerializeField] private float _currentHeat;

    private bool _intermediateBoostActive;
    private bool _postOverheatDecayActive;
    private float _activeDecayPerSecond;
    private bool _spawnScalingSuppressed;

    private const float DefaultPostOverheatDecayPerSecond = 12f;

    /// <summary>Lo llama <see cref="BalanceTuningHub"/> antes de que nadie lea valores.</summary>
    public void SetProfile(SpawnBalanceProfile profile) => _profile = profile;

    public SpawnBalanceProfile Profile => _profile;

    private float BasePointsFirstSegment =>
        _profile != null ? _profile.PointsToReachDisplay80 : _pointsToReachDisplay80;

    private float BasePointsSecondSegment =>
        _profile != null ? _profile.PointsFromDisplay80To100 : _pointsFromDisplay80To100;

    private float EscalationPerOverheatCycle =>
        _profile != null ? _profile.EscalationPerOverheatCycle : _escalationPerOverheatCycle;

    private float PostOverheatDecayPerSecond =>
        _profile != null ? _profile.PostOverheatDecayPerSecond : _postOverheatDecayPerSecond;

    private AnimationCurve SpawnScalingCurve =>
        _profile != null ? _profile.SpawnScalingOverHeatRatio : _spawnScalingOverHeatRatio;

    private float MaxSpawnCountMultiplierAtFullHeat =>
        _profile != null ? _profile.MaxSpawnCountMultiplierAtFullHeat : _maxSpawnCountMultiplierAtFullHeat;

    private float SpawnIntervalScaleAtFullHeat =>
        _profile != null ? _profile.SpawnIntervalScaleAtFullHeat : _spawnIntervalScaleAtFullHeat;

    private float CompletedCycleIntervalStep =>
        _profile != null ? _profile.CompletedCycleIntervalStep : _completedCycleIntervalStep;

    private float CompletedCycleIntervalFloor =>
        _profile != null ? _profile.CompletedCycleIntervalFloor : _completedCycleIntervalFloor;

    private float CompletedCycleBatchStep =>
        _profile != null ? _profile.CompletedCycleBatchStep : _completedCycleBatchStep;

    private float CompletedCycleBatchCeiling =>
        _profile != null ? _profile.CompletedCycleBatchCeiling : _completedCycleBatchCeiling;

    private float CompletedCycleHealthStep =>
        _profile != null ? _profile.CompletedCycleHealthStep : _completedCycleHealthStep;

    private float CompletedCycleHealthCeiling =>
        _profile != null ? _profile.CompletedCycleHealthCeiling : _completedCycleHealthCeiling;

    /// <summary>Puntos actuales de heat.</summary>
    public float CurrentHeat => _currentHeat;

    /// <summary>Puntos necesarios para el primer tramo (0 → 80 % de barra), con escalado.</summary>
    public float PointsFirstSegment => BasePointsFirstSegment * _heatRequirementEscalation;

    /// <summary>Puntos necesarios para el segundo tramo (80 % → 100 % de barra), con escalado.</summary>
    public float PointsSecondSegment => BasePointsSecondSegment * _heatRequirementEscalation;

    /// <summary>Total de puntos para disparar Overheat.</summary>
    public float TotalHeatCapacity => PointsFirstSegment + PointsSecondSegment;

    /// <summary>Compatibilidad UI: mismo significado que capacidad total actual.</summary>
    public float MaxHeat => TotalHeatCapacity;

    public float HeatPerKill => _profile != null ? _profile.HeatPerKill : _heatPerKill;

    public float HeatRequirementEscalation => _heatRequirementEscalation;

    /// <summary>
    /// % LINEAL de heat sobre la capacidad total (0–1). Es el eje X de la curva de escalado de spawn.
    /// NO es lo mismo que <see cref="NormalizedHeat"/>, que es por tramos: con los dos tramos iguales,
    /// un ratio de 0.5 equivale al 80 % de la barra visible.
    /// </summary>
    public float HeatRatio
    {
        get
        {
            float total = TotalHeatCapacity;
            if (total <= 0f)
                return 0f;
            return Mathf.Clamp01(_currentHeat / total);
        }
    }

    /// <summary>Intensidad 0–1 que sale de la curva evaluada en <see cref="HeatRatio"/>.</summary>
    public float CurrentSpawnIntensity
    {
        get
        {
            AnimationCurve curve = SpawnScalingCurve;
            if (curve == null || curve.length == 0)
                return 0f;
            return Mathf.Clamp01(curve.Evaluate(HeatRatio));
        }
    }

    /// <summary>
    /// Mientras esté suprimido (fase de Overheat activa), el escalado por heat no aplica:
    /// ambos multiplicadores vuelven a 1.
    /// </summary>
    public bool IsSpawnScalingSuppressed => _spawnScalingSuppressed;

    public void SetSpawnScalingSuppressed(bool suppressed) => _spawnScalingSuppressed = suppressed;

    /// <summary>Multiplicador del tamaño de los grupos que spawnean, según el heat actual.</summary>
    public float GetSpawnCountMultiplier()
    {
        if (_spawnScalingSuppressed)
            return 1f;
        return Mathf.Lerp(1f, MaxSpawnCountMultiplierAtFullHeat, CurrentSpawnIntensity);
    }

    /// <summary>Multiplicador sobre el intervalo base del spawner (menor = spawns más frecuentes).</summary>
    public float GetSpawnIntervalScale()
    {
        if (_spawnScalingSuppressed)
            return 1f;
        return Mathf.Lerp(1f, SpawnIntervalScaleAtFullHeat, CurrentSpawnIntensity);
    }

    /// <summary>Overheats que ya terminaron. El ciclo en curso todavía no cuenta.</summary>
    public int CompletedOverheatCycles => _completedOverheatCycles;

    /// <summary>
    /// Multiplicador permanente del intervalo según los ciclos ya terminados.
    /// No lo apaga <see cref="IsSpawnScalingSuppressed"/>: se multiplica encima del heat actual.
    /// </summary>
    public float GetCompletedCycleIntervalScale()
    {
        return Mathf.Max(CompletedCycleIntervalFloor, 1f - CompletedCycleIntervalStep * _completedOverheatCycles);
    }

    /// <summary>Multiplicador permanente del batch según los ciclos ya terminados.</summary>
    public float GetCompletedCycleBatchScale()
    {
        return Mathf.Min(CompletedCycleBatchCeiling, 1f + CompletedCycleBatchStep * _completedOverheatCycles);
    }

    /// <summary>Multiplicador permanente de vida al spawnear, según los ciclos ya terminados.</summary>
    public float GetCompletedCycleHealthScale()
    {
        return Mathf.Min(CompletedCycleHealthCeiling, 1f + CompletedCycleHealthStep * _completedOverheatCycles);
    }

    /// <summary>Progreso 0–1 de la barra (0–80 % lineal en puntos del 1.er tramo; 80–100 % lineal en el 2.º).</summary>
    public float NormalizedHeat
    {
        get
        {
            float a = PointsFirstSegment;
            float total = TotalHeatCapacity;
            if (total <= 0f)
                return 0f;

            if (_currentHeat <= a)
                return a > 0f ? Mathf.Clamp01(_currentHeat / a) * 0.8f : 0f;

            float b = PointsSecondSegment;
            if (b <= 0f)
                return 0.8f;

            return 0.8f + Mathf.Clamp01((_currentHeat - a) / b) * 0.2f;
        }
    }

    /// <summary>Entre 80 % y 100 % de barra (antes de Overheat).</summary>
    public bool IsInIntermediatePhase =>
        _currentHeat >= PointsFirstSegment && _currentHeat < TotalHeatCapacity - 0.0001f;

    public bool IsAtOrOverMax => _currentHeat >= TotalHeatCapacity - 0.0001f;

    /// <summary>Disparado al alcanzar el 100 % de la barra (inicio lógico de Overheat).</summary>
    public event System.Action OnOverheat;

    public event System.Action OnHeatChanged;

    public static HeatManager Instance { get; private set; }

    public static HeatManager GetInstance()
    {
        if (Instance != null)
            return Instance;
        return FindAnyObjectByType<HeatManager>();
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

    private void Update()
    {
        if (!_postOverheatDecayActive || _activeDecayPerSecond <= 0f)
            return;

        if (_currentHeat <= 0f)
        {
            _postOverheatDecayActive = false;
            return;
        }

        TickPostOverheatDecay(Time.unscaledDeltaTime);
    }

    /// <summary>
    /// Decay unscaled para no colgarse en hit-stop. Se detiene en pausa de UI.
    /// </summary>
    private void TickPostOverheatDecay(float unscaledDeltaTime)
    {
        if (GameplayPause.IsUiPaused)
            return;

        float next = _currentHeat - _activeDecayPerSecond * unscaledDeltaTime;
        if (next <= 0f)
        {
            _postOverheatDecayActive = false;
            SetHeat(0f);
            return;
        }

        SetHeat(next);
    }

    /// <summary>
    /// Tras Overheat: deja heat residual y activa decay hasta vaciar (el spawn se reanuda
    /// cuando heat cae bajo el umbral del primer tramo).
    /// </summary>
    public void BeginPostOverheatCooldown(float residualHeat)
    {
        float configuredDecay = PostOverheatDecayPerSecond;
        _activeDecayPerSecond = configuredDecay > 0f
            ? configuredDecay
            : DefaultPostOverheatDecayPerSecond;

        SetHeat(residualHeat);
        _postOverheatDecayActive = residualHeat > 0f && _activeDecayPerSecond > 0f;
    }

    public bool IsPostOverheatDecayActive => _postOverheatDecayActive;

    public void StopPostOverheatDecay() => _postOverheatDecayActive = false;

    public void RegisterKill()
    {
        AddHeat(HeatPerKill);
    }

    /// <summary>
    /// Mientras la descarga post-Overheat esté activa, no se puede ganar heat (kills del swarm
    /// que sigue vivo no deben recargar el nivel antes de que termine de bajar).
    /// </summary>
    public void AddHeat(float amount)
    {
        if (amount <= 0f || _postOverheatDecayActive)
            return;

        float cap = TotalHeatCapacity;
        bool wasBelowMax = _currentHeat < cap - 0.0001f;

        _currentHeat += amount;
        if (_currentHeat > cap)
            _currentHeat = cap;

        SyncIntermediateSwarmBoost();

        if (wasBelowMax && _currentHeat >= cap - 0.0001f)
        {
            _postOverheatDecayActive = false;
            if (_logOverheat)
                Debug.Log("Heat al máximo → Overheat", this);

            OnOverheat?.Invoke();
        }

        OnHeatChanged?.Invoke();
    }

    public void SetHeat(float value)
    {
        float cap = TotalHeatCapacity;
        _currentHeat = Mathf.Clamp(value, 0f, cap);
        SyncIntermediateSwarmBoost();
        OnHeatChanged?.Invoke();
    }

    /// <summary>Llama <see cref="OverheatManager"/> al terminar un Overheat: sube el requisito de puntos y cuenta el ciclo para el escalado permanente de spawn.</summary>
    public void ApplyEscalationAfterOverheat()
    {
        _heatRequirementEscalation *= EscalationPerOverheatCycle;
        _completedOverheatCycles++;
        _currentHeat = Mathf.Clamp(_currentHeat, 0f, TotalHeatCapacity);
        SyncIntermediateSwarmBoost();
        OnHeatChanged?.Invoke();
    }

    /// <summary>Para menú / nueva partida.</summary>
    public void ResetHeatProgressAndEscalation()
    {
        _heatRequirementEscalation = 1f;
        _completedOverheatCycles = 0;
        _currentHeat = 0f;
        _spawnScalingSuppressed = false;
        SyncIntermediateSwarmBoost();
        OnHeatChanged?.Invoke();
    }

    private void SyncIntermediateSwarmBoost()
    {
        bool want = IsInIntermediatePhase;
        if (want == _intermediateBoostActive)
            return;

        _intermediateBoostActive = want;
        OverheatSwarmBoost.SetIntensity(want);
    }

    /// <summary>X = ratio lineal de heat (0.5 es el codo del 80 % de barra), Y = intensidad de spawn.</summary>
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
        if (_pointsToReachDisplay80 < 0.01f)
            _pointsToReachDisplay80 = 0.01f;
        if (_pointsFromDisplay80To100 < 0.01f)
            _pointsFromDisplay80To100 = 0.01f;
        if (_escalationPerOverheatCycle < 1.001f)
            _escalationPerOverheatCycle = 1.001f;
        if (_heatRequirementEscalation < 0.01f)
            _heatRequirementEscalation = 0.01f;
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
        if (_completedOverheatCycles < 0)
            _completedOverheatCycles = 0;
        _currentHeat = Mathf.Clamp(_currentHeat, 0f, TotalHeatCapacity);
    }
#endif
}
