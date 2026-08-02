using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform _followTarget;

    [Header("Over-the-shoulder framing")]
    [SerializeField, Tooltip("Pivot height above the target's feet (shoulder/head height).")]
    private float _pivotHeight = 1.6f;

    [SerializeField, Tooltip("Lateral offset of the camera from the pivot. Positive = right shoulder, negative = left shoulder.")]
    private float _shoulderOffset = 0.6f;

    [SerializeField, Tooltip("Vertical offset of the camera from the pivot.")]
    private float _cameraHeightOffset = 0f;

    [SerializeField, Tooltip("Distance the camera sits behind the pivot.")]
    private float _cameraDistance = 3.5f;

    [Header("Look input")]
    [SerializeField, Tooltip("Horizontal mouse look scale.")]
    private float _horizontalSensitivity = 0.12f;

    [SerializeField, Tooltip("Vertical mouse look scale.")]
    private float _verticalSensitivity = 0.12f;

    [SerializeField, Tooltip("Invert vertical mouse look.")]
    private bool _invertVertical;

    [SerializeField, Tooltip("Lower pitch limit.")]
    private float _minPitch = -55f;

    [SerializeField, Tooltip("Upper pitch limit.")]
    private float _maxPitch = 65f;

    [Header("Collision")]
    [SerializeField, Tooltip("Pull the camera closer when terrain or level geometry blocks the desired orbit position.")]
    private bool _avoidCameraClipping = true;

    [SerializeField] private LayerMask _cameraCollisionMask = ~0;
    [SerializeField, Min(0f)] private float _cameraCollisionRadius = 0.25f;
    [SerializeField, Min(0f)] private float _cameraCollisionPadding = 0.12f;
    [SerializeField, Min(0f)] private float _minimumDistanceFromLookPoint = 0.65f;

    [SerializeField] private bool _lockCursorOnPlay = true;

    [Header("Presentation feedback")]
    [SerializeField, Range(0f, 1f)] private float _cameraFeedbackScale = 1f;
    [SerializeField, Min(0f)] private float _presentationImpulseDecay = 12f;
    [SerializeField, Min(0f)] private float _maximumPresentationPositionImpulse = 0.35f;
    [SerializeField, Min(0f)] private float _maximumPresentationRotationImpulse = 5f;
    [SerializeField, Min(0f)] private float _maximumPresentationFovKick = 5f;

    private readonly RaycastHit[] _cameraHitBuffer = new RaycastHit[12];
    private float _yaw;
    private float _pitch;
    private Vector3 _presentationPositionImpulse;
    private Vector3 _presentationRotationImpulse;
    private float _presentationFovKick;
    private Camera _camera;
    private float _baseFieldOfView;

    /// <summary>When true, look input is blocked and the cursor is released for UI.</summary>
    private bool _lookBlockedByUi;

    private void OnEnable()
    {
        CacheCamera();
    }

    private void Start()
    {
        CacheCamera();
        Vector3 euler = transform.eulerAngles;
        _pitch = NormalizeEulerPitch(euler.x);
        _yaw = euler.y;

        // No robar el cursor si una UI ya lo liberó antes de que corriera este Start()
        // (p. ej. la selección de arma inicial que se presenta al arrancar la escena).
        if (_lockCursorOnPlay && !_lookBlockedByUi)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Called by UI flows that need mouse control instead of camera look.</summary>
    public void SetLookBlockedByUi(bool blocked)
    {
        if (blocked == _lookBlockedByUi)
            return;

        _lookBlockedByUi = blocked;

        if (blocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (_lockCursorOnPlay)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void SetFollowTarget(Transform followTarget)
    {
        _followTarget = followTarget;
    }

    public float HorizontalSensitivity
    {
        get => _horizontalSensitivity;
        set => _horizontalSensitivity = Mathf.Max(0.01f, value);
    }

    public float VerticalSensitivity
    {
        get => _verticalSensitivity;
        set => _verticalSensitivity = Mathf.Max(0.01f, value);
    }

    public bool InvertVertical
    {
        get => _invertVertical;
        set => _invertVertical = value;
    }

    public float CameraFeedbackScale
    {
        get => _cameraFeedbackScale;
        set
        {
            _cameraFeedbackScale = Mathf.Clamp01(value);
            if (_cameraFeedbackScale <= 0f)
                ClearPresentationImpulses();
        }
    }

    public Vector3 CurrentPresentationPositionImpulse => _presentationPositionImpulse;
    public Vector3 CurrentPresentationRotationImpulse => _presentationRotationImpulse;
    public float CurrentPresentationFovKick => _presentationFovKick;

    public bool AddPresentationImpulse(Vector3 positionImpulse, Vector3 rotationImpulse)
    {
        return AddPresentationImpulse(positionImpulse, rotationImpulse, 0f);
    }

    public bool AddPresentationImpulse(Vector3 positionImpulse, Vector3 rotationImpulse, float fovKick)
    {
        if (!isActiveAndEnabled ||
            _cameraFeedbackScale <= 0f ||
            (positionImpulse.sqrMagnitude <= 0.000001f &&
             rotationImpulse.sqrMagnitude <= 0.000001f &&
             Mathf.Abs(fovKick) <= 0.0001f))
        {
            return false;
        }

        _presentationPositionImpulse = Vector3.ClampMagnitude(
            _presentationPositionImpulse + positionImpulse,
            _maximumPresentationPositionImpulse);
        _presentationRotationImpulse = Vector3.ClampMagnitude(
            _presentationRotationImpulse + rotationImpulse,
            _maximumPresentationRotationImpulse);
        _presentationFovKick = Mathf.Clamp(
            _presentationFovKick + fovKick,
            -_maximumPresentationFovKick,
            _maximumPresentationFovKick);
        return true;
    }

    public void ClearPresentationImpulses()
    {
        _presentationPositionImpulse = Vector3.zero;
        _presentationRotationImpulse = Vector3.zero;
        _presentationFovKick = 0f;
        if (_camera != null)
            _camera.fieldOfView = _baseFieldOfView;
    }

    public void ApplyMainGameOrbitDefaults()
    {
        _pivotHeight = 1.6f;
        _shoulderOffset = 0.6f;
        _cameraHeightOffset = 0f;
        _cameraDistance = 3.5f;
        _horizontalSensitivity = 0.12f;
        _verticalSensitivity = 0.12f;
        _invertVertical = false;
        _minPitch = -55f;
        _maxPitch = 65f;
        _avoidCameraClipping = true;
        _cameraCollisionRadius = 0.25f;
        _cameraCollisionPadding = 0.12f;
        _minimumDistanceFromLookPoint = 0.65f;
        _lockCursorOnPlay = true;
    }

    private void LateUpdate()
    {
        if (_followTarget == null)
            return;

        Mouse mouse = Mouse.current;
        if (!_lookBlockedByUi && mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * _horizontalSensitivity;

            float verticalSign = _invertVertical ? 1f : -1f;
            _pitch += verticalSign * delta.y * _verticalSensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }

        // Orientación de la cámara basada en el look input.
        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);

        // Pivote anclado al personaje, a la altura del hombro.
        Vector3 pivot = _followTarget.position + Vector3.up * _pivotHeight;

        // Desplazamiento lateral (over-the-shoulder) y vertical en el espacio del orbit.
        Vector3 shoulder = orbit * new Vector3(_shoulderOffset, _cameraHeightOffset, 0f);
        Vector3 anchor = pivot + shoulder;

        // La cámara se ubica detrás del ancla, en la dirección del orbit.
        Vector3 back = orbit * Vector3.back;
        Vector3 desiredPosition = anchor + back * _cameraDistance;

        Vector3 resolvedPosition = ResolveCameraPosition(anchor, desiredPosition);
        Quaternion resolvedRotation = Quaternion.LookRotation(orbit * Vector3.forward, Vector3.up);

        // Presentation feedback is added after gameplay orbit and collision are resolved.
        // It never feeds back into yaw, pitch, follow placement, or gameplay aim.
        transform.position = resolvedPosition +
            orbit * (_presentationPositionImpulse * _cameraFeedbackScale);
        transform.rotation = resolvedRotation *
            Quaternion.Euler(_presentationRotationImpulse * _cameraFeedbackScale);
        if (_camera != null)
            _camera.fieldOfView = _baseFieldOfView + _presentationFovKick * _cameraFeedbackScale;

        DecayPresentationImpulses();
    }

    private void OnDisable()
    {
        ClearPresentationImpulses();
    }

    private Vector3 ResolveCameraPosition(Vector3 anchor, Vector3 desiredPosition)
    {
        if (!_avoidCameraClipping)
            return desiredPosition;

        Vector3 toDesired = desiredPosition - anchor;
        float desiredDistance = toDesired.magnitude;
        if (desiredDistance <= 0.0001f)
            return desiredPosition;

        Vector3 direction = toDesired / desiredDistance;
        if (!TryGetCameraCollision(anchor, direction, desiredDistance, out RaycastHit closestHit))
            return desiredPosition;

        float resolvedDistance = closestHit.distance - _cameraCollisionPadding;
        resolvedDistance = Mathf.Max(0.05f, resolvedDistance);
        resolvedDistance = Mathf.Min(resolvedDistance, desiredDistance);
        return anchor + direction * resolvedDistance;
    }

    private bool TryGetCameraCollision(Vector3 origin, Vector3 direction, float distance, out RaycastHit closestHit)
    {
        closestHit = default;
        float closestDistance = float.PositiveInfinity;

        int hitCount = _cameraCollisionRadius > 0f
            ? Physics.SphereCastNonAlloc(origin, _cameraCollisionRadius, direction, _cameraHitBuffer, distance, _cameraCollisionMask.value, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, _cameraHitBuffer, distance, _cameraCollisionMask.value, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _cameraHitBuffer[i];
            if (hit.distance <= 0f || hit.distance >= closestDistance || IsFollowTargetHit(hit))
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
        }

        return closestDistance < float.PositiveInfinity;
    }

    private bool IsFollowTargetHit(RaycastHit hit)
    {
        if (_followTarget == null || hit.transform == null)
            return false;

        if (hit.transform == _followTarget || hit.transform.IsChildOf(_followTarget))
            return true;

        Rigidbody body = hit.rigidbody;
        return body != null && (body.transform == _followTarget || body.transform.IsChildOf(_followTarget));
    }

    private static float NormalizeEulerPitch(float x)
    {
        if (x > 180f)
            x -= 360f;
        return x;
    }

    private void DecayPresentationImpulses()
    {
        if (_presentationImpulseDecay <= 0f)
            return;

        float decay = Mathf.Exp(-_presentationImpulseDecay * Time.unscaledDeltaTime);
        _presentationPositionImpulse *= decay;
        _presentationRotationImpulse *= decay;
        _presentationFovKick *= decay;

        if (_presentationPositionImpulse.sqrMagnitude < 0.000001f)
            _presentationPositionImpulse = Vector3.zero;
        if (_presentationRotationImpulse.sqrMagnitude < 0.000001f)
            _presentationRotationImpulse = Vector3.zero;
        if (Mathf.Abs(_presentationFovKick) < 0.0001f)
            _presentationFovKick = 0f;
    }

    private void CacheCamera()
    {
        if (_camera == null)
            _camera = GetComponent<Camera>();
        if (_camera != null)
            _baseFieldOfView = _camera.fieldOfView;
    }
}
