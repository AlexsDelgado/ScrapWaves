using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pool genérico para áreas temporales (FireArea, CorrosiveSlimeArea, etc.).
/// </summary>
[DefaultExecutionOrder(-33)]
public class EnemyTimedAreaPool : MonoBehaviour
{
    private static EnemyTimedAreaPool s_Instance;

    [SerializeField, Min(1)] private int _initialPerPrefab = 4;
    [SerializeField, Min(1)] private int _maxPerPrefab = 32;

    private readonly Dictionary<int, Queue<GameObject>> _inactiveByPrefab = new();
    private readonly Dictionary<int, GameObject> _prefabById = new();
    private Transform _parent;

    public static EnemyTimedAreaPool Instance => s_Instance;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        EnsureParent();
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public static bool TrySpawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
            return false;

        if (s_Instance == null)
            s_Instance = FindAnyObjectByType<EnemyTimedAreaPool>();

        if (s_Instance != null && s_Instance.TrySpawnInternal(prefab, position, rotation))
            return true;

        Object.Instantiate(prefab, position, rotation);
        EnemyPoolProfiler.RegisterInstantiate();
        return true;
    }

    private bool TrySpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        int id = prefab.GetInstanceID();
        if (!_inactiveByPrefab.TryGetValue(id, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            _inactiveByPrefab[id] = queue;
            _prefabById[id] = prefab;
            for (int i = 0; i < _initialPerPrefab; i++)
                queue.Enqueue(CreateInstance(prefab));
        }

        GameObject instance;
        if (queue.Count > 0)
            instance = queue.Dequeue();
        else if (CountForPrefab(id) < _maxPerPrefab)
            instance = CreateInstance(prefab);
        else
            return false;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        EnemyPoolProfiler.RegisterPoolGet();
        return true;
    }

    public void Release(GameObject instance, GameObject sourcePrefab)
    {
        if (instance == null || sourcePrefab == null)
            return;

        instance.SetActive(false);
        instance.transform.SetParent(_parent, false);

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

    private int CountForPrefab(int prefabId)
    {
        if (!_inactiveByPrefab.TryGetValue(prefabId, out Queue<GameObject> queue))
            return 0;
        return queue.Count;
    }

    private GameObject CreateInstance(GameObject prefab)
    {
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();

        GameObject instance = Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(instance, targetScene);
        instance.transform.SetParent(_parent, false);
        EnemyPoolProfiler.RegisterInstantiate();
        instance.SetActive(false);

        PooledTimedAreaMember member = instance.GetComponent<PooledTimedAreaMember>();
        if (member == null)
            member = instance.AddComponent<PooledTimedAreaMember>();
        member.Bind(this, prefab);

        return instance;
    }

    private void EnsureParent()
    {
        if (_parent != null)
            return;

        var holder = new GameObject("[PooledEnemyAreas]");
        _parent = holder.transform;
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);
    }
}
