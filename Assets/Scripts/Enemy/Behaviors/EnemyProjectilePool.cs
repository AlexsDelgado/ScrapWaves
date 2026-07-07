using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pool de <see cref="EnemyProjectile"/> (drones enemigos). Patrón similar a <see cref="ProjectilePool"/>.
/// </summary>
[DefaultExecutionOrder(-34)]
public class EnemyProjectilePool : MonoBehaviour
{
    private static EnemyProjectilePool s_Instance;

    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private Transform _container;
    [SerializeField, Min(1)] private int _initialPoolSize = 32;
    [SerializeField] private bool _allowPoolGrowth = true;
    [SerializeField, Min(1)] private int _maxPoolSize = 256;

    private readonly Dictionary<int, Queue<GameObject>> _inactiveByPrefab = new();
    private readonly Dictionary<int, GameObject> _prefabById = new();
    private readonly Dictionary<int, int> _totalByPrefabId = new();
    private Transform _runtimeParent;
    private int _leasedCount;

    public static EnemyProjectilePool Instance => s_Instance;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        EnsureParent();

        if (_projectilePrefab != null)
            PrewarmPrefab(_projectilePrefab, _initialPoolSize);
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public static bool TryLaunch(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 direction,
        int damage,
        float speed)
    {
        if (prefab == null)
            return false;

        if (s_Instance == null)
            s_Instance = FindAnyObjectByType<EnemyProjectilePool>();

        if (s_Instance != null && s_Instance.TryLaunchInternal(prefab, position, rotation, direction, damage, speed))
            return true;

        GameObject go = Object.Instantiate(prefab, position, rotation);
        EnemyPoolProfiler.RegisterInstantiate();
        if (go.TryGetComponent(out EnemyProjectile projectile))
            projectile.Launch(direction, damage, speed);
        return true;
    }

    private bool TryLaunchInternal(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 direction,
        int damage,
        float speed)
    {
        if (prefab == null)
            prefab = _projectilePrefab;
        if (prefab == null)
            return false;

        EnsurePrefabQueue(prefab);

        int id = prefab.GetInstanceID();
        Queue<GameObject> queue = _inactiveByPrefab[id];

        GameObject instance;
        if (queue.Count > 0)
        {
            instance = queue.Dequeue();
        }
        else if (_allowPoolGrowth && CountForPrefab(id) < _maxPoolSize)
        {
            instance = CreateInstance(prefab, enqueueInactive: false);
        }
        else
        {
            return false;
        }

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        _leasedCount++;
        EnemyPoolProfiler.RegisterPoolGet();

        if (instance.TryGetComponent(out EnemyProjectile projectile))
        {
            projectile.BindPool(this, prefab);
            projectile.Launch(direction, damage, speed);
        }

        return true;
    }

    public void Release(GameObject instance, GameObject sourcePrefab)
    {
        if (instance == null || !instance.activeSelf || sourcePrefab == null)
            return;

        instance.SetActive(false);
        instance.transform.SetParent(GetParent(), false);
        _leasedCount = Mathf.Max(0, _leasedCount - 1);

        int id = sourcePrefab.GetInstanceID();
        if (!_inactiveByPrefab.TryGetValue(id, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            _inactiveByPrefab[id] = queue;
            _prefabById[id] = sourcePrefab;
        }

        queue.Enqueue(instance);
        EnemyPoolProfiler.RegisterPoolRelease();
    }

    private void EnsurePrefabQueue(GameObject prefab)
    {
        int id = prefab.GetInstanceID();
        if (_inactiveByPrefab.ContainsKey(id))
            return;

        PrewarmPrefab(prefab, Mathf.Min(8, _initialPoolSize));
    }

    private void PrewarmPrefab(GameObject prefab, int count)
    {
        int id = prefab.GetInstanceID();
        var queue = new Queue<GameObject>();
        _inactiveByPrefab[id] = queue;
        _prefabById[id] = prefab;

        for (int i = 0; i < count; i++)
            queue.Enqueue(CreateInstance(prefab, enqueueInactive: false));
    }

    private int CountForPrefab(int prefabId)
    {
        return _totalByPrefabId.TryGetValue(prefabId, out int total) ? total : 0;
    }

    private GameObject CreateInstance(GameObject prefab, bool enqueueInactive)
    {
        GameObject instance = Instantiate(prefab);
        EnemyPoolProfiler.RegisterInstantiate();
        instance.name = $"{prefab.name} (enemy pool)";

        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(instance, targetScene);
        instance.transform.SetParent(GetParent(), false);

        if (instance.TryGetComponent(out EnemyProjectile projectile))
            projectile.BindPool(this, prefab);

        instance.SetActive(false);

        int prefabId = prefab.GetInstanceID();
        _totalByPrefabId.TryGetValue(prefabId, out int total);
        _totalByPrefabId[prefabId] = total + 1;

        if (enqueueInactive)
        {
            int id = prefab.GetInstanceID();
            if (!_inactiveByPrefab.TryGetValue(id, out Queue<GameObject> queue))
            {
                queue = new Queue<GameObject>();
                _inactiveByPrefab[id] = queue;
                _prefabById[id] = prefab;
            }

            queue.Enqueue(instance);
        }

        return instance;
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

        var holder = new GameObject("[PooledEnemyProjectiles]");
        _runtimeParent = holder.transform;
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);
    }
}
