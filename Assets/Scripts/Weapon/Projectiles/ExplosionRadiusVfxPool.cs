using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pool de anillos de explosión prearmados (evita new GameObject + LineRenderer por impacto).
/// </summary>
[DefaultExecutionOrder(-31)]
public class ExplosionRadiusVfxPool : MonoBehaviour
{
    private static readonly Color DefaultColor = new(1f, 0.42f, 0.05f, 0.9f);
    private static ExplosionRadiusVfxPool s_Instance;

    [SerializeField, Min(1)] private int _initialSize = 8;
    [SerializeField, Min(1)] private int _maxSize = 32;

    private readonly Queue<ExplosionRadiusVfx> _inactive = new();
    private Transform _parent;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        EnsureParent();

        for (int i = 0; i < _initialSize; i++)
            _inactive.Enqueue(CreateInstance());
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public static bool TrySpawn(Vector3 position, float radius, float duration = 0.42f)
    {
        return TrySpawn(position, radius, DefaultColor, duration);
    }

    public static bool TrySpawn(Vector3 position, float radius, Color color, float duration = 0.42f)
    {
        if (radius <= 0f)
            return false;

        if (s_Instance == null)
            s_Instance = FindAnyObjectByType<ExplosionRadiusVfxPool>();

        if (s_Instance != null && s_Instance.TrySpawnInternal(position, radius, duration, color))
            return true;

        ExplosionRadiusVfx.SpawnRuntime(position, radius, duration, color);
        return true;
    }

    private bool TrySpawnInternal(Vector3 position, float radius, float duration, Color color)
    {
        ExplosionRadiusVfx vfx;
        if (_inactive.Count > 0)
            vfx = _inactive.Dequeue();
        else if (TotalCount() < _maxSize)
            vfx = CreateInstance();
        else
            return false;

        vfx.ActivateFromPool(position, radius, duration, color, this);
        EnemyPoolProfiler.RegisterPoolGet();
        return true;
    }

    public void Release(ExplosionRadiusVfx vfx)
    {
        if (vfx == null)
            return;

        vfx.gameObject.SetActive(false);
        vfx.transform.SetParent(_parent, false);
        _inactive.Enqueue(vfx);
        EnemyPoolProfiler.RegisterPoolRelease();
    }

    private int TotalCount() => _inactive.Count;

    private ExplosionRadiusVfx CreateInstance()
    {
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();

        GameObject go = new GameObject("ExplosionRadiusVfx (pool)");
        SceneManager.MoveGameObjectToScene(go, targetScene);
        go.transform.SetParent(_parent, false);

        ExplosionRadiusVfx vfx = go.AddComponent<ExplosionRadiusVfx>();
        vfx.PrepareForPool();
        go.SetActive(false);
        EnemyPoolProfiler.RegisterInstantiate();
        return vfx;
    }

    private void EnsureParent()
    {
        if (_parent != null)
            return;

        var holder = new GameObject("[PooledExplosionVfx]");
        _parent = holder.transform;
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);
    }
}
