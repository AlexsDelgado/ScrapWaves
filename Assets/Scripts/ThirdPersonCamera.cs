using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-150)]
public class ThirdPersonCamera : MonoBehaviour
{
    public static event Action<ThirdPersonCamera> BecameAvailable;

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

    [SerializeField] private LayerMask _cameraCollisionMask = (1 << 0) | (1 << 7);
    [SerializeField, Min(0f)] private float _cameraCollisionRadius = 0.25f;
    [SerializeField, Min(0f)] private float _cameraCollisionPadding = 0.12f;
    [SerializeField, Min(0f), Tooltip("Preferred starting distance when recovering an anchor inside geometry; closer walls still take priority.")]
    private float _minimumDistanceFromLookPoint = 0.65f;
    [SerializeField, Min(0.1f), Tooltip("Outward obstruction recovery in metres/second. Inward correction remains immediate.")]
    private float _obstructionReturnSpeed = 8f;

    [SerializeField] private bool _lockCursorOnPlay = true;

    [Header("Presentation feedback")]
    [SerializeField, Range(0f, 1f)] private float _cameraFeedbackScale = 1f;
    [SerializeField] private bool _screenShakeEnabled = true;
    [SerializeField, Min(0f)] private float _presentationImpulseDecay = 12f;
    [SerializeField, Min(0f)] private float _maximumPresentationPositionImpulse = 0.35f;
    [SerializeField, Min(0f)] private float _maximumPresentationRotationImpulse = 5f;
    [SerializeField, Min(0f)] private float _maximumPresentationFovKick = 5f;
    [SerializeField, Range(0f, 1f), Tooltip("Cosmetic camera travel retained under Reduced Motion.")]
    private float _reducedMotionPositionScale = 0.2f;
    [SerializeField, Range(0f, 1f), Tooltip("Cosmetic camera rotation retained under Reduced Motion.")]
    private float _reducedMotionRotationScale = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("FOV kick retained under Reduced Motion. Zero removes zoom pulses.")]
    private float _reducedMotionFovScale;

    private readonly RaycastHit[] _cameraHitBuffer = new RaycastHit[12];
    private readonly Collider[] _cameraOverlapBuffer = new Collider[16];
    private float _resolvedOrbitDistance = -1f;
    private float _yaw;
    private float _pitch;
    private Vector3 _presentationPositionImpulse;
    private Vector3 _presentationRotationImpulse;
    private float _presentationFovKick;
    private Camera _camera;
    private float _baseFieldOfView;
    private Vector3 _gameplayPosition;
    private Quaternion _gameplayRotation = Quaternion.identity;

    public Vector3 GameplayForward => _gameplayRotation * Vector3.forward;
    public Ray GameplayCenterRay => new(_gameplayPosition + GameplayForward * (_camera != null ? _camera.nearClipPlane : 0f), GameplayForward);

    /// <summary>When true, look input is blocked and the cursor is released for UI.</summary>
    private bool _lookBlockedByUi;

    private void OnEnable()
    {
        CacheCamera();
        _gameplayPosition = transform.position;
        _gameplayRotation = transform.rotation;
        Vector3 euler = transform.eulerAngles;
        _pitch = NormalizeEulerPitch(euler.x);
        _yaw = euler.y;
        BecameAvailable?.Invoke(this);
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
        _resolvedOrbitDistance = -1f;
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

    public bool ScreenShakeEnabled
    {
        get => _screenShakeEnabled;
        set
        {
            _screenShakeEnabled = value;
            if (!_screenShakeEnabled)
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
        return AddPresentationImpulse(positionImpulse, rotationImpulse, fovKick, reducedMotion: false);
    }

    public bool AddPresentationImpulse(
        Vector3 positionImpulse,
        Vector3 rotationImpulse,
        float fovKick,
        bool reducedMotion)
    {
        if (reducedMotion)
        {
            positionImpulse *= Mathf.Clamp01(_reducedMotionPositionScale);
            rotationImpulse *= Mathf.Clamp01(_reducedMotionRotationScale);
            fovKick *= Mathf.Clamp01(_reducedMotionFovScale);
        }

        if (!isActiveAndEnabled ||
            !_screenShakeEnabled ||
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
        _pivotHeight = 1.5f;
        _shoulderOffset = 1f;
        _cameraHeightOffset = 0f;
        _cameraDistance = 3.5f;
        _horizontalSensitivity = 0.12f;
        _verticalSensitivity = 0.12f;
        _invertVertical = false;
        _minPitch = -70f;
        _maxPitch = 70f;
        _avoidCameraClipping = true;
        _cameraCollisionRadius = 0.25f;
        _cameraCollisionPadding = 0.12f;
        _minimumDistanceFromLookPoint = 0.65f;
        _cameraCollisionMask = (1 << 0) | (1 << 7);
        _obstructionReturnSpeed = 8f;
        _lockCursorOnPlay = true;
        _reducedMotionPositionScale = 0.2f;
        _reducedMotionRotationScale = 0.35f;
        _reducedMotionFovScale = 0f;
    }

    private void Update()
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

        _gameplayPosition = ResolveCameraPosition(anchor, desiredPosition);
        _gameplayRotation = Quaternion.LookRotation(orbit * Vector3.forward, Vector3.up);
    }

    private void LateUpdate()
    {
        if (_followTarget == null)
            return;
        // Presentation feedback is added after gameplay orbit and collision are resolved.
        // It never feeds back into yaw, pitch, follow placement, or gameplay aim.
        Vector3 presentationPosition = _gameplayPosition +
            _gameplayRotation * (_presentationPositionImpulse * _cameraFeedbackScale);
        transform.position = SafePresentationPosition(presentationPosition);
        transform.rotation = _gameplayRotation *
            Quaternion.Euler(_presentationRotationImpulse * _cameraFeedbackScale);
        if (_camera != null)
            _camera.fieldOfView = _baseFieldOfView + _presentationFovKick * _cameraFeedbackScale;

        DecayPresentationImpulses();
    }

    private void OnDisable()
    {
        ClearPresentationImpulses();
        transform.SetPositionAndRotation(_gameplayPosition, _gameplayRotation);
    }

    private Vector3 ResolveCameraPosition(Vector3 anchor, Vector3 desiredPosition)
    {
        if (!_avoidCameraClipping)
        {
            _resolvedOrbitDistance = -1f;
            return desiredPosition;
        }

        Vector3 toDesired = desiredPosition - anchor;
        float desiredDistance = toDesired.magnitude;
        if (desiredDistance <= 0.0001f)
            return desiredPosition;

        Vector3 direction = toDesired / desiredDistance;
        // Casts do not reliably report an origin already overlapping geometry.
        if (HasCameraOverlap(anchor))
        {
            Vector3 clearPosition = FindClearOrbitPosition(anchor, direction, desiredDistance);
            _resolvedOrbitDistance = Vector3.Distance(anchor, clearPosition);
            return clearPosition;
        }
        float safeDistance = desiredDistance;
        if (TryGetCameraCollision(anchor, direction, desiredDistance, out RaycastHit closestHit))
            safeDistance = Mathf.Clamp(closestHit.distance - _cameraCollisionPadding, 0f, desiredDistance);
        SmoothObstructionDistance(safeDistance, Time.unscaledDeltaTime);
        Vector3 resolved = anchor + direction * _resolvedOrbitDistance;
        if (HasCameraOverlap(resolved))
        {
            resolved = FindClearOrbitPosition(anchor, direction, desiredDistance);
            _resolvedOrbitDistance = Vector3.Distance(anchor, resolved);
        }
        return resolved;
    }

    private float SmoothObstructionDistance(float safeDistance, float deltaTime)
    {
        if (_resolvedOrbitDistance < 0f || safeDistance < _resolvedOrbitDistance)
            _resolvedOrbitDistance = safeDistance;
        else
            _resolvedOrbitDistance = Mathf.MoveTowards(_resolvedOrbitDistance, safeDistance, _obstructionReturnSpeed * Mathf.Max(0f, deltaTime));
        return _resolvedOrbitDistance;
    }

    private Vector3 FindClearOrbitPosition(Vector3 anchor, Vector3 direction, float distance)
    {
        float step = Mathf.Max(0.1f, _cameraCollisionRadius);
        for (float candidate = Mathf.Min(_minimumDistanceFromLookPoint, distance); candidate < distance; candidate += step)
        {
            Vector3 position = anchor + direction * candidate;
            if (!HasCameraOverlap(position)) return position;
        }
        Vector3 desired = anchor + direction * distance;
        if (!HasCameraOverlap(desired)) return desired;
        return !HasCameraOverlap(_gameplayPosition) ? _gameplayPosition : anchor;
    }

    private bool HasCameraOverlap(Vector3 position)
    {
        if (_cameraCollisionRadius <= 0f) return false;
        int count = Physics.OverlapSphereNonAlloc(position, _cameraCollisionRadius, _cameraOverlapBuffer, _cameraCollisionMask, QueryTriggerInteraction.Ignore);
        Collider[] overlaps = count == _cameraOverlapBuffer.Length
            ? Physics.OverlapSphere(position, _cameraCollisionRadius, _cameraCollisionMask, QueryTriggerInteraction.Ignore)
            : _cameraOverlapBuffer;
        if (overlaps != _cameraOverlapBuffer) count = overlaps.Length;
        for (int i = 0; i < count; i++)
            if (!IsIgnoredCameraCollider(overlaps[i])) return true;
        return false;
    }

    private Vector3 SafePresentationPosition(Vector3 desired)
    {
        if (!_avoidCameraClipping) return desired;
        Vector3 offset = desired - _gameplayPosition;
        float distance = offset.magnitude;
        if (distance > 0.0001f && TryGetCameraCollision(_gameplayPosition, offset / distance, distance, out RaycastHit hit))
            desired = _gameplayPosition + offset / distance * Mathf.Max(0f, hit.distance - _cameraCollisionPadding);
        return HasCameraOverlap(desired) ? _gameplayPosition : desired;
    }

    private bool TryGetCameraCollision(Vector3 origin, Vector3 direction, float distance, out RaycastHit closestHit)
    {
        closestHit = default;
        float closestDistance = float.PositiveInfinity;

        int hitCount = _cameraCollisionRadius > 0f
            ? Physics.SphereCastNonAlloc(origin, _cameraCollisionRadius, direction, _cameraHitBuffer, distance, _cameraCollisionMask.value, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, _cameraHitBuffer, distance, _cameraCollisionMask.value, QueryTriggerInteraction.Ignore);

        RaycastHit[] hits = _cameraHitBuffer;
        if (hitCount == _cameraHitBuffer.Length)
        {
            hits = _cameraCollisionRadius > 0f
                ? Physics.SphereCastAll(origin, _cameraCollisionRadius, direction, distance, _cameraCollisionMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastAll(origin, direction, distance, _cameraCollisionMask, QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
        }

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.distance < 0f || hit.distance >= closestDistance || IsIgnoredCameraCollider(hit.collider))
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
        }

        return closestDistance < float.PositiveInfinity;
    }

    private bool IsIgnoredCameraCollider(Collider collider)
    {
        if (collider == null) return true;
        if (collider.GetComponentInParent<EnemyHealth>() != null) return true;
        if (_followTarget == null) return false;
        Transform hit = collider.transform;
        if (hit == _followTarget || hit.IsChildOf(_followTarget)) return true;
        Rigidbody body = collider.attachedRigidbody;
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

    private void OnValidate()
    {
        _cameraFeedbackScale = Mathf.Clamp01(_cameraFeedbackScale);
        _presentationImpulseDecay = Mathf.Max(0f, _presentationImpulseDecay);
        _maximumPresentationPositionImpulse = Mathf.Max(0f, _maximumPresentationPositionImpulse);
        _maximumPresentationRotationImpulse = Mathf.Max(0f, _maximumPresentationRotationImpulse);
        _maximumPresentationFovKick = Mathf.Max(0f, _maximumPresentationFovKick);
        _reducedMotionPositionScale = Mathf.Clamp01(_reducedMotionPositionScale);
        _reducedMotionRotationScale = Mathf.Clamp01(_reducedMotionRotationScale);
        _reducedMotionFovScale = Mathf.Clamp01(_reducedMotionFovScale);
    }
}
