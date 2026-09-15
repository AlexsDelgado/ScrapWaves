using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(110)]
public sealed class RearThreatPresenter : MonoBehaviour
{
    [Header("Authored view")]
    [SerializeField] private RearThreatSensor _sensor;
    [SerializeField] private MeshFilter _fill;
    [SerializeField] private MeshFilter _outline;
    [SerializeField, Tooltip("Offset from the player root; the default aligns with the current model's hips. X/Z follow the selected forward basis and Y stays vertical.")]
    private Vector3 _hipOffset = new(0f, 0.25f, 0f);

    [Header("Footprint (world units)")]
    [SerializeField, Min(0.01f)] private float _innerRadius = 0.8f;
    [SerializeField, Min(0.02f)] private float _idleOuterRadius = 0.86f;
    [SerializeField, Min(1f)] private float _maximumRadiusMultiplier = 1.1f;
    [SerializeField, Min(0f)] private float _minimumSpikeLength = 0.1f;
    [SerializeField, Min(0f)] private float _maximumSpikeLength = 0.65f;
    [SerializeField, Range(0.2f, 60f)] private float _spikeWidthDegrees = 10f;
    [SerializeField, Min(0.001f)] private float _outlineWidth = 0.03f;

    [Header("Threat response")]
    [SerializeField, Range(0f, 1f), Tooltip("Share of radius growth concentrated around individual threats. Zero restores uniform growth and straight spikes.")]
    private float _localFlex = 0.75f;
    [SerializeField, Range(2f, 120f), Tooltip("Width of the rounded bulge around each spike, in degrees.")]
    private float _shoulderWidthDegrees = 44f;
    [SerializeField, Min(0.01f), Tooltip("Spring response time for an approaching or newly detected threat.")]
    private float _growthTime = 0.12f;
    [SerializeField, Min(0.01f), Tooltip("Spring response time for a receding or disappearing threat.")]
    private float _relaxTime = 0.25f;
    [FormerlySerializedAs("_smoothingTime")]
    [SerializeField, Min(0.01f), Tooltip("How quickly spike bearings follow moving threats. Does not change detection.")]
    private float _directionSmoothingTime = 0.08f;

    [Header("Palette")]
    [SerializeField] private Color _idleColor = new(1f, 0.35f, 0.65f, 0.08f);
    [SerializeField] private Color _alertColor = new(1f, 0.02f, 0.06f, 0.95f);
    [SerializeField] private Color _outlineColor = Color.clear;

#if UNITY_EDITOR
    [Header("Editor preview only")]
    [SerializeField, Range(0f, 1f)] private float _previewUrgency;
    [SerializeField, Tooltip("Up to five angles in degrees from backward; positive is left.")]
    private float[] _previewSpikeAngles = System.Array.Empty<float>();
    [SerializeField, Tooltip("Urgency 0–1 for the corresponding preview spike. Missing entries use Preview Urgency.")]
    private float[] _previewSpikeUrgencies = System.Array.Empty<float>();
    private bool _previewDirty = true;
    private Vector3 _previewPlayerPosition, _previewForward;
    private float _previewHalfArc;
#endif

    private readonly int[] _trackIds = new int[RearThreatMesh.MaximumSpikes];
    private readonly bool[] _matched = new bool[RearThreatMesh.MaximumSpikes];
    private readonly Vector3[] _directions = new Vector3[RearThreatMesh.MaximumSpikes];
    private readonly float[] _angles = new float[RearThreatMesh.MaximumSpikes];
    private readonly float[] _lengths = new float[RearThreatMesh.MaximumSpikes];
    private readonly float[] _lengthVelocities = new float[RearThreatMesh.MaximumSpikes];
    private readonly float[] _targetLengths = new float[RearThreatMesh.MaximumSpikes];
    private readonly Vector3[] _targetDirections = new Vector3[RearThreatMesh.MaximumSpikes];
    private RearThreatMesh _geometry;
    private Mesh _fillMesh, _outlineMesh, _authoredFillMesh, _authoredOutlineMesh;
    private MeshRenderer _fillRenderer, _outlineRenderer;
    private MaterialPropertyBlock _properties;
    private Transform _boundPlayer;
    private float _urgency, _urgencyVelocity;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public RearThreatSensor Sensor => _sensor;
    public Transform Player => _sensor != null ? _sensor.Player : null;
    public MeshFilter Fill => _fill;
    public MeshFilter Outline => _outline;
    public bool HasAuthoredUi => _sensor != null && _fill != null && _outline != null;

    public void Configure(RearThreatSensor sensor, MeshFilter fill, MeshFilter outline)
    {
        ReleaseMeshes();
        ResetVisualState();
        _sensor = sensor;
        _fill = fill;
        _outline = outline;
        CacheRenderers();
#if UNITY_EDITOR
        _previewDirty = true;
#endif
    }

    public void BindPlayer(Transform player)
    {
        if (_sensor != null) _sensor.BindPlayer(player);
        ResetVisualState();
#if UNITY_EDITOR
        _previewDirty = true;
#endif
    }

    private void OnEnable()
    {
        CacheRenderers();
        ResetVisualState();
#if UNITY_EDITOR
        _previewDirty = true;
#endif
    }

    private void OnDisable()
    {
        SetVisible(false);
        ReleaseMeshes();
    }

    private void OnDestroy() => ReleaseMeshes();

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject))
        {
            Vector3 position = Player != null ? Player.position : transform.position;
            Vector3 previewForward = _sensor != null ? _sensor.GetReferenceForward() : transform.forward;
            if (_previewDirty || position != _previewPlayerPosition || previewForward != _previewForward || !Mathf.Approximately(HalfArc, _previewHalfArc)) RefreshPreview();
            return;
        }
#endif
        if (_sensor == null || !_sensor.isActiveAndEnabled || !EnsureMeshes())
        {
            ResetVisualState();
            SetVisible(false);
            return;
        }
        _sensor.RefreshForPresentation();
        RearThreatSnapshot snapshot = _sensor.Snapshot;
        if (!snapshot.IsValidPlayer || _sensor.Player == null)
        {
            ResetVisualState();
            SetVisible(false);
            return;
        }
        if (_boundPlayer != _sensor.Player)
        {
            ResetVisualState();
            _boundPlayer = _sensor.Player;
        }
        Vector3 forward = snapshot.Forward;
        Follow(_sensor.Player.position, forward);
        AdvanceVisuals(snapshot, Time.deltaTime);
        Draw();
        SetVisible(true);
    }

    private void AdvanceVisuals(RearThreatSnapshot snapshot, float deltaTime)
    {
        // Freeze spring velocities as well as positions while paused.
        if (deltaTime <= 0f)
        {
            // The reference basis can still change in the Editor or camera-relative mode.
            // Re-express the frozen world bearings without advancing any spring or track.
            for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
                _angles[i] = Vector3.SignedAngle(-snapshot.Forward, _directions[i], Vector3.up);
            return;
        }
        _urgency = StepResponse(_urgency, snapshot.OverallUrgency, ref _urgencyVelocity, deltaTime);
        UpdateSpikes(snapshot, snapshot.Forward, deltaTime);
    }

    private float StepResponse(float current, float target, ref float velocity, float deltaTime, float maximum = 1f)
    {
        float responseTime = target > current ? _growthTime : _relaxTime;
        // A critically damped spring gives a soft arrival without repeated bouncing.
        float value = Mathf.SmoothDamp(current, target, ref velocity, responseTime, Mathf.Infinity, deltaTime);
        if (value < 0f || (target == 0f && value < 0.0005f)) { value = 0f; velocity = 0f; }
        if (value > maximum) { value = maximum; velocity = 0f; }
        return value;
    }

    private void UpdateSpikes(RearThreatSnapshot snapshot, Vector3 forward, float deltaTime)
    {
        float directionBlend = 1f - Mathf.Exp(-deltaTime / _directionSmoothingTime);
        for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
        {
            _matched[i] = false;
            _targetLengths[i] = 0f;
            _targetDirections[i] = _directions[i];
        }
        // Match existing tracks before allocating replacement slots.
        for (int s = 0; s < snapshot.Count; s++)
        {
            var spike = snapshot.GetSpike(s);
            for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
            {
                if (_trackIds[i] != spike.TrackId) continue;
                SetTarget(i, spike.TrackId, spike.WorldDirection, spike.Urgency);
                break;
            }
        }
        for (int s = 0; s < snapshot.Count; s++)
        {
            var spike = snapshot.GetSpike(s);
            bool found = false;
            for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
                if (_matched[i] && _trackIds[i] == spike.TrackId) { found = true; break; }
            if (found) continue;
            int slot = -1;
            for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
                if (!_matched[i] && (slot < 0 || _lengths[i] < _lengths[slot])) slot = i;
            if (slot >= 0) SetTarget(slot, spike.TrackId, spike.WorldDirection, spike.Urgency);
        }
        float halfArc = HalfArc;
        for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
        {
            if (_lengths[i] <= 0.0005f) _directions[i] = _targetDirections[i];
            else if (_matched[i]) _directions[i] = Vector3.Slerp(_directions[i], _targetDirections[i], directionBlend).normalized;
            _angles[i] = _directions[i].sqrMagnitude > 0f ? Vector3.SignedAngle(-forward, _directions[i], Vector3.up) : 0f;
            // A turn must not carry old rear spikes around to the front of the player.
            if (Mathf.Abs(_angles[i]) > halfArc) _targetLengths[i] = 0f;
            _lengths[i] = StepResponse(_lengths[i], _targetLengths[i], ref _lengthVelocities[i], deltaTime, _maximumSpikeLength);
            if (_lengths[i] < 0.0005f && !_matched[i])
            {
                _lengths[i] = _lengthVelocities[i] = 0f;
                _trackIds[i] = 0;
            }
        }
    }

    private void SetTarget(int slot, int track, Vector3 direction, float urgency)
    {
        if (_trackIds[slot] != track)
        {
            // Unrelated groups emerge at their own bearing, never sweep an old spike across empty space.
            _lengths[slot] = _lengthVelocities[slot] = 0f;
            _directions[slot] = direction;
        }
        _trackIds[slot] = track;
        _matched[slot] = true;
        _targetDirections[slot] = direction;
        _targetLengths[slot] = Mathf.Lerp(_minimumSpikeLength, _maximumSpikeLength, urgency);
    }

    private float HalfArc => Mathf.Clamp((_sensor != null ? _sensor.RearArcDegrees * 0.5f + _sensor.ArcExitMargin : 95f), 0.5f, 180f);

    private void Follow(Vector3 position, Vector3 forward)
    {
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        transform.SetPositionAndRotation(position + rotation * _hipOffset, rotation);
        // The scene UI wrapper may have an authored transform. Geometry is in world units.
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(SafeReciprocal(parentScale.x), SafeReciprocal(parentScale.y), SafeReciprocal(parentScale.z));
    }

    private static float SafeReciprocal(float value) => Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;

    private void Draw()
    {
        float outer = _idleOuterRadius * Mathf.Lerp(1f, _maximumRadiusMultiplier, _urgency);
        _geometry.Update(_fillMesh, _outlineMesh, _innerRadius, outer, HalfArc, _spikeWidthDegrees,
            _outlineWidth, _idleOuterRadius * _maximumRadiusMultiplier + _maximumSpikeLength,
            _angles, _lengths, _localFlex, _idleOuterRadius, _shoulderWidthDegrees, _maximumSpikeLength);
        _properties.SetColor(BaseColor, Color.Lerp(_idleColor, _alertColor, _urgency));
        _fillRenderer.SetPropertyBlock(_properties);
        _properties.SetColor(BaseColor, _outlineColor);
        _outlineRenderer.SetPropertyBlock(_properties);
    }

    private void CacheRenderers()
    {
        _fillRenderer = _fill != null ? _fill.GetComponent<MeshRenderer>() : null;
        _outlineRenderer = _outline != null ? _outline.GetComponent<MeshRenderer>() : null;
        ConfigureRenderer(_fillRenderer);
        ConfigureRenderer(_outlineRenderer);
    }

    private static void ConfigureRenderer(MeshRenderer renderer)
    {
        if (renderer == null) return;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private bool EnsureMeshes()
    {
        if (_fill == null || _outline == null) return false;
        if (_fillRenderer == null || _outlineRenderer == null) CacheRenderers();
        if (_fillRenderer == null || _outlineRenderer == null) return false;
        _geometry ??= new RearThreatMesh();
        _properties ??= new MaterialPropertyBlock();
        if (_fillMesh == null) { _fillMesh = NewMesh("Rear threat fill (preview/runtime)"); _authoredFillMesh = _fill.sharedMesh; }
        if (_outlineMesh == null) { _outlineMesh = NewMesh("Rear threat outline (preview/runtime)"); _authoredOutlineMesh = _outline.sharedMesh; }
        if (_fill.sharedMesh != _fillMesh) { _authoredFillMesh = _fill.sharedMesh; _fill.sharedMesh = _fillMesh; }
        if (_outline.sharedMesh != _outlineMesh) { _authoredOutlineMesh = _outline.sharedMesh; _outline.sharedMesh = _outlineMesh; }
        return true;
    }

    private static Mesh NewMesh(string name)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        return mesh;
    }

    public void SetAuthoredMeshes(Mesh fill, Mesh outline)
    {
        _authoredFillMesh = fill;
        _authoredOutlineMesh = outline;
        if (_fill != null) _fill.sharedMesh = fill;
        if (_outline != null) _outline.sharedMesh = outline;
    }

    public void RestoreAuthoredMeshes()
    {
        if (_fill != null && _fillMesh != null && _fill.sharedMesh == _fillMesh) _fill.sharedMesh = _authoredFillMesh;
        if (_outline != null && _outlineMesh != null && _outline.sharedMesh == _outlineMesh) _outline.sharedMesh = _authoredOutlineMesh;
    }

    private void ReleaseMeshes()
    {
        RestoreAuthoredMeshes();
        DestroyMesh(_fillMesh);
        DestroyMesh(_outlineMesh);
        _fillMesh = _outlineMesh = null;
    }

    private static void DestroyMesh(Mesh mesh)
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
    }

    private void SetVisible(bool visible)
    {
        if (_fillRenderer != null) _fillRenderer.enabled = visible;
        if (_outlineRenderer != null) _outlineRenderer.enabled = visible && _outlineColor.a > 0f;
    }

    private void ResetVisualState()
    {
        _urgency = _urgencyVelocity = 0f;
        _boundPlayer = null;
        for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
        {
            _trackIds[i] = 0;
            _lengths[i] = _lengthVelocities[i] = _angles[i] = 0f;
            _directions[i] = Vector3.back;
        }
    }

    private void OnValidate()
    {
        _innerRadius = Mathf.Max(0.01f, _innerRadius);
        _idleOuterRadius = Mathf.Max(_innerRadius + 0.02f, _idleOuterRadius);
        _maximumRadiusMultiplier = Mathf.Max(1f, _maximumRadiusMultiplier);
        _minimumSpikeLength = Mathf.Max(0f, _minimumSpikeLength);
        _maximumSpikeLength = Mathf.Max(_minimumSpikeLength, _maximumSpikeLength);
        _spikeWidthDegrees = Mathf.Clamp(_spikeWidthDegrees, 0.2f, 60f);
        _outlineWidth = Mathf.Clamp(_outlineWidth, 0.001f, (_idleOuterRadius - _innerRadius) * 0.49f);
        _localFlex = Mathf.Clamp01(_localFlex);
        _shoulderWidthDegrees = Mathf.Clamp(_shoulderWidthDegrees, _spikeWidthDegrees, 120f);
        _growthTime = Mathf.Max(0.01f, _growthTime);
        _relaxTime = Mathf.Max(0.01f, _relaxTime);
        _directionSmoothingTime = Mathf.Max(0.01f, _directionSmoothingTime);
#if UNITY_EDITOR
        _previewDirty = true;
#endif
    }

#if UNITY_EDITOR
    public void RefreshPreview()
    {
        if (Application.IsPlaying(gameObject) || !EnsureMeshes()) return;
        OnValidate();
        _previewDirty = false;
        _urgency = Mathf.Clamp01(_previewUrgency);
        for (int i = 0; i < RearThreatMesh.MaximumSpikes; i++)
        {
            _angles[i] = _previewSpikeAngles != null && i < _previewSpikeAngles.Length ? Mathf.Clamp(_previewSpikeAngles[i], -HalfArc, HalfArc) : 0f;
            float urgency = _previewSpikeUrgencies != null && i < _previewSpikeUrgencies.Length ? Mathf.Clamp01(_previewSpikeUrgencies[i]) : _urgency;
            _lengths[i] = _previewSpikeAngles != null && i < _previewSpikeAngles.Length ? Mathf.Lerp(_minimumSpikeLength, _maximumSpikeLength, urgency) : 0f;
        }
        if (Player != null)
        {
            Vector3 forward = _sensor.GetReferenceForward();
            Follow(Player.position, forward);
        }
        _previewPlayerPosition = Player != null ? Player.position : transform.position;
        _previewForward = _sensor != null ? _sensor.GetReferenceForward() : transform.forward;
        _previewHalfArc = HalfArc;
        Draw();
        SetVisible(true);
    }
#endif
}
