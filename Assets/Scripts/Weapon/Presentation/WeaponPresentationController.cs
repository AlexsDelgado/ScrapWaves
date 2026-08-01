using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public sealed class WeaponPresentationController : MonoBehaviour, IWeaponPresentationSink
{
    private sealed class ActiveLoop
    {
        public WeaponPresentationCueData CueData;
        public WeaponVfxPool VfxPool;
        public PooledWeaponVfx Vfx;
        public WeaponAudioVoiceHandle AudioHandle;
        public float Intensity;
        public Vector3 Position;
    }

    [SerializeField] private WeaponPresentationProfile _profile;
    [SerializeField] private ThirdPersonCamera _camera;
    [SerializeField] private AudioManager _audioManager;
    [SerializeField, Range(1, 64)] private int _audioVoiceCount = 16;
    [SerializeField, Range(0f, 1f)] private float _audioSpatialBlend = 1f;

    private readonly Dictionary<WeaponPresentationCue, WeaponVfxPool> _vfxPools = new();
    private readonly Dictionary<WeaponPresentationCue, float> _nextCueTimes = new();
    private readonly Dictionary<int, ActiveLoop> _activeLoops = new();

    private Transform _runtimeRoot;
    private WeaponAudioVoicePool _audioVoices;
    private bool _poolsReady;
    private int _nextLoopId = 1;

    public WeaponPresentationProfile Profile => _profile;
    public int ActiveLoopCount => _activeLoops.Count;
    public int ActiveAudioVoiceCount => _audioVoices?.ActiveCount ?? 0;

    public int ActiveVfxCount
    {
        get
        {
            int activeCount = 0;
            foreach (WeaponVfxPool pool in _vfxPools.Values)
                activeCount += pool.ActiveCount;
            return activeCount;
        }
    }

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
        _poolsReady = false;
        BuildRuntimePools();
    }

    public void SetProfile(WeaponPresentationProfile profile)
    {
        if (_profile == profile && _poolsReady)
            return;

        ReleaseRuntimeState();
        DestroyRuntimeRoot();
        _profile = profile;
        _poolsReady = false;
        ResolveSceneDependencies();
        BuildRuntimePools();
    }

    public void Emit(in WeaponPresentationContext context)
    {
        TryEmitAtTime(in context, Time.unscaledTime);
    }

    public bool TryEmitAtTime(in WeaponPresentationContext context, float now)
    {
        EnsurePools();
        if (!TryResolveCue(context.Cue, out WeaponPresentationCueData cueData) ||
            cueData.Loop ||
            IsRateLimited(context.Cue, now))
        {
            return false;
        }

        bool emitted = false;

        if (_vfxPools.TryGetValue(context.Cue, out WeaponVfxPool vfxPool))
            emitted |= vfxPool.TryPlay(in context, now, loop: false, out _);

        if (_audioVoices != null)
        {
            emitted |= _audioVoices.TryPlayOneShot(
                cueData,
                context.Position,
                ResolveSfxVolume() * Mathf.Clamp01(context.Intensity),
                now,
                out _);
        }

        if (_camera != null)
        {
            emitted |= _camera.AddPresentationImpulse(
                cueData.CameraPositionImpulse * context.Intensity,
                cueData.CameraRotationImpulse * context.Intensity);
        }

        if (emitted)
            _nextCueTimes[context.Cue] = now + cueData.MinReplayInterval;

        return emitted;
    }

    public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)
    {
        return BeginLoopAtTime(in context, Time.unscaledTime);
    }

    public WeaponPresentationLoopHandle BeginLoopAtTime(in WeaponPresentationContext context, float now)
    {
        EnsurePools();
        if (!TryResolveCue(context.Cue, out WeaponPresentationCueData cueData) ||
            !cueData.Loop ||
            IsRateLimited(context.Cue, now))
        {
            return default;
        }

        _vfxPools.TryGetValue(context.Cue, out WeaponVfxPool vfxPool);
        PooledWeaponVfx vfx = null;
        bool hasVfx = vfxPool != null && vfxPool.TryPlay(in context, now, loop: true, out vfx);

        WeaponAudioVoiceHandle audioHandle = default;
        bool hasAudio = _audioVoices != null && _audioVoices.TryBeginLoop(
            cueData,
            context.Position,
            ResolveSfxVolume() * Mathf.Clamp01(context.Intensity),
            out audioHandle);

        bool hasCameraImpulse = _camera != null && _camera.AddPresentationImpulse(
            cueData.CameraPositionImpulse * context.Intensity,
            cueData.CameraRotationImpulse * context.Intensity);

        if (!hasVfx && !hasAudio)
        {
            if (hasCameraImpulse)
                _nextCueTimes[context.Cue] = now + cueData.MinReplayInterval;
            return default;
        }

        int loopId = GetNextLoopId();
        _activeLoops.Add(loopId, new ActiveLoop
        {
            CueData = cueData,
            VfxPool = vfxPool,
            Vfx = vfx,
            AudioHandle = audioHandle,
            Intensity = context.Intensity,
            Position = context.Position
        });

        _nextCueTimes[context.Cue] = now + cueData.MinReplayInterval;
        return new WeaponPresentationLoopHandle(loopId);
    }

    public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
        if (!handle.IsValid || !_activeLoops.TryGetValue(handle.Id, out ActiveLoop loop))
            return;

        if (loop.Vfx != null)
            loop.Vfx.UpdateTransform(in context);

        if (loop.AudioHandle.IsValid)
        {
            loop.Intensity = context.Intensity;
            loop.Position = context.Position;
            _audioVoices?.UpdateLoop(
                loop.AudioHandle,
                context.Position,
                ResolveSfxVolume() * Mathf.Clamp01(context.Intensity),
                loop.CueData.Volume);
        }
    }

    public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
        if (!handle.IsValid || !_activeLoops.TryGetValue(handle.Id, out ActiveLoop loop))
            return;

        if (loop.Vfx != null)
        {
            loop.Vfx.UpdateTransform(in context);
            loop.VfxPool?.Release(loop.Vfx);
        }

        if (loop.AudioHandle.IsValid)
            _audioVoices?.Release(loop.AudioHandle);

        _activeLoops.Remove(handle.Id);
    }

    public void StopAllLoops()
    {
        foreach (ActiveLoop loop in _activeLoops.Values)
        {
            if (loop.Vfx != null)
                loop.VfxPool?.Release(loop.Vfx);
            if (loop.AudioHandle.IsValid)
                _audioVoices?.Release(loop.AudioHandle);
        }

        _activeLoops.Clear();
    }

    private void OnEnable()
    {
        ResolveSceneDependencies();
        if (Application.isPlaying)
            EnsurePools();
    }

    private void Update()
    {
        if (!_poolsReady)
            return;

        float now = Time.unscaledTime;
        foreach (WeaponVfxPool pool in _vfxPools.Values)
            pool.Tick(now);

        _audioVoices?.Tick(now);

        float globalSfxVolume = ResolveSfxVolume();
        foreach (ActiveLoop loop in _activeLoops.Values)
        {
            if (!loop.AudioHandle.IsValid)
                continue;

            _audioVoices?.UpdateLoop(
                loop.AudioHandle,
                loop.Position,
                globalSfxVolume * Mathf.Clamp01(loop.Intensity),
                loop.CueData.Volume);
        }
    }

    private void OnDisable()
    {
        ReleaseRuntimeState();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeState();
    }

    private void EnsurePools()
    {
        if (!_poolsReady)
            BuildRuntimePools();
    }

    private void ResolveSceneDependencies()
    {
        if (_camera == null)
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
        if (_audioManager == null)
            _audioManager = AudioManager.Instance;
    }

    private void BuildRuntimePools()
    {
        _vfxPools.Clear();
        _audioVoices = null;
        _nextCueTimes.Clear();
        _poolsReady = true;

        if (_profile == null)
            return;

        _profile.RebuildCache();
        GameObject runtimeRootObject = new("Weapon Presentation Runtime");
        runtimeRootObject.transform.SetParent(transform, false);
        _runtimeRoot = runtimeRootObject.transform;

        bool hasAudio = false;
        IReadOnlyList<WeaponPresentationCueData> cues = _profile.Cues;
        for (int i = 0; i < cues.Count; i++)
        {
            WeaponPresentationCueData cueData = cues[i];
            if (cueData == null || cueData.Cue == WeaponPresentationCue.None)
                continue;

            cueData.Sanitize();
            if (cueData.VfxPrefab != null && !_vfxPools.ContainsKey(cueData.Cue))
                _vfxPools.Add(cueData.Cue, new WeaponVfxPool(cueData, _runtimeRoot));

            if (!hasAudio && HasValidAudioClip(cueData))
                hasAudio = true;
        }

        if (hasAudio)
            _audioVoices = new WeaponAudioVoicePool(_runtimeRoot, _audioVoiceCount, _audioSpatialBlend);
    }

    private void ReleaseRuntimeState()
    {
        StopAllLoops();

        foreach (WeaponVfxPool pool in _vfxPools.Values)
            pool.ReleaseAll();

        _audioVoices?.ReleaseAll();
        _nextCueTimes.Clear();
        _camera?.ClearPresentationImpulses();
    }

    private void DestroyRuntimeRoot()
    {
        if (_runtimeRoot == null)
            return;

        GameObject runtimeRootObject = _runtimeRoot.gameObject;
        _runtimeRoot = null;
        _vfxPools.Clear();
        _audioVoices = null;

        if (Application.isPlaying)
            Destroy(runtimeRootObject);
        else
            DestroyImmediate(runtimeRootObject);
    }

    private bool TryResolveCue(
        WeaponPresentationCue cue,
        out WeaponPresentationCueData cueData)
    {
        cueData = null;
        return _profile != null && _profile.TryGetCueData(cue, out cueData);
    }

    private bool IsRateLimited(WeaponPresentationCue cue, float now)
    {
        return _nextCueTimes.TryGetValue(cue, out float nextTime) && now < nextTime;
    }

    private float ResolveSfxVolume()
    {
        return _audioManager != null
            ? _audioManager.SfxVolume
            : AudioManager.EffectiveSfxVolume;
    }

    private int GetNextLoopId()
    {
        if (_nextLoopId <= 0)
            _nextLoopId = 1;

        while (_activeLoops.ContainsKey(_nextLoopId))
            _nextLoopId = _nextLoopId == int.MaxValue ? 1 : _nextLoopId + 1;

        int loopId = _nextLoopId;
        _nextLoopId = _nextLoopId == int.MaxValue ? 1 : _nextLoopId + 1;
        return loopId;
    }

    private static bool HasValidAudioClip(WeaponPresentationCueData cueData)
    {
        if (cueData.AudioClips == null)
            return false;

        for (int i = 0; i < cueData.AudioClips.Count; i++)
        {
            if (cueData.AudioClips[i] != null)
                return true;
        }

        return false;
    }
}
