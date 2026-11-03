using UnityEngine;

/// <summary>Shows an armed ambush preview without moving its trigger or spawn origin.</summary>
[DisallowMultipleComponent]
public sealed class ZoneSpawnerPresentation : MonoBehaviour
{
    [SerializeField] private ZoneSpawner _spawner;
    [SerializeField] private GameObject _hologramRoot;
    [Tooltip("Upright visual pivot. Keep import rotation and mesh scale on a child.")]
    [SerializeField] private Transform _hologramPivot;
    [SerializeField] private Renderer[] _cornerLamps = new Renderer[0];
    [SerializeField] private Material _lampOnMaterial;
    [SerializeField] private Material _lampOffMaterial;

    [Header("Hologram motion")]
    [SerializeField] private float _spinDegreesPerSecond = 24f;
    [SerializeField, Min(0f)] private float _bobHeight = 0.3f;
    [SerializeField, Min(0.1f)] private float _bobPeriod = 3f;

    private ZoneSpawner _subscribedSpawner;
    private Transform _capturedPivot;
    private Vector3 _restPosition;
    private Quaternion _restRotation;
    private float _elapsedTime;
    private bool _lampsInitialized;
    private bool _lampsAvailable;

    private void Awake()
    {
        ResolveSpawner();
        CaptureRestPose();
    }

    private void OnEnable()
    {
        ResolveSpawner();
        _subscribedSpawner = _spawner;
        if (_subscribedSpawner != null)
            _subscribedSpawner.ArmedChanged += HandleArmedChanged;

        _elapsedTime = 0f;
        _lampsInitialized = false;
        RefreshPresentation(_elapsedTime);
    }

    private void OnDisable()
    {
        if (_subscribedSpawner != null)
            _subscribedSpawner.ArmedChanged -= HandleArmedChanged;
        _subscribedSpawner = null;
        ApplyState(false, false);
    }

    private void Update()
    {
        if (!RefreshState())
            return;
        if (!GameplayPause.IsUiPaused)
            _elapsedTime += Time.deltaTime;
        SetAnimationTime(_elapsedTime);
    }

    private void HandleArmedChanged(bool armed)
    {
        RefreshPresentation(_elapsedTime);
    }

    /// <summary>Refreshes gameplay state and samples motion only while the hologram is visible.</summary>
    public void RefreshPresentation(float elapsedTime)
    {
        if (RefreshState())
            SetAnimationTime(elapsedTime);
    }

    private bool RefreshState()
    {
        ResolveSpawner();
        bool running = _spawner != null && _spawner.isActiveAndEnabled
            && (GameManager.Instance == null || GameManager.Instance.IsPlaying);
        bool armed = running && _spawner.IsArmed;
        ApplyState(armed, running && _spawner.IsAvailable);
        return armed && _hologramRoot != null && _hologramRoot.activeInHierarchy;
    }

    /// <summary>Applies a deterministic presentation state without changing gameplay.</summary>
    public void ApplyState(bool armed, bool available)
    {
        // Only a visual child may be hidden: the controller must remain subscribed to refresh.
        if (_hologramRoot != null && _hologramRoot != gameObject
            && !transform.IsChildOf(_hologramRoot.transform)
            && _hologramRoot.activeSelf != armed)
            _hologramRoot.SetActive(armed);

        bool lampsAvailable = armed && available;
        if (_lampsInitialized && _lampsAvailable == lampsAvailable)
            return;

        _lampsInitialized = true;
        _lampsAvailable = lampsAvailable;
        Material lampMaterial = lampsAvailable ? _lampOnMaterial : _lampOffMaterial;
        if (lampMaterial == null || _cornerLamps == null)
            return;

        for (int i = 0; i < _cornerLamps.Length; i++)
        {
            Renderer lamp = _cornerLamps[i];
            if (lamp != null)
                lamp.sharedMaterial = lampMaterial;
        }
    }

    /// <summary>Moves only the preview pivot from its authored pose, without cumulative drift.</summary>
    public void SetAnimationTime(float elapsedTime)
    {
        CaptureRestPose();
        if (_capturedPivot == null)
            return;
        if (float.IsNaN(elapsedTime) || float.IsInfinity(elapsedTime))
            elapsedTime = 0f;

        float period = Mathf.Max(0.1f, _bobPeriod);
        float phase = Mathf.Repeat(elapsedTime, period) / period * Mathf.PI * 2f;
        Vector3 offset = Vector3.up * (Mathf.Sin(phase) * Mathf.Max(0f, _bobHeight));
        Transform parent = _capturedPivot.parent;
        if (parent != null)
            offset = parent.InverseTransformVector(offset);

        _capturedPivot.localPosition = _restPosition + offset;
        _capturedPivot.localRotation = _restRotation
            * Quaternion.AngleAxis(elapsedTime * _spinDegreesPerSecond % 360f, Vector3.up);
    }

    private void ResolveSpawner()
    {
        if (_spawner == null)
            _spawner = GetComponentInParent<ZoneSpawner>();
    }

    private void CaptureRestPose()
    {
        if (_hologramPivot == null || _capturedPivot == _hologramPivot)
            return;
        // Misconfigured references must never animate the trigger/controller hierarchy.
        if (_hologramPivot == transform || transform.IsChildOf(_hologramPivot))
            return;

        _capturedPivot = _hologramPivot;
        _restPosition = _capturedPivot.localPosition;
        _restRotation = _capturedPivot.localRotation;
    }
}
