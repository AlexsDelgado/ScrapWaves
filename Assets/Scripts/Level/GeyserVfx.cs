using UnityEngine;

/// <summary>Continuous, bounded presentation for an always-ready traversal opening.</summary>
[DisallowMultipleComponent]
public sealed class GeyserVfx : MonoBehaviour
{
    public enum Kind { Trash, HotAir }
    [SerializeField] private Kind _kind;
    [SerializeField] private MapLaunchPad _launcher;
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
    [Header("Entry feedback")]
    [SerializeField, Range(4, 40)] private int _entryParticleCount = 28;
    [SerializeField, Range(.1f, 1f)] private float _entryAccentSeconds = .45f;
    [SerializeField, Range(1f, 2.5f)] private float _entryBrightness = 1.45f;
    [Header("Hot air only")]
    [SerializeField, Range(0f, .012f)] private float _heatDistortion = .0035f;

    private MaterialPropertyBlock _heatProperties;
    private MapLaunchPad _subscribedLauncher;
    private float _accentRemaining;
    private float _flowTime;
    public Kind GeyserKind => _kind;
    public int MaximumParticleBudget => _updraftBudget + (_kind == Kind.Trash ? _debrisBudget : 0) + 40;
    public int ActiveParticleCount => (_updraft != null ? _updraft.particleCount : 0) +
        (_debris != null && _kind == Kind.Trash ? _debris.particleCount : 0) + (_entryBurst != null ? _entryBurst.particleCount : 0);

    private void OnEnable()
    {
        ApplyTuning();
        BindLauncher(_launcher != null ? _launcher : GetComponentInParent<MapLaunchPad>());
        if (_updraft != null) _updraft.Play();
        if (_debris != null && _kind == Kind.Trash) _debris.Play();
    }

    private void OnDisable()
    {
        if (_subscribedLauncher != null) _subscribedLauncher.OnLaunched -= HandleLaunch;
        _subscribedLauncher = null;
        foreach (ParticleSystem particles in new[] { _updraft, _debris, _entryBurst })
            if (particles != null) particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
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
        _accentRemaining = _entryAccentSeconds;
        if (_entryBurst != null) { _entryBurst.Play(); _entryBurst.Emit(_entryParticleCount); }
        if (_kind == Kind.Trash && _debris != null) _debris.Emit(Mathf.Min(6, _debrisBudget));
        TickPresentation(0f);
    }

    private void Update() => TickPresentation(Time.deltaTime);

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
            if (_kind != Kind.Trash) _debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (_entryBurst != null)
        {
            var main = _entryBurst.main; main.maxParticles = 40; main.loop = false;
            main.playOnAwake = false; main.startColor = new Color(_flowColor.r, _flowColor.g, _flowColor.b, .48f);
            var emission = _entryBurst.emission; emission.rateOverTime = 0f;
        }
        if (_heatShimmer != null) _heatShimmer.enabled = _kind == Kind.HotAir && _heatDistortion > 0f;
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
