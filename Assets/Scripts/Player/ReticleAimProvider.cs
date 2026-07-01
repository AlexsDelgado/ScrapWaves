using UnityEngine;

[DisallowMultipleComponent]
public class ReticleAimProvider : MonoBehaviour
{
    private const int MortarPredictionSegments = 64;
    private const int MortarHitBufferSize = 32;

    [SerializeField, Tooltip("Camera used for the center-screen reticle ray. Empty uses Camera.main.")]
    private Camera _aimCamera;

    [SerializeField, Tooltip("Root ignored by the reticle ray, usually the player root. Empty uses this transform.")]
    private Transform _ignoredRoot;

    [SerializeField, Min(1f)] private float _maxAimDistance = 150f;
    [SerializeField] private LayerMask _aimMask = ~0;

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
    private readonly RaycastHit[] _mortarHitBuffer = new RaycastHit[MortarHitBufferSize];

    private void Awake()
    {
        if (_ignoredRoot == null)
            _ignoredRoot = transform;
    }

    public bool TryGetAimDirection(Vector3 origin, out Vector3 direction)
    {
        return TryGetAimDirection(origin, _maxAimDistance, out direction);
    }

    public bool TryGetAimDirection(Vector3 origin, float fallbackDistance, out Vector3 direction)
    {
        direction = Vector3.zero;

        Camera camera = ResolveCamera();
        if (camera == null)
            return false;

        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = GetTargetPoint(ray, origin, fallbackDistance);
        direction = targetPoint - origin;
        return direction.sqrMagnitude > 0.0001f;
    }

    public bool TryGetMortarTerrainImpact(
        Vector3 origin,
        Vector3 aimDirection,
        float range,
        float arcHeight,
        float collisionRadius,
        float travelTime,
        out RaycastHit terrainHit)
    {
        terrainHit = default;
        if (aimDirection.sqrMagnitude <= 0.0001f)
            return false;

        Vector3 target = origin + aimDirection.normalized * Mathf.Max(0f, range);
        float maximumTime = MortarTrajectory.GetMaximumNormalizedTime(travelTime);
        float predictionStep = maximumTime / MortarPredictionSegments;
        float radius = Mathf.Max(0.01f, collisionRadius);
        Vector3 previous = origin;

        // Sweep chords along the same parabola as the live shell while ignoring enemy hits.
        for (int i = 1; i <= MortarPredictionSegments; i++)
        {
            float t = predictionStep * i;
            Vector3 next = MortarTrajectory.Evaluate(origin, target, arcHeight, t);
            if (TryGetMortarSegmentHit(previous, next, radius, out terrainHit))
                return true;

            previous = next;
        }

        return false;
    }

    public static bool IsValidMortarTerrainTransform(Transform candidate, Transform ignoredRoot)
    {
        if (candidate == null)
            return false;

        if (ignoredRoot != null
            && (candidate == ignoredRoot || candidate.IsChildOf(ignoredRoot)))
        {
            return false;
        }

        if (candidate.GetComponentInParent<IDamageable>() != null)
            return false;

        return candidate.GetComponentInParent<EnemyRegistryMember>() == null;
    }

    private Camera ResolveCamera()
    {
        if (_aimCamera == null)
            _aimCamera = Camera.main;

        return _aimCamera;
    }

    private Vector3 GetTargetPoint(Ray ray, Vector3 origin, float fallbackDistance)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, _maxAimDistance, _aimMask.value, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = GetNoHitTargetPoint(ray, origin, fallbackDistance);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];
            if (IsIgnoredHit(hit))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestPoint = hit.point;
        }

        return closestPoint;
    }

    private Vector3 GetNoHitTargetPoint(Ray ray, Vector3 origin, float fallbackDistance)
    {
        // Keep no-hit shots converged with the reticle at weapon reach, not at camera max distance.
        float distance = Mathf.Clamp(fallbackDistance, 1f, _maxAimDistance);
        Vector3 cameraToOrigin = ray.origin - origin;
        float projection = Vector3.Dot(ray.direction, cameraToOrigin);
        float c = cameraToOrigin.sqrMagnitude - distance * distance;
        float discriminant = projection * projection - c;

        if (discriminant >= 0f)
        {
            float root = Mathf.Sqrt(discriminant);
            float near = -projection - root;
            float far = -projection + root;
            float rayDistance = near >= 0f ? near : far;
            if (rayDistance >= 0f)
                return ray.GetPoint(rayDistance);
        }

        float closestRayDistance = Mathf.Max(0f, -projection);
        return ray.GetPoint(closestRayDistance);
    }

    private bool TryGetMortarSegmentHit(
        Vector3 start,
        Vector3 end,
        float radius,
        out RaycastHit terrainHit)
    {
        terrainHit = default;
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return false;

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            radius,
            displacement / distance,
            _mortarHitBuffer,
            distance,
            _aimMask.value,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _mortarHitBuffer[i];
            if (!IsValidMortarTerrainTransform(hit.transform, _ignoredRoot))
                continue;

            Rigidbody body = hit.rigidbody;
            if (body != null
                && !IsValidMortarTerrainTransform(body.transform, _ignoredRoot))
            {
                continue;
            }

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            terrainHit = hit;
            found = true;
        }

        return found;
    }

    private bool IsIgnoredHit(RaycastHit hit)
    {
        if (_ignoredRoot == null || hit.transform == null)
            return false;

        if (hit.transform == _ignoredRoot || hit.transform.IsChildOf(_ignoredRoot))
            return true;

        Rigidbody attachedBody = hit.rigidbody;
        return attachedBody != null
            && (attachedBody.transform == _ignoredRoot || attachedBody.transform.IsChildOf(_ignoredRoot));
    }
}
