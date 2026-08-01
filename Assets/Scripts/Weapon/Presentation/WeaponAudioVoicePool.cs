using UnityEngine;

public readonly struct WeaponAudioVoiceHandle
{
    internal readonly int Index;
    internal readonly int Generation;

    internal WeaponAudioVoiceHandle(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    public bool IsValid => Index >= 0 && Generation > 0;
}

public sealed class WeaponAudioVoicePool
{
    private sealed class VoiceSlot
    {
        public AudioSource Source;
        public bool Active;
        public bool Loop;
        public float ReleaseTime;
        public int Generation;
    }

    private readonly VoiceSlot[] _voices;

    public WeaponAudioVoicePool(Transform root, int capacity, float spatialBlend)
    {
        int voiceCount = Mathf.Clamp(capacity, 1, 64);
        _voices = new VoiceSlot[voiceCount];

        for (int i = 0; i < voiceCount; i++)
        {
            GameObject voiceObject = new($"Weapon Audio Voice {i + 1}");
            if (root != null)
                voiceObject.transform.SetParent(root, false);

            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = Mathf.Clamp01(spatialBlend);

            _voices[i] = new VoiceSlot
            {
                Source = source
            };
        }
    }

    public int Capacity => _voices.Length;

    public int ActiveCount
    {
        get
        {
            int activeCount = 0;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Active)
                    activeCount++;
            }

            return activeCount;
        }
    }

    public bool TryPlayOneShot(
        WeaponPresentationCueData cueData,
        Vector3 position,
        float globalVolume,
        float now,
        out WeaponAudioVoiceHandle handle)
    {
        handle = default;
        if (!TrySelectClip(cueData, out AudioClip clip) || !TryAcquire(out int voiceIndex))
            return false;

        VoiceSlot voice = _voices[voiceIndex];
        float pitch = Random.Range(cueData.PitchMin, cueData.PitchMax);
        ConfigureVoice(voice, clip, position, cueData.Volume * globalVolume, pitch, loop: false);
        voice.ReleaseTime = now + Mathf.Max(0.01f, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch)));
        voice.Source.Play();

        handle = CreateHandle(voiceIndex, voice);
        return true;
    }

    public bool TryBeginLoop(
        WeaponPresentationCueData cueData,
        Vector3 position,
        float globalVolume,
        out WeaponAudioVoiceHandle handle)
    {
        handle = default;
        if (!TrySelectClip(cueData, out AudioClip clip) || !TryAcquire(out int voiceIndex))
            return false;

        VoiceSlot voice = _voices[voiceIndex];
        float pitch = Random.Range(cueData.PitchMin, cueData.PitchMax);
        ConfigureVoice(voice, clip, position, cueData.Volume * globalVolume, pitch, loop: true);
        voice.ReleaseTime = float.PositiveInfinity;
        voice.Source.Play();

        handle = CreateHandle(voiceIndex, voice);
        return true;
    }

    public bool TryGetSource(WeaponAudioVoiceHandle handle, out AudioSource source)
    {
        source = null;
        if (!TryGetVoice(handle, out VoiceSlot voice))
            return false;

        source = voice.Source;
        return source != null;
    }

    public void UpdateLoop(
        WeaponAudioVoiceHandle handle,
        Vector3 position,
        float globalVolume,
        float cueVolume)
    {
        if (!TryGetVoice(handle, out VoiceSlot voice) || !voice.Loop)
            return;

        voice.Source.transform.position = position;
        voice.Source.volume = Mathf.Clamp01(cueVolume * globalVolume);
    }

    public void Release(WeaponAudioVoiceHandle handle)
    {
        if (TryGetVoice(handle, out VoiceSlot voice))
            Release(voice);
    }

    public void Tick(float now)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            VoiceSlot voice = _voices[i];
            if (voice.Active && !voice.Loop && now >= voice.ReleaseTime)
                Release(voice);
        }
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < _voices.Length; i++)
            Release(_voices[i]);
    }

    private static void ConfigureVoice(
        VoiceSlot voice,
        AudioClip clip,
        Vector3 position,
        float volume,
        float pitch,
        bool loop)
    {
        voice.Active = true;
        voice.Loop = loop;
        voice.Source.transform.position = position;
        voice.Source.clip = clip;
        voice.Source.volume = Mathf.Clamp01(volume);
        voice.Source.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
        voice.Source.loop = loop;
    }

    private WeaponAudioVoiceHandle CreateHandle(int voiceIndex, VoiceSlot voice)
    {
        voice.Generation++;
        if (voice.Generation <= 0)
            voice.Generation = 1;

        return new WeaponAudioVoiceHandle(voiceIndex, voice.Generation);
    }

    private bool TryAcquire(out int voiceIndex)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active)
                continue;

            voiceIndex = i;
            return true;
        }

        voiceIndex = -1;
        return false;
    }

    private bool TryGetVoice(WeaponAudioVoiceHandle handle, out VoiceSlot voice)
    {
        voice = null;
        if (!handle.IsValid || handle.Index >= _voices.Length)
            return false;

        VoiceSlot candidate = _voices[handle.Index];
        if (!candidate.Active || candidate.Generation != handle.Generation)
            return false;

        voice = candidate;
        return true;
    }

    private static bool TrySelectClip(WeaponPresentationCueData cueData, out AudioClip clip)
    {
        clip = null;
        if (cueData?.AudioClips == null || cueData.AudioClips.Count == 0)
            return false;

        int startIndex = Random.Range(0, cueData.AudioClips.Count);
        for (int offset = 0; offset < cueData.AudioClips.Count; offset++)
        {
            AudioClip candidate = cueData.AudioClips[(startIndex + offset) % cueData.AudioClips.Count];
            if (candidate == null)
                continue;

            clip = candidate;
            return true;
        }

        return false;
    }

    private static void Release(VoiceSlot voice)
    {
        if (voice?.Source != null)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Source.loop = false;
        }

        if (voice == null)
            return;

        voice.Active = false;
        voice.Loop = false;
        voice.ReleaseTime = 0f;
    }
}
