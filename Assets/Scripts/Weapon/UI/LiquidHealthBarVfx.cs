using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>Animated liquid for a horizontal health Image. Owns only its runtime material.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class LiquidHealthBarVfx : MonoBehaviour
{
    [SerializeField, Min(0f)] private float _flowSpeed = 1f;
    [SerializeField, Range(0f, 2f)] private float _waveStrength = 1f;
    [SerializeField, Min(0.01f)] private float _fillSmoothTime = 0.16f;

    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int ClockId = Shader.PropertyToID("_LiquidTime");
    private static readonly int ImpactId = Shader.PropertyToID("_Impact");
    private static readonly int WaveId = Shader.PropertyToID("_WaveStrength");
    private static readonly int LowHealthId = Shader.PropertyToID("_LowHealth");
    private static readonly int SizeId = Shader.PropertyToID("_LiquidSize");
    private static readonly int SpriteUvId = Shader.PropertyToID("_SpriteUVRect");

    private Image _image;
    private Material _material;
    private Material _originalMaterial;
    private Image.Type _originalType;
    private float _targetFill;
    private float _displayFill;
    private float _fillVelocity;
    private float _clock;
    private float _impact;
    private bool _hasValue;
    private bool _ownsImage;

    public bool IsRenderingEffect => _material != null && isActiveAndEnabled;

    private void OnEnable()
    {
        _image = GetComponent<Image>();
        Shader shader = Resources.Load<Shader>("UI/LiquidHealthBar");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("Liquid health shader unavailable; keeping the standard health bar.", this);
            return;
        }

        _originalMaterial = _image.material;
        _originalType = _image.type;
        _material = new Material(shader)
        {
            name = "Liquid health (instance)",
            hideFlags = HideFlags.HideAndDontSave
        };
        _ownsImage = true;
        // The shader draws the fill boundary, so it needs the entire bar's rectangle.
        _image.type = Image.Type.Simple;
        _image.material = _material;
        _displayFill = _hasValue ? _targetFill : _image.fillAmount;
        _fillVelocity = 0f;
        _impact = 0f;
        ApplyProperties();
    }

    public void SetFill(float normalizedHealth, bool immediate = false)
    {
        normalizedHealth = float.IsNaN(normalizedHealth) ? 0f : Mathf.Clamp01(normalizedHealth);
        if (_image == null)
            _image = GetComponent<Image>();

        // Preserve the real value for the normal Image if the effect is disabled.
        _image.fillAmount = normalizedHealth;
        if (!_hasValue || immediate || HudVfxAccessibility.StaticEffects || normalizedHealth <= 0f)
        {
            _displayFill = normalizedHealth;
            _fillVelocity = 0f;
            _impact = 0f;
        }
        else if (normalizedHealth < _targetFill)
        {
            _impact = Mathf.Clamp01(_impact + 0.25f + (_targetFill - normalizedHealth) * 2.5f);
        }

        _targetFill = normalizedHealth;
        _hasValue = true;
        ApplyProperties();
    }

    private void Update()
    {
        if (_material == null || !_image.isActiveAndEnabled)
            return;

        if (HudVfxAccessibility.StaticEffects)
        {
            _displayFill = _targetFill;
            _fillVelocity = _impact = 0f;
            ApplyProperties();
            return;
        }
        float dt = HudVfxAccessibility.DeltaTime;
        if (dt <= 0f)
        {
            ApplyProperties();
            return;
        }
        _clock += dt * _flowSpeed;
        _displayFill = Mathf.SmoothDamp(_displayFill, _targetFill, ref _fillVelocity,
            _fillSmoothTime, Mathf.Infinity, dt);
        if (Mathf.Abs(_displayFill - _targetFill) < 0.0001f)
            _displayFill = _targetFill;
        _impact = Mathf.MoveTowards(_impact, 0f, dt * 1.8f);
        ApplyProperties();
    }

    private void ApplyProperties()
    {
        if (_material == null)
            return;

        Rect rect = _image.rectTransform.rect;
        Transform imageTransform = _image.transform;
        // The authored HUD stretches this Image differently on each axis.
        // Use its displayed proportions so bubbles stay round.
        Vector4 size = new Vector4(
            Mathf.Max(0.0001f, rect.width * imageTransform.TransformVector(Vector3.right).magnitude),
            Mathf.Max(0.0001f, rect.height * imageTransform.TransformVector(Vector3.up).magnitude), 0f, 0f);
        Sprite sprite = _image.overrideSprite != null ? _image.overrideSprite : _image.sprite;
        Vector4 outerUv = sprite != null ? DataUtility.GetOuterUV(sprite) : new Vector4(0f, 0f, 1f, 1f);
        Vector4 spriteUv = new Vector4(outerUv.x, outerUv.y,
            Mathf.Max(0.000001f, outerUv.z - outerUv.x),
            Mathf.Max(0.000001f, outerUv.w - outerUv.y));
        WriteProperties(_material, size, spriteUv);
        // Mask can supply a stencil copy; it needs the same animated values.
        Material rendered = _image.materialForRendering;
        if (rendered != null && rendered != _material)
            WriteProperties(rendered, size, spriteUv);
    }

    private void WriteProperties(Material material, Vector4 size, Vector4 spriteUv)
    {
        material.SetFloat(FillId, _displayFill);
        material.SetFloat(ClockId, _clock);
        bool staticEffects = HudVfxAccessibility.StaticEffects;
        material.SetFloat(ImpactId, staticEffects ? 0f : _impact);
        material.SetFloat(WaveId, _waveStrength);
        material.SetFloat(LowHealthId, !staticEffects && _targetFill > 0f && _targetFill <= 0.25f ? 1f : 0f);
        material.SetVector(SizeId, size);
        material.SetVector(SpriteUvId, spriteUv);
    }

    private void OnDisable()
    {
        if (_ownsImage && _image != null)
        {
            _image.material = _originalMaterial;
            _image.type = _originalType;
            if (_hasValue)
                _image.fillAmount = _targetFill;
        }
        _ownsImage = false;
        if (_material != null)
        {
            if (Application.isPlaying)
                Destroy(_material);
            else
                DestroyImmediate(_material);
            _material = null;
        }
    }
}
