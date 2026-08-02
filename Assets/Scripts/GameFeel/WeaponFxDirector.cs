using System.Collections.Generic;
using UnityEngine;

public sealed class WeaponFxDirector
{
    private readonly Dictionary<WeaponPresentationCue, WeaponVfxPool> _pools = new();
    private readonly Transform _root;
    private WeaponPresentationProfile _profile;
    private GameFeelRuntimeOptions _options;

    public WeaponFxDirector(
        WeaponPresentationProfile profile,
        Transform root,
        GameFeelRuntimeOptions options)
    {
        _profile = profile;
        _root = root;
        _options = options;
        BuildPools();
    }

    public int SuppressionCount { get; private set; }

    public int ActiveCount
    {
        get
        {
            int count = 0;
            foreach (WeaponVfxPool pool in _pools.Values)
                count += pool.ActiveCount;
            return count;
        }
    }

    public int TotalCapacity
    {
        get
        {
            int count = 0;
            foreach (WeaponVfxPool pool in _pools.Values)
                count += pool.Capacity;
            return count;
        }
    }

    public void SetOptions(GameFeelRuntimeOptions options)
    {
        _options = options;
    }

    public bool TryPlay(
        WeaponPresentationCueData cueData,
        in WeaponPresentationContext context,
        float now,
        bool loop,
        out PooledWeaponVfx instance)
    {
        instance = null;
        if (_options == null || !_options.ProductionPresentationEnabled || !_options.VfxEnabled || cueData == null)
            return false;

        if (!_pools.TryGetValue(cueData.Cue, out WeaponVfxPool pool))
            return false;

        if (ShouldSuppress(cueData, in context))
        {
            SuppressionCount++;
            return false;
        }

        bool played = pool.TryPlay(in context, now, loop, out instance);
        if (!played)
            SuppressionCount++;
        return played;
    }

    public void Release(WeaponPresentationCue cue, PooledWeaponVfx instance)
    {
        if (instance != null && _pools.TryGetValue(cue, out WeaponVfxPool pool))
            pool.Release(instance);
    }

    public void Tick(float now)
    {
        foreach (WeaponVfxPool pool in _pools.Values)
            pool.Tick(now);
    }

    public void ReleaseAll()
    {
        foreach (WeaponVfxPool pool in _pools.Values)
            pool.ReleaseAll();
    }

    private void BuildPools()
    {
        _pools.Clear();
        if (_profile == null)
            return;

        IReadOnlyList<WeaponPresentationCueData> cues = _profile.Cues;
        for (int i = 0; i < cues.Count; i++)
        {
            WeaponPresentationCueData cueData = cues[i];
            if (cueData == null || cueData.Cue == WeaponPresentationCue.None || cueData.VfxPrefab == null)
                continue;
            if (!_pools.ContainsKey(cueData.Cue))
                _pools.Add(cueData.Cue, new WeaponVfxPool(cueData, _root));
        }
    }

    private bool ShouldSuppress(WeaponPresentationCueData cueData, in WeaponPresentationContext context)
    {
        if (cueData.EssentialGameplayCue)
            return false;

        if (_options.Quality < cueData.MinimumQuality)
            return true;

        WeaponDensitySettings density = _profile.Density;
        int secondaryLimit = Mathf.Min(
            density.GetSecondaryLimit(_options.Quality),
            density.DenseCombatThreshold);
        if (cueData.SecondaryEffect && ActiveCount >= secondaryLimit)
            return true;

        if (cueData.SecondaryEffect && Camera.main != null)
        {
            float distance = Vector3.Distance(Camera.main.transform.position, context.Position);
            if (distance > density.DistantSecondaryCutoff)
                return true;
        }

        return false;
    }
}
