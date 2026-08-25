using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public sealed class CombatFeedbackDirector
{
    private readonly struct LoopKey : IEquatable<LoopKey>
    {
        public readonly WeaponInstance Weapon;
        public readonly WeaponFeedbackEvent Event;

        public LoopKey(WeaponInstance weapon, WeaponFeedbackEvent feedbackEvent)
        {
            Weapon = weapon;
            Event = feedbackEvent;
        }

        public bool Equals(LoopKey other) => ReferenceEquals(Weapon, other.Weapon) && Event == other.Event;
        public override bool Equals(object obj) => obj is LoopKey other && Equals(other);
        public override int GetHashCode() => (RuntimeHelpers.GetHashCode(Weapon) * 397) ^ (int)Event;
    }

    private sealed class ActiveLoop
    {
        public WeaponPresentationCueData CueData;
        public PooledWeaponVfx Vfx;
        public WeaponAudioVoiceHandle Audio;
        public WeaponFeedbackContext Context;
    }

    private readonly WeaponPresentationProfile _profile;
    private readonly GameFeelRuntimeOptions _options;
    private readonly WeaponFxDirector _fx;
    private readonly WeaponAudioDirector _audio;
    private readonly CameraFeedbackController _camera;
    private readonly HitStopController _hitStop;
    private readonly WeaponRecoilFeedback _recoil;
    private readonly CombatTextDirector _combatText;
    private readonly Dictionary<WeaponPresentationCue, float> _nextCueTimes = new();
    private readonly Dictionary<LoopKey, ActiveLoop> _activeSemanticLoops = new();
    private readonly Dictionary<int, ActiveLoop> _activeLegacyLoops = new();
    private int _nextLegacyLoopId = 1;

    public CombatFeedbackDirector(
        WeaponPresentationProfile profile,
        Transform runtimeRoot,
        ThirdPersonCamera camera,
        WeaponRecoilFeedback recoil,
        GameFeelRuntimeOptions options,
        CameraFeedbackController cameraFeedback,
        HitStopController hitStop,
        int audioVoiceCount,
        float audioSpatialBlend,
        CombatTextDirector combatText = null)
    {
        _profile = profile;
        _options = options;
        _camera = cameraFeedback ?? new CameraFeedbackController();
        _hitStop = hitStop ?? new HitStopController();
        _recoil = recoil;
        _combatText = combatText;
        _camera.Bind(camera);
        _fx = new WeaponFxDirector(profile, runtimeRoot, options);
        _audio = new WeaponAudioDirector(
            runtimeRoot,
            audioVoiceCount,
            audioSpatialBlend,
            profile?.Heat,
            options);
    }

    public int ActiveLoopCount => _activeSemanticLoops.Count + _activeLegacyLoops.Count;
    public int ActiveVfxCount => _fx.ActiveCount;
    public int TotalVfxCapacity => _fx.TotalCapacity;
    public int ActiveAudioVoiceCount => _audio.ActiveCount;
    public int AudioVoiceCapacity => _audio.Capacity;
    public int SuppressionCount => _fx.SuppressionCount;

    public bool EmitLegacy(in WeaponPresentationContext context, float globalVolume, float now)
    {
        if (_profile == null || !_profile.TryGetCueData(context.Cue, out WeaponPresentationCueData cueData) ||
            cueData.Loop || IsRateLimited(cueData.Cue, now))
        {
            return false;
        }

        WeaponFeedbackContext feedback = ConvertLegacy(in context);
        return PlayCue(cueData, in feedback, globalVolume, now, playHitStop: true, playEnemyReaction: true);
    }

    public WeaponPresentationLoopHandle BeginLegacyLoop(
        in WeaponPresentationContext context,
        float globalVolume,
        float now)
    {
        if (_profile == null || !_profile.TryGetCueData(context.Cue, out WeaponPresentationCueData cueData) ||
            !cueData.Loop || IsRateLimited(cueData.Cue, now))
        {
            return default;
        }

        WeaponFeedbackContext feedback = ConvertLegacy(in context);
        ActiveLoop loop = BeginLoop(cueData, in feedback, globalVolume, now);
        if (loop == null)
            return default;

        int id = GetNextLegacyLoopId();
        _activeLegacyLoops.Add(id, loop);
        return new WeaponPresentationLoopHandle(id);
    }

    public void UpdateLegacyLoop(
        WeaponPresentationLoopHandle handle,
        in WeaponPresentationContext context,
        float globalVolume)
    {
        if (!handle.IsValid || !_activeLegacyLoops.TryGetValue(handle.Id, out ActiveLoop loop))
            return;

        WeaponFeedbackContext feedback = ConvertLegacy(in context);
        UpdateLoop(loop, in feedback, globalVolume);
    }

    public void EndLegacyLoop(
        WeaponPresentationLoopHandle handle,
        in WeaponPresentationContext context)
    {
        if (!handle.IsValid || !_activeLegacyLoops.TryGetValue(handle.Id, out ActiveLoop loop))
            return;

        EndLoop(loop);
        _activeLegacyLoops.Remove(handle.Id);
    }

    public void EmitSemantic(
        WeaponFeedbackEvent feedbackEvent,
        in WeaponFeedbackContext context,
        float globalVolume,
        float now)
    {
        WeaponFeedbackContext routedContext = feedbackEvent == WeaponFeedbackEvent.DamageConfirmed && context.IsKill
            ? context.WithIntensity(context.EventIntensity * EnemyDeathFeedback.ResolveIntensity(context.Target))
            : context;

        if (feedbackEvent == WeaponFeedbackEvent.DamageConfirmed)
        {
            _combatText?.TryEmit(in routedContext, now);
            if (_options.EnemyReactionEnabled && !routedContext.DamageKind.IsBurnFamily())
            {
                EnemyHitFeedback.TryPlay(
                    in routedContext,
                    ShouldReduceFlash(),
                    _options.ReducedMotion);
            }
        }

        if (_profile == null)
            return;

        if (feedbackEvent == WeaponFeedbackEvent.ShotFired)
        {
            EndSemanticLoop(WeaponFeedbackEvent.ChargeStarted, in routedContext);
            _recoil?.Request(
                in routedContext,
                _profile.Heat,
                _options.HeatPresentationEnabled,
                _options.ReducedMotion);
        }

        if (!_profile.TryResolveCue(feedbackEvent, in routedContext, out WeaponPresentationCueData cueData) ||
            cueData.Loop || IsRateLimited(cueData.Cue, now))
        {
            return;
        }

        bool playPresentation = feedbackEvent != WeaponFeedbackEvent.DamageConfirmed || context.IsKill;
        bool played = false;
        if (playPresentation)
            played = PlayCue(cueData, in routedContext, globalVolume, now, playHitStop: false, playEnemyReaction: false);

        if (feedbackEvent == WeaponFeedbackEvent.DamageConfirmed && !routedContext.DamageKind.IsBurnFamily())
        {
            played |= _hitStop.Request(
                cueData.HitStopDuration * Mathf.Clamp01(routedContext.EventIntensity),
                cueData.HitStopPriority,
                _options.HitStopEnabled,
                _options.ReducedShake || _options.ReducedFlash,
                _options.ReducedMotion,
                IsImportant(in routedContext),
                now);
        }

        if (played)
            _nextCueTimes[cueData.Cue] = now + cueData.MinReplayInterval;
    }

    public void BeginSemanticLoop(
        WeaponFeedbackEvent feedbackEvent,
        in WeaponFeedbackContext context,
        float globalVolume,
        float now)
    {
        LoopKey key = new(context.Weapon, feedbackEvent);
        if (_activeSemanticLoops.TryGetValue(key, out ActiveLoop active))
        {
            UpdateLoop(active, in context, globalVolume);
            return;
        }

        if (_profile == null || !_profile.TryResolveCue(feedbackEvent, in context, out WeaponPresentationCueData cueData) ||
            !cueData.Loop || IsRateLimited(cueData.Cue, now))
        {
            return;
        }

        ActiveLoop loop = BeginLoop(cueData, in context, globalVolume, now);
        if (loop != null)
            _activeSemanticLoops.Add(key, loop);
    }

    public void UpdateSemanticLoop(
        WeaponFeedbackEvent feedbackEvent,
        in WeaponFeedbackContext context,
        float globalVolume)
    {
        LoopKey key = new(context.Weapon, feedbackEvent);
        if (_activeSemanticLoops.TryGetValue(key, out ActiveLoop loop))
            UpdateLoop(loop, in context, globalVolume);
    }

    public void EndSemanticLoop(WeaponFeedbackEvent feedbackEvent, in WeaponFeedbackContext context)
    {
        LoopKey key = new(context.Weapon, feedbackEvent);
        if (!_activeSemanticLoops.TryGetValue(key, out ActiveLoop loop))
            return;

        EndLoop(loop);
        _activeSemanticLoops.Remove(key);
    }

    public void ConfigureProjectile(
        Projectile projectile,
        ProjectilePresentationArchetypeId archetype,
        in WeaponFeedbackContext context)
    {
        if (projectile == null || _profile == null || !_options.ProductionPresentationEnabled || !_options.VfxEnabled ||
            !_profile.TryGetProjectileArchetype(archetype, out ProjectileArchetypePresentation presentation))
        {
            return;
        }

        ProjectileVisualController visuals = projectile.GetComponent<ProjectileVisualController>();
        if (visuals == null)
            return;

        visuals.Apply(
            presentation,
            _profile.Heat,
            context.NormalizedHeat,
            _options.HeatPresentationEnabled,
            _options.Quality,
            _profile.QualitySettings);
    }

    public void Tick(float now, float unscaledDeltaTime, bool tickSharedState = true)
    {
        _fx.Tick(now);
        _audio.Tick(now);
        if (tickSharedState)
            _hitStop.Tick(unscaledDeltaTime);
    }

    public void StopAll()
    {
        foreach (ActiveLoop loop in _activeSemanticLoops.Values)
            EndLoop(loop);
        foreach (ActiveLoop loop in _activeLegacyLoops.Values)
            EndLoop(loop);
        _activeSemanticLoops.Clear();
        _activeLegacyLoops.Clear();
        _fx.ReleaseAll();
        _audio.ReleaseAll();
        _camera.Clear();
        _hitStop.Restore();
        _nextCueTimes.Clear();
    }

    private bool PlayCue(
        WeaponPresentationCueData cueData,
        in WeaponFeedbackContext context,
        float globalVolume,
        float now,
        bool playHitStop,
        bool playEnemyReaction)
    {
        WeaponPresentationContext presentation = WeaponPresentationContext.FromFeedback(
            cueData.Cue,
            in context,
            _profile,
            cueData,
            _options.Quality,
            ShouldReduceFlash(),
            _options.ScreenFlashEnabled);

        bool played = _fx.TryPlay(cueData, in presentation, now, loop: false, out _);
        played |= _audio.TryPlayOneShot(cueData, in context, globalVolume, now, out _);
        played |= _camera.Request(
            cueData,
            in context,
            _profile.Heat,
            _options.CameraFeedbackEnabled && _options.ScreenShakeEnabled,
            _options.ReducedShake,
            _options.ReducedMotion,
            now);

        if (playEnemyReaction && _options.EnemyReactionEnabled)
            played |= EnemyHitFeedback.TryPlay(
                in context,
                ShouldReduceFlash(),
                _options.ReducedMotion);
        if (playHitStop)
        {
            played |= _hitStop.Request(
                cueData.HitStopDuration,
                cueData.HitStopPriority,
                _options.HitStopEnabled,
                _options.ReducedShake || _options.ReducedFlash,
                _options.ReducedMotion,
                IsImportant(in context),
                now);
        }

        if (played)
            _nextCueTimes[cueData.Cue] = now + cueData.MinReplayInterval;
        return played;
    }

    private ActiveLoop BeginLoop(
        WeaponPresentationCueData cueData,
        in WeaponFeedbackContext context,
        float globalVolume,
        float now)
    {
        WeaponPresentationContext presentation = WeaponPresentationContext.FromFeedback(
            cueData.Cue,
            in context,
            _profile,
            cueData,
            _options.Quality,
            ShouldReduceFlash(),
            _options.ScreenFlashEnabled);
        _fx.TryPlay(cueData, in presentation, now, loop: true, out PooledWeaponVfx vfx);
        _audio.TryBeginLoop(cueData, in context, globalVolume, out WeaponAudioVoiceHandle audio);
        _camera.Request(
            cueData,
            in context,
            _profile.Heat,
            _options.CameraFeedbackEnabled && _options.ScreenShakeEnabled,
            _options.ReducedShake,
            _options.ReducedMotion,
            now);

        if (vfx == null && !audio.IsValid)
            return null;

        _nextCueTimes[cueData.Cue] = now + cueData.MinReplayInterval;
        return new ActiveLoop
        {
            CueData = cueData,
            Vfx = vfx,
            Audio = audio,
            Context = context
        };
    }

    private void UpdateLoop(ActiveLoop loop, in WeaponFeedbackContext context, float globalVolume)
    {
        loop.Context = context;
        if (loop.Vfx != null)
        {
            WeaponPresentationContext presentation = WeaponPresentationContext.FromFeedback(
                loop.CueData.Cue,
                in context,
                _profile,
                loop.CueData,
                _options.Quality,
                ShouldReduceFlash(),
                _options.ScreenFlashEnabled);
            loop.Vfx.UpdateTransform(in presentation);
        }
        if (loop.Audio.IsValid)
            _audio.UpdateLoop(loop.Audio, in context, globalVolume, loop.CueData);
    }

    private void EndLoop(ActiveLoop loop)
    {
        if (loop == null)
            return;
        if (loop.Vfx != null)
            _fx.Release(loop.CueData.Cue, loop.Vfx);
        if (loop.Audio.IsValid)
            _audio.Release(loop.Audio);
    }

    private bool IsRateLimited(WeaponPresentationCue cue, float now)
    {
        return _nextCueTimes.TryGetValue(cue, out float nextTime) && now < nextTime;
    }

    private bool ShouldReduceFlash()
    {
        return _options.ReducedFlash || !_options.ScreenFlashEnabled;
    }

    private static bool IsImportant(in WeaponFeedbackContext context)
    {
        return context.IsKill || context.IsCritical || context.IsWeakPoint || context.IsAbilityDamage;
    }

    private static WeaponFeedbackContext ConvertLegacy(in WeaponPresentationContext context)
    {
        return new WeaponFeedbackContext(
            context.Weapon,
            context.Mode,
            context.NormalizedHeat,
            context.Position,
            context.Direction,
            context.Position,
            context.ImpactNormal,
            context.DamageAmount,
            context.IsCritical,
            context.IsWeakPoint,
            context.IsKill,
            context.IsAbility,
            context.TargetClass,
            context.SurfaceType,
            context.ExplosionRadius,
            context.Intensity,
            context.Target,
            context.Anchor);
    }

    private int GetNextLegacyLoopId()
    {
        if (_nextLegacyLoopId <= 0)
            _nextLegacyLoopId = 1;
        while (_activeLegacyLoops.ContainsKey(_nextLegacyLoopId))
            _nextLegacyLoopId = _nextLegacyLoopId == int.MaxValue ? 1 : _nextLegacyLoopId + 1;
        int value = _nextLegacyLoopId;
        _nextLegacyLoopId = _nextLegacyLoopId == int.MaxValue ? 1 : _nextLegacyLoopId + 1;
        return value;
    }
}
