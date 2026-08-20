using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyStatusFeedback))]
public sealed class EnemyHitFeedback : MonoBehaviour
{
    private sealed class VisualState
    {
        public Transform Transform;
        public Vector3 Position;
        public Vector3 Scale;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int LuminescenceId = Shader.PropertyToID("_Luminescence");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static Material s_flashMaterial;

    [Header("Authoring")]
    [SerializeField] private EnemyReactionProfile _profile;
    [SerializeField, Tooltip("Optional cosmetic-only root. Gameplay colliders and navigation should remain outside it.")]
    private Transform _visualRoot;
    [SerializeField] private Renderer[] _renderers;
    [SerializeField] private AnimationCurve _responseCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private readonly List<VisualState> _visuals = new();
    private readonly List<Renderer> _flashShells = new();
    private MaterialPropertyBlock _block;
    private Vector3 _hitDirection;
    private Color _activeColor;
    private float _activeDuration;
    private float _remaining;
    private float _intensity;
    private float _displacement;
    private float _squash;
    private bool _reducedFlash;

    public bool IsPlaying => _remaining > 0f;
    public EnemyReactionTier CurrentTier { get; private set; }
    public int FlashShellCount => _flashShells.Count;

    private void Awake() => CacheVisuals();

    private void OnEnable()
    {
        CacheVisuals();
        RestoreVisuals();
    }

    private void Update()
    {
        if (_remaining <= 0f)
            return;
        _remaining = Mathf.Max(0f, _remaining - Time.unscaledDeltaTime);
        float normalized = 1f - _remaining / Mathf.Max(0.01f, _activeDuration);
        float response = Mathf.Clamp01(_responseCurve.Evaluate(normalized)) * _intensity;
        ApplyFlash(response);
        ApplyTransformResponse(response);
        if (_remaining <= 0f)
            RestoreVisuals();
    }

    private void OnDisable() => RestoreVisuals();

    private void OnDestroy()
    {
        for (int i = 0; i < _flashShells.Count; i++)
            if (_flashShells[i] != null)
                DestroySafely(_flashShells[i].gameObject);
        _flashShells.Clear();
    }

    public void Play(in WeaponFeedbackContext context, bool reducedFlash)
    {
        Play(in context, reducedFlash, EnemyReactionRuntime.ReducedMotion);
    }

    public void Play(in WeaponFeedbackContext context, bool reducedFlash, bool reducedMotion)
    {
        if (!EnemyReactionRuntime.Enabled)
            return;
        CacheVisuals();
        _profile = EnemyReactionProfile.Resolve(_profile);
        CurrentTier = _profile.ResolveTier(in context, ResolveMaximumHealth());
        float classScale = _profile.GetClassScale(context.TargetClass);
        float signature = ResolveWeaponSignature(context.WeaponType);
        float incoming = Mathf.Clamp(context.EventIntensity * classScale * signature, 0.1f, _profile.MaximumAccumulatedIntensity);
        _intensity = Mathf.Clamp(Mathf.Max(_intensity * 0.62f, incoming), 0f, _profile.MaximumAccumulatedIntensity);
        float motionDurationScale = reducedMotion ? _profile.ReducedMotionDurationScale : 1f;
        _activeDuration = _profile.GetDuration(CurrentTier) *
            (context.IsCritical || context.IsWeakPoint ? 1.25f : 1f) *
            motionDurationScale;
        _remaining = Mathf.Max(_remaining, _activeDuration);
        _hitDirection = ResolveDirection(in context);
        _activeColor = _profile.GetHitColor(CurrentTier);
        float displacementScale = reducedMotion ? _profile.ReducedMotionDisplacementScale : 1f;
        float squashScale = reducedMotion ? _profile.ReducedMotionSquashScale : 1f;
        _displacement = _profile.GetDisplacement(CurrentTier) * signature * displacementScale;
        _squash = _profile.GetSquash(CurrentTier) * signature * squashScale;
        _reducedFlash = reducedFlash || EnemyReactionRuntime.ReducedFlash;
        ApplyFlash(_intensity);
        ApplyTransformResponse(_intensity);
        EnemyDeathFeedback.RecordHit(in context);
    }

    public static bool TryPlay(in WeaponFeedbackContext context, bool reducedFlash)
    {
        return TryPlay(in context, reducedFlash, EnemyReactionRuntime.ReducedMotion);
    }

    public static bool TryPlay(in WeaponFeedbackContext context, bool reducedFlash, bool reducedMotion)
    {
        if (context.Target == null || !EnemyReactionRuntime.Enabled)
            return false;
        EnemyHitFeedback feedback = context.Target.GetComponentInParent<EnemyHitFeedback>();
        if (feedback == null)
            feedback = context.Target.GetComponentInChildren<EnemyHitFeedback>(true);
        if (feedback == null)
        {
            EnemyHealth health = context.Target.GetComponentInParent<EnemyHealth>();
            WeaponDummyEnemy dummy = context.Target.GetComponentInParent<WeaponDummyEnemy>();
            Transform root = health != null ? health.transform : dummy != null ? dummy.transform : null;
            if (root != null)
                feedback = root.gameObject.AddComponent<EnemyHitFeedback>();
        }
        if (feedback == null)
            return false;
        feedback.Play(in context, reducedFlash, reducedMotion);
        return true;
    }

    private void CacheVisuals()
    {
        _profile = EnemyReactionProfile.Resolve(_profile);
        _block ??= new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
        if (_visuals.Count == 0)
        {
            if (_visualRoot != null && _visualRoot != transform)
                AddVisual(_visualRoot);
            else
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer renderer = _renderers[i];
                    if (renderer == null || renderer is LineRenderer || renderer.GetComponentInParent<EnemyStatusVisual>() != null)
                        continue;
                    Transform candidate = renderer.transform;
                    if (candidate == transform || candidate.GetComponent<Collider>() != null || candidate.GetComponent<Rigidbody>() != null)
                        continue;
                    AddVisual(candidate);
                }
            }
        }
        if (_flashShells.Count == 0)
            CreateFlashShells();
    }

    private void AddVisual(Transform candidate)
    {
        for (int i = 0; i < _visuals.Count; i++)
            if (_visuals[i].Transform == candidate)
                return;
        _visuals.Add(new VisualState { Transform = candidate, Position = candidate.localPosition, Scale = candidate.localScale });
    }

    private void CreateFlashShells()
    {
        Material material = GetFlashMaterial();
        if (material == null)
            return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer source = _renderers[i];
            if (source == null || source is LineRenderer || source.gameObject.name.StartsWith("[Enemy Hit Flash]"))
                continue;
            Renderer shell = CreateFlashShell(source, material);
            if (shell != null)
                _flashShells.Add(shell);
        }
    }

    private static Renderer CreateFlashShell(Renderer source, Material material)
    {
        GameObject go = new("[Enemy Hit Flash] " + source.gameObject.name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(source.transform, false);
        go.transform.localScale = Vector3.one * 1.012f;
        Renderer shell = null;
        if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
        {
            SkinnedMeshRenderer copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = skinned.sharedMesh;
            copy.rootBone = skinned.rootBone;
            copy.bones = skinned.bones;
            copy.localBounds = skinned.localBounds;
            shell = copy;
        }
        else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            shell = go.AddComponent<MeshRenderer>();
        }
        if (shell == null)
        {
            DestroySafely(go);
            return null;
        }
        int materialCount = Mathf.Max(1, source.sharedMaterials.Length);
        Material[] materials = new Material[materialCount];
        for (int i = 0; i < materialCount; i++)
            materials[i] = material;
        shell.sharedMaterials = materials;
        shell.shadowCastingMode = ShadowCastingMode.Off;
        shell.receiveShadows = false;
        shell.enabled = false;
        return shell;
    }

    private void ApplyFlash(float amount)
    {
        float visibility = (_reducedFlash ? 0.35f : 1f) * Mathf.Clamp01(amount);
        Color color = _activeColor;
        color.a *= visibility * 0.62f;
        for (int i = 0; i < _flashShells.Count; i++)
        {
            Renderer shell = _flashShells[i];
            if (shell == null)
                continue;
            shell.enabled = visibility > 0.001f;
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(EmissionColorId, color);
            _block.SetFloat(EmissionIntensityId, 1.25f);
            _block.SetFloat(LuminescenceId, 0.4f);
            _block.SetFloat(PulseId, visibility);
            shell.SetPropertyBlock(_block);
        }
    }

    private void ApplyTransformResponse(float amount)
    {
        for (int i = 0; i < _visuals.Count; i++)
        {
            VisualState visual = _visuals[i];
            if (visual.Transform == null)
                continue;
            Vector3 localDirection = visual.Transform.parent != null
                ? visual.Transform.parent.InverseTransformDirection(_hitDirection)
                : _hitDirection;
            visual.Transform.localPosition = visual.Position + localDirection * (_displacement * amount);
            float squash = Mathf.Clamp(_squash * amount, 0f, 0.35f);
            visual.Transform.localScale = Vector3.Scale(visual.Scale, new Vector3(1f + squash, 1f - squash, 1f + squash));
        }
    }

    private void RestoreVisuals()
    {
        for (int i = 0; i < _flashShells.Count; i++)
            if (_flashShells[i] != null)
                _flashShells[i].enabled = false;
        for (int i = 0; i < _visuals.Count; i++)
        {
            VisualState visual = _visuals[i];
            if (visual.Transform == null)
                continue;
            visual.Transform.localPosition = visual.Position;
            visual.Transform.localScale = visual.Scale;
        }
        _remaining = 0f;
        _intensity = 0f;
    }

    private int ResolveMaximumHealth()
    {
        EnemyHealth health = GetComponentInParent<EnemyHealth>();
        if (health == null)
            health = GetComponentInChildren<EnemyHealth>(true);
        if (health != null)
            return health.MaxHealth;
        WeaponDummyEnemy dummy = GetComponentInParent<WeaponDummyEnemy>();
        if (dummy == null)
            dummy = GetComponentInChildren<WeaponDummyEnemy>(true);
        return dummy != null ? dummy.MaxHealth : 1;
    }

    private static Vector3 ResolveDirection(in WeaponFeedbackContext context)
    {
        Vector3 direction = context.Direction.sqrMagnitude > 0.0001f ? context.Direction.normalized : Vector3.forward;
        if (context.WeaponType == WeaponType.RotatingBlade)
        {
            Vector3 lateral = Vector3.Cross(Vector3.up, direction);
            if (lateral.sqrMagnitude > 0.0001f)
                direction = lateral.normalized;
        }
        return direction;
    }

    private static float ResolveWeaponSignature(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.Flamethrower => 0.5f,
            WeaponType.RocketLauncher => 1.18f,
            WeaponType.Mortar => 1.25f,
            WeaponType.RotatingBlade => 0.82f,
            _ => 1f
        };
    }

    private static Material GetFlashMaterial()
    {
        if (s_flashMaterial != null)
            return s_flashMaterial;
        Shader shader = Shader.Find("ScrapWaves/GameFeel/Scrap VFX");
        if (shader == null)
            return null;
        s_flashMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_flashMaterial;
    }

    private static void DestroySafely(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(value);
        else
            Object.DestroyImmediate(value);
    }

    private void OnValidate()
    {
        _responseCurve ??= AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    }
}
