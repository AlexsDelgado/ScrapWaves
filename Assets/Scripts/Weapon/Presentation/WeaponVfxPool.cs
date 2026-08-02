using System.Collections.Generic;
using UnityEngine;

public sealed class WeaponVfxPool
{
    private readonly WeaponPresentationCueData _cueData;
    private readonly Transform _root;
    private readonly List<PooledWeaponVfx> _instances;

    public WeaponVfxPool(WeaponPresentationCueData cueData, Transform root)
    {
        _cueData = cueData;
        _root = root;

        _cueData?.Sanitize();
        int capacity = _cueData != null ? _cueData.MaxSimultaneous : 1;
        _instances = new List<PooledWeaponVfx>(capacity);

        if (_cueData?.VfxPrefab == null)
            return;

        for (int i = 0; i < _cueData.PrewarmCount; i++)
            CreateInstance();
    }

    public int Count => _instances.Count;
    public int Capacity => _cueData != null ? _cueData.MaxSimultaneous : 0;

    public int ActiveCount
    {
        get
        {
            int activeCount = 0;
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null && _instances[i].IsActive)
                    activeCount++;
            }

            return activeCount;
        }
    }

    public bool TryPlay(
        in WeaponPresentationContext context,
        float now,
        bool loop,
        out PooledWeaponVfx instance)
    {
        instance = FindAvailableInstance();
        if (instance == null)
            instance = CreateInstance();

        if (instance == null)
            return false;

        instance.Play(in context, _cueData.Duration, now, loop);
        return true;
    }

    public void Tick(float now)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            PooledWeaponVfx instance = _instances[i];
            if (instance != null && instance.ShouldRelease(now))
                instance.Release();
        }
    }

    public void Release(PooledWeaponVfx instance)
    {
        if (instance == null)
            return;

        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] != instance)
                continue;

            instance.Release();
            return;
        }
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] != null)
                _instances[i].Release();
        }
    }

    private PooledWeaponVfx FindAvailableInstance()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            PooledWeaponVfx instance = _instances[i];
            if (instance != null && !instance.IsActive)
                return instance;
        }

        return null;
    }

    private PooledWeaponVfx CreateInstance()
    {
        if (_cueData?.VfxPrefab == null || _instances.Count >= _cueData.MaxSimultaneous)
            return null;

        GameObject instanceObject = _root != null
            ? Object.Instantiate(_cueData.VfxPrefab, _root)
            : Object.Instantiate(_cueData.VfxPrefab);
        instanceObject.name = $"{_cueData.VfxPrefab.name} (Weapon VFX Pool)";

        PooledWeaponVfx instance = instanceObject.GetComponent<PooledWeaponVfx>();
        if (instance == null)
            instance = instanceObject.AddComponent<PooledWeaponVfx>();

        instance.Initialize();
        _instances.Add(instance);
        return instance;
    }
}
