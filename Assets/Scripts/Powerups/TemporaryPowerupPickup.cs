using UnityEngine;

/// <summary>Pickup de power-up temporal: se recoge caminando (imán + PickupRange de gameplay).</summary>
[RequireComponent(typeof(WorldPickup))]
public class TemporaryPowerupPickup : MonoBehaviour, IPickable
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField, Tooltip("Si true, conserva mesh/materiales del prefab (sin swap ni tint).")]
    private bool _useAuthoredVisual = true;

    [SerializeField] private TemporaryPowerupType _type = TemporaryPowerupType.Nuke;

    private TemporaryPowerupPool _pool;
    private bool _consumed;
    private MeshFilter _meshFilter;
    private Renderer _renderer;
    private MaterialPropertyBlock _propertyBlock;

    public bool UsesAuthoredVisual => _useAuthoredVisual;
    public TemporaryPowerupType Type => _type;

    /// <summary>Marca instancia runtime (esfera + tint) vs prefab con art authored.</summary>
    public void SetUseAuthoredVisual(bool useAuthoredVisual) => _useAuthoredVisual = useAuthoredVisual;

    public void Activate(TemporaryPowerupPool pool, TemporaryPowerupType type)
    {
        _pool = pool;
        _type = type;
        _consumed = false;
        if (TryGetComponent(out WorldPickup worldPickup))
            worldPickup.ConfigureForGameplayCollection(1.5f, 6f, 12f);

        if (!_useAuthoredVisual)
            ApplyVisual(type);
    }

    public void OnPickedUp()
    {
        if (_consumed)
            return;
        _consumed = true;

        TemporaryPowerupController controller = TemporaryPowerupController.Instance;
        if (controller == null)
            controller = FindAnyObjectByType<TemporaryPowerupController>();
        controller?.Apply(_type);

        if (_pool != null)
            _pool.Release(gameObject);
        else
            Destroy(gameObject);
    }

    private void ApplyVisual(TemporaryPowerupType type)
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_renderer == null)
            _renderer = GetComponent<Renderer>();

        Mesh mesh = _pool != null ? _pool.ResolveMesh(type) : null;
        if (_meshFilter != null && mesh != null)
            _meshFilter.sharedMesh = mesh;

        if (_renderer == null)
            return;

        Color c = ResolveColor(type);
        _propertyBlock ??= new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(BaseColorId, c);
        _propertyBlock.SetColor(ColorId, c);
        _renderer.SetPropertyBlock(_propertyBlock);
    }

    private static Color ResolveColor(TemporaryPowerupType type)
    {
        return type switch
        {
            TemporaryPowerupType.ExtraDamage => new Color(1f, 0.4f, 0.1f),
            TemporaryPowerupType.ExtraSpeed => new Color(0.3f, 0.7f, 1f),
            TemporaryPowerupType.ExtraScavenging => new Color(0.3f, 0.95f, 0.4f),
            TemporaryPowerupType.Invulnerability => new Color(1f, 1f, 1f),
            TemporaryPowerupType.FullHeal => new Color(1f, 0.3f, 0.55f),
            TemporaryPowerupType.Nuke => new Color(1f, 0.85f, 0.15f),
            _ => Color.white
        };
    }
}
