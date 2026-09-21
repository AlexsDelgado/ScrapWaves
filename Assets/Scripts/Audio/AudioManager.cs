using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Global audio: SFX through <see cref="PlayOneShot"/> and playlist BGM (normal + optional Overheat layer).
/// Assign <see cref="AudioSource"/> and clips in the Inspector; other scripts call <see cref="Instance"/> or the static helpers.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-60)]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }
    public static event Action<AudioManager> BecameAvailable;
    public static float EffectiveSfxVolume => Instance != null ? Instance.SfxVolume : 1f;

    [Header("Fuentes (3 AudioSource en este u otros hijos)")]
    [SerializeField, Tooltip("SFX: disparos, golpes, UI corta.")]
    private AudioSource _sfx;

    [SerializeField, Tooltip("Música base (playlist, sin loop).")]
    private AudioSource _musicNormal;

    [SerializeField, Tooltip("Segunda capa en loop (volumen 0 fuera de Overheat). Opcional.")]
    private AudioSource _musicOverheatLayer;

    [Header("SFX — clips")]
    [SerializeField] private AudioClip _shoot;
    [SerializeField] private AudioClip _enemyHit;
    [SerializeField] private AudioClip _enemyDeath;
    [SerializeField] private AudioClip _levelUp;
    [SerializeField] private AudioClip _overheatStart;
    [SerializeField] private AudioClip _overheatEnd;
    [SerializeField] private AudioClip _playerHurt;

    [SerializeField, Range(0f, 1f)] private float _sfxVolumeScale = 1f;

    [Header("Música — playlist")]
    [SerializeField] private AudioClip[] _bgmTracks;
    [SerializeField] private BgmTrackSelector.Mode _bgmMode = BgmTrackSelector.Mode.ShuffleBag;
    [SerializeField] private AudioClip _bgmOverheatLayer;
    [SerializeField, Min(0f)] private float _bgmGapMinSeconds = 10f;
    [SerializeField, Min(0f)] private float _bgmGapMaxSeconds = 30f;

    [SerializeField, Range(0f, 1f)] private float _musicMainVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float _musicOverheatVolume = 0.35f;

    private PlayerXP _subscribedXp;
    private OverheatManager _subscribedOverheat;
    private BgmTrackSelector _trackSelector;
    private Coroutine _playlistRoutine;

    private void OnEnable()
    {
        Instance = this;
        BecameAvailable?.Invoke(this);
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        SubscribeGameEvents();
        StartMainBgm();
        StartOverheatLayerIdle();
    }

    private void OnDestroy()
    {
        StopPlaylist();
        UnsubscribeGameEvents();
    }

    private void SubscribeGameEvents()
    {
        _subscribedXp = FindAnyObjectByType<PlayerXP>();
        if (_subscribedXp != null)
            _subscribedXp.OnLevelUp += OnPlayerLevelUp;

        _subscribedOverheat = FindAnyObjectByType<OverheatManager>();
        if (_subscribedOverheat != null)
        {
            _subscribedOverheat.OnOverheatStarted += OnOverheatStartedHandler;
            _subscribedOverheat.OnOverheatFinished += OnOverheatFinishedHandler;
        }
    }

    private void UnsubscribeGameEvents()
    {
        if (_subscribedXp != null)
            _subscribedXp.OnLevelUp -= OnPlayerLevelUp;

        if (_subscribedOverheat != null)
        {
            _subscribedOverheat.OnOverheatStarted -= OnOverheatStartedHandler;
            _subscribedOverheat.OnOverheatFinished -= OnOverheatFinishedHandler;
        }

        _subscribedXp = null;
        _subscribedOverheat = null;
    }

    private void OnPlayerLevelUp(int _) => PlayLevelUp();

    private void OnOverheatStartedHandler()
    {
        PlayOverheatStart();
        SetOverheatLayerActive(true);
    }

    private void OnOverheatFinishedHandler(OverheatEndReason _)
    {
        PlayOverheatEnd();
        SetOverheatLayerActive(false);
    }

    private void StartMainBgm()
    {
        StopPlaylist();
        if (_musicNormal == null || _bgmTracks == null || _bgmTracks.Length == 0)
            return;

        int usable = CountUsableTracks(_bgmTracks);
        if (usable == 0)
            return;

        _musicNormal.loop = false;
        _musicNormal.volume = _musicMainVolume;
        _trackSelector = CreateSelector();
        _playlistRoutine = StartCoroutine(RunPlaylist());
    }

    private BgmTrackSelector CreateSelector()
    {
        BgmTrackSelector.Mode mode = _bgmMode;
        int count = _bgmTracks.Length;
        if (mode == BgmTrackSelector.Mode.AlternateTwo && count != 2)
            mode = BgmTrackSelector.Mode.ShuffleBag;
        return new BgmTrackSelector(mode, count);
    }

    private IEnumerator RunPlaylist()
    {
        while (_musicNormal != null && _bgmTracks != null && _bgmTracks.Length > 0)
        {
            AudioClip clip = null;
            for (int attempt = 0; attempt < _bgmTracks.Length; attempt++)
            {
                int index = _trackSelector.Next();
                clip = _bgmTracks[index];
                if (clip != null)
                    break;
            }

            if (clip == null)
                yield break;

            _musicNormal.clip = clip;
            _musicNormal.Play();

            while (_musicNormal != null && _musicNormal.isPlaying)
                yield return null;

            if (_musicNormal == null)
                yield break;

            float gapMin = Mathf.Min(_bgmGapMinSeconds, _bgmGapMaxSeconds);
            float gapMax = Mathf.Max(_bgmGapMinSeconds, _bgmGapMaxSeconds);
            float gap = gapMin >= gapMax ? gapMin : UnityEngine.Random.Range(gapMin, gapMax);
            if (gap > 0f)
                yield return new WaitForSecondsRealtime(gap);
        }
    }

    private void StopPlaylist()
    {
        if (_playlistRoutine != null)
        {
            StopCoroutine(_playlistRoutine);
            _playlistRoutine = null;
        }
    }

    private static int CountUsableTracks(AudioClip[] tracks)
    {
        int count = 0;
        for (int i = 0; i < tracks.Length; i++)
        {
            if (tracks[i] != null)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Capa Overheat en loop con volumen 0 hasta activar; así no corta la base.
    /// </summary>
    private void StartOverheatLayerIdle()
    {
        if (_musicOverheatLayer == null || _bgmOverheatLayer == null)
            return;

        _musicOverheatLayer.loop = true;
        _musicOverheatLayer.clip = _bgmOverheatLayer;
        _musicOverheatLayer.volume = 0f;
        _musicOverheatLayer.Play();
    }

    /// <summary>Activa o silencia la capa extra de Overheat (sin tocar la BGM base).</summary>
    public void SetOverheatLayerActive(bool active)
    {
        if (_musicOverheatLayer == null)
            return;

        if (_bgmOverheatLayer != null && _musicOverheatLayer.clip != _bgmOverheatLayer)
        {
            _musicOverheatLayer.clip = _bgmOverheatLayer;
            if (!_musicOverheatLayer.isPlaying)
                _musicOverheatLayer.Play();
        }

        _musicOverheatLayer.volume = active ? ResolveOverheatLayerVolume() : 0f;
    }

    public void PlayShoot() => PlaySfx(_shoot);

    public void PlayEnemyHit() => PlaySfx(_enemyHit);

    public void PlayEnemyDeath() => PlaySfx(_enemyDeath);

    public void PlayLevelUp() => PlaySfx(_levelUp);

    public void PlayOverheatStart() => PlaySfx(_overheatStart);

    public void PlayOverheatEnd() => PlaySfx(_overheatEnd);

    public void PlayPlayerHurt() => PlaySfx(_playerHurt);

    public float SfxVolume
    {
        get => _sfxVolumeScale;
        set => _sfxVolumeScale = Mathf.Clamp01(value);
    }

    public float MusicVolume
    {
        get => _musicMainVolume;
        set
        {
            _musicMainVolume = Mathf.Clamp01(value);
            if (_musicNormal != null)
                _musicNormal.volume = _musicMainVolume;
            if (_musicOverheatLayer != null && _musicOverheatLayer.volume > 0f)
                _musicOverheatLayer.volume = ResolveOverheatLayerVolume();
        }
    }

    private float ResolveOverheatLayerVolume()
    {
        float defaultVolume = Mathf.Max(0.0001f, UserSettingsData.DefaultMusicVolume);
        return Mathf.Clamp01(_musicOverheatVolume * (_musicMainVolume / defaultVolume));
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || _sfx == null)
            return;

        _sfx.PlayOneShot(clip, _sfxVolumeScale);
    }

    /// <summary>Llamadas seguras desde cualquier script sin referencia.</summary>
    public static void TryPlayShoot() => Instance?.PlayShoot();

    public static void TryPlayEnemyHit() => Instance?.PlayEnemyHit();

    public static void TryPlayEnemyDeath() => Instance?.PlayEnemyDeath();

    public static void TryPlayPlayerHurt() => Instance?.PlayPlayerHurt();
}
