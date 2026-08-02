using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public sealed class WeaponPresentationController : MonoBehaviour, IWeaponFeedbackSink
{
    [Header("Presentation profile")]
    [SerializeField] private WeaponPresentationProfile _profile;
    [SerializeField] private ThirdPersonCamera _camera;
    [SerializeField] private AudioManager _audioManager;
    [SerializeField] private WeaponRecoilFeedback _recoilFeedback;

    [Header("Pooled audio")]
    [SerializeField, Range(1, 64)] private int _audioVoiceCount = 16;
    [SerializeField, Range(0f, 1f)] private float _audioSpatialBlend = 1f;

    [Header("Runtime switches")]
    [SerializeField] private GameFeelRuntimeOptions _runtimeOptions = new();

    [Header("Bounded feedback")]
    [SerializeField] private CameraFeedbackController _cameraFeedback = new();
    [SerializeField] private HitStopController _hitStop = new();

    private Transform _runtimeRoot;
    private CombatFeedbackDirector _director;
    private HeadHunterChargeVfx _debugChargeVfx;
    private bool _directorReady;

    public WeaponPresentationProfile Profile => _profile;
    public GameFeelRuntimeOptions RuntimeOptions => _runtimeOptions;
    public int ActiveLoopCount => _director?.ActiveLoopCount ?? 0;
    public int ActiveAudioVoiceCount => _director?.ActiveAudioVoiceCount ?? 0;
    public int AudioVoiceCapacity => _director?.AudioVoiceCapacity ?? 0;
    public int ActiveVfxCount => _director?.ActiveVfxCount ?? 0;
    public int TotalVfxCapacity => _director?.TotalVfxCapacity ?? 0;
    public int SuppressionCount => _director?.SuppressionCount ?? 0;

    public void Configure(
        WeaponPresentationProfile profile,
        ThirdPersonCamera camera,
        AudioManager audioManager,
        int audioVoiceCount = 16)
    {
        ReleaseRuntimeState();
        DestroyRuntimeRoot();
        _profile = profile;
        _camera = camera;
        _audioManager = audioManager;
        _audioVoiceCount = Mathf.Clamp(audioVoiceCount, 1, 64);
        _directorReady = false;
        BuildDirector();
    }

    public void SetProfile(WeaponPresentationProfile profile)
    {
        if (_profile == profile && _directorReady)
            return;

        ReleaseRuntimeState();
        DestroyRuntimeRoot();
        _profile = profile;
        _directorReady = false;
        ResolveSceneDependencies();
        BuildDirector();
    }

    public void Emit(in WeaponPresentationContext context)
    {
        TryEmitAtTime(in context, Time.unscaledTime);
    }

    public bool TryEmitAtTime(in WeaponPresentationContext context, float now)
    {
        EnsureDirector();
        return _director != null && _director.EmitLegacy(in context, ResolveSfxVolume(), now);
    }

    public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)
    {
        return BeginLoopAtTime(in context, Time.unscaledTime);
    }

    public WeaponPresentationLoopHandle BeginLoopAtTime(in WeaponPresentationContext context, float now)
    {
        EnsureDirector();
        return _director != null
            ? _director.BeginLegacyLoop(in context, ResolveSfxVolume(), now)
            : default;
    }

    public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
        _director?.UpdateLegacyLoop(handle, in context, ResolveSfxVolume());
    }

    public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
        _director?.EndLegacyLoop(handle, in context);
    }

    public void StopAllLoops()
    {
        _director?.StopAll();
    }

    public void OnChargeStarted(in WeaponFeedbackContext context)
    {
        EnsureDirector();
        _director?.BeginSemanticLoop(
            WeaponFeedbackEvent.ChargeStarted,
            in context,
            ResolveSfxVolume(),
            Time.unscaledTime);
        if (_runtimeOptions.DebugGeometryEnabled && context.Anchor != null)
        {
            DismissDebugCharge();
            float duration = _profile != null &&
                             _profile.TryResolveCue(WeaponFeedbackEvent.ChargeStarted, in context, out WeaponPresentationCueData cue)
                ? Mathf.Max(0.1f, cue.Duration)
                : 1f;
            _debugChargeVfx = HeadHunterChargeVfx.Spawn(context.Anchor, context.Direction, duration);
        }
    }

    public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress)
    {
        EnsureDirector();
        WeaponFeedbackContext scaled = new(
            context.Weapon,
            context.Mode,
            context.NormalizedHeat,
            context.Origin,
            context.Direction,
            context.ImpactPosition,
            context.ImpactNormal,
            context.DamageAmount,
            context.IsCritical,
            context.IsWeakPoint,
            context.IsKill,
            context.IsAbilityDamage,
            context.TargetClass,
            context.SurfaceType,
            context.ExplosionRadius,
            Mathf.Clamp01(normalizedProgress),
            context.Target,
            context.Anchor);
        _director?.UpdateSemanticLoop(
            WeaponFeedbackEvent.ChargeStarted,
            in scaled,
            ResolveSfxVolume());
        _debugChargeVfx?.SetChargeProgress(Mathf.Clamp01(normalizedProgress), context.Direction);
    }

    public void OnChargeCancelled(in WeaponFeedbackContext context)
    {
        _director?.EndSemanticLoop(WeaponFeedbackEvent.ChargeStarted, in context);
        DismissDebugCharge();
        EmitSemantic(WeaponFeedbackEvent.ChargeCancelled, in context);
    }

    public void OnShotFired(in WeaponFeedbackContext context)
    {
        DismissDebugCharge();
        EmitSemantic(WeaponFeedbackEvent.ShotFired, in context);
    }

    public void OnSustainedFireStarted(in WeaponFeedbackContext context)
    {
        EnsureDirector();
        _director?.BeginSemanticLoop(
            WeaponFeedbackEvent.SustainedFireStarted,
            in context,
            ResolveSfxVolume(),
            Time.unscaledTime);
    }

    public void OnSustainedFireStopped(in WeaponFeedbackContext context)
    {
        _director?.EndSemanticLoop(WeaponFeedbackEvent.SustainedFireStarted, in context);
        EmitSemantic(WeaponFeedbackEvent.SustainedFireStopped, in context);
    }

    public void OnProjectileImpact(in WeaponFeedbackContext context) =>
        EmitSemantic(WeaponFeedbackEvent.ProjectileImpact, in context);

    public void OnDamageConfirmed(in WeaponFeedbackContext context) =>
        EmitSemantic(WeaponFeedbackEvent.DamageConfirmed, in context);

    public void OnStatusApplied(in WeaponFeedbackContext context) =>
        EmitSemantic(WeaponFeedbackEvent.StatusApplied, in context);

    public void OnAmmoEmpty(in WeaponFeedbackContext context) =>
        EmitSemantic(WeaponFeedbackEvent.AmmoEmpty, in context);

    public void OnHeatThresholdCrossed(in WeaponFeedbackContext context, float normalizedThreshold)
    {
        WeaponFeedbackContext threshold = new(
            context.Weapon,
            context.Mode,
            Mathf.Clamp01(normalizedThreshold),
            context.Origin,
            context.Direction,
            context.ImpactPosition,
            context.ImpactNormal,
            context.DamageAmount,
            context.IsCritical,
            context.IsWeakPoint,
            context.IsKill,
            context.IsAbilityDamage,
            context.TargetClass,
            context.SurfaceType,
            context.ExplosionRadius,
            Mathf.Lerp(0.65f, 1.2f, Mathf.Clamp01(normalizedThreshold)),
            context.Target,
            context.Anchor);
        EmitSemantic(WeaponFeedbackEvent.HeatThresholdCrossed, in threshold);
    }

    public void ConfigureProjectile(
        Projectile projectile,
        ProjectilePresentationArchetypeId archetype,
        in WeaponFeedbackContext context)
    {
        EnsureDirector();
        _director?.ConfigureProjectile(projectile, archetype, in context);
    }

    public void SetVfxEnabled(bool value) => SetChannel(ref _runtimeOptions.VfxEnabled, value);
    public void SetAudioEnabled(bool value) => SetChannel(ref _runtimeOptions.AudioEnabled, value);
    public void SetCameraFeedbackEnabled(bool value) => SetChannel(ref _runtimeOptions.CameraFeedbackEnabled, value);
    public void SetHitStopEnabled(bool value) => SetChannel(ref _runtimeOptions.HitStopEnabled, value);
    public void SetEnemyReactionEnabled(bool value) => SetChannel(ref _runtimeOptions.EnemyReactionEnabled, value);
    public void SetHeatPresentationEnabled(bool value) => _runtimeOptions.HeatPresentationEnabled = value;
    public void SetProductionPresentationEnabled(bool value) => SetChannel(ref _runtimeOptions.ProductionPresentationEnabled, value);
    public void SetDebugGeometryEnabled(bool value)
    {
        _runtimeOptions.DebugGeometryEnabled = value;
        if (!value)
            DismissDebugCharge();
    }
    public void SetReducedShake(bool value) => _runtimeOptions.ReducedShake = value;
    public void SetReducedFlash(bool value) => _runtimeOptions.ReducedFlash = value;
    public void SetQuality(GameFeelQualityLevel value) => _runtimeOptions.Quality = value;

    private void OnEnable()
    {
        ResolveSceneDependencies();
        if (Application.isPlaying)
            EnsureDirector();
    }

    private void Update()
    {
        _director?.Tick(Time.unscaledTime, Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        ReleaseRuntimeState();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeState();
    }

    private void OnValidate()
    {
        _audioVoiceCount = Mathf.Clamp(_audioVoiceCount, 1, 64);
        _audioSpatialBlend = Mathf.Clamp01(_audioSpatialBlend);
        _runtimeOptions ??= new GameFeelRuntimeOptions();
        _cameraFeedback ??= new CameraFeedbackController();
        _hitStop ??= new HitStopController();
        _cameraFeedback.Sanitize();
        _hitStop.Sanitize();
    }

    private void EmitSemantic(WeaponFeedbackEvent feedbackEvent, in WeaponFeedbackContext context)
    {
        EnsureDirector();
        _director?.EmitSemantic(
            feedbackEvent,
            in context,
            ResolveSfxVolume(),
            Time.unscaledTime);
    }

    private void EnsureDirector()
    {
        if (!_directorReady)
            BuildDirector();
    }

    private void ResolveSceneDependencies()
    {
        if (_camera == null)
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
        if (_audioManager == null)
            _audioManager = AudioManager.Instance;
        if (_recoilFeedback == null)
            _recoilFeedback = GetComponent<WeaponRecoilFeedback>();
    }

    private void BuildDirector()
    {
        _directorReady = true;
        _director = null;
        if (_profile == null)
            return;

        _profile.RebuildCache();
        GameObject rootObject = new("Weapon Presentation Runtime");
        rootObject.transform.SetParent(transform, false);
        _runtimeRoot = rootObject.transform;
        _director = new CombatFeedbackDirector(
            _profile,
            _runtimeRoot,
            _camera,
            _recoilFeedback,
            _runtimeOptions,
            _cameraFeedback,
            _hitStop,
            _audioVoiceCount,
            _audioSpatialBlend);
    }

    private void ReleaseRuntimeState()
    {
        DismissDebugCharge();
        _director?.StopAll();
    }

    private void DestroyRuntimeRoot()
    {
        if (_runtimeRoot == null)
            return;

        GameObject rootObject = _runtimeRoot.gameObject;
        _runtimeRoot = null;
        _director = null;
        if (Application.isPlaying)
            Destroy(rootObject);
        else
            DestroyImmediate(rootObject);
    }

    private float ResolveSfxVolume()
    {
        return _audioManager != null ? _audioManager.SfxVolume : AudioManager.EffectiveSfxVolume;
    }

    private void SetChannel(ref bool channel, bool value)
    {
        if (channel == value)
            return;
        channel = value;
        if (!value)
            _director?.StopAll();
    }

    private void DismissDebugCharge()
    {
        if (_debugChargeVfx == null)
            return;
        _debugChargeVfx.Dismiss();
        _debugChargeVfx = null;
    }
}
