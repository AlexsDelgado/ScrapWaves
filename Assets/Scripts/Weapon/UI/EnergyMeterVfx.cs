using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>Bottom-up fire heat and flowing XP effects on the existing HUD Images.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class EnergyMeterVfx : MonoBehaviour
{
    private static readonly int ModeId = Shader.PropertyToID("_ExperienceMode");
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int ClockId = Shader.PropertyToID("_EffectTime");
    private static readonly int OverheatId = Shader.PropertyToID("_Overheat");
    private static readonly int BurstId = Shader.PropertyToID("_HeatBurst");
    private static readonly int PickupId = Shader.PropertyToID("_PickupSweep");
    private static readonly int LevelId = Shader.PropertyToID("_LevelSweep");
    private static readonly int SizeId = Shader.PropertyToID("_MeterSize");
    private static readonly int UvId = Shader.PropertyToID("_SpriteUVRect");

    private readonly EnergyMeterAnimation _animation = new EnergyMeterAnimation();
    private Image _image;
    private Material _material;
    private Material _originalMaterial;
    private Image.Type _originalType;
    private float _clock;

    public bool IsRenderingEffect => _material != null && isActiveAndEnabled;

    private void OnEnable()
    {
        _image = GetComponent<Image>();
        Shader shader = Resources.Load<Shader>("UI/EnergyMeter");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("Energy meter shader unavailable; keeping the standard HUD fill.", this);
            return;
        }
        _originalMaterial = _image.material;
        _originalType = _image.type;
        _material = new Material(shader)
        {
            name = "HUD energy (instance)",
            hideFlags = HideFlags.HideAndDontSave
        };
        _image.type = Image.Type.Simple;
        _image.material = _material;
        _animation.SnapToTarget();
        ApplyProperties();
    }

    public void SetHeat(float value, bool overheating, bool immediate = false)
    {
        _animation.SetHeat(value, overheating);
        if (immediate || HudVfxAccessibility.StaticEffects) _animation.SnapToTarget();
        RefreshImage();
    }

    public void SetExperience(float value, int level, bool immediate = false)
    {
        _animation.SetExperience(value, level);
        if (immediate || HudVfxAccessibility.StaticEffects) _animation.SnapToTarget();
        RefreshImage();
    }

    private void RefreshImage()
    {
        if (_image == null)
            _image = GetComponent<Image>();
        // Retain the real gameplay value for disabling/fallback, even during a level sweep.
        _image.fillAmount = _animation.TargetFill;
        ApplyProperties();
    }

    private void Update()
    {
        if (_material == null || !_image.isActiveAndEnabled)
            return;
        if (HudVfxAccessibility.StaticEffects)
        {
            _animation.SnapToTarget();
            ApplyProperties();
            return;
        }
        float dt = HudVfxAccessibility.DeltaTime;
        float speed = _animation.IsExperience ? 1f : 0.65f + _animation.Fill * 0.75f + _animation.Overheat * 0.6f;
        _clock += dt * speed;
        _animation.Advance(dt);
        ApplyProperties();
    }

    private void ApplyProperties()
    {
        if (_material == null)
            return;
        Rect rect = _image.rectTransform.rect;
        Transform tr = _image.transform;
        Vector4 size = new Vector4(
            Mathf.Max(0.0001f, rect.width * tr.TransformVector(Vector3.right).magnitude),
            Mathf.Max(0.0001f, rect.height * tr.TransformVector(Vector3.up).magnitude), 0f, 0f);
        Sprite sprite = _image.overrideSprite != null ? _image.overrideSprite : _image.sprite;
        Vector4 outer = sprite != null ? DataUtility.GetOuterUV(sprite) : new Vector4(0f, 0f, 1f, 1f);
        Vector4 uv = new Vector4(outer.x, outer.y,
            Mathf.Max(0.000001f, outer.z - outer.x), Mathf.Max(0.000001f, outer.w - outer.y));
        WriteProperties(_material, size, uv);
        Material rendered = _image.materialForRendering;
        if (rendered != null && rendered != _material)
            WriteProperties(rendered, size, uv);
    }

    private void WriteProperties(Material material, Vector4 size, Vector4 uv)
    {
        material.SetFloat(ModeId, _animation.IsExperience ? 1f : 0f);
        material.SetFloat(FillId, _animation.Fill);
        material.SetFloat(ClockId, _clock);
        material.SetFloat(OverheatId, _animation.Overheat);
        material.SetFloat(BurstId, _animation.HeatBurst);
        material.SetFloat(PickupId, _animation.PickupSweep);
        material.SetFloat(LevelId, _animation.LevelSweep);
        material.SetVector(SizeId, size);
        material.SetVector(UvId, uv);
    }

    private void OnDisable()
    {
        if (_material == null)
            return;
        if (_image != null)
        {
            _image.material = _originalMaterial;
            _image.type = _originalType;
            _image.fillAmount = _animation.TargetFill;
        }
        if (Application.isPlaying)
            Destroy(_material);
        else
            DestroyImmediate(_material);
        _material = null;
    }
}
