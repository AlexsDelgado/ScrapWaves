using UnityEngine;

/// <summary>
/// Repite el albedo (y la máscara de metal, si aplica) según el tamaño en el mundo.
/// Los cubos del graybox tienen UV 0-1 en cada cara: sin esto la textura se estira.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class GrayboxWorldTiling : MonoBehaviour
{
    [SerializeField, Min(0.25f)] private float _metersPerTile = 48f;
    [SerializeField, Min(1f)] private float _maxRepeats = 4f;
    [SerializeField] private bool _tileMetallicMap;

    private void OnEnable() => Apply();

#if UNITY_EDITOR
    private void OnValidate() => Apply();
#endif

    public void Configure(float metersPerTile, float maxRepeats, bool tileMetallicMap)
    {
        _metersPerTile = Mathf.Max(0.25f, metersPerTile);
        _maxRepeats = Mathf.Max(1f, maxRepeats);
        _tileMetallicMap = tileMetallicMap;
        Apply();
    }

    public void Apply()
    {
        if (!TryGetComponent(out Renderer renderer))
            return;

        Vector3 size = renderer.bounds.size;
        float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        if (longest < 0.01f)
            return;

        float repeats = longest / Mathf.Max(0.25f, _metersPerTile);
        repeats = Mathf.Clamp(repeats, 1f, _maxRepeats);
        var scale = new Vector4(repeats, repeats, 0f, 0f);

        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetVector("_BaseMap_ST", scale);
        block.SetVector("_MainTex_ST", scale);
        if (_tileMetallicMap)
            block.SetVector("_MetallicGlossMap_ST", scale);
        renderer.SetPropertyBlock(block);
    }
}
