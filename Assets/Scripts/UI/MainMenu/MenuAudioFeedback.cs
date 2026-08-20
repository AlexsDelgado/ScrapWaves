using UnityEngine;

[DisallowMultipleComponent]
public sealed class MenuAudioFeedback : MonoBehaviour
{
    [SerializeField] private AudioSource _source;
    [SerializeField] private AudioClip[] _navigationClips;
    [SerializeField] private AudioClip _confirmClip;
    [SerializeField] private AudioClip _rejectClip;
    [SerializeField] private AudioClip _localOpenClip;
    [SerializeField] private AudioClip _localCloseClip;
    [SerializeField, Range(0f, 1f)] private float _navigationVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float _impactVolume = 0.9f;
    [SerializeField, Range(0f, 0.1f)] private float _pitchVariation = 0.035f;
    [SerializeField, Min(0f)] private float _minimumNavigationInterval = 0.035f;

    private int _navigationIndex;
    private float _nextNavigationTime;

    public void PlayNavigation()
    {
        if (_source == null || _navigationClips == null || _navigationClips.Length == 0 ||
            Time.unscaledTime < _nextNavigationTime)
        {
            return;
        }

        AudioClip clip = FindNextNavigationClip();
        if (clip == null)
            return;

        _nextNavigationTime = Time.unscaledTime + _minimumNavigationInterval;
        Play(clip, _navigationVolume, true);
    }

    public void PlayConfirm() => Play(_confirmClip, _impactVolume, false);
    public void PlayReject() => Play(_rejectClip, _navigationVolume, false);
    public void PlayLocalOpen() => Play(_localOpenClip, _impactVolume * 0.72f, false);
    public void PlayLocalClose() => Play(_localCloseClip, _navigationVolume, false);

    private AudioClip FindNextNavigationClip()
    {
        int count = _navigationClips.Length;
        for (int offset = 0; offset < count; offset++)
        {
            int index = (_navigationIndex + offset) % count;
            AudioClip clip = _navigationClips[index];
            if (clip == null)
                continue;

            _navigationIndex = (index + 1) % count;
            return clip;
        }

        return null;
    }

    private void Play(AudioClip clip, float volume, bool varyPitch)
    {
        if (_source == null || clip == null)
            return;

        _source.pitch = varyPitch
            ? 1f + Random.Range(-_pitchVariation, _pitchVariation)
            : 1f;
        float resolvedVolume = ResolvePlaybackVolume(volume);
        if (resolvedVolume > 0f)
            _source.PlayOneShot(clip, resolvedVolume);
    }

    private static float ResolvePlaybackVolume(float authoredVolume)
    {
        UserSettingsService settings = UserSettingsService.Instance;
        float sfxVolume = settings != null ? settings.SfxVolume : 1f;
        return Mathf.Clamp01(authoredVolume) * Mathf.Clamp01(sfxVolume);
    }
}
