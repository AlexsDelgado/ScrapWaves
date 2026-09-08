using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-34)]
public class ScrapPickupPool : MonoBehaviour
{
    private static ScrapPickupPool s_Instance;
    private readonly Queue<GameObject> _inactive = new();
    private Transform _parent;

    public static ScrapPickupPool GetInstance()
    {
        if (s_Instance != null)
            return s_Instance;
        s_Instance = FindAnyObjectByType<ScrapPickupPool>();
        if (s_Instance != null)
            return s_Instance;
        var go = new GameObject("[ScrapPickupPool]");
        s_Instance = go.AddComponent<ScrapPickupPool>();
        return s_Instance;
    }

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        EnsureParent();
        for (int i = 0; i < 8; i++)
            _inactive.Enqueue(CreateInstance());
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public bool TrySpawn(Vector3 position)
    {
        GameObject instance = _inactive.Count > 0 ? _inactive.Dequeue() : CreateInstance();
        instance.transform.SetPositionAndRotation(position + Vector3.up * 0.4f, Quaternion.identity);
        if (instance.TryGetComponent(out ScrapPickup pickup))
            pickup.Activate(this);
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

    private GameObject CreateInstance()
    {
        EnsureParent();
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "ScrapPickup";
        go.transform.localScale = Vector3.one * 0.35f;
        go.transform.SetParent(_parent, false);
        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);
        if (go.TryGetComponent(out Renderer r))
            r.material.color = new Color(0.95f, 0.75f, 0.2f);
        // ScrapPickup requires WorldPickup; Unity adds it first.
        go.AddComponent<ScrapPickup>();
        go.SetActive(false);
        return go;
    }

    private void EnsureParent()
    {
        if (_parent != null)
            return;
        var holder = new GameObject("[PooledScrap]");
        _parent = holder.transform;
        SceneManager.MoveGameObjectToScene(holder, SceneManager.GetActiveScene());
    }
}
