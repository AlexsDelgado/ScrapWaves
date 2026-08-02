using UnityEngine;

public sealed class WeaponAudioDirector
{
    private readonly WeaponAudioVoicePool _voices;
    private readonly WeaponHeatPresentationSettings _heat;
    private readonly GameFeelRuntimeOptions _options;

    public WeaponAudioDirector(
        Transform root,
        int capacity,
        float spatialBlend,
        WeaponHeatPresentationSettings heat,
        GameFeelRuntimeOptions options)
    {
        _voices = new WeaponAudioVoicePool(root, capacity, spatialBlend);
        _heat = heat;
        _options = options;
    }

    public int ActiveCount => _voices.ActiveCount;
    public int Capacity => _voices.Capacity;

    public bool TryPlayOneShot(
        WeaponPresentationCueData cueData,
        in WeaponFeedbackContext context,
        float globalVolume,
        float now,
        out WeaponAudioVoiceHandle handle)
    {
        handle = default;
        if (_options == null || !_options.ProductionPresentationEnabled || !_options.AudioEnabled)
            return false;

        if (cueData.LayerAudioClips && cueData.AudioClips != null && cueData.AudioClips.Count > 1)
        {
            bool playedAny = false;
            for (int i = 0; i < cueData.AudioClips.Count; i++)
            {
                bool mechanicalLayer = i == cueData.AudioClips.Count - 1;
                float layerVolume = mechanicalLayer
                    ? cueData.MechanicalLayerVolume * ResolveMechanicalHeatScale(cueData, context.NormalizedHeat)
                    : 1f;
                if (!_voices.TryPlayOneShotClip(
                        cueData,
                        cueData.AudioClips[i],
                        context.ImpactPosition != default ? context.ImpactPosition : context.Origin,
                        globalVolume * Mathf.Clamp01(context.EventIntensity),
                        layerVolume,
                        now,
                        out WeaponAudioVoiceHandle layerHandle))
                {
                    continue;
                }

                if (!handle.IsValid)
                    handle = layerHandle;
                ApplyHeatPitch(layerHandle, context.NormalizedHeat);
                playedAny = true;
            }

            return playedAny;
        }

        bool played = _voices.TryPlayOneShot(
            cueData,
            context.ImpactPosition != default ? context.ImpactPosition : context.Origin,
            globalVolume * Mathf.Clamp01(context.EventIntensity) *
            ResolveMechanicalHeatScale(cueData, context.NormalizedHeat),
            now,
            out handle);
        ApplyHeatPitch(handle, context.NormalizedHeat);
        return played;
    }

    public bool TryBeginLoop(
        WeaponPresentationCueData cueData,
        in WeaponFeedbackContext context,
        float globalVolume,
        out WeaponAudioVoiceHandle handle)
    {
        handle = default;
        if (_options == null || !_options.ProductionPresentationEnabled || !_options.AudioEnabled)
            return false;

        bool played = _voices.TryBeginLoop(
            cueData,
            context.Origin,
            globalVolume * Mathf.Clamp01(context.EventIntensity) *
            ResolveMechanicalHeatScale(cueData, context.NormalizedHeat),
            out handle);
        ApplyHeatPitch(handle, context.NormalizedHeat);
        return played;
    }

    public void UpdateLoop(
        WeaponAudioVoiceHandle handle,
        in WeaponFeedbackContext context,
        float globalVolume,
        WeaponPresentationCueData cueData)
    {
        if (cueData == null)
            return;

        _voices.UpdateLoop(
            handle,
            context.Origin,
            globalVolume * Mathf.Clamp01(context.EventIntensity) *
            ResolveMechanicalHeatScale(cueData, context.NormalizedHeat),
            cueData.Volume);
        float intensityPitch = cueData.ApplyEventIntensityToPitch
            ? Mathf.Lerp(0.9f, 1.2f, Mathf.InverseLerp(0.65f, 1.25f, context.EventIntensity))
            : 1f;
        ApplyHeatPitch(handle, context.NormalizedHeat, intensityPitch);
    }

    public void Release(WeaponAudioVoiceHandle handle) => _voices.Release(handle);
    public void Tick(float now) => _voices.Tick(now);
    public void ReleaseAll() => _voices.ReleaseAll();

    private void ApplyHeatPitch(WeaponAudioVoiceHandle handle, float normalizedHeat, float extraMultiplier = 1f)
    {
        if (!handle.IsValid || _heat == null)
            return;

        _voices.SetPitchMultiplier(
            handle,
            Mathf.Max(0.01f, _heat.AudioPitch.Evaluate(Mathf.Clamp01(normalizedHeat)) * extraMultiplier));
    }

    private float ResolveMechanicalHeatScale(WeaponPresentationCueData cueData, float normalizedHeat)
    {
        if (cueData == null || !cueData.ApplyHeatStrainToMechanicalLayer || _heat == null)
            return 1f;

        float strain = Mathf.Clamp01(_heat.MechanicalStrainVolume.Evaluate(Mathf.Clamp01(normalizedHeat)));
        return Mathf.Lerp(0.55f, 1f, strain);
    }
}
