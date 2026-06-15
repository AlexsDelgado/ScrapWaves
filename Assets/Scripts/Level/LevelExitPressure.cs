using System;
using UnityEngine;

/// <summary>
/// Tras reunir todas las llaves: overheat permanente, sin bosses/elites de overheat,
/// y spawn rate ×2 → ×3 → ×4 cada minuto.
/// </summary>
[DisallowMultipleComponent]
public class LevelExitPressure : MonoBehaviour
{
    [SerializeField] private LevelExitObjective _exitObjective;
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private BossManager _bossManager;
    [SerializeField] private OverheatEliteWaveSpawner _eliteSpawner;

    [SerializeField, Min(0.1f), Tooltip("Minutos entre cada escalón de presión.")]
    private float _minutesPerTier = 1f;

    [SerializeField, Tooltip("Multiplicadores de spawn por escalón (×2, ×3, ×4…).")]
    private float[] _spawnMultipliers = { 2f, 3f, 4f };

    private bool _active;
    private float _startTime;
    private int _currentTier = -1;

    public bool IsActive => _active;
    public float CurrentSpawnMultiplier { get; private set; } = 1f;

    public event Action<float> OnPressureTierChanged;

    private void Awake()
    {
        if (_exitObjective == null)
            _exitObjective = FindAnyObjectByType<LevelExitObjective>();
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_bossManager == null)
            _bossManager = FindAnyObjectByType<BossManager>();
        if (_eliteSpawner == null)
            _eliteSpawner = FindAnyObjectByType<OverheatEliteWaveSpawner>();
    }

    private void OnEnable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected += ActivatePressure;
    }

    private void OnDisable()
    {
        if (_exitObjective != null)
            _exitObjective.OnAllKeysCollected -= ActivatePressure;

        if (_active)
            ExitSpawnPressure.SetActive(false, 1f);
    }

    private void Update()
    {
        if (!_active)
            return;

        float elapsedMinutes = (Time.time - _startTime) / 60f;
        int tier = Mathf.Min(
            Mathf.FloorToInt(elapsedMinutes / Mathf.Max(0.01f, _minutesPerTier)),
            Mathf.Max(0, _spawnMultipliers.Length - 1));

        if (tier == _currentTier)
            return;

        _currentTier = tier;
        float mult = _spawnMultipliers[Mathf.Clamp(tier, 0, _spawnMultipliers.Length - 1)];
        CurrentSpawnMultiplier = mult;
        ExitSpawnPressure.SetMultiplier(mult);
        OnPressureTierChanged?.Invoke(mult);
    }

    private void ActivatePressure()
    {
        if (_active)
            return;

        _active = true;
        _startTime = Time.time;
        _currentTier = -1;

        _bossManager?.SetExitPhaseActive(true);
        _eliteSpawner?.SetExitPhaseDisabled(true);
        _overheatManager?.EnterPermanentOverheat();

        float initial = _spawnMultipliers.Length > 0 ? _spawnMultipliers[0] : 2f;
        ExitSpawnPressure.SetActive(true, initial);
        CurrentSpawnMultiplier = initial;
        _currentTier = 0;
        OnPressureTierChanged?.Invoke(initial);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_minutesPerTier < 0.1f)
            _minutesPerTier = 0.1f;
        if (_spawnMultipliers == null || _spawnMultipliers.Length == 0)
            _spawnMultipliers = new[] { 2f, 3f, 4f };
    }
#endif
}
