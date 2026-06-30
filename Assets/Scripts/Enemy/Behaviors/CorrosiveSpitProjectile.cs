using UnityEngine;

/// <summary>
/// Escupitajo parabolico del Giga Worm. Al impactar deja un <see cref="CorrosiveSlimeArea"/>.
/// </summary>
public class CorrosiveSpitProjectile : MonoBehaviour
{
    private const int CollisionBufferSize = 8;

    private Vector3 _start;
    private Vector3 _target;
    private float _travelTime;
    private float _elapsed;
    private float _arcHeight;
    private float _collisionRadius;
    private GameObject _slimeAreaPrefab;
    private readonly RaycastHit[] _collisionHits = new RaycastHit[CollisionBufferSize];
    private bool _consumed;

    public static CorrosiveSpitProjectile Launch(
        GameObject slimeAreaPrefab,
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        float collisionRadius)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CorrosiveSpitProjectile";
        go.transform.position = start;
        go.transform.localScale = Vector3.one * (collisionRadius * 2f);

        Collider col = go.GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.35f, 0.85f, 0.2f, 0.9f)
            };
        }

        CorrosiveSpitProjectile projectile = go.AddComponent<CorrosiveSpitProjectile>();
        projectile.Configure(slimeAreaPrefab, start, target, travelTime, arcHeight, collisionRadius);
        return projectile;
    }

    private void Configure(
        GameObject slimeAreaPrefab,
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        float collisionRadius)
    {
        _slimeAreaPrefab = slimeAreaPrefab;
        _start = start;
        _target = target;
        _travelTime = Mathf.Max(0.05f, travelTime);
        _arcHeight = Mathf.Max(0f, arcHeight);
        _collisionRadius = Mathf.Max(0.05f, collisionRadius);
        _elapsed = 0f;
        _consumed = false;
        transform.position = _start;
    }

    private void Update()
    {
        if (_consumed)
            return;

        _elapsed += Time.deltaTime;
        float t = _elapsed / _travelTime;
        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = MortarTrajectory.Evaluate(_start, _target, _arcHeight, t);

        if (TryGetCollision(previousPosition, nextPosition, out Vector3 collisionPoint))
        {
            transform.position = collisionPoint;
            SpawnSlimeArea(collisionPoint);
            Consume();
            return;
        }

        transform.position = nextPosition;
        if (t >= MortarTrajectory.GetMaximumNormalizedTime(_travelTime))
        {
            SpawnSlimeArea(_target);
            Consume();
        }
    }

    private bool TryGetCollision(Vector3 start, Vector3 end, out Vector3 collisionPoint)
    {
        collisionPoint = end;
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return false;

        Vector3 direction = displacement / distance;
        int hitCount = Physics.SphereCastNonAlloc(
            start,
            _collisionRadius,
            direction,
            _collisionHits,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        RaycastHit closestHit = default;
        float closestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _collisionHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.collider.GetComponentInParent<PlayerHealth>() != null)
                continue;

            if (hit.collider.GetComponentInParent<EnemyHealth>() != null)
                continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                found = true;
            }
        }

        if (!found)
            return false;

        collisionPoint = closestHit.point;
        return true;
    }

    private void SpawnSlimeArea(Vector3 point)
    {
        if (_slimeAreaPrefab == null)
            return;

        Vector3 pos = point;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            pos = hit.point;

        Instantiate(_slimeAreaPrefab, pos, Quaternion.identity);
    }

    private void Consume()
    {
        if (_consumed)
            return;

        _consumed = true;
        Destroy(gameObject);
    }
}
