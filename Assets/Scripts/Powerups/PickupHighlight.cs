using UnityEngine;

/// <summary>
/// Readability aura for floor pickups: a fresnel rim shell over the pickup mesh plus a flat ground ring.
/// Both use the additive "ScrapWaves/GameFeel/Pickup Highlight" shader (materials live in Resources) and are
/// tinted per instance with a MaterialPropertyBlock. Attach is idempotent, so pooled pickups reuse the children.
/// </summary>
[DisallowMultipleComponent]
public sealed class PickupHighlight : MonoBehaviour
{
    private const string RimMaterialPath = "GameFeel/PickupRim";
    private const string RingMaterialPath = "GameFeel/PickupRing";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private static Material s_rim;
    private static Material s_ring;
    private static Mesh s_groundQuad;

    private Renderer _shell;
    private Transform _ring;
    private Renderer _ringRenderer;
    private MaterialPropertyBlock _block;
    private Color _color = Color.white;
    private float _ringDiameter = 1.6f;
    private float _groundY;
    private float _nextGroundProbe;
    private float _phase;

    public static PickupHighlight Attach(GameObject root, Color color, bool shell, bool ring, float ringDiameter, MeshFilter shellSource = null)
    {
        if (root == null)
            return null;

        PickupHighlight highlight = root.GetComponent<PickupHighlight>();
        if (highlight == null)
            highlight = root.AddComponent<PickupHighlight>();
        highlight.Configure(color, shell, ring, ringDiameter, shellSource);
        return highlight;
    }

    /// <summary>Hides both effects (pooled commons reuse a drop that previously showed a rare aura).</summary>
    public void SetVisible(bool visible)
    {
        if (_shell != null)
            _shell.gameObject.SetActive(visible);
        if (_ring != null)
            _ring.gameObject.SetActive(visible);
    }

    private void Configure(Color color, bool shell, bool ring, float ringDiameter, MeshFilter shellSource)
    {
        _color = color;
        _ringDiameter = ringDiameter;
        EnsureMaterials();
        _block ??= new MaterialPropertyBlock();

        if (shell)
            EnsureShell(shellSource != null ? shellSource : GetComponent<MeshFilter>());
        if (_shell != null)
        {
            _shell.gameObject.SetActive(shell);
            Tint(_shell, 1.6f);
        }

        if (ring)
            EnsureRing();
        if (_ring != null)
        {
            _ring.gameObject.SetActive(ring);
            Tint(_ringRenderer, 1.4f);
            _nextGroundProbe = 0f;
        }
    }

    private void OnEnable()
    {
        _phase = Random.value * 6.28f;
        _nextGroundProbe = 0f;
    }

    private void LateUpdate()
    {
        if (_ring == null || !_ring.gameObject.activeSelf)
            return;

        Vector3 pos = transform.position;
        if (Time.time >= _nextGroundProbe)
        {
            _nextGroundProbe = Time.time + 0.25f;
            _groundY = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y + 0.04f
                : pos.y - 0.5f;
        }

        float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
        _ring.SetPositionAndRotation(new Vector3(pos.x, _groundY, pos.z), Quaternion.identity);
        _ring.localScale = Vector3.one * (_ringDiameter / parentScale);

        // Static effects freeze the pulse; the shader then reads a constant phase.
        float pulse = HudVfxAccessibility.StaticEffects ? _phase : Time.unscaledTime * 3f + _phase;
        TintPulse(_shell, pulse);
        TintPulse(_ringRenderer, pulse);
    }

    private void TintPulse(Renderer target, float pulse)
    {
        if (target == null)
            return;
        target.GetPropertyBlock(_block);
        _block.SetFloat(PulseId, pulse);
        target.SetPropertyBlock(_block);
    }

    private void Tint(Renderer target, float intensity)
    {
        if (target == null)
            return;
        target.GetPropertyBlock(_block);
        _block.SetColor(ColorId, _color);
        _block.SetFloat(IntensityId, intensity);
        target.SetPropertyBlock(_block);
    }

    private void EnsureShell(MeshFilter source)
    {
        if (source == null || source.sharedMesh == null || s_rim == null)
            return;
        if (_shell != null)
        {
            if (_shell.transform.parent == source.transform)
                return;
            // Pooled drop switched visual: rebuild the shell on the new mesh.
            Destroy(_shell.gameObject);
            _shell = null;
        }

        var go = new GameObject("PickupRim");
        go.transform.SetParent(source.transform, false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = source.sharedMesh;
        var meshRenderer = go.AddComponent<MeshRenderer>();
        var mats = new Material[Mathf.Max(1, source.sharedMesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = s_rim;
        meshRenderer.sharedMaterials = mats;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        _shell = meshRenderer;
    }

    private void EnsureRing()
    {
        if (_ring != null || s_ring == null)
            return;

        var go = new GameObject("PickupGroundRing");
        go.transform.SetParent(transform, false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = GetGroundQuad();
        _ringRenderer = go.AddComponent<MeshRenderer>();
        _ringRenderer.sharedMaterial = s_ring;
        _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ringRenderer.receiveShadows = false;
        _ring = go.transform;
    }

    private static void EnsureMaterials()
    {
        if (s_rim == null)
            s_rim = Resources.Load<Material>(RimMaterialPath);
        if (s_ring == null)
            s_ring = Resources.Load<Material>(RingMaterialPath);
    }

    private static Mesh GetGroundQuad()
    {
        if (s_groundQuad != null)
            return s_groundQuad;

        s_groundQuad = new Mesh { name = "PickupGroundQuad" };
        s_groundQuad.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
        };
        s_groundQuad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        s_groundQuad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        s_groundQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        s_groundQuad.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));
        return s_groundQuad;
    }
}
