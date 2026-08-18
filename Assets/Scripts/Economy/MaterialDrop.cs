using UnityEngine;

public class MaterialDrop : MonoBehaviour
{
    [SerializeField, Min(1)] private int _defaultAmount = 1;
    [SerializeField, Min(0.01f)] private float _pickupRadius = 0.75f;
    [SerializeField, Min(0f)] private float _magnetRadius = 7f;
    [SerializeField, Min(0f)] private float _magnetSpeed = 14f;
    [SerializeField, Min(0f), Tooltip("Offset vertical al spawnear para no enterrar el modelo en el suelo.")]
    private float _spawnHeightOffset = 0.5f;
    [SerializeField, Min(0f), Tooltip("Rotación lenta del drop (grados/segundo) sobre el visual.")]
    private float _spinDegreesPerSecond = 45f;
    [SerializeField, Range(0.1f, 1f), Tooltip("Visual scale of dropped materials. Does not affect pickup range.")]
    private float _visualScale = 0.6f;

    [SerializeField, Tooltip("Layer(s) contra las que cae el drop. Vacío = Terrain + Default.")]
    private LayerMask _groundMask;

    private MaterialPool _pool;
    private MaterialPoolMember _member;
    private MaterialType _material;
    private int _amount;
    private MeshRenderer _legacyRenderer;
    private Transform _visualRoot;
    private readonly System.Collections.Generic.Dictionary<MaterialType, GameObject> _visuals = new();

    private bool _isFalling;
    private float _fallVelocity;

    private void Awake()
    {
        _member = GetComponent<MaterialPoolMember>();
        _legacyRenderer = GetComponent<MeshRenderer>();
        EnsureVisualRoot();

        if (_groundMask.value == 0)
            _groundMask = LayerMask.GetMask("Terrain", "Default");
    }

    public void ActivateFromPool(MaterialPool pool, MaterialType material, int amount)
    {
        _pool = pool;
        _material = material;
        _amount = amount > 0 ? amount : _defaultAmount;

        // El enemigo puede haber muerto en el aire (p. ej. voladores): cae por gravedad simple
        // hasta tocar el suelo en vez de quedar flotando en el punto exacto de la muerte.
        _isFalling = true;
        _fallVelocity = 0f;

        ApplyVisual(pool != null ? pool.VisualCatalog : null, material);
    }

    private void ApplyVisual(MaterialDropVisualCatalog catalog, MaterialType material)
    {
        EnsureVisualRoot();

        if (_legacyRenderer != null)
            _legacyRenderer.enabled = false;

        foreach (var pair in _visuals)
        {
            if (pair.Value != null)
                pair.Value.SetActive(false);
        }

        GameObject prefab = catalog != null ? catalog.GetVisualPrefab(material) : null;
        if (prefab == null)
        {
            // Sin visual: no reactivar la esfera vacía; el pickup por distancia sigue funcionando.
            Debug.LogWarning($"MaterialDrop: no hay VisualPrefab para {material}. Asignalo en MaterialDropVisualCatalog.", this);
            return;
        }

        if (!_visuals.TryGetValue(material, out GameObject instance) || instance == null)
        {
            // Preservar escala/rotación del prefab (los Pickups usan ~50–200 de scale).
            instance = Instantiate(prefab, _visualRoot, false);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            StripGameplayComponents(instance);
            _visuals[material] = instance;
        }

        instance.SetActive(true);
    }

    private static void StripGameplayComponents(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            Destroy(colliders[i]);

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
            Destroy(bodies[i]);
    }

    private void EnsureVisualRoot()
    {
        if (_visualRoot == null)
        {
            Transform existing = transform.Find("Visuals");
            if (existing != null)
            {
                _visualRoot = existing;
            }
            else
            {
                var go = new GameObject("Visuals");
                _visualRoot = go.transform;
                _visualRoot.SetParent(transform, false);
                _visualRoot.localPosition = Vector3.zero;
                _visualRoot.localRotation = Quaternion.identity;
            }
        }

        _visualRoot.localScale = Vector3.one * _visualScale;
    }

    private void Update()
    {
        if (_spinDegreesPerSecond > 0f && _visualRoot != null)
            _visualRoot.Rotate(Vector3.up, _spinDegreesPerSecond * Time.deltaTime);

        if (_isFalling)
        {
            Vector3 fallPos = transform.position;
            if (PickupGroundFall.Tick(ref fallPos, ref _fallVelocity, Time.deltaTime, _spawnHeightOffset, _groundMask))
                _isFalling = false;
            transform.position = fallPos;
            return;
        }

        MaterialPickupReceiver receiver = MaterialPickupReceiver.Instance;
        if (receiver == null)
            return;

        Vector3 target = receiver.PickupPoint;
        float dist = Vector3.Distance(transform.position, target);
        if (dist <= _pickupRadius)
        {
            receiver.GrantMaterial(_material, _amount);
            if (_member != null)
                _member.Despawn();
            else
                _pool?.Release(gameObject);
            return;
        }

        if (_magnetRadius > 0f && _magnetSpeed > 0f && dist <= _magnetRadius)
            transform.position = Vector3.MoveTowards(transform.position, target, _magnetSpeed * Time.deltaTime);
    }
}
