using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-220)]
public sealed class UserSettingsService : MonoBehaviour
{
    public const string PlayerPrefsKey = "ScrapWaves.UserSettings.v1.Data";
    private const float SaveDebounceSeconds = 0.2f;

    public static UserSettingsService Instance { get; private set; }
    public static event Action<UserSettingsService> InstanceChanged;

    public event Action<UserSettingsChange> Changed;

    private UserSettingsData _data = UserSettingsData.CreateDefault();
    private bool _initialized;
    private bool _saveFailureReported;
    private bool _loadFailureReported;
    private bool _savePending;
    private float _saveAtUnscaledTime;

    public UserSettingsData Current
    {
        get
        {
            EnsureInitialized();
            return _data.Clone();
        }
    }

    public float HorizontalSensitivity
    {
        get { EnsureInitialized(); return _data.HorizontalSensitivity; }
        set => SetHorizontalSensitivity(value);
    }

    public float VerticalSensitivity
    {
        get { EnsureInitialized(); return _data.VerticalSensitivity; }
        set => SetVerticalSensitivity(value);
    }

    public bool InvertY
    {
        get { EnsureInitialized(); return _data.InvertY; }
        set => SetInvertY(value);
    }

    public float SfxVolume
    {
        get { EnsureInitialized(); return _data.SfxVolume; }
        set => SetSfxVolume(value);
    }

    public float MusicVolume
    {
        get { EnsureInitialized(); return _data.MusicVolume; }
        set => SetMusicVolume(value);
    }

    public bool ReducedMotion
    {
        get { EnsureInitialized(); return _data.ReducedMotion; }
        set => SetReducedMotion(value);
    }

    public bool ScreenShake
    {
        get { EnsureInitialized(); return _data.ScreenShake; }
        set => SetScreenShake(value);
    }

    public bool ScreenFlash
    {
        get { EnsureInitialized(); return _data.ScreenFlash; }
        set => SetScreenFlash(value);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (Application.isPlaying)
            {
                // Returning to the authored title scene loads a fresh copy of its
                // persistent bootstrap. Keep the surviving service/transition root
                // and discard this scene copy before its sibling applier subscribes.
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
            else
            {
                Debug.LogError("UserSettingsService: multiple authored services were found. Keep exactly one persistent service.", this);
                enabled = false;
            }
            return;
        }

        Instance = this;
        EnsureInitialized();
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);
        InstanceChanged?.Invoke(this);
    }

    private void OnDestroy()
    {
        FlushPendingSave();
        if (Instance != this)
            return;

        Instance = null;
        InstanceChanged?.Invoke(null);
    }

    private void Update()
    {
        if (_savePending && Time.unscaledTime >= _saveAtUnscaledTime)
            FlushPendingSave();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            FlushPendingSave();
    }

    private void OnApplicationQuit()
    {
        FlushPendingSave();
    }

    public void SetHorizontalSensitivity(float value)
    {
        EnsureInitialized();
        value = UserSettingsData.SanitizeSensitivity(value, UserSettingsData.DefaultHorizontalSensitivity);
        if (Mathf.Approximately(_data.HorizontalSensitivity, value))
            return;
        _data.HorizontalSensitivity = value;
        Commit(UserSettingsChange.HorizontalSensitivity);
    }

    public void SetVerticalSensitivity(float value)
    {
        EnsureInitialized();
        value = UserSettingsData.SanitizeSensitivity(value, UserSettingsData.DefaultVerticalSensitivity);
        if (Mathf.Approximately(_data.VerticalSensitivity, value))
            return;
        _data.VerticalSensitivity = value;
        Commit(UserSettingsChange.VerticalSensitivity);
    }

    public void SetInvertY(bool value)
    {
        EnsureInitialized();
        if (_data.InvertY == value)
            return;
        _data.InvertY = value;
        Commit(UserSettingsChange.InvertY);
    }

    public void SetSfxVolume(float value)
    {
        EnsureInitialized();
        value = UserSettingsData.SanitizeVolume(value, UserSettingsData.DefaultSfxVolume);
        if (Mathf.Approximately(_data.SfxVolume, value))
            return;
        _data.SfxVolume = value;
        Commit(UserSettingsChange.SfxVolume);
    }

    public void SetMusicVolume(float value)
    {
        EnsureInitialized();
        value = UserSettingsData.SanitizeVolume(value, UserSettingsData.DefaultMusicVolume);
        if (Mathf.Approximately(_data.MusicVolume, value))
            return;
        _data.MusicVolume = value;
        Commit(UserSettingsChange.MusicVolume);
    }

    public void SetReducedMotion(bool value)
    {
        EnsureInitialized();
        if (_data.ReducedMotion == value)
            return;
        _data.ReducedMotion = value;
        Commit(UserSettingsChange.ReducedMotion);
    }

    public void SetScreenShake(bool value)
    {
        EnsureInitialized();
        if (_data.ScreenShake == value)
            return;
        _data.ScreenShake = value;
        Commit(UserSettingsChange.ScreenShake);
    }

    public void SetScreenFlash(bool value)
    {
        EnsureInitialized();
        if (_data.ScreenFlash == value)
            return;
        _data.ScreenFlash = value;
        Commit(UserSettingsChange.ScreenFlash);
    }

    public void ResetCategory(UserSettingsCategory category)
    {
        EnsureInitialized();
        UserSettingsData defaults = UserSettingsData.CreateDefault();
        UserSettingsChange requested = category switch
        {
            UserSettingsCategory.Controls => UserSettingsChange.Controls,
            UserSettingsCategory.Audio => UserSettingsChange.Audio,
            UserSettingsCategory.Feedback => UserSettingsChange.Feedback,
            _ => UserSettingsChange.None
        };
        Apply(defaults, requested);
    }

    public void ResetAll()
    {
        EnsureInitialized();
        Apply(UserSettingsData.CreateDefault(), UserSettingsChange.All);
    }

    public void ReloadFromStorage()
    {
        FlushPendingSave();
        UserSettingsData previous = _initialized ? _data.Clone() : UserSettingsData.CreateDefault();
        LoadFromStorage();
        UserSettingsChange changed = DetermineChanges(previous, _data, UserSettingsChange.All);
        if (changed != UserSettingsChange.None)
            Changed?.Invoke(changed);
    }

    public void FlushPendingSave()
    {
        if (!_savePending)
            return;
        _savePending = false;
        SaveToStorage();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            LoadFromStorage();
    }

    private void LoadFromStorage()
    {
        _data = UserSettingsData.CreateDefault();
        try
        {
            if (PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
                UserSettingsData loaded = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonUtility.FromJson<UserSettingsData>(json);
                if (loaded != null)
                    _data = loaded;
            }
        }
        catch (Exception exception)
        {
            if (!_loadFailureReported && Application.isPlaying)
            {
                Debug.LogWarning($"UserSettingsService: saved settings could not be read; defaults will be used ({exception.Message}).", this);
                _loadFailureReported = true;
            }
            _data = UserSettingsData.CreateDefault();
        }

        _data.Sanitize();
        _initialized = true;
    }

    private void Apply(UserSettingsData source, UserSettingsChange requested)
    {
        if (source == null || requested == UserSettingsChange.None)
            return;

        UserSettingsData previous = _data.Clone();
        if ((requested & UserSettingsChange.HorizontalSensitivity) != 0)
            _data.HorizontalSensitivity = source.HorizontalSensitivity;
        if ((requested & UserSettingsChange.VerticalSensitivity) != 0)
            _data.VerticalSensitivity = source.VerticalSensitivity;
        if ((requested & UserSettingsChange.InvertY) != 0)
            _data.InvertY = source.InvertY;
        if ((requested & UserSettingsChange.SfxVolume) != 0)
            _data.SfxVolume = source.SfxVolume;
        if ((requested & UserSettingsChange.MusicVolume) != 0)
            _data.MusicVolume = source.MusicVolume;
        if ((requested & UserSettingsChange.ReducedMotion) != 0)
            _data.ReducedMotion = source.ReducedMotion;
        if ((requested & UserSettingsChange.ScreenShake) != 0)
            _data.ScreenShake = source.ScreenShake;
        if ((requested & UserSettingsChange.ScreenFlash) != 0)
            _data.ScreenFlash = source.ScreenFlash;

        _data.Sanitize();
        UserSettingsChange changed = DetermineChanges(previous, _data, requested);
        if (changed != UserSettingsChange.None)
            Commit(changed);
    }

    private void Commit(UserSettingsChange changed)
    {
        QueueSave();
        Changed?.Invoke(changed);
    }

    private void QueueSave()
    {
        _savePending = true;
        if (!Application.isPlaying)
        {
            FlushPendingSave();
            return;
        }

        _saveAtUnscaledTime = Time.unscaledTime + SaveDebounceSeconds;
    }

    private void SaveToStorage()
    {
        try
        {
            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(_data));
            PlayerPrefs.Save();
        }
        catch (Exception exception)
        {
            if (_saveFailureReported)
                return;

            Debug.LogWarning($"UserSettingsService: settings could not be saved; in-memory values remain active ({exception.Message}).", this);
            _saveFailureReported = true;
        }
    }

    private static UserSettingsChange DetermineChanges(
        UserSettingsData before,
        UserSettingsData after,
        UserSettingsChange requested)
    {
        UserSettingsChange changed = UserSettingsChange.None;
        if ((requested & UserSettingsChange.HorizontalSensitivity) != 0 &&
            !Mathf.Approximately(before.HorizontalSensitivity, after.HorizontalSensitivity))
            changed |= UserSettingsChange.HorizontalSensitivity;
        if ((requested & UserSettingsChange.VerticalSensitivity) != 0 &&
            !Mathf.Approximately(before.VerticalSensitivity, after.VerticalSensitivity))
            changed |= UserSettingsChange.VerticalSensitivity;
        if ((requested & UserSettingsChange.InvertY) != 0 && before.InvertY != after.InvertY)
            changed |= UserSettingsChange.InvertY;
        if ((requested & UserSettingsChange.SfxVolume) != 0 &&
            !Mathf.Approximately(before.SfxVolume, after.SfxVolume))
            changed |= UserSettingsChange.SfxVolume;
        if ((requested & UserSettingsChange.MusicVolume) != 0 &&
            !Mathf.Approximately(before.MusicVolume, after.MusicVolume))
            changed |= UserSettingsChange.MusicVolume;
        if ((requested & UserSettingsChange.ReducedMotion) != 0 && before.ReducedMotion != after.ReducedMotion)
            changed |= UserSettingsChange.ReducedMotion;
        if ((requested & UserSettingsChange.ScreenShake) != 0 && before.ScreenShake != after.ScreenShake)
            changed |= UserSettingsChange.ScreenShake;
        if ((requested & UserSettingsChange.ScreenFlash) != 0 && before.ScreenFlash != after.ScreenFlash)
            changed |= UserSettingsChange.ScreenFlash;
        return changed;
    }
}
