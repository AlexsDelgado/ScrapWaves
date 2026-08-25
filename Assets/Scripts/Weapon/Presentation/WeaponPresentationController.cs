using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public sealed class WeaponPresentationController : MonoBehaviour, IWeaponFeedbackSink,
    ICombatTextStatusLifecycleSink
{
    private sealed class DirectorRuntime
    {
        public CombatFeedbackDirector Director;
    }

    private readonly struct LegacyLoopRoute
    {
        public readonly CombatFeedbackDirector Director;
        public readonly WeaponPresentationLoopHandle InternalHandle;

        public LegacyLoopRoute(CombatFeedbackDirector director, WeaponPresentationLoopHandle internalHandle)
        {
            Director = director;
            InternalHandle = internalHandle;
        }
    }

    [Header("Presentation profile")]
    [SerializeField] private WeaponPresentationProfile _profile;
    [SerializeField] private ThirdPersonCamera _camera;
    [SerializeField] private AudioManager _audioManager;
    [SerializeField] private WeaponRecoilFeedback _recoilFeedback;
    [SerializeField] private CombatTextProfile _combatTextProfile;

    [Header("Pooled audio")]
    [SerializeField, Range(1, 64)] private int _audioVoiceCount = 16;
    [SerializeField, Range(0f, 1f)] private float _audioSpatialBlend = 1f;

    [Header("Runtime switches")]
    [SerializeField] private GameFeelRuntimeOptions _runtimeOptions = new();

    [Header("Bounded feedback")]
    [SerializeField] private CameraFeedbackController _cameraFeedback = new();
    [SerializeField] private HitStopController _hitStop = new();

    private readonly Dictionary<WeaponPresentationProfile, DirectorRuntime> _directors = new();
    private readonly Dictionary<int, LegacyLoopRoute> _legacyLoopRoutes = new();
    private Transform _runtimeRoot;
    private CombatTextDirector _combatText;
    private bool? _combatTextCompactOverride;
    private bool _hasLocalAccessibilityOverride;
    private HeadHunterChargeVfx _debugChargeVfx;
    private int _nextLegacyLoopId = 1;

    public WeaponPresentationProfile Profile => _profile;
    public GameFeelRuntimeOptions RuntimeOptions => _runtimeOptions;
    public int ActiveLoopCount => SumActiveLoops();
    public int ActiveAudioVoiceCount => SumActiveAudioVoices();
    public int AudioVoiceCapacity => SumAudioVoiceCapacity();
    public int ActiveVfxCount => SumActiveVfx();
    public int TotalVfxCapacity => SumVfxCapacity();
    public int SuppressionCount => SumSuppressions();
    public CombatTextMetrics CombatTextMetrics => _combatText?.Metrics;
    public int ActiveCombatTextCount => _combatText?.ActiveViewCount ?? 0;
    public bool HasLocalAccessibilityOverride => _hasLocalAccessibilityOverride;
    public bool CombatTextCompactFormatting => _combatText?.CompactLargeNumbers ??
        _combatTextCompactOverride ?? (_combatTextProfile == null || _combatTextProfile.CompactLargeNumbers);
    public PresentationAccessibilityState AppliedAccessibilityState => new(
        _runtimeOptions.ReducedMotion,
        _runtimeOptions.ReducedShake,
        _runtimeOptions.ReducedFlash,
        _runtimeOptions.CombatText,
        _runtimeOptions.CombatTextScale);

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
        RegisterProfile(profile);
    }

    // Sets the fallback profile used by the single-weapon sandbox and legacy calls
    // that do not carry a WeaponInstance. Gameplay weapon contexts route by their
    // own WeaponData profile and therefore never replace one another.
    public void SetProfile(WeaponPresentationProfile profile)
    {
        if (_profile == profile && (profile == null || _directors.ContainsKey(profile)))
            return;

        ReleaseRuntimeState();
        DestroyRuntimeRoot();
        _profile = profile;
        ResolveSceneDependencies();
        RegisterProfile(profile);
    }

    public void RegisterProfile(WeaponPresentationProfile profile)
    {
        GetOrCreateDirector(profile);
    }

    public void Emit(in WeaponPresentationContext context)
    {
        TryEmitAtTime(in context, Time.unscaledTime);
    }

    public bool TryEmitAtTime(in WeaponPresentationContext context, float now)
    {
        CombatFeedbackDirector director = ResolveDirector(context.Weapon);
        return director != null && director.EmitLegacy(in context, ResolveSfxVolume(), now);
    }

    public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)
    {
        return BeginLoopAtTime(in context, Time.unscaledTime);
    }

    public WeaponPresentationLoopHandle BeginLoopAtTime(in WeaponPresentationContext context, float now)
    {
        CombatFeedbackDirector director = ResolveDirector(context.Weapon);
        if (director == null)
            return default;

        WeaponPresentationLoopHandle internalHandle = director.BeginLegacyLoop(in context, ResolveSfxVolume(), now);
        if (!internalHandle.IsValid)
            return default;

        int id = GetNextLegacyLoopId();
        _legacyLoopRoutes.Add(id, new LegacyLoopRoute(director, internalHandle));
        return new WeaponPresentationLoopHandle(id);
    }

    public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
        if (handle.IsValid && _legacyLoopRoutes.TryGetValue(handle.Id, out LegacyLoopRoute route))
            route.Director.UpdateLegacyLoop(route.InternalHandle, in context, ResolveSfxVolume());
    }

    public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
        if (!handle.IsValid || !_legacyLoopRoutes.TryGetValue(handle.Id, out LegacyLoopRoute route))
            return;

        route.Director.EndLegacyLoop(route.InternalHandle, in context);
        _legacyLoopRoutes.Remove(handle.Id);
    }

    public void StopAllLoops()
    {
        ReleaseRuntimeState();
    }

    public void OnChargeStarted(in WeaponFeedbackContext context)
    {
        CombatFeedbackDirector director = ResolveDirector(context.Weapon);
        director?.BeginSemanticLoop(
            WeaponFeedbackEvent.ChargeStarted,
            in context,
            ResolveSfxVolume(),
            Time.unscaledTime);
        if (_runtimeOptions.DebugGeometryEnabled && context.Anchor != null)
        {
            DismissDebugCharge();
            WeaponPresentationProfile profile = ResolveProfile(context.Weapon);
            float duration = profile != null &&
                             profile.TryResolveCue(WeaponFeedbackEvent.ChargeStarted, in context, out WeaponPresentationCueData cue)
                ? Mathf.Max(0.1f, cue.Duration)
                : 1f;
            _debugChargeVfx = HeadHunterChargeVfx.Spawn(context.Anchor, context.Direction, duration);
        }
    }

    public void OnChargeUpdated(in WeaponFeedbackContext context, float normalizedProgress)
    {
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
            context.Anchor,
            context.ReferenceDamage,
            context.ActionSequenceId,
            context.DamageKind,
            context.StatusInstanceId,
            context.StatusKind,
            context.SegmentIndex);
        ResolveDirector(context.Weapon)?.UpdateSemanticLoop(
            WeaponFeedbackEvent.ChargeStarted,
            in scaled,
            ResolveSfxVolume());
        _debugChargeVfx?.SetChargeProgress(Mathf.Clamp01(normalizedProgress), context.Direction);
    }

    public void OnChargeCancelled(in WeaponFeedbackContext context)
    {
        ResolveDirector(context.Weapon)?.EndSemanticLoop(WeaponFeedbackEvent.ChargeStarted, in context);
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
        ResolveDirector(context.Weapon)?.BeginSemanticLoop(
            WeaponFeedbackEvent.SustainedFireStarted,
            in context,
            ResolveSfxVolume(),
            Time.unscaledTime);
    }

    public void OnSustainedFireStopped(in WeaponFeedbackContext context)
    {
        ResolveDirector(context.Weapon)?.EndSemanticLoop(WeaponFeedbackEvent.SustainedFireStarted, in context);
        EmitSemantic(WeaponFeedbackEvent.SustainedFireStopped, in context);
    }

    public void OnProjectileImpact(in WeaponFeedbackContext context) =>
        EmitSemantic(WeaponFeedbackEvent.ProjectileImpact, in context);

    public void OnDamageConfirmed(in WeaponFeedbackContext context)
    {
        EnsureCombatTextDirector();
        CombatFeedbackDirector director = ResolveDirector(context.Weapon);
        if (director != null)
        {
            director.EmitSemantic(
                WeaponFeedbackEvent.DamageConfirmed,
                in context,
                ResolveSfxVolume(),
                Time.unscaledTime);
            return;
        }

        _combatText?.TryEmit(in context, Time.unscaledTime);
    }

    public void OnStatusSegmentClosed(
        Transform target,
        WeaponStatusKind statusKind,
        int statusInstanceId,
        int segmentIndex)
    {
        if (statusInstanceId <= 0)
            return;
        EnsureCombatTextDirector();
        _combatText?.NotifyStatusSegmentClosed(
            target,
            statusKind,
            statusInstanceId,
            segmentIndex,
            Time.unscaledTime);
    }

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
            context.Anchor,
            context.ReferenceDamage,
            context.ActionSequenceId,
            context.DamageKind,
            context.StatusInstanceId,
            context.StatusKind,
            context.SegmentIndex);
        EmitSemantic(WeaponFeedbackEvent.HeatThresholdCrossed, in threshold);
    }

    public void ConfigureProjectile(
        Projectile projectile,
        ProjectilePresentationArchetypeId archetype,
        in WeaponFeedbackContext context)
    {
        ResolveDirector(context.Weapon)?.ConfigureProjectile(projectile, archetype, in context);
    }

    public void SetVfxEnabled(bool value) => SetChannel(ref _runtimeOptions.VfxEnabled, value);
    public void SetAudioEnabled(bool value) => SetChannel(ref _runtimeOptions.AudioEnabled, value);
    public void SetCameraFeedbackEnabled(bool value) => SetChannel(ref _runtimeOptions.CameraFeedbackEnabled, value);
    public void SetHitStopEnabled(bool value) => SetChannel(ref _runtimeOptions.HitStopEnabled, value);
    public void SetEnemyReactionEnabled(bool value)
    {
        SetChannel(ref _runtimeOptions.EnemyReactionEnabled, value);
        EnemyReactionRuntime.Apply(_runtimeOptions);
    }
    public void SetHeatPresentationEnabled(bool value) => _runtimeOptions.HeatPresentationEnabled = value;
    public void SetProductionPresentationEnabled(bool value) => SetChannel(ref _runtimeOptions.ProductionPresentationEnabled, value);
    public void SetDebugGeometryEnabled(bool value)
    {
        _runtimeOptions.DebugGeometryEnabled = value;
        if (!value)
            DismissDebugCharge();
    }
    public void SetReducedShake(bool value)
    {
        if (_runtimeOptions.ReducedShake == value)
            return;
        _hasLocalAccessibilityOverride = true;
        _runtimeOptions.ReducedShake = value;
    }
    public void SetReducedMotion(bool value)
    {
        if (_runtimeOptions.ReducedMotion == value)
            return;
        _hasLocalAccessibilityOverride = true;
        _runtimeOptions.ReducedMotion = value;
        EnemyReactionRuntime.Apply(_runtimeOptions);
    }
    public void SetReducedFlash(bool value)
    {
        if (_runtimeOptions.ReducedFlash == value)
            return;
        _hasLocalAccessibilityOverride = true;
        _runtimeOptions.ReducedFlash = value;
        EnemyReactionRuntime.Apply(_runtimeOptions);
    }
    public void SetCombatTextMode(CombatTextMode value)
    {
        CombatTextMode sanitized = PresentationAccessibilitySettings.SanitizeCombatTextMode(value);
        if (_runtimeOptions.CombatText == sanitized)
            return;
        _hasLocalAccessibilityOverride = true;
        _runtimeOptions.CombatText = sanitized;
    }
    public void SetCombatTextScale(float value)
    {
        float sanitized = PresentationAccessibilitySettings.SanitizeCombatTextScale(value);
        if (Mathf.Approximately(_runtimeOptions.CombatTextScale, sanitized))
            return;
        _hasLocalAccessibilityOverride = true;
        _runtimeOptions.CombatTextScale = sanitized;
    }
    public void ApplyPersistedAccessibility()
    {
        _hasLocalAccessibilityOverride = false;
        ApplyAccessibilityState(PresentationAccessibilityRuntime.Current);
    }
    public void SetCombatTextCompactFormatting(bool compact)
    {
        _combatTextCompactOverride = compact;
        _combatText?.SetCompactLargeNumbersOverride(compact);
    }
    public void ResetCombatTextMetrics() => _combatText?.ResetMetrics();
    public void SetQuality(GameFeelQualityLevel value)
    {
        if (_runtimeOptions.Quality == value)
            return;
        _runtimeOptions.Quality = value;
        EnemyReactionRuntime.Apply(_runtimeOptions);
        if (!Application.isPlaying)
            return;

        // Quality-specific pools are fixed after construction, so a deliberate
        // quality switch rebuilds them instead of growing during combat.
        ReleaseRuntimeState();
        DestroyRuntimeRoot();
        EnsureCombatTextDirector();
        RegisterProfile(_profile);
    }

    private void OnEnable()
    {
        PresentationAccessibilityRuntime.Changed += HandleAccessibilityChanged;
        HandleAccessibilityChanged(PresentationAccessibilityRuntime.Current);
        ResolveSceneDependencies();
        EnemyReactionRuntime.Apply(_runtimeOptions);
        if (Application.isPlaying)
        {
            EnsureCombatTextDirector();
            RegisterProfile(_profile);
        }
    }

    private void Update()
    {
        bool hitStopWasActive = _hitStop.IsActive;
        bool tickSharedState = true;
        foreach (DirectorRuntime runtime in _directors.Values)
        {
            runtime.Director.Tick(Time.unscaledTime, Time.unscaledDeltaTime, tickSharedState);
            tickSharedState = false;
        }
        bool pausedOutsideHitStop = Time.timeScale <= 0f && !hitStopWasActive;
        if (!pausedOutsideHitStop)
            _combatText?.Tick(Time.unscaledTime, Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        PresentationAccessibilityRuntime.Changed -= HandleAccessibilityChanged;
        ReleaseRuntimeState();
        _combatText?.StopAll();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeState();
        _combatText?.Dispose();
        _combatText = null;
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
        EnemyReactionRuntime.Apply(_runtimeOptions);
    }

    private void EmitSemantic(WeaponFeedbackEvent feedbackEvent, in WeaponFeedbackContext context)
    {
        ResolveDirector(context.Weapon)?.EmitSemantic(
            feedbackEvent,
            in context,
            ResolveSfxVolume(),
            Time.unscaledTime);
    }

    private CombatFeedbackDirector ResolveDirector(WeaponInstance weapon)
    {
        return GetOrCreateDirector(ResolveProfile(weapon));
    }

    private WeaponPresentationProfile ResolveProfile(WeaponInstance weapon)
    {
        return weapon?.Data?.PresentationProfile != null
            ? weapon.Data.PresentationProfile
            : _profile;
    }

    private CombatFeedbackDirector GetOrCreateDirector(WeaponPresentationProfile profile)
    {
        if (profile == null)
            return null;
        if (_directors.TryGetValue(profile, out DirectorRuntime existing))
            return existing.Director;

        ResolveSceneDependencies();
        profile.RebuildCache();
        EnsureRuntimeRoot();
        EnsureCombatTextDirector();
        GameObject profileRootObject = new($"{profile.name} Presentation");
        profileRootObject.transform.SetParent(_runtimeRoot, false);
        CombatFeedbackDirector director = new(
            profile,
            profileRootObject.transform,
            _camera,
            _recoilFeedback,
            _runtimeOptions,
            _cameraFeedback,
            _hitStop,
            _audioVoiceCount,
            _audioSpatialBlend,
            _combatText);
        _directors.Add(profile, new DirectorRuntime
        {
            Director = director
        });
        return director;
    }

    private void EnsureRuntimeRoot()
    {
        if (_runtimeRoot != null)
            return;
        GameObject rootObject = new("Weapon Presentation Runtime");
        rootObject.transform.SetParent(transform, false);
        _runtimeRoot = rootObject.transform;
    }

    private void EnsureCombatTextDirector()
    {
        if (_combatText != null)
        {
            _combatText.SetCamera(ResolveRenderingCamera());
            return;
        }

        ResolveSceneDependencies();
        EnsureRuntimeRoot();
        _combatText = new CombatTextDirector(
            _runtimeRoot,
            ResolveRenderingCamera(),
            _combatTextProfile,
            _runtimeOptions);
        if (_combatTextCompactOverride.HasValue)
            _combatText.SetCompactLargeNumbersOverride(_combatTextCompactOverride.Value);
    }

    private Camera ResolveRenderingCamera()
    {
        if (_camera != null && _camera.TryGetComponent(out Camera renderingCamera))
            return renderingCamera;
        return Camera.main;
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

    private void ReleaseRuntimeState()
    {
        DismissDebugCharge();
        foreach (DirectorRuntime runtime in _directors.Values)
            runtime.Director.StopAll();
        _legacyLoopRoutes.Clear();
    }

    private void DestroyRuntimeRoot()
    {
        _combatText?.Dispose();
        _combatText = null;
        _directors.Clear();
        _legacyLoopRoutes.Clear();
        if (_runtimeRoot == null)
            return;

        GameObject rootObject = _runtimeRoot.gameObject;
        _runtimeRoot = null;
        if (Application.isPlaying)
        {
            rootObject.SetActive(false);
            Destroy(rootObject);
        }
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
            ReleaseRuntimeState();
    }

    private void DismissDebugCharge()
    {
        if (_debugChargeVfx == null)
            return;
        _debugChargeVfx.Dismiss();
        _debugChargeVfx = null;
    }

    private void HandleAccessibilityChanged(PresentationAccessibilityState state)
    {
        if (_hasLocalAccessibilityOverride)
            return;
        ApplyAccessibilityState(state);
    }

    private void ApplyAccessibilityState(PresentationAccessibilityState state)
    {
        _runtimeOptions ??= new GameFeelRuntimeOptions();
        _runtimeOptions.ReducedMotion = state.ReducedMotion;
        _runtimeOptions.ReducedShake = state.ReducedShake;
        _runtimeOptions.ReducedFlash = state.ReducedFlash;
        _runtimeOptions.CombatText = state.CombatText;
        _runtimeOptions.CombatTextScale = state.CombatTextScale;
        EnemyReactionRuntime.Apply(_runtimeOptions);
    }

    private int GetNextLegacyLoopId()
    {
        if (_nextLegacyLoopId <= 0)
            _nextLegacyLoopId = 1;
        while (_legacyLoopRoutes.ContainsKey(_nextLegacyLoopId))
            _nextLegacyLoopId = _nextLegacyLoopId == int.MaxValue ? 1 : _nextLegacyLoopId + 1;
        int value = _nextLegacyLoopId;
        _nextLegacyLoopId = _nextLegacyLoopId == int.MaxValue ? 1 : _nextLegacyLoopId + 1;
        return value;
    }

    private int SumActiveLoops()
    {
        int total = 0;
        foreach (DirectorRuntime runtime in _directors.Values)
            total += runtime.Director.ActiveLoopCount;
        return total;
    }

    private int SumActiveAudioVoices()
    {
        int total = 0;
        foreach (DirectorRuntime runtime in _directors.Values)
            total += runtime.Director.ActiveAudioVoiceCount;
        return total;
    }

    private int SumAudioVoiceCapacity()
    {
        int total = 0;
        foreach (DirectorRuntime runtime in _directors.Values)
            total += runtime.Director.AudioVoiceCapacity;
        return total;
    }

    private int SumActiveVfx()
    {
        int total = 0;
        foreach (DirectorRuntime runtime in _directors.Values)
            total += runtime.Director.ActiveVfxCount;
        return total;
    }

    private int SumVfxCapacity()
    {
        int total = 0;
        foreach (DirectorRuntime runtime in _directors.Values)
            total += runtime.Director.TotalVfxCapacity;
        return total;
    }

    private int SumSuppressions()
    {
        int total = 0;
        foreach (DirectorRuntime runtime in _directors.Values)
            total += runtime.Director.SuppressionCount;
        return total;
    }
}
