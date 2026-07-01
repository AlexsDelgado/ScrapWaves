using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-40)]
public class SwarmEnemyPool : MonoBehaviour
{
    [SerializeField, Tooltip("Root enemy prefab (recommended EnemyPro: SimpleFollow + Rigidbody; legacy EnemyFollow).")]
    private GameObject _enemyPrefab;

    [SerializeField, Tooltip("Clone parent; if null, a runtime container is created in the scene (avoids the persistent parent warning).")]
    private Transform _container;

    [SerializeField, Tooltip("If active, logs which parent the pool uses on startup.")]
    private bool _debugLogParent;

    [SerializeField, Min(1)] private int _initialPoolSize = 64;

    [SerializeField] private bool _allowPoolGrowth = true;

    [SerializeField, Min(1)] private int _maxPoolSize = 512;

    private readonly Queue<GameObject> _inactive = new Queue<GameObject>();
    private readonly List<GameObject> _instances = new List<GameObject>();

    private int _leasedCount;

    private Transform _runtimeParent;
    private bool _ownsRuntimeParent;

    public int ActiveLeasedCount => _leasedCount;
    public int TotalPooledInstances => _instances.Count;
    public int InactiveCount => _inactive.Count;

    private Transform GetPoolParent()
    {
        if (_container != null)
            return _container;
        EnsureRuntimeParentExists();
        return _runtimeParent;
    }

    private void EnsureRuntimeParentExists()
    {
        if (_container != null || _runtimeParent != null)
            return;

        var holder = new GameObject($"[PooledEnemies] {gameObject.name}");
        _runtimeParent = holder.transform;
        _ownsRuntimeParent = true;

        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);

        if (_debugLogParent)
        {
            Debug.Log(
                $"SwarmEnemyPool: contenedor runtime '{holder.name}' → escena '{targetScene.path}' " +
                $"(pool '{gameObject.name}', scene válida={gameObject.scene.IsValid()}, instanceID={GetInstanceID()}).",
                this);
        }
    }

    private void Awake()
    {
        if (_container == null)
            EnsureRuntimeParentExists();

        if (_enemyPrefab == null)
        {
            Debug.LogError("SwarmEnemyPool: asigna un prefab de enemigo.", this);
            return;
        }

        if (_container != null && _debugLogParent)
        {
            Debug.Log(
                $"SwarmEnemyPool: usando _container '{_container.name}' en escena '{_container.gameObject.scene.path}'.",
                this);
        }

        if (_maxPoolSize < _initialPoolSize)
            _maxPoolSize = _initialPoolSize;

        for (int i = 0; i < _initialPoolSize; i++)
            CreateInstance(enqueueInactive: true);
    }

    private void OnDestroy()
    {
        if (_ownsRuntimeParent && _runtimeParent != null)
            Destroy(_runtimeParent.gameObject);
    }

    private void OnValidate()
    {
        if (_maxPoolSize < _initialPoolSize)
            _maxPoolSize = _initialPoolSize;
    }

    public GameObject TryGet()
    {
        if (_enemyPrefab == null)
            return null;

        GameObject instance;

        if (_inactive.Count > 0)
        {
            instance = _inactive.Dequeue();
        }
        else if (_allowPoolGrowth && _instances.Count < _maxPoolSize)
        {
            instance = CreateInstance(enqueueInactive: false);
        }
        else
        {
            return null;
        }

        ActivateInstance(instance);
        return instance;
    }

    public void Release(GameObject instance)
    {
        if (instance == null || !instance.activeSelf)
            return;

        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        if (pooled == null || !pooled.BelongsTo(this))
            return;

        pooled.NotifyDespawned();
        instance.SetActive(false);
        instance.transform.SetParent(GetPoolParent(), false);
        _leasedCount--;
        _inactive.Enqueue(instance);
    }

    /// <summary>Devuelve al pool todas las instancias activas (leased).</summary>
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
        EnsureRuntimeParentExists();

        GameObject instance = Instantiate(_enemyPrefab);
        instance.name = $"{_enemyPrefab.name} (pool)";

        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(instance, targetScene);

        instance.transform.SetParent(GetPoolParent(), false);

        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        if (pooled == null)
            pooled = instance.AddComponent<SwarmPooledEnemy>();
        pooled.Bind(this);

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
