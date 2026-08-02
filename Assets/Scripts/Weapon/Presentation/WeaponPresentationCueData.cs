using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WeaponPresentationCueData
{
    [Header("Identity")]
    public WeaponPresentationCue Cue;
    [Tooltip("Authored and pooled production effect prefab.")]
    public GameObject VfxPrefab;

    [Header("Audio")]
    public List<AudioClip> AudioClips = new();
    [Tooltip("Play every authored clip as a simultaneous layer instead of selecting one as a random variant.")]
    public bool LayerAudioClips;
    [Range(0f, 1f), Tooltip("Volume applied to the final clip when it is authored as the mechanical layer.")]
    public float MechanicalLayerVolume = 0.7f;
    [Tooltip("Drive the mechanical layer volume from the weapon heat strain curve.")]
    public bool ApplyHeatStrainToMechanicalLayer;
    [Tooltip("Use semantic event intensity to raise loop pitch as progress increases.")]
    public bool ApplyEventIntensityToPitch;

    [Range(0f, 1f)] public float Volume = 1f;
    [Range(0.01f, 3f)] public float PitchMin = 0.96f;
    [Range(0.01f, 3f)] public float PitchMax = 1.04f;
    [Range(0f, 1f)] public float SpatialBlend = 1f;
    [Min(0f)] public float MinimumDistance = 1f;
    [Min(0.1f)] public float MaximumDistance = 32f;
    [Range(0, 256)] public int AudioPriority = 128;

    [Header("Lifetime and concurrency")]
    [Min(0f)] public float Duration = 0.2f;
    [Min(0f)] public float MinReplayInterval;
    [Min(0)] public int PrewarmCount = 1;
    [Min(1)] public int MaxSimultaneous = 8;
    public bool Loop;

    [Header("Camera")]
    public Vector3 CameraPositionImpulse;
    public Vector3 CameraRotationImpulse;
    [Min(0f)] public float CameraFovKick;
    [Min(0f)] public float CameraMinReplayInterval;

    [Header("Hit stop")]
    [Min(0f)] public float HitStopDuration;
    [Min(0)] public int HitStopPriority;

    [Header("Quality and density")]
    [Tooltip("Essential cues remain visible at every quality and are never density-suppressed.")]
    public bool EssentialGameplayCue = true;
    [Tooltip("Secondary layers may be suppressed at distance or under swarm density.")]
    public bool SecondaryEffect;
    public GameFeelQualityLevel MinimumQuality = GameFeelQualityLevel.Low;

    [Header("Heat response")]
    [Tooltip("Per-cue multiplier applied after the shared weapon heat curves.")]
    public AnimationCurve HeatMultiplier = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    public void Sanitize()
    {
        AudioClips ??= new List<AudioClip>();
        MechanicalLayerVolume = Mathf.Clamp01(MechanicalLayerVolume);
        Volume = Mathf.Clamp01(Volume);
        PitchMin = Mathf.Clamp(PitchMin, 0.01f, 3f);
        PitchMax = Mathf.Clamp(PitchMax, 0.01f, 3f);
        if (PitchMin > PitchMax)
            (PitchMin, PitchMax) = (PitchMax, PitchMin);

        Duration = Mathf.Max(0f, Duration);
        MinReplayInterval = Mathf.Max(0f, MinReplayInterval);
        SpatialBlend = Mathf.Clamp01(SpatialBlend);
        MinimumDistance = Mathf.Max(0f, MinimumDistance);
        MaximumDistance = Mathf.Max(Mathf.Max(0.1f, MinimumDistance), MaximumDistance);
        AudioPriority = Mathf.Clamp(AudioPriority, 0, 256);
        CameraFovKick = Mathf.Max(0f, CameraFovKick);
        CameraMinReplayInterval = Mathf.Max(0f, CameraMinReplayInterval);
        HitStopDuration = Mathf.Max(0f, HitStopDuration);
        HitStopPriority = Mathf.Max(0, HitStopPriority);
        MaxSimultaneous = Mathf.Max(1, MaxSimultaneous);
        PrewarmCount = Mathf.Clamp(PrewarmCount, 0, MaxSimultaneous);
        HeatMultiplier ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
    }
}
