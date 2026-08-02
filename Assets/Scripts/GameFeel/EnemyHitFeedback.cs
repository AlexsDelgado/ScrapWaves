using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHitFeedback : MonoBehaviour
{
    private static readonly int HitAmountId = Shader.PropertyToID("_HitAmount");
    private static readonly int HitColorId = Shader.PropertyToID("_HitColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Visual root")]
    [SerializeField, Tooltip("Optional cosmetic-only root. Leave empty to use material flash without moving the gameplay root.")]
    private Transform _visualRoot;
    [SerializeField] private Renderer[] _renderers;

    [Header("Hit response")]
    [SerializeField] private Color _regularHitColor = new(1f, 0.48f, 0.12f, 1f);
    [SerializeField] private Color _criticalHitColor = new(1f, 0.96f, 0.55f, 1f);
    [SerializeField] private Color _weakPointHitColor = Color.white;
    [SerializeField, Min(0.01f)] private float _flashDuration = 0.09f;
    [SerializeField, Min(0f)] private float _visualDisplacement = 0.08f;
    [SerializeField, Range(0f, 0.5f)] private float _squashAmount = 0.08f;
    [SerializeField] private AnimationCurve _responseCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField, Range(0f, 1f)] private float _eliteResponseScale = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _bossResponseScale = 0.45f;

    private MaterialPropertyBlock _propertyBlock;
    private Color[] _baseColors;
    private Vector3 _visualBasePosition;
    private Vector3 _visualBaseScale;
    private Vector3 _hitDirection;
    private Color _activeColor;
    private float _activeDuration;
    private float _remaining;
    private float _intensity;
    private bool _reducedFlash;

    public bool IsPlaying => _remaining > 0f;

    private void Awake()
    {
        CacheVisuals();
    }

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
        ApplyMaterialResponse(response);
        ApplyTransformResponse(response);

        if (_remaining <= 0f)
            RestoreVisuals();
    }

    private void OnDisable()
    {
        RestoreVisuals();
    }

    public void Play(in WeaponFeedbackContext context, bool reducedFlash)
    {
        CacheVisuals();
        float classScale = context.TargetClass switch
        {
            WeaponEnemyKind.Elite => _eliteResponseScale,
            WeaponEnemyKind.Boss => _bossResponseScale,
            _ => 1f
        };

        _activeColor = context.IsWeakPoint
            ? _weakPointHitColor
            : context.IsCritical ? _criticalHitColor : _regularHitColor;
        _activeDuration = _flashDuration * (context.IsCritical || context.IsWeakPoint ? 1.35f : 1f);
        _remaining = Mathf.Max(_remaining, _activeDuration);
        _hitDirection = context.Direction.sqrMagnitude > 0.0001f ? context.Direction.normalized : Vector3.forward;
        _intensity = Mathf.Clamp01(context.EventIntensity * classScale);
        _reducedFlash = reducedFlash;
        ApplyMaterialResponse(_intensity);
        ApplyTransformResponse(_intensity);
    }

    public static bool TryPlay(in WeaponFeedbackContext context, bool reducedFlash)
    {
        if (context.Target == null)
            return false;

        EnemyHitFeedback feedback = context.Target.GetComponentInParent<EnemyHitFeedback>();
        if (feedback == null)
            feedback = context.Target.GetComponentInChildren<EnemyHitFeedback>(true);
        if (feedback == null)
            return false;

        feedback.Play(in context, reducedFlash);
        return true;
    }

    private void CacheVisuals()
    {
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);

        _propertyBlock ??= new MaterialPropertyBlock();
        if (_baseColors == null || _baseColors.Length != _renderers.Length)
        {
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                if (material != null && material.HasProperty(BaseColorId))
                    _baseColors[i] = material.GetColor(BaseColorId);
                else if (material != null && material.HasProperty(ColorId))
                    _baseColors[i] = material.GetColor(ColorId);
                else
                    _baseColors[i] = Color.white;
            }
        }

        if (_visualRoot != null)
        {
            _visualBasePosition = _visualRoot.localPosition;
            _visualBaseScale = _visualRoot.localScale;
        }
    }

    private void ApplyMaterialResponse(float amount)
    {
        float flashAmount = _reducedFlash ? amount * 0.35f : amount;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(HitAmountId, flashAmount);
            _propertyBlock.SetColor(HitColorId, _activeColor);
            Color tint = Color.Lerp(_baseColors[i], _activeColor, flashAmount * 0.75f);
            _propertyBlock.SetColor(BaseColorId, tint);
            _propertyBlock.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void ApplyTransformResponse(float amount)
    {
        if (_visualRoot == null || _visualRoot == transform)
            return;

        Vector3 localDirection = _visualRoot.parent != null
            ? _visualRoot.parent.InverseTransformDirection(_hitDirection)
            : _hitDirection;
        _visualRoot.localPosition = _visualBasePosition + localDirection * (_visualDisplacement * amount);
        float squash = _squashAmount * amount;
        _visualRoot.localScale = Vector3.Scale(
            _visualBaseScale,
            new Vector3(1f + squash, 1f - squash, 1f + squash));
    }

    private void RestoreVisuals()
    {
        if (_renderers != null && _baseColors != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(HitAmountId, 0f);
                _propertyBlock.SetColor(BaseColorId, _baseColors[i]);
                _propertyBlock.SetColor(ColorId, _baseColors[i]);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        if (_visualRoot != null && _visualRoot != transform)
        {
            _visualRoot.localPosition = _visualBasePosition;
            _visualRoot.localScale = _visualBaseScale;
        }

        _remaining = 0f;
    }

    private void OnValidate()
    {
        _flashDuration = Mathf.Max(0.01f, _flashDuration);
        _visualDisplacement = Mathf.Max(0f, _visualDisplacement);
        _squashAmount = Mathf.Clamp(_squashAmount, 0f, 0.5f);
        _eliteResponseScale = Mathf.Clamp01(_eliteResponseScale);
        _bossResponseScale = Mathf.Clamp01(_bossResponseScale);
        _responseCurve ??= AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    }
}
