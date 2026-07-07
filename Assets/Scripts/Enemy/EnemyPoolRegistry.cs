using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Registro multi-prefab de pools de enemigos. Spawners orbitales/zona/elite lo usan
/// cuando <see cref="UseEnemyPool"/> está activo.
/// </summary>
[DefaultExecutionOrder(-42)]
public class EnemyPoolRegistry : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public GameObject Prefab;
        [Min(0)] public int InitialSize = 16;
        [Min(1)] public int MaxSize = 128;
    }

    private static EnemyPoolRegistry s_Instance;

    [SerializeField, Tooltip("Si está activo, los spawners usan pool en lugar de Instantiate por enemigo.")]
    private bool _useEnemyPool = true;

    [SerializeField] private Transform _container;

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    [SerializeField, Tooltip("Auto-registrar prefabs de la ruleta al Awake.")]
    private EnemySpawnRouletteConfig _rouletteConfig;

    [SerializeField] private bool _allowPoolGrowth = true;

    private readonly Dictionary<int, EnemyPrefabPool> _poolsByPrefabId = new Dictionary<int, EnemyPrefabPool>();
    private Transform _runtimeParent;

    public static bool UseEnemyPool => s_Instance != null && s_Instance._useEnemyPool;
    public static EnemyPoolRegistry Instance => s_Instance;

    public int TotalLeased
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<int, EnemyPrefabPool> pair in _poolsByPrefabId)
                total += pair.Value.ActiveLeasedCount;
            return total;
        }
    }

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        EnsureParent();
        RegisterConfiguredEntries();
    }

    public static void EnsureExists()
    {
        if (s_Instance != null)
            return;

        s_Instance = FindAnyObjectByType<EnemyPoolRegistry>();
        if (s_Instance == null)
        {
            var go = new GameObject("EnemyPoolRegistry");
            s_Instance = go.AddComponent<EnemyPoolRegistry>();
        }

        EnsureFxPool<EnemyProjectilePool>("EnemyProjectilePool");
        EnsureFxPool<EnemyTimedAreaPool>("EnemyTimedAreaPool");
        EnsureFxPool<ExplosionRadiusVfxPool>("ExplosionRadiusVfxPool");
    }

    private static void EnsureFxPool<T>(string objectName) where T : Component
    {
        if (FindAnyObjectByType<T>() != null)
            return;

        var go = new GameObject(objectName);
        go.AddComponent<T>();
    }

    public void RegisterFromRoulette(EnemySpawnRouletteConfig config)
    {
        _rouletteConfig = config;
        RegisterConfiguredEntries();
    }

    private void RegisterConfiguredEntries()
    {
        if (_entries != null)
        {
            for (int i = 0; i < _entries.Length; i++)
                RegisterPrefab(_entries[i]);
        }

        if (_rouletteConfig != null && _rouletteConfig.Entries != null)
        {
            foreach (EnemySpawnRouletteConfig.Entry entry in _rouletteConfig.Entries)
            {
                if (entry?.Prefab == null)
                    continue;

                RegisterPrefab(new Entry
                {
                    Prefab = entry.Prefab,
                    InitialSize = Mathf.Max(8, entry.BatchSize * 2),
                    MaxSize = 256
                });
            }
        }
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;

        if (_runtimeParent != null)
            Destroy(_runtimeParent.gameObject);
    }

    public void SetUseEnemyPool(bool enabled) => _useEnemyPool = enabled;

    public void RegisterPrefab(Entry entry)
    {
        if (entry?.Prefab == null)
            return;

        int id = entry.Prefab.GetInstanceID();
        if (_poolsByPrefabId.ContainsKey(id))
            return;

        _poolsByPrefabId[id] = new EnemyPrefabPool(
            entry.Prefab,
            GetParent(),
            entry.InitialSize,
            _allowPoolGrowth,
            entry.MaxSize);
    }

    public bool TryGet(GameObject prefab, out GameObject instance)
    {
        instance = null;
        EnsureExists();
        if (prefab == null || !_useEnemyPool)
            return false;

        int id = prefab.GetInstanceID();
        if (!_poolsByPrefabId.TryGetValue(id, out EnemyPrefabPool pool))
        {
            RegisterPrefab(new Entry { Prefab = prefab, InitialSize = 8, MaxSize = 128 });
            _poolsByPrefabId.TryGetValue(id, out pool);
        }

        instance = pool?.TryGet();
        return instance != null;
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
            return;

        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        if (pooled == null || pooled.SourcePrefab == null)
            return;

        int id = pooled.SourcePrefab.GetInstanceID();
        if (_poolsByPrefabId.TryGetValue(id, out EnemyPrefabPool pool))
            pool.Release(instance);
    }

    public void ReleaseAllActive()
    {
        foreach (KeyValuePair<int, EnemyPrefabPool> pair in _poolsByPrefabId)
            pair.Value.ReleaseAllActive();
    }

    private Transform GetParent()
    {
        if (_container != null)
            return _container;
        EnsureParent();
        return _runtimeParent;
    }

    private void EnsureParent()
    {
        if (_container != null || _runtimeParent != null)
            return;

        var holder = new GameObject("[PooledEnemies] Registry");
        _runtimeParent = holder.transform;
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);
    }
}
