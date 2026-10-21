using UnityEngine;

[DisallowMultipleComponent]
public class ReticleAimProvider : MonoBehaviour
{
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
        => TryGetAimSolutionCore(origin, fallbackDistance, preferDamageableAimPoint, 1f, out solution);

    private bool TryGetAimSolutionCore(Vector3 origin, float fallbackDistance, bool preferDamageableAimPoint,
        float minimumDistance, out AimSolution solution)
    {
        solution = default;
        if (!TryGetGameplayRay(ResolveCamera(), out Ray ray)) return false;
        solution = new AimSolution(origin, GetTargetPoint(ray, origin, fallbackDistance, preferDamageableAimPoint, minimumDistance), Time.frameCount);
        return solution.IsValid;
    }

    public AimSolution ResolveWeaponAim(Vector3 origin, WeaponInstance weapon, BasicProjectileWeapon behaviour = null)
    {
        float? authored = GetAuthoredConvergenceDistance(behaviour);
        float distance = authored ?? (weapon?.Data != null ? weapon.Data.BaseRange : _maxAimDistance);
        float minimumDistance = authored.HasValue ? 0f : 1f;
        return TryGetAimSolutionCore(origin, distance, WeaponAimPolicy.PreferDamageableAimPoint(weapon), minimumDistance, out AimSolution solution)
            ? solution : CreateFallback(origin, distance, ResolveCamera(), transform.forward, minimumDistance);
    }

    public static float ResolveConvergenceDistance(WeaponInstance weapon, BasicProjectileWeapon behaviour, float fallback = 0f)
    {
        float? authored = GetAuthoredConvergenceDistance(behaviour);
        if (authored.HasValue) return authored.Value;
        return weapon?.Data != null ? weapon.Data.BaseRange : fallback;
    }

    public static AimSolution CreateWeaponFallback(Vector3 origin, WeaponInstance weapon, BasicProjectileWeapon behaviour,
        Camera camera, Vector3 forward)
    {
        float? authored = GetAuthoredConvergenceDistance(behaviour);
        float distance = authored ?? (weapon?.Data != null ? weapon.Data.BaseRange : 0f);
        return CreateFallback(origin, distance, camera, forward, authored.HasValue ? 0f : 1f);
    }

    private static float? GetAuthoredConvergenceDistance(BasicProjectileWeapon behaviour)
    {
        float? value = behaviour?.GetManualAimConvergenceDistance();
        return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value) && value.Value > 0f ? value : null;
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

    public static AimSolution CreateFallback(Vector3 origin, float distance, Camera camera, Vector3 forward, float minimumDistance = 1f)
    {
        if (TryGetGameplayRay(camera, out Ray ray)) forward = ray.direction;
        if (forward.sqrMagnitude <= 0.0001f) forward = Vector3.forward;
        return new AimSolution(origin, origin + forward.normalized * Mathf.Max(minimumDistance, distance), Time.frameCount);
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
        return MortarTrajectory.TryPredictTerrainCollision(origin, target, arcHeight, travelTime,
            collisionRadius, _ignoredRoot, _mortarHitBuffer, out terrainHit);
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

    // Match the player's walkable slope and ground layers. Default is intentional:
    // authored elevated pads and geyser walkable meshes also live on that layer.
    public const float MortarGroundMinimumUpDot = 0.6427876f; // cos(50 degrees)
    // MonoBehaviour types can first initialize on Unity's loading thread. Resolve
    // layer names only when a physics query calls this property on the main thread.
    private static int s_MortarGroundMask, s_MortarActorMask;
    private static int MortarGroundMask
    {
        get
        {
            if (s_MortarGroundMask == 0)
            {
                s_MortarActorMask = LayerMask.GetMask("Enemy", "Player");
                s_MortarGroundMask = LayerMask.GetMask("Default", "Terrain");
            }
            return s_MortarGroundMask;
        }
    }

    public static bool IsValidMortarGroundHit(RaycastHit hit, Transform ignoredRoot)
    {
        return hit.normal.y >= MortarGroundMinimumUpDot
            && IsValidMortarGroundCollider(hit.collider, ignoredRoot);
    }

    public static bool IsValidMortarGroundCollider(Collider candidate, Transform ignoredRoot)
    {
        if (candidate == null || candidate.isTrigger
            || (MortarGroundMask & (1 << candidate.gameObject.layer)) == 0
            || !IsValidMortarTerrainTransform(candidate.transform, ignoredRoot))
            return false;

        Rigidbody body = candidate.attachedRigidbody;
        if (body != null && !body.isKinematic) return false;
        // Inspect the collider hierarchy, not RaycastHit.transform (which can be
        // the attached body's transform). Child hitboxes can have their own layer.
        for (Transform current = candidate.transform; current != null; current = current.parent)
        {
            int layer = current.gameObject.layer;
            if ((s_MortarActorMask & (1 << layer)) != 0)
                return false;
        }

        // These authored colliders enclose interactive props or spawn exclusion
        // volumes; an upward face does not make them a floor.
        return candidate.GetComponentInParent<CraftingStation>() == null
            && candidate.GetComponentInParent<EnemySpawnBlockVolume>() == null;
    }

    private Camera ResolveCamera()
    {
        if (_aimCamera == null)
            _aimCamera = Camera.main;

        return _aimCamera;
    }

    private Vector3 GetTargetPoint(Ray ray, Vector3 origin, float fallbackDistance, bool preferDamageableAimPoint, float minimumDistance = 1f)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, _maxAimDistance, _aimMask.value, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = GetNoHitTargetPoint(ray, origin, fallbackDistance, minimumDistance);
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

    private Vector3 GetNoHitTargetPoint(Ray ray, Vector3 origin, float fallbackDistance, float minimumDistance = 1f)
    {
        // Keep no-hit shots converged with the reticle at weapon reach, not at camera max distance.
        float distance = Mathf.Clamp(fallbackDistance, minimumDistance, _maxAimDistance);
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
