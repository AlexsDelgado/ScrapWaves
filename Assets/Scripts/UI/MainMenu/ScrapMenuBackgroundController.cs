using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ScrapMenuBackgroundController : MonoBehaviour
{
    [Header("Authored targets")]
    [SerializeField] private Graphic _proceduralBackground;
    [SerializeField] private Graphic _breathingLight;
    [SerializeField] private Material _proceduralBackgroundMaterial;
    [SerializeField] private RectTransform _showcaseSubject;
    [SerializeField] private RectTransform _hazardBand;
    [SerializeField] private ParticleSystem _ambientSparks;

    [Header("Subordinate ambient motion")]
    [SerializeField, Min(0.1f)] private float _breathDuration = 3.4f;
    [SerializeField, Range(0f, 0.1f)] private float _breathAlphaAmount = 0.035f;
    [SerializeField, Range(0f, 10f)] private float _showcaseTurnRange = 5.5f;
    [SerializeField, Min(0.1f)] private float _showcaseTurnDuration = 7.5f;
    [SerializeField] private Vector2 _hazardTravel = new(14f, 0f);
    [SerializeField, Min(0.1f)] private float _hazardTravelDuration = 12f;
    [SerializeField] private Vector2 _ambientSparkInterval = new(2.5f, 5f);

    private Color _baseLightColor;
    private Quaternion _showcaseRestingRotation;
    private Vector2 _hazardRestingPosition;
    private float _seed;
    private float _nextSparkTime;
    private bool _captured;
    private bool _reducedMotion;
    private Material _runtimeBackgroundMaterial;

    private void Awake()
    {
        CaptureRestingValues();
        _seed = Random.Range(0f, 100f);
        ScheduleNextSpark();
    }

    private void OnEnable()
    {
        CaptureRestingValues();
        CreateRuntimeBackgroundMaterial();
        ScheduleNextSpark();
    }

    private void OnDisable()
    {
        RestoreRestingValues();
        ReleaseRuntimeBackgroundMaterial();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeBackgroundMaterial();
    }

    private void Update()
    {
        CaptureRestingValues();
        float now = Time.unscaledTime + _seed;
        float motionScale = _reducedMotion ? 0f : 1f;

        if (_runtimeBackgroundMaterial != null)
        {
            _runtimeBackgroundMaterial.SetFloat("_UnscaledTime", _reducedMotion ? 0f : Time.unscaledTime);
            _runtimeBackgroundMaterial.SetFloat("_MotionScale", motionScale);
        }

        if (_breathingLight != null)
        {
            float wave = Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, _breathDuration));
            Color color = _baseLightColor;
            color.a = Mathf.Clamp01(_baseLightColor.a + wave * _breathAlphaAmount * motionScale);
            _breathingLight.color = color;
        }

        if (_showcaseSubject != null)
        {
            float turn = Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, _showcaseTurnDuration));
            _showcaseSubject.localRotation = _showcaseRestingRotation *
                Quaternion.Euler(0f, turn * _showcaseTurnRange * motionScale, 0f);
        }

        if (_hazardBand != null)
        {
            float travel = Mathf.Sin(now * Mathf.PI * 2f / Mathf.Max(0.1f, _hazardTravelDuration));
            _hazardBand.anchoredPosition = _hazardRestingPosition + _hazardTravel * (travel * motionScale);
        }

        if (!_reducedMotion && _ambientSparks != null && Time.unscaledTime >= _nextSparkTime)
        {
            _ambientSparks.Play(true);
            ScheduleNextSpark();
        }
    }

    public void ApplyReducedMotion(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        if (reducedMotion)
            RestoreRestingValues();
        ScheduleNextSpark();
    }

    private void CaptureRestingValues()
    {
        if (_captured)
            return;
        if (_breathingLight != null)
            _baseLightColor = _breathingLight.color;
        if (_showcaseSubject != null)
            _showcaseRestingRotation = _showcaseSubject.localRotation;
        if (_hazardBand != null)
            _hazardRestingPosition = _hazardBand.anchoredPosition;
        _captured = true;
    }

    private void RestoreRestingValues()
    {
        if (!_captured)
            return;
        if (_breathingLight != null)
            _breathingLight.color = _baseLightColor;
        if (_showcaseSubject != null)
            _showcaseSubject.localRotation = _showcaseRestingRotation;
        if (_hazardBand != null)
            _hazardBand.anchoredPosition = _hazardRestingPosition;
    }

    private void ScheduleNextSpark()
    {
        float minimum = Mathf.Max(0.1f, Mathf.Min(_ambientSparkInterval.x, _ambientSparkInterval.y));
        float maximum = Mathf.Max(minimum, Mathf.Max(_ambientSparkInterval.x, _ambientSparkInterval.y));
        _nextSparkTime = Time.unscaledTime + Random.Range(minimum, maximum) * (_reducedMotion ? 2f : 1f);
    }

    private void CreateRuntimeBackgroundMaterial()
    {
        if (_runtimeBackgroundMaterial != null ||
            _proceduralBackground == null ||
            _proceduralBackgroundMaterial == null)
        {
            return;
        }

        _runtimeBackgroundMaterial = new Material(_proceduralBackgroundMaterial)
        {
            name = _proceduralBackgroundMaterial.name + " (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        _proceduralBackground.material = _runtimeBackgroundMaterial;
    }

    private void ReleaseRuntimeBackgroundMaterial()
    {
        if (_runtimeBackgroundMaterial == null)
            return;

        if (_proceduralBackground != null && _proceduralBackground.material == _runtimeBackgroundMaterial)
            _proceduralBackground.material = _proceduralBackgroundMaterial;

        if (Application.isPlaying)
            Destroy(_runtimeBackgroundMaterial);
        else
            DestroyImmediate(_runtimeBackgroundMaterial);
        _runtimeBackgroundMaterial = null;
    }
}
