using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MortarShellImpact : MonoBehaviour
{
    private const int ArcSegments = 18;
    private const int CollisionBufferSize = 16;
    private static Material s_shellMaterial;

    private Vector3 _start;
    private Vector3 _target;
    private float _travelTime;
    private float _elapsed;
    private float _arcHeight;
    private float _explosionRadius;
    private float _falloff;
    private int _damage;
    private float _knockback;
    private float _collisionRadius;
    private Transform _ignoredRoot;
    private LineRenderer _line;
    private readonly Vector3[] _arcPoints = new Vector3[ArcSegments + 1];
    private readonly RaycastHit[] _collisionHits = new RaycastHit[CollisionBufferSize];
    private readonly List<IDamageable> _damagedThisExplosion = new();

    public static MortarShellImpact Launch(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback,
        float collisionRadius,
        Transform ignoredRoot)
    {
        GameObject go = new GameObject("MortarShellImpact");
        MortarShellImpact shell = go.AddComponent<MortarShellImpact>();
        shell.Configure(start, target, travelTime, arcHeight, damage, explosionRadius, falloff, knockback, collisionRadius, ignoredRoot);
        return shell;
    }

    private void Configure(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback,
        float collisionRadius,
        Transform ignoredRoot)
    {
        _start = start;
        _target = target;
        _travelTime = Mathf.Max(0.05f, travelTime);
        _arcHeight = Mathf.Max(0f, arcHeight);
        _damage = Mathf.Max(1, damage);
        _explosionRadius = Mathf.Max(0f, explosionRadius);
        _falloff = Mathf.Clamp01(falloff);
        _knockback = Mathf.Max(0f, knockback);
        _collisionRadius = Mathf.Max(0.01f, collisionRadius);
        _ignoredRoot = ignoredRoot;
        transform.position = _start;
        BuildLineRenderer();
        BuildShellVisual();
        UpdateArcVisual();
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = _elapsed / _travelTime;
        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = GetArcPoint(t);

        if (TryGetCollision(previousPosition, nextPosition, out Vector3 collisionPoint))
        {
            transform.position = collisionPoint;
            Detonate(collisionPoint);
            return;
        }

        transform.position = nextPosition;
        if (t >= MortarTrajectory.GetMaximumNormalizedTime(_travelTime))
            Destroy(gameObject);
    }

    // Sweeps the shell between frames so fast projectiles cannot tunnel through map geometry.
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
            if (ShouldIgnoreCollision(hit))
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestHit = hit;
            closestDistance = hit.distance;
            found = true;
        }

        if (!found)
            return false;

        collisionPoint = closestHit.point;
        return true;
    }

    private bool ShouldIgnoreCollision(RaycastHit hit)
    {
        Transform hitTransform = hit.transform;
        if (hitTransform == null)
            return true;

        if (hitTransform == transform || hitTransform.IsChildOf(transform))
            return true;

        if (_ignoredRoot == null)
            return false;

        if (hitTransform == _ignoredRoot || hitTransform.IsChildOf(_ignoredRoot))
            return true;

        Rigidbody body = hit.rigidbody;
        return body != null && (body.transform == _ignoredRoot || body.transform.IsChildOf(_ignoredRoot));
    }

    private void BuildLineRenderer()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.widthMultiplier = 0.06f;
        _line.positionCount = _arcPoints.Length;
        _line.material = new Material(Shader.Find("Sprites/Default"));
        _line.startColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        _line.endColor = new Color(1f, 0.35f, 0.05f, 0.25f);
    }

    private void BuildShellVisual()
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Mortar Shell Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * Mathf.Max(0.12f, _collisionRadius * 2f);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
            Destroy(visualCollider);

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetShellMaterial();
    }

    private void UpdateArcVisual()
    {
        for (int i = 0; i < _arcPoints.Length; i++)
        {
            float t = i / (float)(_arcPoints.Length - 1);
            _arcPoints[i] = GetArcPoint(t);
        }

        _line.SetPositions(_arcPoints);
    }

    private Vector3 GetArcPoint(float t)
    {
        return MortarTrajectory.Evaluate(_start, _target, _arcHeight, t);
    }

    private static Material GetShellMaterial()
    {
        if (s_shellMaterial != null)
            return s_shellMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_shellMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        s_shellMaterial.color = new Color(1f, 0.42f, 0.04f, 1f);
        return s_shellMaterial;
    }

    private void Detonate()
    {
        Detonate(_target);
    }

    private void Detonate(Vector3 explosionCenter)
    {
        _target = explosionCenter;
        ExplosionRadiusVfx.Spawn(explosionCenter, _explosionRadius);

        _damagedThisExplosion.Clear();
        Collider[] hits = Physics.OverlapSphere(explosionCenter, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            if (_damagedThisExplosion.Contains(damageable))
                continue;

            _damagedThisExplosion.Add(damageable);
            float distance = Vector3.Distance(explosionCenter, hits[i].transform.position);
            float t = _explosionRadius <= 0f ? 1f : Mathf.Clamp01(distance / _explosionRadius);
            float falloffScale = Mathf.Lerp(1f, 1f - _falloff, t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(_damage * falloffScale));
            if (damageable.ApplyDamage(finalDamage))
                EnemyKnockbackReceiver.TryApply(damageable, explosionCenter, _knockback * falloffScale);
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        if (_explosionRadius <= 0f)
            return;

        Gizmos.color = new Color(1f, 0.62f, 0.05f, 0.8f);
        Gizmos.DrawWireSphere(_target, _explosionRadius);
    }
}
