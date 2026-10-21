using UnityEngine;

/// <summary>One exposure clock; airborne exits suspend it until Base or the phase resets it.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerPlatformState))]
public sealed class PlayerUpperLevelOverheat : MonoBehaviour
{
    [SerializeField] private PlayerPlatformState _platformState;
    [SerializeField] private PlayerHealth _health;
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField, Min(0f)] private float _graceSeconds = 2f;
    [SerializeField, Min(1)] private int _damagePerTick = 5;
    [SerializeField, Min(.1f)] private float _tickInterval = 2f;
    [SerializeField, Tooltip("Authored reusable fire and smoke; never created at runtime.")]
    private ParticleSystem[] _effects = System.Array.Empty<ParticleSystem>();

    private float _exposure;
    private float _tickRemaining;
    private bool _effectsPlaying;
    private bool _effectsPaused;

    private void Awake()
    {
        if (_platformState == null) _platformState = GetComponent<PlayerPlatformState>();
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_overheatManager == null) _overheatManager = FindAnyObjectByType<OverheatManager>();
    }

    private void OnEnable()
    {
        if (_health != null)
        {
            _health.OnPlayerDied += ResetExposure;
            _health.OnReset += ResetExposure;
        }
        if (_overheatManager != null) _overheatManager.OnOverheatFinished += HandleOverheatFinished;
        ResetExposure();
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.OnPlayerDied -= ResetExposure;
            _health.OnReset -= ResetExposure;
        }
        if (_overheatManager != null) _overheatManager.OnOverheatFinished -= HandleOverheatFinished;
        ResetExposure();
    }

    private void Update() => Tick(Time.deltaTime);

    private void Tick(float deltaTime)
    {
        if (_platformState == null || !_platformState.IsRunActive ||
            _overheatManager == null || !_overheatManager.IsOverheating)
        {
            ResetExposure();
            return;
        }

        // Always reconcile spatial exit before pause or a due hit, including a teleport.
        if (!_platformState.IsInsideUpper)
        {
            StopEffects();
            if (_platformState.IsSimulationActive && _platformState.IsBaseSupported) ResetExposure();
            return;
        }
        if (!_platformState.IsSimulationActive)
        {
            PauseEffects();
            return;
        }

        PlayEffects();
        float elapsed = Mathf.Max(0f, deltaTime);
        if (_exposure < _graceSeconds)
        {
            _exposure = Mathf.Min(_graceSeconds, _exposure + elapsed);
            if (_exposure < _graceSeconds) return;
            // First hit when the grace completes. Discard overshoot instead of catching up.
            _tickRemaining = 0f;
        }
        else _tickRemaining -= elapsed;

        if (_tickRemaining > 0f) return;
        _tickRemaining = Mathf.Max(.1f, _tickInterval);
        _health.TakeDamage(Mathf.Max(1, _damagePerTick));
    }

    private void HandleOverheatFinished(OverheatEndReason reason) => ResetExposure();

    private void ResetExposure()
    {
        _exposure = 0f;
        _tickRemaining = 0f;
        StopEffects();
    }

    private void PlayEffects()
    {
        if (_effectsPlaying && !_effectsPaused) return;
        foreach (ParticleSystem effect in _effects)
            if (effect != null && effect.gameObject.activeInHierarchy) effect.Play(false);
        _effectsPlaying = true;
        _effectsPaused = false;
    }

    private void PauseEffects()
    {
        if (!_effectsPlaying || _effectsPaused) return;
        foreach (ParticleSystem effect in _effects)
            if (effect != null) effect.Pause(false);
        _effectsPaused = true;
    }

    private void StopEffects()
    {
        foreach (ParticleSystem effect in _effects)
            if (effect != null) effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        _effectsPlaying = _effectsPaused = false;
    }
}
