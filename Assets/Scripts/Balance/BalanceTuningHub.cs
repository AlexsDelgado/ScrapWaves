using UnityEngine;

/// <summary>
/// Punto único de tuneo de spawn para el game designer. Reparte un <see cref="SpawnBalanceProfile"/>
/// a <see cref="DifficultyManager"/>, <see cref="HeatManager"/> y <see cref="OrbitalSpawner"/>, y expone
/// en un solo lugar los valores que realmente importan mientras se balancea.
/// Como el profile es un asset, lo que se toque en Play Mode no se pierde al salir.
/// Corre antes que los managers (DifficultyManager está en -45) para inyectar antes de la primera lectura.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-60)]
public class BalanceTuningHub : MonoBehaviour
{
    [SerializeField, Tooltip("El asset con todas las perillas de spawn. Se reparte a los managers al arrancar.")]
    private SpawnBalanceProfile _profile;

    [Header("Referencias (vacío = FindAnyObjectByType)")]
    [SerializeField] private DifficultyManager _difficultyManager;
    [SerializeField] private HeatManager _heatManager;
    [SerializeField] private OrbitalSpawner _orbitalSpawner;

    public SpawnBalanceProfile Profile => _profile;
    public DifficultyManager DifficultyManager => _difficultyManager;
    public HeatManager HeatManager => _heatManager;
    public OrbitalSpawner OrbitalSpawner => _orbitalSpawner;

    private void Awake()
    {
        ResolveReferences();
        ApplyProfile();
    }

    /// <summary>Resuelve las referencias sin tocar nada. También lo usa el Inspector fuera de Play Mode.</summary>
    public void ResolveReferences()
    {
        if (_difficultyManager == null)
            _difficultyManager = FindAnyObjectByType<DifficultyManager>(FindObjectsInactive.Include);
        if (_heatManager == null)
            _heatManager = FindAnyObjectByType<HeatManager>(FindObjectsInactive.Include);
        if (_orbitalSpawner == null)
            _orbitalSpawner = FindAnyObjectByType<OrbitalSpawner>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// Inyecta el profile en los managers que no tengan uno propio asignado a mano.
    /// </summary>
    public void ApplyProfile()
    {
        if (_profile == null)
            return;

        if (_difficultyManager != null && _difficultyManager.Profile == null)
            _difficultyManager.SetProfile(_profile);
        if (_heatManager != null && _heatManager.Profile == null)
            _heatManager.SetProfile(_profile);
        if (_orbitalSpawner != null && _orbitalSpawner.Profile == null)
            _orbitalSpawner.SetProfile(_profile);
    }

    // ---- Readout en vivo (lo consume BalanceTuningHubEditor) ----

    /// <summary>Intensidad 0–1 del escalado por tiempo.</summary>
    public float TimeIntensity => _difficultyManager != null ? _difficultyManager.CurrentIntensity : 0f;

    public float MinutesSinceScalingStarted =>
        _difficultyManager != null ? _difficultyManager.MinutesSinceScalingStarted : 0f;

    /// <summary>% lineal de heat: el eje X de la curva de escalado por heat.</summary>
    public float HeatRatio => _heatManager != null ? _heatManager.HeatRatio : 0f;

    /// <summary>% de la barra visible del HUD (por tramos, no lineal).</summary>
    public float HeatBarPercent => _heatManager != null ? _heatManager.NormalizedHeat * 100f : 0f;

    /// <summary>Intensidad 0–1 que sale de la curva de heat.</summary>
    public float HeatSpawnIntensity => _heatManager != null ? _heatManager.CurrentSpawnIntensity : 0f;

    /// <summary>Segundos reales entre oleadas con el heat actual.</summary>
    public float EffectiveSpawnIntervalSeconds =>
        _orbitalSpawner != null ? _orbitalSpawner.CurrentSpawnInterval : 0f;

    /// <summary>Multiplicador total sobre el BatchSize de la ruleta (tiempo x presión).</summary>
    public float EffectiveSpawnCountMultiplier
    {
        get
        {
            float diff = _difficultyManager != null ? _difficultyManager.GetSpawnCountMultiplier() : 1f;
            float heat = _heatManager != null ? _heatManager.GetSpawnCountMultiplier() : 1f;
            return diff * Mathf.Max(heat, OverheatSwarmBoost.ExitPressureSpawnMultiplier);
        }
    }

    public int ActiveEnemies => EnemyRegistry.ActiveCount;

    public int MaxActiveEnemies => _orbitalSpawner != null ? _orbitalSpawner.MaxActiveEnemies : 0;

    /// <summary>En qué estado está el spawneo ahora mismo, en una línea.</summary>
    public string SpawnStateLabel
    {
        get
        {
            if (_heatManager == null)
                return "sin HeatManager";
            if (_heatManager.IsSpawnScalingSuppressed)
                return "suprimido (fase de Overheat)";
            if (_heatManager.IsPostOverheatDecayActive)
            {
                return _heatManager.CurrentHeat >= _heatManager.PointsFirstSegment
                    ? "pausado (decay post-Overheat)"
                    : "reanudando (decay post-Overheat)";
            }
            return "normal";
        }
    }

    /// <summary>
    /// Pone el heat en un punto concreto de la BARRA visible (0–1), que es como lo piensa el designer.
    /// Los dos tramos cuestan lo mismo, así que 0.8 de barra equivale a ratio 0.5.
    /// </summary>
    public void SetHeatByBarPercent(float barPercent01)
    {
        if (_heatManager == null)
            return;

        float p = Mathf.Clamp01(barPercent01);
        float first = _heatManager.PointsFirstSegment;
        float second = _heatManager.PointsSecondSegment;

        float points = p <= 0.8f
            ? (0.8f > 0f ? p / 0.8f * first : 0f)
            : first + (p - 0.8f) / 0.2f * second;

        _heatManager.StopPostOverheatDecay();
        _heatManager.SetHeat(points);
    }

    /// <summary>
    /// Llena la barra vía <see cref="HeatManager.AddHeat"/> para que dispare el evento de Overheat.
    /// <see cref="HeatManager.SetHeat"/> no lo dispara, así que poner la barra al 100 % no alcanza.
    /// </summary>
    public void TriggerOverheat()
    {
        if (_heatManager == null)
            return;

        _heatManager.StopPostOverheatDecay();
        _heatManager.AddHeat(_heatManager.TotalHeatCapacity);
    }
}
