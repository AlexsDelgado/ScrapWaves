using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pool de un solo prefab de enemigo. Usado por <see cref="EnemyPoolRegistry"/>.
/// </summary>
public sealed class EnemyPrefabPool
{
    private readonly GameObject _prefab;
    private readonly Transform _parent;
    private readonly bool _allowGrowth;
    private readonly int _maxSize;
    private readonly Queue<GameObject> _inactive = new Queue<GameObject>();
    private readonly List<GameObject> _instances = new List<GameObject>();

    private int _leasedCount;

    public GameObject Prefab => _prefab;
    public int ActiveLeasedCount => _leasedCount;
    public int TotalInstances => _instances.Count;

    public EnemyPrefabPool(GameObject prefab, Transform parent, int initialSize, bool allowGrowth, int maxSize)
    {
        _prefab = prefab;
        _parent = parent;
        _allowGrowth = allowGrowth;
        _maxSize = Mathf.Max(1, maxSize);

        int prewarm = Mathf.Clamp(initialSize, 0, _maxSize);
        for (int i = 0; i < prewarm; i++)
            CreateInstance(enqueueInactive: true);
    }

    public GameObject TryGet()
    {
        if (_prefab == null)
            return null;

        GameObject instance;
        if (_inactive.Count > 0)
        {
            instance = _inactive.Dequeue();
        }
        else if (_allowGrowth && _instances.Count < _maxSize)
        {
            instance = CreateInstance(enqueueInactive: false);
        }
        else
        {
            return null;
        }

        ActivateInstance(instance);
        EnemyPoolProfiler.RegisterPoolGet();
        return instance;
    }

    public void Release(GameObject instance)
    {
        if (instance == null || !instance.activeSelf)
            return;

        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        if (pooled == null || !pooled.IsBoundToRegistry)
            return;

        pooled.NotifyDespawned();
        instance.SetActive(false);
        instance.transform.SetParent(_parent, false);
        _leasedCount--;
        _inactive.Enqueue(instance);
        EnemyPoolProfiler.RegisterPoolRelease();
    }

    public void ReleaseAllActive()
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            GameObject go = _instances[i];
            if (go != null && go.activeSelf)
                Release(go);
        }
    }

    private GameObject CreateInstance(bool enqueueInactive)
    {
        GameObject instance = Object.Instantiate(_prefab);
        EnemyPoolProfiler.RegisterInstantiate();
        instance.name = $"{_prefab.name} (pool)";

        Scene targetScene = _parent != null && _parent.gameObject.scene.IsValid()
            ? _parent.gameObject.scene
            : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(instance, targetScene);
        instance.transform.SetParent(_parent, false);

        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        if (pooled == null)
            pooled = instance.AddComponent<SwarmPooledEnemy>();
        pooled.BindRegistry(_prefab);

        instance.SetActive(false);
        _instances.Add(instance);

        if (enqueueInactive)
            _inactive.Enqueue(instance);

        return instance;
    }

    private void ActivateInstance(GameObject instance)
    {
        instance.SetActive(true);
        _leasedCount++;
        instance.GetComponent<SwarmPooledEnemy>()?.NotifySpawned();
    }
}
