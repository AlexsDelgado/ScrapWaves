using UnityEngine;

/// <summary>Continuous, bounded presentation for a traversal opening outside overheat.</summary>
[DisallowMultipleComponent]
public sealed class GeyserVfx : MonoBehaviour
{
    public enum Kind { Trash, HotAir }
    [SerializeField] private Kind _kind;
    [SerializeField] private MapLaunchPad _launcher;
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private ParticleSystem _updraft;
    [SerializeField] private ParticleSystem _debris;
    [SerializeField] private ParticleSystem _entryBurst;
    [SerializeField] private Renderer _heatShimmer;
    [Header("Continuous updraft")]
    [SerializeField] private Color _flowColor = new(.83f, .79f, .64f, .30f);
    [SerializeField, Range(1f, 24f)] private float _particlesPerSecond = 12f;
    [SerializeField, Range(.5f, 5f)] private float _upwardSpeed = 2.2f;
    [SerializeField, Range(1f, 5f)] private float _lifetime = 3f;
    [SerializeField, Range(.25f, 2f)] private float _wispSize = .9f;
    [SerializeField, Range(.5f, 2f)] private float _openingRadius = 1.25f;
    [SerializeField, Range(8, 64)] private int _updraftBudget = 48;
    [Header("Trash only")]
    [SerializeField, Range(0f, 8f)] private float _debrisPerSecond = 4f;
    [SerializeField, Range(4, 24)] private int _debrisBudget = 20;
    [SerializeField, Range(.1f, .7f), Tooltip("Width in metres of the existing one-metre scrap mesh, before particle size variation.")]
    private float _debrisSize = .36f;
    [SerializeField, Range(.25f, 2f), Tooltip("Multiplier for bounded, nonzero tumbling on all three axes.")]
    private float _debrisTumbleRate = 1f;
    [Header("Entry feedback")]
    [SerializeField, Range(4, 40)] private int _entryParticleCount = 28;
    [SerializeField, Range(.1f, 1f)] private float _entryAccentSeconds = .45f;
    [SerializeField, Range(1f, 2.5f)] private float _entryBrightness = 1.45f;
    [Header("Hot air only")]
    [SerializeField, Range(0f, .012f)] private float _heatDistortion = .0035f;

    private MaterialPropertyBlock _heatProperties;
    private MapLaunchPad _subscribedLauncher;
    private OverheatManager _subscribedOverheat;
    private bool _overheatSuppressed;
    private bool _resumeUpdraft;
    private bool _resumeDebris;
    private bool _updraftEmissionEnabled;
    private bool _debrisEmissionEnabled;
    private bool _entryEmissionEnabled;
    private bool _shimmerEnabled;
    private float _accentRemaining;
    private float _flowTime;
    public Kind GeyserKind => _kind;
    public int MaximumParticleBudget => _updraftBudget + (_kind == Kind.Trash ? _debrisBudget : 0) + 40;
    public int ActiveParticleCount => (_updraft != null ? _updraft.particleCount : 0) +
        (_debris != null && _kind == Kind.Trash ? _debris.particleCount : 0) + (_entryBurst != null ? _entryBurst.particleCount : 0);

    private void OnEnable()
    {
        BindLauncher(_launcher != null ? _launcher : GetComponentInParent<MapLaunchPad>());
        // Retain a suppression snapshot across disable/enable. Re-tuning or
        // starting loops here would overwrite a deliberately stopped effect.
        if (!_overheatSuppressed)
        {
            ApplyTuning();
            if (_updraft != null && _updraft.gameObject.activeInHierarchy) _updraft.Play();
            if (_debris != null && _kind == Kind.Trash && _debris.gameObject.activeInHierarchy) _debris.Play();
        }
        BindOverheat();
        RefreshOverheatSuppression();
    }

    private void OnDisable()
    {
        if (_subscribedLauncher != null) _subscribedLauncher.OnLaunched -= HandleLaunch;
        _subscribedLauncher = null;
        UnbindOverheat();
        ClearParticles();
        if (_heatShimmer != null) _heatShimmer.enabled = false;
    }

    public void BindLauncher(MapLaunchPad launcher)
    {
        if (_subscribedLauncher != null) _subscribedLauncher.OnLaunched -= HandleLaunch;
        _launcher = launcher;
        _subscribedLauncher = isActiveAndEnabled ? launcher : null;
        if (_subscribedLauncher != null) _subscribedLauncher.OnLaunched += HandleLaunch;
    }

    private void HandleLaunch(PlayerMovement movement) => PlayEntryBurst();

    public void PlayEntryBurst()
    {
        if (!isActiveAndEnabled)
            return;
        RefreshOverheatSuppression();
        if (_overheatSuppressed)
            return;

        _accentRemaining = _entryAccentSeconds;
        if (_entryBurst != null) { _entryBurst.Play(); _entryBurst.Emit(_entryParticleCount); }
        if (_kind == Kind.Trash && _debris != null) _debris.Emit(Mathf.Min(6, _debrisBudget));
        TickPresentation(0f);
    }

    private void Update()
    {
        // Also reconcile when enabled after a phase started, or after a manager
        // is replaced. The ordinary-to-permanent transition need not emit an event.
        if (_overheatManager == null) BindOverheat();
        RefreshOverheatSuppression();
        if (!_overheatSuppressed) TickPresentation(Time.deltaTime);
    }

    private void BindOverheat()
    {
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_subscribedOverheat == _overheatManager)
            return;
        UnbindOverheat();
        _subscribedOverheat = _overheatManager;
        if (_subscribedOverheat == null)
            return;
        _subscribedOverheat.OnOverheatStarted += RefreshOverheatSuppression;
        _subscribedOverheat.OnOverheatFinished += HandleOverheatFinished;
    }

    private void UnbindOverheat()
    {
        if (_subscribedOverheat != null)
        {
            _subscribedOverheat.OnOverheatStarted -= RefreshOverheatSuppression;
            _subscribedOverheat.OnOverheatFinished -= HandleOverheatFinished;
        }
        _subscribedOverheat = null;
    }

    private void HandleOverheatFinished(OverheatEndReason _) => RefreshOverheatSuppression();

    private void RefreshOverheatSuppression()
    {
        bool suppressed = _overheatManager != null && _overheatManager.IsOverheating;
        if (suppressed == _overheatSuppressed)
            return;

        _overheatSuppressed = suppressed;
        if (suppressed)
        {
            _resumeUpdraft = _updraft != null && _updraft.isPlaying && _updraft.gameObject.activeInHierarchy;
            _resumeDebris = _debris != null && _kind == Kind.Trash && _debris.isPlaying && _debris.gameObject.activeInHierarchy;
            _updraftEmissionEnabled = _updraft != null && _updraft.emission.enabled;
            _debrisEmissionEnabled = _debris != null && _debris.emission.enabled;
            _entryEmissionEnabled = _entryBurst != null && _entryBurst.emission.enabled;
            _shimmerEnabled = _heatShimmer != null && _heatShimmer.enabled;
            SuppressEffects();
            return;
        }

        SetEmission(_updraft, _updraftEmissionEnabled);
        SetEmission(_debris, _debrisEmissionEnabled);
        SetEmission(_entryBurst, _entryEmissionEnabled);
        if (_heatShimmer != null) _heatShimmer.enabled = _shimmerEnabled;
        if (!isActiveAndEnabled)
            return;
        if (_resumeUpdraft && _updraft != null && _updraft.gameObject.activeInHierarchy) _updraft.Play();
        if (_resumeDebris && _debris != null && _debris.gameObject.activeInHierarchy) _debris.Play();
        // Entry feedback is a one-shot: never replay it when a phase ends.
    }

    private void SuppressEffects()
    {
        _accentRemaining = 0f;
        SetEmission(_updraft, false);
        SetEmission(_debris, false);
        SetEmission(_entryBurst, false);
        ClearParticles();
        if (_heatShimmer != null) _heatShimmer.enabled = false;
    }

    private void ClearParticles()
    {
        if (_updraft != null) _updraft.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_debris != null) _debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_entryBurst != null) _entryBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static void SetEmission(ParticleSystem particles, bool enabled)
    {
        if (particles == null) return;
        var emission = particles.emission;
        emission.enabled = enabled;
    }

    private void TickPresentation(float deltaTime)
    {
        _flowTime += Mathf.Max(0f, deltaTime);
        _accentRemaining = Mathf.Max(0f, _accentRemaining - deltaTime);
        float accent = _entryAccentSeconds > 0f ? _accentRemaining / _entryAccentSeconds : 0f;
        if (_updraft != null)
        {
            var main = _updraft.main;
            Color color = _flowColor;
            float brightness = Mathf.Lerp(1f, _entryBrightness, accent);
            color.r *= brightness; color.g *= brightness; color.b *= brightness;
            main.startColor = color;
        }
        if (_heatShimmer != null && _kind == Kind.HotAir)
        {
            _heatProperties ??= new MaterialPropertyBlock();
            _heatProperties.SetFloat("_FlowTime", _flowTime);
            _heatProperties.SetFloat("_Distortion", _heatDistortion * Mathf.Lerp(1f, 1.5f, accent));
            _heatShimmer.SetPropertyBlock(_heatProperties);
        }
    }

    public void ApplyTuning()
    {
        // Runtime tuning must not restart emission or replace the pre-overheat snapshot.
        if (_subscribedOverheat != null) RefreshOverheatSuppression();
        if (_updraft != null)
        {
            var main = _updraft.main;
            main.loop = true; main.playOnAwake = true; main.startDelay = 0f;
            main.maxParticles = _updraftBudget; main.startSpeed = _upwardSpeed;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetime * .8f, _lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(_wispSize * .75f, _wispSize * 1.2f);
            main.startColor = _flowColor; main.useUnscaledTime = false;
            var emission = _updraft.emission; emission.enabled = true; emission.rateOverTime = _particlesPerSecond;
            var shape = _updraft.shape; shape.radius = _openingRadius;
        }
        if (_debris != null)
        {
            var main = _debris.main; main.maxParticles = _debrisBudget;
            main.loop = true; main.playOnAwake = _kind == Kind.Trash; main.startDelay = 0f;
            var emission = _debris.emission; emission.rateOverTime = _kind == Kind.Trash ? _debrisPerSecond : 0f;
            if (_kind == Kind.Trash)
            {
                main.startSize = new ParticleSystem.MinMaxCurve(_debrisSize * .75f, _debrisSize * 1.25f);
                main.startRotation3D = true;
                var rotation = _debris.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = true;
                rotation.x = new ParticleSystem.MinMaxCurve(1.8f * _debrisTumbleRate, 3.2f * _debrisTumbleRate);
                rotation.y = new ParticleSystem.MinMaxCurve(-4f * _debrisTumbleRate, -2.2f * _debrisTumbleRate);
                rotation.z = new ParticleSystem.MinMaxCurve(.9f * _debrisTumbleRate, 2.5f * _debrisTumbleRate);
            }
            if (_kind != Kind.Trash) _debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (_entryBurst != null)
        {
            var main = _entryBurst.main; main.maxParticles = 40; main.loop = false;
            main.playOnAwake = false; main.startColor = new Color(_flowColor.r, _flowColor.g, _flowColor.b, .48f);
            var emission = _entryBurst.emission; emission.rateOverTime = 0f;
        }
        if (_heatShimmer != null) _heatShimmer.enabled = _kind == Kind.HotAir && _heatDistortion > 0f;
        if (_overheatSuppressed) SuppressEffects();
        TickPresentation(0f);
    }

#if UNITY_EDITOR
    private void OnValidate() => ApplyTuning();
    public void ConfigureAuthoring(Kind kind, ParticleSystem updraft, ParticleSystem debris, ParticleSystem burst, Renderer shimmer)
    {
        _kind = kind; _updraft = updraft; _debris = debris; _entryBurst = burst; _heatShimmer = shimmer;
        _flowColor = kind == Kind.Trash ? new Color(.83f,.79f,.64f,.30f) : new Color(.83f,.89f,.91f,.25f);
        _particlesPerSecond = kind == Kind.Trash ? 12f : 14f;
        ApplyTuning();
    }
    public void SetPreviewClock(float seconds) { _flowTime = Mathf.Max(0f, seconds); TickPresentation(0f); }
#endif
}
