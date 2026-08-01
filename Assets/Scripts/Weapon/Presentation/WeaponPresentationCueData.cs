using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WeaponPresentationCueData
{
    public WeaponPresentationCue Cue;
    public GameObject VfxPrefab;
    public List<AudioClip> AudioClips = new();

    [Range(0f, 1f)] public float Volume = 1f;
    [Range(0.01f, 3f)] public float PitchMin = 0.96f;
    [Range(0.01f, 3f)] public float PitchMax = 1.04f;

    [Min(0f)] public float Duration = 0.2f;
    [Min(0f)] public float MinReplayInterval;
    [Min(0)] public int PrewarmCount = 1;
    [Min(1)] public int MaxSimultaneous = 8;

    public Vector3 CameraPositionImpulse;
    public Vector3 CameraRotationImpulse;
    public bool Loop;

    public void Sanitize()
    {
        AudioClips ??= new List<AudioClip>();
        Volume = Mathf.Clamp01(Volume);
        PitchMin = Mathf.Clamp(PitchMin, 0.01f, 3f);
        PitchMax = Mathf.Clamp(PitchMax, 0.01f, 3f);
        if (PitchMin > PitchMax)
            (PitchMin, PitchMax) = (PitchMax, PitchMin);

        Duration = Mathf.Max(0f, Duration);
        MinReplayInterval = Mathf.Max(0f, MinReplayInterval);
        MaxSimultaneous = Mathf.Max(1, MaxSimultaneous);
        PrewarmCount = Mathf.Clamp(PrewarmCount, 0, MaxSimultaneous);
    }
}
