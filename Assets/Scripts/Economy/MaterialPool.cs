using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-33)]
public class MaterialPool : MonoBehaviour
{
    [SerializeField] private GameObject _materialOrbPrefab;
    [SerializeField] private MaterialDropVisualCatalog _visualCatalog;
    [SerializeField] private Transform _container;
    [SerializeField, Min(1)] private int _initialPoolSize = 128;
    [SerializeField] private bool _allowPoolGrowth = true;
    [SerializeField, Min(1)] private int _maxPoolSize = 2048;

    private readonly Queue<GameObject> _inactive = new();
    private readonly List<GameObject> _instances = new();
    private Transform _runtimeParent;
    private bool _ownsRuntimeParent;
    private int _leasedCount;

    public static MaterialPool Instance { get; private set; }
    public MaterialDropVisualCatalog VisualCatalog => _visualCatalog;

    public void SetVisualCatalog(MaterialDropVisualCatalog catalog) => _visualCatalog = catalog;

    public static MaterialPool GetInstance()
    {
        if (Instance != null)
            return Instance;
        return FindAnyObjectByType<MaterialPool>();
    }

    private void OnEnable() => Instance = this;
    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Awake()
    {
        if (_container == null)
            EnsureRuntimeParentExists();

        if (_materialOrbPrefab == null)
        {
            Debug.LogError("MaterialPool: assign material orb prefab.", this);
            return;
        }

        for (int i = 0; i < _initialPoolSize; i++)
            CreateInstance(enqueueInactive: true);
    }

    private void OnDestroy()
    {
        if (_ownsRuntimeParent && _runtimeParent != null)
            Destroy(_runtimeParent.gameObject);
    }

    public bool TrySpawn(Vector3 worldPosition, MaterialType material, int amount)
    {
        if (_materialOrbPrefab == null || amount <= 0)
            return false;

        GameObject instance = TryGet();
        if (instance == null)
            return false;

        instance.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        MaterialDrop drop = instance.GetComponent<MaterialDrop>();
        if (drop == null)
        {
            Release(instance);
            return false;
        }

        drop.ActivateFromPool(this, material, amount);
        return true;
    }

    public void Release(GameObject instance)
    {
        if (instance == null || !instance.activeSelf)
            return;

        MaterialPoolMember member = instance.GetComponent<MaterialPoolMember>();
        if (member == null || !member.BelongsTo(this))
            return;

        instance.SetActive(false);
        instance.transform.SetParent(GetPoolParent(), false);
        _leasedCount--;
        _inactive.Enqueue(instance);
    }

    private GameObject TryGet()
    {
        GameObject instance;
        if (_inactive.Count > 0)
            instance = _inactive.Dequeue();
        else if (_allowPoolGrowth && _instances.Count < _maxPoolSize)
            instance = CreateInstance(enqueueInactive: false);
        else
            return null;

        instance.SetActive(true);
        _leasedCount++;
        return instance;
    }

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

        var holder = new GameObject($"[PooledMaterials] {gameObject.name}");
        _runtimeParent = holder.transform;
        _ownsRuntimeParent = true;
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);
    }

    private GameObject CreateInstance(bool enqueueInactive)
    {
        EnsureRuntimeParentExists();
        GameObject instance = Instantiate(_materialOrbPrefab);
        instance.name = $"{_materialOrbPrefab.name} (pool)";
        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(instance, targetScene);
        instance.transform.SetParent(GetPoolParent(), false);

        MaterialPoolMember member = instance.GetComponent<MaterialPoolMember>();
        if (member == null)
            member = instance.AddComponent<MaterialPoolMember>();
        member.Bind(this);

        instance.SetActive(false);
        _instances.Add(instance);
        if (enqueueInactive)
            _inactive.Enqueue(instance);
        return instance;
    }
}
