using UnityEngine;

/// <summary>
/// Marca un área temporal pooled y devuelve al pool al expirar.
/// </summary>
public class PooledTimedAreaMember : MonoBehaviour
{
    private EnemyTimedAreaPool _pool;
    private GameObject _sourcePrefab;

    public void Bind(EnemyTimedAreaPool pool, GameObject sourcePrefab)
    {
        _pool = pool;
        _sourcePrefab = sourcePrefab;
    }

    public void ReturnToPool()
    {
        if (_pool != null && _sourcePrefab != null)
            _pool.Release(gameObject, _sourcePrefab);
        else
        {
            EnemyPoolProfiler.RegisterDestroy();
            Destroy(gameObject);
        }
    }
}
