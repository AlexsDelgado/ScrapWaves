using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponPresentation", menuName = "ScrapWaves/Weapon Presentation Profile")]
public sealed class WeaponPresentationProfile : ScriptableObject
{
    [SerializeField] private WeaponType _weaponType;
    [SerializeField] private List<WeaponPresentationCueData> _cues = new();

    private Dictionary<WeaponPresentationCue, WeaponPresentationCueData> _cueLookup;
    private bool _cacheReady;
    private bool _hasDuplicateCues;

    public WeaponType WeaponType => _weaponType;
    public IReadOnlyList<WeaponPresentationCueData> Cues => _cues;
    public bool HasDuplicateCues
    {
        get
        {
            EnsureCache();
            return _hasDuplicateCues;
        }
    }

    public bool TryGetCueData(WeaponPresentationCue cue, out WeaponPresentationCueData cueData)
    {
        EnsureCache();
        if (cue == WeaponPresentationCue.None)
        {
            cueData = null;
            return false;
        }

        return _cueLookup.TryGetValue(cue, out cueData);
    }

    public void RebuildCache()
    {
        _cueLookup ??= new Dictionary<WeaponPresentationCue, WeaponPresentationCueData>();
        _cueLookup.Clear();
        _hasDuplicateCues = false;

        _cues ??= new List<WeaponPresentationCueData>();
        for (int i = 0; i < _cues.Count; i++)
        {
            WeaponPresentationCueData cueData = _cues[i];
            if (cueData == null)
                continue;

            cueData.Sanitize();
            if (cueData.Cue == WeaponPresentationCue.None)
                continue;

            if (!_cueLookup.TryAdd(cueData.Cue, cueData))
                _hasDuplicateCues = true;
        }

        _cacheReady = true;
    }

    private void OnEnable()
    {
        _cacheReady = false;
        EnsureCache();
    }

    private void OnValidate()
    {
        _cacheReady = false;
        EnsureCache();
    }

    private void EnsureCache()
    {
        if (!_cacheReady || _cueLookup == null)
            RebuildCache();
    }
}
