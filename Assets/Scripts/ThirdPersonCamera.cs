using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform _followTarget;

    [SerializeField, Tooltip("Camera position relative to the pivot in orbit space. Y is height, negative Z is behind.")]
    private Vector3 _followOffset = new Vector3(0f, 1.7f, -4.2f);

    [SerializeField, Tooltip("Point the camera looks at, relative to the target.")]
    private Vector3 _lookAtOffset = new Vector3(0f, 1.2f, 0f);

    [SerializeField, Tooltip("Horizontal mouse look scale.")]
    private float _horizontalSensitivity = 0.12f;

    [SerializeField, Tooltip("Vertical mouse look scale.")]
    private float _verticalSensitivity = 0.12f;

    [SerializeField, Tooltip("Invert vertical mouse look.")]
    private bool _invertVertical;

    [SerializeField, Tooltip("Lower pitch limit.")]
    private float _minPitch = -70f;

    [SerializeField, Tooltip("Upper pitch limit.")]
    private float _maxPitch = 70f;

    [SerializeField, Tooltip("Pull the camera closer when terrain or level geometry blocks the desired orbit position.")]
    private bool _avoidCameraClipping = true;

    [SerializeField] private LayerMask _cameraCollisionMask = ~0;
    [SerializeField, Min(0f)] private float _cameraCollisionRadius = 0.25f;
    [SerializeField, Min(0f)] private float _cameraCollisionPadding = 0.12f;
    [SerializeField, Min(0f)] private float _minimumDistanceFromLookPoint = 0.65f;

    [SerializeField] private bool _lockCursorOnPlay = true;

    private readonly RaycastHit[] _cameraHitBuffer = new RaycastHit[12];
    private float _yaw;
    private float _pitch;

    /// <summary>When true, look input is blocked and the cursor is released for UI.</summary>
    private bool _lookBlockedByUi;

    private void Start()
    {
        Vector3 euler = transform.eulerAngles;
        _pitch = NormalizeEulerPitch(euler.x);
        _yaw = euler.y;

        if (_lockCursorOnPlay)
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

    public void ApplyMainGameOrbitDefaults()
    {
        _followOffset = new Vector3(0f, 1.9f, -4.2f);
        _lookAtOffset = new Vector3(0f, 1.2f, 0f);
        _horizontalSensitivity = 0.12f;
        _verticalSensitivity = 0.12f;
        _invertVertical = true;
        _minPitch = -70f;
        _maxPitch = 70f;
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

            float verticalSign = _invertVertical ? -1f : 1f;
            _pitch += verticalSign * delta.y * _verticalSensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }

        Quaternion yawRot = Quaternion.AngleAxis(_yaw, Vector3.up);
        Quaternion pitchRot = Quaternion.AngleAxis(_pitch, Vector3.right);
        Quaternion orbit = yawRot * pitchRot;

        Vector3 pivot = _followTarget.position;
        Vector3 lookPoint = pivot + _lookAtOffset;
        Vector3 desiredPosition = pivot + orbit * _followOffset;

        transform.position = ResolveCameraPosition(lookPoint, desiredPosition);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
    }

    private Vector3 ResolveCameraPosition(Vector3 lookPoint, Vector3 desiredPosition)
    {
        if (!_avoidCameraClipping)
            return desiredPosition;

        Vector3 toDesired = desiredPosition - lookPoint;
        float desiredDistance = toDesired.magnitude;
        if (desiredDistance <= 0.0001f)
            return desiredPosition;

        Vector3 direction = toDesired / desiredDistance;
        if (!TryGetCameraCollision(lookPoint, direction, desiredDistance, out RaycastHit closestHit))
            return desiredPosition;

        float resolvedDistance = closestHit.distance - _cameraCollisionPadding;
        if (resolvedDistance > _minimumDistanceFromLookPoint)
            resolvedDistance = Mathf.Max(_minimumDistanceFromLookPoint, resolvedDistance);
        else
            resolvedDistance = Mathf.Max(0.05f, resolvedDistance);

        resolvedDistance = Mathf.Min(resolvedDistance, desiredDistance);
        return lookPoint + direction * resolvedDistance;
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
}
