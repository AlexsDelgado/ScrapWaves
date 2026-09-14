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
    [SerializeField, Min(0f), Tooltip("Maximum distance from the gameplay aim ray to a nearby enemy surface, in metres. Zero disables assistance.")]
    private float _aimAssistRadius = 0.35f;

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
    private readonly RaycastHit[] _assistHitBuffer = new RaycastHit[32];
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
        return TryGetAimDirection(origin, fallbackDistance, false, out direction);
    }

    public bool TryGetAimDirection(Vector3 origin, float fallbackDistance, bool preferDamageableAimPoint, out Vector3 direction)
    {
        bool valid = TryGetAimSolution(origin, fallbackDistance, preferDamageableAimPoint, out AimSolution solution);
        // Preserve the original overload's unnormalized return value.
        direction = valid ? solution.TargetPoint - solution.Origin : Vector3.zero;
        return valid;
    }

    public Camera AimCamera => ResolveCamera();

    public bool TryGetAimSolution(Vector3 origin, float fallbackDistance, bool preferDamageableAimPoint, out AimSolution solution)
    {
        solution = default;
        if (!TryGetGameplayRay(ResolveCamera(), out Ray ray)) return false;
        solution = new AimSolution(origin, GetTargetPoint(ray, origin, fallbackDistance, preferDamageableAimPoint), Time.frameCount);
        return solution.IsValid;
    }

    public AimSolution ResolveWeaponAim(Vector3 origin, WeaponInstance weapon)
    {
        float distance = weapon?.Data != null ? weapon.Data.BaseRange : _maxAimDistance;
        return TryGetAimSolution(origin, distance, WeaponAimPolicy.PreferDamageableAimPoint(weapon), out AimSolution solution)
            ? solution : CreateFallback(origin, distance, ResolveCamera(), transform.forward);
    }

    public static bool TryGetGameplayRay(Camera camera, out Ray ray)
    {
        ray = default;
        if (camera == null) return false;
        ThirdPersonCamera controller = camera.GetComponent<ThirdPersonCamera>();
        ray = controller != null && controller.isActiveAndEnabled
            ? controller.GameplayCenterRay
            : camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return true;
    }

    public static AimSolution CreateFallback(Vector3 origin, float distance, Camera camera, Vector3 forward)
    {
        if (TryGetGameplayRay(camera, out Ray ray)) forward = ray.direction;
        if (forward.sqrMagnitude <= 0.0001f) forward = Vector3.forward;
        return new AimSolution(origin, origin + forward.normalized * Mathf.Max(1f, distance), Time.frameCount);
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

    private Vector3 GetTargetPoint(Ray ray, Vector3 origin, float fallbackDistance, bool preferDamageableAimPoint)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, _maxAimDistance, _aimMask.value, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = GetNoHitTargetPoint(ray, origin, fallbackDistance);
        bool directlyHitsEnemy = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];
            if (IsIgnoredHit(hit))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestPoint = hit.point;
            directlyHitsEnemy = ResolveDamageableTargetRoot(hit) != null;
        }

        // Direct hits always retain the exact aimed-at surface. Assistance has no persistent target.
        if (preferDamageableAimPoint && !directlyHitsEnemy && _aimAssistRadius > 0f)
            closestPoint = GetNearbyEnemyPoint(ray, Mathf.Min(closestDistance, _maxAimDistance), closestPoint);
        return closestPoint;
    }

    private Vector3 GetNearbyEnemyPoint(Ray ray, float maximumDistance, Vector3 fallback)
    {
        int count = Physics.SphereCastNonAlloc(ray, _aimAssistRadius, _assistHitBuffer,
            maximumDistance, _aimMask.value, QueryTriggerInteraction.Ignore);
        float bestGap = _aimAssistRadius * _aimAssistRadius;
        float bestDepth = float.PositiveInfinity;
        Vector3 result = fallback;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _assistHitBuffer[i];
            if (IsIgnoredHit(hit)) continue;
            Transform root = ResolveDamageableTargetRoot(hit);
            if (root == null) continue;

            // Minimize distance to the actual collider, rather than its center or bounding box.
            Bounds bounds = hit.collider.bounds;
            float centerDepth = Vector3.Dot(bounds.center - ray.origin, ray.direction);
            float extent = bounds.extents.magnitude;
            float low = Mathf.Max(0f, centerDepth - extent);
            float high = Mathf.Min(maximumDistance, centerDepth + extent);
            if (high <= low) continue;
            for (int step = 0; step < 24; step++)
            {
                float a = Mathf.Lerp(low, high, 1f / 3f);
                float b = Mathf.Lerp(low, high, 2f / 3f);
                Vector3 pa = ray.GetPoint(a);
                Vector3 pb = ray.GetPoint(b);
                if ((hit.collider.ClosestPoint(pa) - pa).sqrMagnitude <= (hit.collider.ClosestPoint(pb) - pb).sqrMagnitude)
                    high = b;
                else low = a;
            }
            float depth = (low + high) * 0.5f;
            Vector3 onRay = ray.GetPoint(depth);
            Vector3 point = hit.collider.ClosestPoint(onRay);
            float gap = (point - onRay).sqrMagnitude;
            if (gap > bestGap || (Mathf.Approximately(gap, bestGap) && depth >= bestDepth)) continue;
            Vector3 delta = point - ray.origin;
            int blockers = Physics.RaycastNonAlloc(ray.origin, delta.normalized, _hitBuffer,
                delta.magnitude, _aimMask.value, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int j = 0; j < blockers; j++)
            {
                if (!IsIgnoredHit(_hitBuffer[j]) && ResolveDamageableTargetRoot(_hitBuffer[j]) != root)
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked) continue;
            bestGap = gap;
            bestDepth = depth;
            result = point;
        }
        return result;
    }

    private static Transform ResolveDamageableTargetRoot(RaycastHit hit)
    {
        Transform targetRoot = ResolveDamageableTargetRoot(hit.transform);
        if (targetRoot != null)
            return targetRoot;

        Rigidbody body = hit.rigidbody;
        return body != null ? ResolveDamageableTargetRoot(body.transform) : null;
    }

    private static Transform ResolveDamageableTargetRoot(Transform candidate)
    {
        if (candidate == null)
            return null;

        EnemyRegistryMember member = candidate.GetComponentInParent<EnemyRegistryMember>();
        if (member != null)
            return member.transform;

        IDamageable damageable = candidate.GetComponentInParent<IDamageable>();
        return damageable is Component damageableComponent ? damageableComponent.transform : null;
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
            float far = -projection + root;
            if (far >= 0f)
                return ray.GetPoint(far);
        }

        return origin + ray.direction * distance;
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
