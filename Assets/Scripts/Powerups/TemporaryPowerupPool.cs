using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-34)]
public class TemporaryPowerupPool : MonoBehaviour
{
    private static TemporaryPowerupPool s_Instance;

    [SerializeField, Min(1)] private int _initialSize = 8;
    [SerializeField, Min(1)] private int _maxSize = 32;

    private readonly Queue<GameObject> _inactive = new();
    private Transform _parent;

    public static TemporaryPowerupPool Instance => s_Instance;

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

    public bool TrySpawn(Vector3 position, TemporaryPowerupType type)
    {
        GameObject instance;
        if (_inactive.Count > 0)
            instance = _inactive.Dequeue();
        else if (CountActiveAndInactive() < _maxSize)
            instance = CreateInstance();
        else
            return false;

        instance.transform.SetPositionAndRotation(position + Vector3.up * 0.5f, Quaternion.identity);
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
        _inactive.Enqueue(instance);
    }

    private int CountActiveAndInactive() => _inactive.Count; // soft cap

    private GameObject CreateInstance()
    {
        EnsureParent();
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "TemporaryPowerup";
        go.transform.localScale = Vector3.one * 0.55f;
        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        // TemporaryPowerupPickup requires WorldPickup; Unity adds it first.
        go.AddComponent<TemporaryPowerupPickup>();
        go.SetActive(false);
        go.transform.SetParent(_parent, false);
        return go;
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
