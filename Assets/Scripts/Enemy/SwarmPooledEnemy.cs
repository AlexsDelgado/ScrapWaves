using UnityEngine;

public class SwarmPooledEnemy : MonoBehaviour
{
    private SwarmEnemyPool _legacyPool;
    private GameObject _sourcePrefab;
    private bool _registryBound;

    public GameObject SourcePrefab => _sourcePrefab != null ? _sourcePrefab : _legacyPool != null ? _legacyPool.EnemyPrefab : null;
    public bool IsBound => _legacyPool != null || _registryBound;
    public bool IsBoundToRegistry => _registryBound;

    public void Bind(SwarmEnemyPool pool)
    {
        _legacyPool = pool;
        _registryBound = false;
        if (pool != null && pool.EnemyPrefab != null)
            _sourcePrefab = pool.EnemyPrefab;
    }

    public void BindRegistry(GameObject sourcePrefab = null)
    {
        _legacyPool = null;
        _registryBound = true;
        if (sourcePrefab != null)
            _sourcePrefab = sourcePrefab;
    }

    public bool BelongsTo(SwarmEnemyPool pool) => _legacyPool == pool;

    public void NotifySpawned()
    {
        ResetForPoolSpawn();
    }

    public void NotifyDespawned()
    {
        ResetForPoolDespawn();
    }

    public void Despawn()
    {
        if (_legacyPool != null)
        {
            _legacyPool.Release(gameObject);
            return;
        }

        if (_registryBound && EnemyPoolRegistry.Instance != null)
        {
            EnemyPoolRegistry.Instance.Release(gameObject);
            return;
        }

        EnemyPoolProfiler.RegisterDestroy();
        Destroy(gameObject);
    }

    private void ResetForPoolSpawn()
    {
        if (TryGetComponent(out EnemyHealth health))
            health.PrepareForPoolSpawn();

        GetComponent<EnemyFollow>()?.PrepareForSpawn();
        GetComponent<SimpleFollow>()?.PrepareForSpawn();

        IEnemySpawnLifecycle[] lifecycles = GetComponents<IEnemySpawnLifecycle>();
        for (int i = 0; i < lifecycles.Length; i++)
            lifecycles[i].OnPoolSpawn();
    }

    private void ResetForPoolDespawn()
    {
        GetComponent<EnemyFollow>()?.OnDespawned();
        GetComponent<SimpleFollow>()?.OnDespawned();

        IEnemySpawnLifecycle[] lifecycles = GetComponents<IEnemySpawnLifecycle>();
        for (int i = 0; i < lifecycles.Length; i++)
            lifecycles[i].OnPoolDespawn();
    }
}
