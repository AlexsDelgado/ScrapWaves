using UnityEngine;

/// <summary>Base resets the exposure clock; damage escalation persists until the Overheat event or run resets.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerPlatformState))]
public sealed class PlayerUpperLevelOverheat : MonoBehaviour
{
    [SerializeField] private PlayerPlatformState _platformState;
    [SerializeField] private PlayerHealth _health;
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField, Tooltip("The authored upper volume owns starting damage and the per-hit multiplier.")]
    private UpperLevelVolume _upperLevelVolume;
    [SerializeField, Min(0f)] private float _graceSeconds = 2f;
    [SerializeField, Min(.1f)] private float _tickInterval = 2f;
    [SerializeField, Tooltip("Authored reusable fire and smoke; never created at runtime.")]
    private ParticleSystem[] _effects = System.Array.Empty<ParticleSystem>();

    // Keep fractional growth here; never multiply the rounded integer submitted to PlayerHealth.
    private double _nextDamage;
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
            _health.OnPlayerDied += ResetHazard;
            _health.OnReset += ResetHazard;
        }
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += ResetHazard;
            _overheatManager.OnOverheatFinished += HandleOverheatFinished;
        }
        ResetHazard();
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.OnPlayerDied -= ResetHazard;
            _health.OnReset -= ResetHazard;
        }
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= ResetHazard;
            _overheatManager.OnOverheatFinished -= HandleOverheatFinished;
        }
        ResetHazard();
    }

    private void Update() => Tick(Time.deltaTime);

    private void Tick(float deltaTime)
    {
        if (_platformState == null || !_platformState.IsRunActive || _upperLevelVolume == null ||
            _overheatManager == null || !_overheatManager.IsOverheating)
        {
            ResetHazard();
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
        int damage = (int)System.Math.Min(int.MaxValue, System.Math.Max(1d, System.Math.Round(_nextDamage)));
        // Advance per hazard tick even when shields or invulnerability absorb it. Advance first so
        // synchronous death/reset callbacks from TakeDamage retain their freshly reset value.
        _nextDamage = System.Math.Min(int.MaxValue, _nextDamage * _upperLevelVolume.DamageMultiplier);
        _health.TakeDamage(damage);
    }

    private void HandleOverheatFinished(OverheatEndReason reason) => ResetHazard();

    private void ResetHazard()
    {
        _nextDamage = _upperLevelVolume != null ? _upperLevelVolume.StartingDamage : 1d;
        ResetExposure();
    }

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
