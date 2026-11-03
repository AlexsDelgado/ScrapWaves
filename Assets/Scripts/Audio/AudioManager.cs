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
    [SerializeField, Tooltip("Variaciones PSX. Si tiene clips, reemplazan los campos sueltos de abajo.")]
    private PsxSfxLibrary _library;

    [SerializeField, Tooltip("SFX sintetizados (Tools/SfxGen). Tienen prioridad sobre la librería PSX y los clips sueltos.")]
    private SynthSfxLibrary _synth;

    [SerializeField, Tooltip("Sonido al recoger XP. Apagado a pedido de diseño.")]
    private bool _playXpSfx;

    [SerializeField, Tooltip("Sonido al subir de nivel. Apagado a pedido de diseño.")]
    private bool _playLevelUpSfx;

    [SerializeField, Tooltip("Alarma repetida al estar en el nivel superior durante Overheat. Apagada a pedido de diseño.")]
    private bool _playUpperAlarmSfx;

    [SerializeField] private AudioClip _shoot;
    [SerializeField] private AudioClip _enemyHit;
    [SerializeField] private AudioClip _enemyDeath;
    [SerializeField] private AudioClip _levelUp;
    [SerializeField] private AudioClip _overheatStart;
    [SerializeField] private AudioClip _overheatEnd;
    [SerializeField] private AudioClip _playerHurt;
    [SerializeField, Min(0f), Tooltip("Mínimo entre hurts. El daño puede llegar más seguido; el SFX no se apila.")]
    private float _playerHurtCooldown = 1f;
    private float _nextPlayerHurtTime;

    [SerializeField, Range(0f, 1f)] private float _sfxVolumeScale = UserSettingsData.DefaultSfxVolume;

    [Header("Música — playlist")]
    [SerializeField] private AudioClip[] _bgmTracks;
    [SerializeField] private BgmTrackSelector.Mode _bgmMode = BgmTrackSelector.Mode.ShuffleBag;
    [SerializeField] private AudioClip _bgmOverheatLayer;
    [SerializeField, Min(0f)] private float _bgmGapMinSeconds = 10f;
    [SerializeField, Min(0f)] private float _bgmGapMaxSeconds = 30f;

    [SerializeField, Range(0f, 1f)] private float _musicMainVolume = UserSettingsData.DefaultMusicVolume;
    [SerializeField, Range(0f, 1f)] private float _musicOverheatVolume = 0.1556f;

    private PlayerXP _subscribedXp;
    private OverheatManager _subscribedOverheat;
    private PlayerMovement _subscribedMovement;
    private float _nextXpTime;
    private float _nextDeathTime;
    private HeatManager _subscribedHeat;
    private float _lastHeat;
    private bool _heatWarned;
    private float _nextSlideTime;
    private PlayerPlatformState _subscribedPlatform;
    private float _nextUpperAlarmTime;
    private const float UpperAlarmGain = 2.5f;
    private const float DashGain = 0.6f;
    private const float EnemyDeathGain = 0.6f;
    private AudioSource[] _spatialVoices;
    private int _nextSpatialVoice;
    private readonly System.Collections.Generic.Dictionary<string, float> _nextSpatialTime = new();
    private BgmTrackSelector _trackSelector;
    private Coroutine _playlistRoutine;

    private void OnEnable()
    {
        Instance = this;
        if (_synth != null)
            SynthSfxLibrary.Active = _synth;
        BecameAvailable?.Invoke(this);
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
        if (_synth != null && SynthSfxLibrary.Active == _synth)
            SynthSfxLibrary.Active = null;
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

        _subscribedMovement = FindAnyObjectByType<PlayerMovement>();
        if (_subscribedMovement != null)
        {
            _subscribedMovement.OnJump += PlayJump;
            _subscribedMovement.OnAirJump += PlayJump;
            _subscribedMovement.OnLanded += PlayLand;
            _subscribedMovement.OnDashStarted += PlayDash;
            _subscribedMovement.OnSlideStarted += PlaySlide;
        }

        _subscribedPlatform = FindAnyObjectByType<PlayerPlatformState>();

        _subscribedHeat = FindAnyObjectByType<HeatManager>();
        if (_subscribedHeat != null)
        {
            _lastHeat = _subscribedHeat.CurrentHeat;
            _subscribedHeat.OnHeatChanged += OnHeatChangedHandler;
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

        if (_subscribedMovement != null)
        {
            _subscribedMovement.OnJump -= PlayJump;
            _subscribedMovement.OnAirJump -= PlayJump;
            _subscribedMovement.OnLanded -= PlayLand;
            _subscribedMovement.OnDashStarted -= PlayDash;
            _subscribedMovement.OnSlideStarted -= PlaySlide;
        }

        if (_subscribedHeat != null)
            _subscribedHeat.OnHeatChanged -= OnHeatChangedHandler;

        _subscribedXp = null;
        _subscribedOverheat = null;
        _subscribedMovement = null;
        _subscribedHeat = null;
        _subscribedPlatform = null;
    }

    private void Update() => TickUpperLevelAlarm();

    /// <summary>Alarma repetida mientras hay Overheat y el jugador sigue en el nivel superior, donde el fuego lo daña.</summary>
    private void TickUpperLevelAlarm()
    {
        bool inDanger = _playUpperAlarmSfx && _subscribedOverheat != null && _subscribedOverheat.IsOverheating &&
                        _subscribedPlatform != null && _subscribedPlatform.IsSimulationActive &&
                        _subscribedPlatform.IsInsideUpper;
        if (!inDanger)
        {
            _nextUpperAlarmTime = 0f;
            return;
        }

        if (Time.unscaledTime < _nextUpperAlarmTime)
            return;

        AudioClip clip = Synth("overheat_alarm");
        if (clip == null)
            return;

        PlaySfx(clip, UpperAlarmGain);
        _nextUpperAlarmTime = Time.unscaledTime + clip.length - 0.03f;
    }

    private void OnPlayerLevelUp(int _) => PlayLevelUp();

    /// <summary>Aviso al entrar en la fase intermedia (80 %) subiendo el calor, nunca durante Overheat ni al decaer.</summary>
    private void OnHeatChangedHandler()
    {
        float heat = _subscribedHeat.CurrentHeat;
        bool rising = heat > _lastHeat;
        _lastHeat = heat;

        if (!_subscribedHeat.IsInIntermediatePhase)
        {
            _heatWarned = false;
            return;
        }

        bool overheating = _subscribedOverheat != null && _subscribedOverheat.IsOverheating;
        if (_heatWarned || !rising || overheating)
            return;

        _heatWarned = true;
        PlayOverheatWarning();
    }

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

    public void PlayShoot() => PlaySfx(Resolve(Synth("shot"), Resolve(_library != null ? _library.PickShoot() : null, _shoot)));

    public void PlayEnemyHit() => PlaySfx(_enemyHit);

    public void PlayEnemyDeath()
    {
        if (Time.unscaledTime < _nextDeathTime)
            return;

        AudioClip clip = Resolve(Synth("enemy_death"), Resolve(_library != null ? _library.PickEnemyDeath() : null, _enemyDeath));
        if (clip == null)
            return;

        _nextDeathTime = Time.unscaledTime + 0.07f;
        PlaySfx(clip, EnemyDeathGain);
    }

    public void PlayLevelUp()
    {
        if (_playLevelUpSfx)
            PlaySfx(Resolve(Synth("levelup"), Resolve(_library != null ? _library.PickLevelUp() : null, _levelUp)));
    }

    public void PlayOverheatStart() => PlaySfx(Resolve(Synth("overheat_start"), Resolve(_library != null ? _library.PickOverheatStart() : null, _overheatStart)));

    public void PlayOverheatEnd() => PlaySfx(Resolve(Synth("overheat_end"), Resolve(_library != null ? _library.PickOverheatEnd() : null, _overheatEnd)));

    public void PlayJump() => PlaySfx(Resolve(Synth("jump"), _library != null ? _library.PickJump() : null));

    public void PlayLand()
    {
        if (Time.timeSinceLevelLoad < 0.75f)
            return;

        PlaySfx(Resolve(Synth("land"), _library != null ? _library.PickLand() : null));
    }

    public void PlayXp()
    {
        if (!_playXpSfx || Time.unscaledTime < _nextXpTime)
            return;

        AudioClip clip = Resolve(Synth("xp"), _library != null ? _library.PickXp() : null);
        if (clip == null)
            return;

        _nextXpTime = Time.unscaledTime + 0.08f;
        PlaySfx(clip);
    }

    public void PlayCraft() => PlaySfx(_library != null ? _library.PickCraft() : null);

    public void PlayUiError() => PlaySfx(_library != null ? _library.PickUiError() : null);

    public void PlayBossSpawn() => PlaySfx(_library != null ? _library.PickBossSpawn() : null);

    public void PlayVictory() => PlaySfx(_library != null ? _library.PickVictory() : null);

    public void PlayDefeat() => PlaySfx(_library != null ? _library.PickDefeat() : null);

    public void PlayPlayerHurt()
    {
        if (Time.unscaledTime < _nextPlayerHurtTime)
            return;

        AudioClip clip = Resolve(Synth("hurt"), Resolve(_library != null ? _library.PickHurt() : null, _playerHurt));
        if (clip == null)
            return;

        _nextPlayerHurtTime = Time.unscaledTime + _playerHurtCooldown;
        PlaySfx(clip);
    }

    private static AudioClip Resolve(AudioClip variation, AudioClip fallback) => variation != null ? variation : fallback;

    private AudioClip Synth(string key) => _synth != null ? _synth.Pick(key) : null;

    public void PlayDash() => PlaySfx(Synth("dash"), DashGain);

    public void PlaySlide()
    {
        if (Time.unscaledTime < _nextSlideTime)
            return;

        _nextSlideTime = Time.unscaledTime + 0.25f;
        PlaySfx(Synth("slide"));
    }

    public void PlayOverheatWarning() => PlaySfx(Synth("overheat_warning"));

    /// <summary>Spawn de un enemigo del enjambre: sonido propio por tipo, posicional y con cooldown por tipo.</summary>
    public void PlayEnemySpawn(GameObject enemy)
    {
        string kind = EnemyKind(enemy);
        if (kind != null)
            PlayAt(kind + "_spawn", enemy.transform.position, 0.3f);
    }

    /// <summary>Muerte de un enemigo: sonido propio por tipo; los tipos sin receta usan el genérico.</summary>
    public void PlayEnemyDeath(GameObject enemy)
    {
        if (enemy == null)
        {
            PlayEnemyDeath();
            return;
        }

        string kind = EnemyKind(enemy);
        if (kind != null && PlayAt(kind + "_death", enemy.transform.position, 0.08f, EnemyDeathGain))
            return;

        if (_synth != null && PlayAt("enemy_death", enemy.transform.position, 0.07f, EnemyDeathGain))
            return;

        PlayEnemyDeath();
    }

    private static string EnemyKind(GameObject enemy)
    {
        if (enemy == null)
            return null;
        if (enemy.TryGetComponent<BomberDroneBehavior>(out _)) return "bomber";
        if (enemy.TryGetComponent<ChargerEnemyBehavior>(out _)) return "charger";
        if (enemy.TryGetComponent<DestroyerBehavior>(out _)) return "destroyer";
        if (enemy.TryGetComponent<FlyingRangedBehavior>(out _)) return "flyer";
        if (enemy.TryGetComponent<GigaWormBehavior>(out _)) return "worm";
        if (enemy.TryGetComponent<HellfireSlimeBehavior>(out _)) return "slime";
        return null;
    }

    /// <summary>One-shot 3D desde un pool chico. Devuelve false si no hay clip sintetizado; true si sonó o está en cooldown.</summary>
    private bool PlayAt(string key, Vector3 position, float cooldown, float gain = 1f)
    {
        if (_synth == null || !_synth.Has(key))
            return false;

        float now = Time.unscaledTime;
        if (_nextSpatialTime.TryGetValue(key, out float next) && now < next)
            return true;

        AudioClip clip = _synth.Pick(key);
        if (clip == null)
            return false;

        if (_spatialVoices == null)
            CreateSpatialVoices();

        AudioSource voice = _spatialVoices[_nextSpatialVoice];
        _nextSpatialVoice = (_nextSpatialVoice + 1) % _spatialVoices.Length;
        _nextSpatialTime[key] = now + cooldown;

        voice.transform.position = position;
        voice.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(_sfxVolumeScale * gain);
        voice.Play();
        return true;
    }

    private void CreateSpatialVoices()
    {
        _spatialVoices = new AudioSource[10];
        for (int i = 0; i < _spatialVoices.Length; i++)
        {
            GameObject go = new($"Spatial SFX Voice {i + 1}");
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 8f;
            source.maxDistance = 70f;
            _spatialVoices[i] = source;
        }
    }

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

    private void PlaySfx(AudioClip clip, float gain = 1f)
    {
        if (clip == null || _sfx == null)
            return;

        _sfx.PlayOneShot(clip, _sfxVolumeScale * gain);
    }

    /// <summary>Llamadas seguras desde cualquier script sin referencia.</summary>
    public static void TryPlayShoot() => Instance?.PlayShoot();

    public static void TryPlayEnemyHit() => Instance?.PlayEnemyHit();

    public static void TryPlayEnemyDeath() => Instance?.PlayEnemyDeath();

    public static void TryPlayEnemyDeath(GameObject enemy) => Instance?.PlayEnemyDeath(enemy);

    public static void TryPlayEnemySpawn(GameObject enemy) => Instance?.PlayEnemySpawn(enemy);

    public static void TryPlayPlayerHurt() => Instance?.PlayPlayerHurt();

    public static void TryPlayXp() => Instance?.PlayXp();

    public static void TryPlayCraft() => Instance?.PlayCraft();

    public static void TryPlayUiError() => Instance?.PlayUiError();

    public static void TryPlayBossSpawn() => Instance?.PlayBossSpawn();

    public static void TryPlayVictory() => Instance?.PlayVictory();

    public static void TryPlayDefeat() => Instance?.PlayDefeat();
}
