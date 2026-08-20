using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponRecoilFeedback : MonoBehaviour
{
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField, Tooltip("Cosmetic-only barrel or weapon transform. Never assign the projectile spawn transform itself.")]
    private Transform _recoilRoot;
    [SerializeField] private Renderer[] _heatRenderers;
    [SerializeField, Min(0f)] private float _automaticRecoilDistance = 0.06f;
    [SerializeField, Min(0f)] private float _manualRecoilDistance = 0.1f;
    [SerializeField, Min(0.01f)] private float _recoilInSpeed = 38f;
    [SerializeField, Min(0.01f)] private float _recoverySpeed = 16f;
    [SerializeField] private AnimationCurve _recoilCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("Multiplier for cosmetic recoil travel when Reduced Motion is enabled.")]
    private float _reducedMotionRecoilScale = 0.2f;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3 _baseLocalPosition;
    private float _targetRecoil;
    private float _currentRecoil;
    private float _activeRecoilDistance;
    private float _normalizedHeat;
    private WeaponHeatPresentationSettings _heatSettings;
    private bool _heatEnabled;
    private bool _reducedMotionActive;

    public Transform RecoilRoot => _recoilRoot;

    private void Awake()
    {
        CacheState();
    }

    private void OnEnable()
    {
        CacheState();
    }

    private void Update()
    {
        float speed = _currentRecoil < _targetRecoil ? _recoilInSpeed : _recoverySpeed;
        _currentRecoil = Mathf.MoveTowards(_currentRecoil, _targetRecoil, speed * Time.unscaledDeltaTime);
        _targetRecoil = Mathf.MoveTowards(_targetRecoil, 0f, _recoverySpeed * Time.unscaledDeltaTime);
        if (_currentRecoil <= 0.0001f && _targetRecoil <= 0.0001f)
            _activeRecoilDistance = 0f;

        if (_recoilRoot != null)
        {
            float normalized = _activeRecoilDistance > 0.0001f
                ? Mathf.Clamp01(_currentRecoil / _activeRecoilDistance)
                : 0f;
            float curvedDistance = _activeRecoilDistance * Mathf.Max(0f, _recoilCurve.Evaluate(normalized));
            _recoilRoot.localPosition = _baseLocalPosition + Vector3.back * curvedDistance;
        }

        ApplyHeat();
    }

    private void OnDisable()
    {
        _currentRecoil = 0f;
        _targetRecoil = 0f;
        _activeRecoilDistance = 0f;
        _reducedMotionActive = false;
        if (_recoilRoot != null)
            _recoilRoot.localPosition = _baseLocalPosition;
    }

    public void Request(in WeaponFeedbackContext context, WeaponHeatPresentationSettings heat, bool heatEnabled)
    {
        Request(in context, heat, heatEnabled, reducedMotion: false);
    }

    public void Request(
        in WeaponFeedbackContext context,
        WeaponHeatPresentationSettings heat,
        bool heatEnabled,
        bool reducedMotion)
    {
        float motionScale = reducedMotion ? Mathf.Clamp01(_reducedMotionRecoilScale) : 1f;
        if (reducedMotion && !_reducedMotionActive)
        {
            // Apply a newly-enabled accessibility setting immediately instead of
            // letting a previously queued full-travel impulse win the Max below.
            _currentRecoil *= motionScale;
            _targetRecoil *= motionScale;
            _activeRecoilDistance *= motionScale;
        }
        _reducedMotionActive = reducedMotion;

        AutomaticWeaponMount automaticMount = context.Anchor != null
            ? context.Anchor.GetComponentInParent<AutomaticWeaponMount>()
            : null;
        if (context.Mode == WeaponFeedbackMode.Automatic && automaticMount != null)
        {
            automaticMount.RequestRecoil(context.EventIntensity * motionScale);
            return;
        }

        float distance = context.Mode == WeaponFeedbackMode.Manual || context.Mode == WeaponFeedbackMode.Active
            ? _manualRecoilDistance
            : _automaticRecoilDistance;
        _targetRecoil = Mathf.Max(
            _targetRecoil,
            distance * Mathf.Clamp01(context.EventIntensity) * motionScale);
        _activeRecoilDistance = Mathf.Max(_activeRecoilDistance, _targetRecoil);
        _normalizedHeat = context.NormalizedHeat;
        _heatSettings = heat;
        _heatEnabled = heatEnabled;
    }

    private void CacheState()
    {
        _propertyBlock ??= new MaterialPropertyBlock();
        if (_recoilRoot != null)
            _baseLocalPosition = _recoilRoot.localPosition;
        if (_heatRenderers == null || _heatRenderers.Length == 0)
            _heatRenderers = _recoilRoot != null
                ? _recoilRoot.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
    }

    private void ApplyHeat()
    {
        if (_heatRenderers == null || _heatSettings == null)
            return;

        float heatValue = _heatEnabled ? _normalizedHeat : 0f;
        Color heatColor = _heatSettings.Color.Evaluate(heatValue);
        float emission = _heatSettings.Emission.Evaluate(heatValue);
        for (int i = 0; i < _heatRenderers.Length; i++)
        {
            Renderer renderer = _heatRenderers[i];
            if (renderer == null)
                continue;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(HeatId, heatValue);
            _propertyBlock.SetFloat(EmissionIntensityId, emission);
            _propertyBlock.SetColor(BaseColorId, heatColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void OnValidate()
    {
        _automaticRecoilDistance = Mathf.Max(0f, _automaticRecoilDistance);
        _manualRecoilDistance = Mathf.Max(0f, _manualRecoilDistance);
        _recoilInSpeed = Mathf.Max(0.01f, _recoilInSpeed);
        _recoverySpeed = Mathf.Max(0.01f, _recoverySpeed);
        _reducedMotionRecoilScale = Mathf.Clamp01(_reducedMotionRecoilScale);
        _recoilCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}
