using UnityEngine;

[DisallowMultipleComponent]
public class ReticleAimProvider : MonoBehaviour
{
    [SerializeField, Tooltip("Camera used for the center-screen reticle ray. Empty uses Camera.main.")]
    private Camera _aimCamera;

    [SerializeField, Tooltip("Root ignored by the reticle ray, usually the player root. Empty uses this transform.")]
    private Transform _ignoredRoot;

    [SerializeField, Min(1f)] private float _maxAimDistance = 150f;
    [SerializeField] private LayerMask _aimMask = ~0;

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    private void Awake()
    {
        if (_ignoredRoot == null)
            _ignoredRoot = transform;
    }

    public bool TryGetAimDirection(Vector3 origin, out Vector3 direction)
    {
        direction = Vector3.zero;

        Camera camera = ResolveCamera();
        if (camera == null)
            return false;

        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = GetTargetPoint(ray);
        direction = targetPoint - origin;
        return direction.sqrMagnitude > 0.0001f;
    }

    private Camera ResolveCamera()
    {
        if (_aimCamera == null)
            _aimCamera = Camera.main;

        return _aimCamera;
    }

    private Vector3 GetTargetPoint(Ray ray)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, _hitBuffer, _maxAimDistance, _aimMask.value, QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = ray.origin + ray.direction * _maxAimDistance;

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
