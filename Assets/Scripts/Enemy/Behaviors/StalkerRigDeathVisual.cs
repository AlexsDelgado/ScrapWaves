using UnityEngine;

/// <summary>Owns only the detached rig after logical death; it never delays health, rewards or objective completion.</summary>
public sealed class StalkerRigDeathVisual : MonoBehaviour
{
    private static readonly int DeathState = Animator.StringToHash("Base Layer.Death");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private Animator _animator;
    private SkinnedMeshRenderer _body;
    private Material[] _fadeMaterials;
    private MaterialPropertyBlock _fadeBlock;
    private float _duration;
    private float _elapsed;
    private bool _configured;
    private bool _fadeAttempted;
    private Transform[] _bones;
    private Vector3[] _handoffPositions;
    private Quaternion[] _handoffRotations;

    public bool IsComplete { get; private set; }

    public void Configure(Animator animator, SkinnedMeshRenderer body, float duration = 1.1f)
    {
        _animator = animator;
        _body = body;
        _duration = Mathf.Max(0.1f, duration);
        _elapsed = 0f;
        _configured = _animator != null && _body != null;
        if (!_configured) return;
        // The live rig can be conformed to the flight arch after Animator evaluation.
        // Preserve that visible pose while the authored death animation takes over.
        _bones = _body.bones;
        _handoffPositions = new Vector3[_bones.Length];
        _handoffRotations = new Quaternion[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
        {
            _handoffPositions[i] = _bones[i].localPosition;
            _handoffRotations[i] = _bones[i].localRotation;
        }
        _animator.enabled = true;
        _animator.applyRootMotion = false;
        _animator.speed = 1f;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.CrossFadeInFixedTime(DeathState, 0.08f, 0, 0f);
        // Transient hit/status shells belong to the dying gameplay actor, not its death pose.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != _body) renderers[i].enabled = false;
    }

    private void Update() => Step(Time.deltaTime);
    private void LateUpdate() => ApplyPoseHandoff();

    public void ApplyPoseHandoff()
    {
        if (!_configured || IsComplete || _elapsed >= 0.2f) return;
        float blend = Mathf.SmoothStep(0f, 1f, _elapsed / 0.2f);
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;
            _bones[i].localPosition = Vector3.Lerp(_handoffPositions[i], _bones[i].localPosition, blend);
            _bones[i].localRotation = Quaternion.Slerp(_handoffRotations[i], _bones[i].localRotation, blend);
        }
    }

    /// <summary>Does not evaluate Animator; deterministic previews can evaluate it separately.</summary>
    public void Step(float deltaTime)
    {
        if (!_configured || IsComplete || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        _elapsed += deltaTime;
        float fadeDuration = Mathf.Min(0.25f, _duration);
        float fade = Mathf.Clamp01((_elapsed - (_duration - fadeDuration)) / fadeDuration);
        if (fade > 0f)
        {
            if (!_fadeAttempted) BeginFade();
            if (_fadeMaterials != null && _body != null)
            {
                _fadeBlock.Clear();
                _fadeBlock.SetFloat(DissolveId, fade);
                _fadeBlock.SetFloat(OpacityId, 1f - fade);
                _body.SetPropertyBlock(_fadeBlock);
            }
        }
        if (_elapsed < _duration) return;
        IsComplete = true;
        if (_body != null) _body.enabled = false;
        if (Application.isPlaying) Destroy(gameObject);
        else enabled = false;
    }

    private void BeginFade()
    {
        _fadeAttempted = true;
        if (_body == null) return;
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Enemy Disintegration");
        if (shader == null) return;
        Material[] originals = _body.sharedMaterials;
        _fadeMaterials = new Material[originals.Length];
        _fadeBlock = new MaterialPropertyBlock();
        for (int i = 0; i < originals.Length; i++)
        {
            Material source = originals[i];
            Material material = new(shader) { hideFlags = HideFlags.HideAndDontSave };
            Texture texture = Texture2D.whiteTexture;
            Color color = Color.white;
            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;
            if (source != null)
            {
                if (source.HasProperty("_BaseMap"))
                {
                    texture = source.GetTexture("_BaseMap") ?? Texture2D.whiteTexture;
                    scale = source.GetTextureScale("_BaseMap");
                    offset = source.GetTextureOffset("_BaseMap");
                }
                else if (source.mainTexture != null)
                {
                    texture = source.mainTexture;
                    scale = source.mainTextureScale;
                    offset = source.mainTextureOffset;
                }
                if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color")) color = source.GetColor("_Color");
            }
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetColor("_BaseColor", color);
            material.SetColor("_EdgeColor", color);
            material.SetColor("_AshColor", color * 0.5f);
            material.SetFloat("_Luminescence", 0f);
            material.SetFloat("_Opacity", 1f);
            _fadeMaterials[i] = material;
        }
        _body.sharedMaterials = _fadeMaterials;
    }

    private void OnDestroy()
    {
        if (_fadeMaterials == null) return;
        for (int i = 0; i < _fadeMaterials.Length; i++)
        {
            if (_fadeMaterials[i] == null) continue;
            if (Application.isPlaying) Destroy(_fadeMaterials[i]);
            else DestroyImmediate(_fadeMaterials[i]);
        }
    }
}
