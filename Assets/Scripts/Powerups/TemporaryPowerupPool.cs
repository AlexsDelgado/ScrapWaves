using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-34)]
public class TemporaryPowerupPool : MonoBehaviour
{
    private static TemporaryPowerupPool s_Instance;
    private const string CatalogResourcePath = "TemporaryPowerupVisualCatalog";
    private const string PrefabFolder = "Assets/Prefabs/Pickups/powerups";

    private static readonly string[] PrefabNames =
    {
        "PowerUp_ExtraDamage",
        "PowerUp_ExtraSpeed",
        "PowerUp_ExtraScavenging",
        "PowerUp_Invulnerability",
        "PowerUp_FullHeal",
        "PowerUp_Nuke"
    };

    [SerializeField, Min(1)] private int _initialSize = 8;
    [SerializeField, Min(1)] private int _maxSize = 48;
    [SerializeField] private TemporaryPowerupVisualCatalog _visualCatalog;
    [SerializeField, Tooltip("Prefabs authored por TemporaryPowerupType (índice = enum).")]
    private GameObject[] _prefabsByType = new GameObject[6];

    private readonly Queue<GameObject>[] _inactiveByType = new Queue<GameObject>[6];
    private readonly Queue<GameObject> _inactiveRuntime = new();
    private Transform _parent;
    private Mesh _fallbackSphereMesh;

    public static TemporaryPowerupPool Instance => s_Instance;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        for (int i = 0; i < _inactiveByType.Length; i++)
            _inactiveByType[i] = new Queue<GameObject>();

        EnsureCatalog();
        EnsureAllPrefabs();
        EnsureParent();
        CacheFallbackSphere();
        Prewarm();
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public static TemporaryPowerupPool GetInstance()
    {
        if (s_Instance != null)
            return s_Instance;
        s_Instance = FindAnyObjectByType<TemporaryPowerupPool>();
        if (s_Instance != null)
            return s_Instance;

        var go = new GameObject("[TemporaryPowerupPool]");
        s_Instance = go.AddComponent<TemporaryPowerupPool>();
        return s_Instance;
    }

    public Mesh ResolveMesh(TemporaryPowerupType type)
    {
        EnsureCatalog();
        Mesh mesh = _visualCatalog != null ? _visualCatalog.GetMesh(type) : null;
        return mesh != null ? mesh : _fallbackSphereMesh;
    }

    public bool TrySpawn(Vector3 position, TemporaryPowerupType type)
    {
        int index = (int)type;
        GameObject prefab = GetPrefab(type);
        bool usePrefab = prefab != null;
        GameObject instance;

        if (usePrefab)
        {
            Queue<GameObject> queue = _inactiveByType[index];
            if (queue.Count > 0)
                instance = queue.Dequeue();
            else if (CountPooled() < _maxSize)
                instance = CreatePrefabInstance(type, prefab);
            else
                return false;
        }
        else
        {
            if (_inactiveRuntime.Count > 0)
                instance = _inactiveRuntime.Dequeue();
            else if (CountPooled() < _maxSize)
                instance = CreateRuntimeInstance();
            else
                return false;
        }

        if (instance == null)
            return false;

        Quaternion rot = usePrefab ? prefab.transform.rotation : Quaternion.identity;
        instance.transform.SetPositionAndRotation(position + Vector3.up * 0.5f, rot);
        if (instance.TryGetComponent(out TemporaryPowerupPickup pickup))
            pickup.Activate(this, type);
        instance.SetActive(true);
        return true;
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
            return;
        instance.SetActive(false);
        instance.transform.SetParent(_parent, false);

        if (instance.TryGetComponent(out TemporaryPowerupPickup pickup)
            && pickup.UsesAuthoredVisual
            && (int)pickup.Type >= 0
            && (int)pickup.Type < _inactiveByType.Length)
        {
            _inactiveByType[(int)pickup.Type].Enqueue(instance);
            return;
        }

        _inactiveRuntime.Enqueue(instance);
    }

    private void Prewarm()
    {
        int typesWithPrefab = 0;
        for (int i = 0; i < PrefabNames.Length; i++)
        {
            if (GetPrefab((TemporaryPowerupType)i) != null)
                typesWithPrefab++;
        }

        if (typesWithPrefab > 0)
        {
            int perType = Mathf.Max(1, _initialSize / Mathf.Max(1, typesWithPrefab));
            for (int i = 0; i < PrefabNames.Length; i++)
            {
                var type = (TemporaryPowerupType)i;
                GameObject prefab = GetPrefab(type);
                if (prefab == null)
                    continue;
                for (int n = 0; n < perType; n++)
                    _inactiveByType[i].Enqueue(CreatePrefabInstance(type, prefab));
            }
            return;
        }

        for (int i = 0; i < _initialSize; i++)
            _inactiveRuntime.Enqueue(CreateRuntimeInstance());
    }

    private int CountPooled()
    {
        int total = _inactiveRuntime.Count;
        for (int i = 0; i < _inactiveByType.Length; i++)
            total += _inactiveByType[i].Count;
        return total;
    }

    private GameObject GetPrefab(TemporaryPowerupType type)
    {
        EnsureAllPrefabs();
        int index = (int)type;
        if (_prefabsByType == null || index < 0 || index >= _prefabsByType.Length)
            return null;
        return _prefabsByType[index];
    }

    private GameObject CreatePrefabInstance(TemporaryPowerupType type, GameObject prefab)
    {
        EnsureParent();
        GameObject go = Instantiate(prefab, _parent);
        go.name = PrefabNames[(int)type] + "(Clone)";
        if (go.TryGetComponent(out TemporaryPowerupPickup pickup))
            pickup.SetUseAuthoredVisual(true);
        go.SetActive(false);
        return go;
    }

    private GameObject CreateRuntimeInstance()
    {
        EnsureParent();
        CacheFallbackSphere();

        var go = new GameObject("TemporaryPowerup");
        go.transform.localScale = Vector3.one;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = _fallbackSphereMesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateRuntimeMaterial();

        var pickup = go.AddComponent<TemporaryPowerupPickup>();
        pickup.SetUseAuthoredVisual(false);
        go.SetActive(false);
        go.transform.SetParent(_parent, false);
        return go;
    }

    private void EnsureAllPrefabs()
    {
        if (_prefabsByType == null || _prefabsByType.Length != PrefabNames.Length)
            _prefabsByType = new GameObject[PrefabNames.Length];

        for (int i = 0; i < PrefabNames.Length; i++)
        {
            if (_prefabsByType[i] != null)
                continue;
#if UNITY_EDITOR
            string path = PrefabFolder + "/" + PrefabNames[i] + ".prefab";
            _prefabsByType[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#endif
        }
    }

    private void EnsureCatalog()
    {
        if (_visualCatalog != null)
            return;
        _visualCatalog = Resources.Load<TemporaryPowerupVisualCatalog>(CatalogResourcePath);
    }

    private void CacheFallbackSphere()
    {
        if (_fallbackSphereMesh != null)
            return;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _fallbackSphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying)
            Destroy(temp);
        else
            DestroyImmediate(temp);
    }

    private static Material CreateRuntimeMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        var mat = new Material(shader);
        mat.name = "TemporaryPowerupRuntime";
        return mat;
    }

    private void EnsureParent()
    {
        if (_parent != null)
            return;
        var holder = new GameObject("[PooledPowerups]");
        if (gameObject.scene.IsValid())
            SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
        else
            SceneManager.MoveGameObjectToScene(holder, SceneManager.GetActiveScene());
        _parent = holder.transform;
    }
}
