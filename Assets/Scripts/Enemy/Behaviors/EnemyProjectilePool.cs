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

    private readonly Queue<GameObject> _inactive = new Queue<GameObject>();
    private readonly List<GameObject> _instances = new List<GameObject>();
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

        if (_projectilePrefab == null)
            return;

        for (int i = 0; i < _initialPoolSize; i++)
            CreateInstance(enqueueInactive: true);
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

        GameObject instance;
        if (_inactive.Count > 0)
            instance = _inactive.Dequeue();
        else if (_allowPoolGrowth && _instances.Count < _maxPoolSize)
            instance = CreateInstance(enqueueInactive: false);
        else
            return false;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        _leasedCount++;
        EnemyPoolProfiler.RegisterPoolGet();

        if (instance.TryGetComponent(out EnemyProjectile projectile))
        {
            projectile.BindPool(this);
            projectile.Launch(direction, damage, speed);
        }

        return true;
    }

    public void Release(GameObject instance)
    {
        if (instance == null || !instance.activeSelf)
            return;

        instance.SetActive(false);
        instance.transform.SetParent(GetParent(), false);
        _leasedCount = Mathf.Max(0, _leasedCount - 1);
        _inactive.Enqueue(instance);
        EnemyPoolProfiler.RegisterPoolRelease();
    }

    private GameObject CreateInstance(bool enqueueInactive)
    {
        GameObject instance = Instantiate(_projectilePrefab);
        EnemyPoolProfiler.RegisterInstantiate();
        instance.name = $"{_projectilePrefab.name} (enemy pool)";

        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(instance, targetScene);
        instance.transform.SetParent(GetParent(), false);

        if (instance.TryGetComponent(out EnemyProjectile projectile))
            projectile.BindPool(this);

        instance.SetActive(false);
        _instances.Add(instance);
        if (enqueueInactive)
            _inactive.Enqueue(instance);
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
