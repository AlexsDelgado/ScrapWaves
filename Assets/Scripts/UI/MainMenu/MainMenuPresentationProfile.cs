using UnityEngine;

[CreateAssetMenu(
    fileName = "MainMenuPresentationProfile",
    menuName = "ScrapWaves/UI/Main Menu Presentation Profile")]
public sealed class MainMenuPresentationProfile : ScriptableObject
{
    [Header("Scrappunk palette")]
    [SerializeField] private Color _coal = new(0.035f, 0.043f, 0.039f, 1f);
    [SerializeField] private Color _deepSteel = new(0.067f, 0.078f, 0.075f, 1f);
    [SerializeField] private Color _plate = new(0.122f, 0.145f, 0.133f, 1f);
    [SerializeField] private Color _bone = new(0.949f, 0.961f, 0.922f, 1f);
    [SerializeField] private Color _mutedSteel = new(0.678f, 0.741f, 0.69f, 1f);
    [SerializeField] private Color _scrapGreen = new(0.659f, 0.78f, 0.561f, 1f);
    [SerializeField] private Color _warningRust = new(0.851f, 0.416f, 0.196f, 1f);
    [SerializeField] private Color _danger = new(0.78f, 0.29f, 0.263f, 1f);

    [Header("Menu item state")]
    [SerializeField, Range(0.8f, 1.1f)] private float _unselectedScale = 0.95f;
    [SerializeField, Range(1f, 1.3f)] private float _selectedScale = 1.1f;
    [SerializeField] private Vector2 _selectedOffset = new(-24f, 6f);
    [SerializeField, Min(0.01f)] private float _focusDuration = 0.12f;
    [SerializeField, Range(0.8f, 1f)] private float _pressScaleMultiplier = 0.96f;
    [SerializeField, Min(0.01f)] private float _pressDuration = 0.06f;
    [SerializeField] private AnimationCurve _focusCurve = new(
        new Keyframe(0f, 0f, 0f, 6f),
        new Keyframe(1f, 1f, 0f, 0f));

    [Header("Intro")]
    [SerializeField] private Vector2 _titleStartOffset = new(-60f, 0f);
    [SerializeField, Range(0.5f, 1f)] private float _titleStartScale = 0.82f;
    [SerializeField, Min(0.01f)] private float _titleDuration = 0.31f;
    [SerializeField] private Vector2 _itemStartOffset = new(-130f, 0f);
    [SerializeField, Range(0.5f, 1f)] private float _itemStartScale = 0.8f;
    [SerializeField, Min(0.01f)] private float _itemDuration = 0.27f;
    [SerializeField, Min(0f)] private float _itemStagger = 0.05f;
    [SerializeField] private AnimationCurve _entranceCurve = new(
        new Keyframe(0f, 0f, 0f, 6f),
        new Keyframe(0.76f, 1.04f, 0f, 0f),
        new Keyframe(1f, 1f, 0f, 0f));

    [Header("Whole-screen feedback")]
    [SerializeField] private Vector2 _navigationPunchDistance = new(10f, -7f);
    [SerializeField, Range(0f, 2f)] private float _navigationPunchRotation = 0.55f;
    [SerializeField, Min(0.01f)] private float _punchDuration = 0.1f;
    [SerializeField, Range(0f, 1f)] private float _hoverPunchMultiplier = 0.4f;
    [SerializeField, Range(0f, 0.2f)] private float _flashOpacity = 0.06f;
    [SerializeField, Min(0.01f)] private float _flashDuration = 0.09f;
    [SerializeField, Min(0f)] private float _punchCooldown = 0.035f;
    [SerializeField] private AnimationCurve _punchCurve = new(
        new Keyframe(0f, 1f),
        new Keyframe(0.45f, -0.15f),
        new Keyframe(1f, 0f));

    [Header("Local screens")]
    [SerializeField, Min(0.01f)] private float _localScreenOpenDuration = 0.22f;
    [SerializeField, Min(0.01f)] private float _localScreenCloseDuration = 0.18f;
    [SerializeField] private Vector2 _localScreenStartOffset = new(90f, 0f);
    [SerializeField] private AnimationCurve _localScreenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public Color Coal => _coal;
    public Color DeepSteel => _deepSteel;
    public Color Plate => _plate;
    public Color Bone => _bone;
    public Color MutedSteel => _mutedSteel;
    public Color ScrapGreen => _scrapGreen;
    public Color WarningRust => _warningRust;
    public Color Danger => _danger;
    public float UnselectedScale => _unselectedScale;
    public float SelectedScale => _selectedScale;
    public Vector2 SelectedOffset => _selectedOffset;
    public float FocusDuration => _focusDuration;
    public float PressScaleMultiplier => _pressScaleMultiplier;
    public float PressDuration => _pressDuration;
    public AnimationCurve FocusCurve => _focusCurve;
    public Vector2 TitleStartOffset => _titleStartOffset;
    public float TitleStartScale => _titleStartScale;
    public float TitleDuration => _titleDuration;
    public Vector2 ItemStartOffset => _itemStartOffset;
    public float ItemStartScale => _itemStartScale;
    public float ItemDuration => _itemDuration;
    public float ItemStagger => _itemStagger;
    public AnimationCurve EntranceCurve => _entranceCurve;
    public Vector2 NavigationPunchDistance => _navigationPunchDistance;
    public float NavigationPunchRotation => _navigationPunchRotation;
    public float PunchDuration => _punchDuration;
    public float HoverPunchMultiplier => _hoverPunchMultiplier;
    public float FlashOpacity => _flashOpacity;
    public float FlashDuration => _flashDuration;
    public float PunchCooldown => _punchCooldown;
    public AnimationCurve PunchCurve => _punchCurve;
    public float LocalScreenOpenDuration => _localScreenOpenDuration;
    public float LocalScreenCloseDuration => _localScreenCloseDuration;
    public Vector2 LocalScreenStartOffset => _localScreenStartOffset;
    public AnimationCurve LocalScreenCurve => _localScreenCurve;
}
