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
    [SerializeField, Min(0.01f), Tooltip("Scaled heat points required to fill the bar from 0% to 80%.")]
    private float _pointsToReachDisplay80 = 50f;

    [SerializeField, Min(0.01f), Tooltip("Scaled heat points required to go from 80% to 100% (Overheat). Same base effort as the first segment.")]
    private float _pointsFromDisplay80To100 = 50f;

    [SerializeField, Min(1.001f), Tooltip("Multiplier applied to the total requirement after each completed Overheat.")]
    private float _escalationPerOverheatCycle = 1.12f;

    [SerializeField, Min(0f), Tooltip("Heat granted by each enemy kill (through RegisterKill or AddHeat).")]
    private float _heatPerKill = 5f;

    [SerializeField, Tooltip("Log Overheat events to the console.")]
    private bool _logOverheat;

    [SerializeField, Tooltip("Accumulated scaling (increases when Overheat ends).")]
    private float _heatRequirementEscalation = 1f;

    [SerializeField] private float _currentHeat;

    private bool _intermediateBoostActive;

    /// <summary>Puntos actuales de heat.</summary>
    public float CurrentHeat => _currentHeat;

    /// <summary>Puntos necesarios para el primer tramo (0 → 80 % de barra), con escalado.</summary>
    public float PointsFirstSegment => _pointsToReachDisplay80 * _heatRequirementEscalation;

    /// <summary>Puntos necesarios para el segundo tramo (80 % → 100 % de barra), con escalado.</summary>
    public float PointsSecondSegment => _pointsFromDisplay80To100 * _heatRequirementEscalation;

    /// <summary>Total de puntos para disparar Overheat.</summary>
    public float TotalHeatCapacity => PointsFirstSegment + PointsSecondSegment;

    /// <summary>Compatibilidad UI: mismo significado que capacidad total actual.</summary>
    public float MaxHeat => TotalHeatCapacity;

    public float HeatPerKill => _heatPerKill;

    public float HeatRequirementEscalation => _heatRequirementEscalation;

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

    public void RegisterKill()
    {
        AddHeat(_heatPerKill);
    }

    public void AddHeat(float amount)
    {
        if (amount <= 0f)
            return;

        float cap = TotalHeatCapacity;
        bool wasBelowMax = _currentHeat < cap - 0.0001f;

        _currentHeat += amount;
        if (_currentHeat > cap)
            _currentHeat = cap;

        SyncIntermediateSwarmBoost();

        if (wasBelowMax && _currentHeat >= cap - 0.0001f)
        {
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

    /// <summary>Llama <see cref="OverheatManager"/> al terminar un Overheat: sube el requisito de puntos para el siguiente ciclo.</summary>
    public void ApplyEscalationAfterOverheat()
    {
        _heatRequirementEscalation *= _escalationPerOverheatCycle;
        _currentHeat = Mathf.Clamp(_currentHeat, 0f, TotalHeatCapacity);
        SyncIntermediateSwarmBoost();
        OnHeatChanged?.Invoke();
    }

    /// <summary>Para menú / nueva partida.</summary>
    public void ResetHeatProgressAndEscalation()
    {
        _heatRequirementEscalation = 1f;
        _currentHeat = 0f;
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
        _currentHeat = Mathf.Clamp(_currentHeat, 0f, TotalHeatCapacity);
    }
#endif
}
