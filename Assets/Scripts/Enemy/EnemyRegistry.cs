using System.Collections.Generic;
using UnityEngine;

public static class EnemyRegistry
{
    private static readonly List<Transform> _activeEnemies = new List<Transform>(256);
    private static readonly List<Collider> _candidateColliders = new List<Collider>(8);
    private static readonly HashSet<Transform> _excludeScratch = new HashSet<Transform>();

    public static int ActiveCount => _activeEnemies.Count;

    public static void Register(Transform enemyTransform)
    {
        if (enemyTransform == null)
            return;

        for (int i = 0; i < _activeEnemies.Count; i++)
        {
            if (_activeEnemies[i] == enemyTransform)
                return;
        }

        _activeEnemies.Add(enemyTransform);
    }

    public static void Unregister(Transform enemyTransform)
    {
        if (enemyTransform == null)
            return;

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            if (_activeEnemies[i] == enemyTransform)
            {
                _activeEnemies.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>Copia todos los transforms de enemigos activos, sin filtrar por distancia (Destroyer: succión del swarm).</summary>
    public static int CollectActive(List<Transform> results)
    {
        results.Clear();

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform t = _activeEnemies[i];
            if (t == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            results.Add(t);
        }

        return results.Count;
    }

    public static int CollectActiveEnemyColliders(List<Collider> results, bool includeTriggers = false)
    {
        results.Clear();

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform enemy = _activeEnemies[i];
            if (enemy == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            _candidateColliders.Clear();
            enemy.GetComponentsInChildren(false, _candidateColliders);
            for (int c = 0; c < _candidateColliders.Count; c++)
            {
                Collider collider = _candidateColliders[c];
                if (collider == null || !collider.enabled)
                    continue;

                if (!includeTriggers && collider.isTrigger)
                    continue;

                if (!results.Contains(collider))
                    results.Add(collider);
            }
        }

        _candidateColliders.Clear();
        return results.Count;
    }

    public static bool TryGetClosestOnPlane(Vector3 from, float range, out Transform closest)
    {
        closest = null;
        if (range <= 0f)
            return false;

        float rangeSqr = range * range;
        float bestSqr = float.MaxValue;

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform t = _activeEnemies[i];
            if (t == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            Vector3 delta = t.position - from;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr > rangeSqr || sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            closest = t;
        }

        return closest != null;
    }

    // Picks one active enemy in range without allocating a temporary candidate list.
    public static bool TryGetRandomOnPlane(Vector3 from, float range, out Transform random)
    {
        random = null;
        if (range <= 0f)
            return false;

        float rangeSqr = range * range;
        int candidatesSeen = 0;

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform t = _activeEnemies[i];
            if (t == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            Vector3 delta = t.position - from;
            delta.y = 0f;
            if (delta.sqrMagnitude > rangeSqr)
                continue;

            candidatesSeen++;
            if (Random.Range(0, candidatesSeen) == 0)
                random = t;
        }

        return random != null;
    }

    public static Vector3 GetAimPoint(Transform target)
    {
        if (target == null)
            return Vector3.zero;

        _candidateColliders.Clear();
        target.GetComponentsInChildren(false, _candidateColliders);

        Bounds bodyBounds = default;
        Bounds fallbackBounds = default;
        bool hasBodyBounds = false;
        bool hasFallbackBounds = false;

        for (int i = 0; i < _candidateColliders.Count; i++)
        {
            Collider collider = _candidateColliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!hasFallbackBounds)
            {
                fallbackBounds = collider.bounds;
                hasFallbackBounds = true;
            }
            else
            {
                fallbackBounds.Encapsulate(collider.bounds);
            }

            if (collider.isTrigger)
                continue;

            if (!hasBodyBounds)
            {
                bodyBounds = collider.bounds;
                hasBodyBounds = true;
            }
            else
            {
                bodyBounds.Encapsulate(collider.bounds);
            }
        }

        _candidateColliders.Clear();

        if (hasBodyBounds)
            return bodyBounds.center;

        if (hasFallbackBounds)
            return fallbackBounds.center;

        return target.position;
    }

    // Finds the closest active enemy inside a horizontal cone.
    public static bool TryGetClosestOnPlaneInCone(Vector3 from, Vector3 forward, float range, float coneAngle, out Transform closest)
    {
        closest = null;
        if (range <= 0f)
            return false;

        float rangeSqr = range * range;
        float bestSqr = float.MaxValue;

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform t = _activeEnemies[i];
            if (t == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            Vector3 delta = t.position - from;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr > rangeSqr || sqr >= bestSqr)
                continue;

            if (!IsInsideHorizontalCone(delta, forward, coneAngle))
                continue;

            bestSqr = sqr;
            closest = t;
        }

        return closest != null;
    }

    // Picks one active enemy inside a horizontal cone without allocating a candidate list.
    public static bool TryGetRandomOnPlaneInCone(Vector3 from, Vector3 forward, float range, float coneAngle, out Transform random)
    {
        random = null;
        if (range <= 0f)
            return false;

        float rangeSqr = range * range;
        int candidatesSeen = 0;

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform t = _activeEnemies[i];
            if (t == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            Vector3 delta = t.position - from;
            delta.y = 0f;
            if (delta.sqrMagnitude > rangeSqr)
                continue;

            if (!IsInsideHorizontalCone(delta, forward, coneAngle))
                continue;

            candidatesSeen++;
            if (Random.Range(0, candidatesSeen) == 0)
                random = t;
        }

        return random != null;
    }

    /// <summary>
    /// Como <see cref="TryGetClosestOnPlane"/>, pero ignora candidatos cuya diferencia vertical supere <paramref name="maxAbsDeltaY"/>.
    /// Útil para evitar que el autoataque apunte a pisos muy arriba/abajo.
    /// </summary>
    public static bool TryGetClosestOnPlaneWithinVerticalDelta(Vector3 from, float range, float maxAbsDeltaY, out Transform closest)
    {
        closest = null;
        if (range <= 0f || maxAbsDeltaY < 0f)
            return false;

        float rangeSqr = range * range;
        float bestSqr = float.MaxValue;

        for (int i = _activeEnemies.Count - 1; i >= 0; i--)
        {
            Transform t = _activeEnemies[i];
            if (t == null)
            {
                _activeEnemies.RemoveAt(i);
                continue;
            }

            float dy = Mathf.Abs(t.position.y - from.y);
            if (dy > maxAbsDeltaY)
                continue;

            Vector3 delta = t.position - from;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr > rangeSqr || sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            closest = t;
        }

        return closest != null;
    }

    // Fills results with the closest active enemies inside a horizontal aim cone.
    public static int CollectClosestOnPlaneInCone(Vector3 from, Vector3 forward, float range, float coneAngle, int maxCount, List<Transform> results)
    {
        results.Clear();
        if (range <= 0f || maxCount <= 0)
            return 0;

        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float rangeSqr = range * range;
        float cosHalfAngle = Mathf.Cos(Mathf.Clamp(coneAngle, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
        bool useCone = coneAngle < 359.9f;

        while (results.Count < maxCount)
        {
            _excludeScratch.Clear();
            for (int r = 0; r < results.Count; r++)
                _excludeScratch.Add(results[r]);

            Transform best = null;
            float bestSqr = float.MaxValue;

            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                Transform candidate = _activeEnemies[i];
                if (candidate == null)
                {
                    _activeEnemies.RemoveAt(i);
                    continue;
                }

                if (_excludeScratch.Contains(candidate))
                    continue;

                Vector3 delta = candidate.position - from;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr <= 0.0001f || sqr > rangeSqr || sqr >= bestSqr)
                    continue;

                if (useCone && Vector3.Dot(forward, delta.normalized) < cosHalfAngle)
                    continue;

                best = candidate;
                bestSqr = sqr;
            }

            if (best == null)
                break;

            results.Add(best);
        }

        return results.Count;
    }

    // Fills results with the closest active enemies inside a horizontal radius.
    public static int CollectClosestOnPlane(Vector3 from, float range, int maxCount, List<Transform> results)
    {
        results.Clear();
        if (range <= 0f || maxCount <= 0)
            return 0;

        float rangeSqr = range * range;

        while (results.Count < maxCount)
        {
            _excludeScratch.Clear();
            for (int r = 0; r < results.Count; r++)
                _excludeScratch.Add(results[r]);

            Transform best = null;
            float bestSqr = float.MaxValue;

            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                Transform candidate = _activeEnemies[i];
                if (candidate == null)
                {
                    _activeEnemies.RemoveAt(i);
                    continue;
                }

                if (_excludeScratch.Contains(candidate))
                    continue;

                Vector3 delta = candidate.position - from;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr > rangeSqr || sqr >= bestSqr)
                    continue;

                best = candidate;
                bestSqr = sqr;
            }

            if (best == null)
                break;

            results.Add(best);
        }

        return results.Count;
    }

    // Fills results with the closest active enemies inside a full 3D cone.
    public static int CollectClosestInCone(Vector3 from, Vector3 forward, float range, float coneAngle, int maxCount, List<Transform> results)
    {
        results.Clear();
        if (range <= 0f || maxCount <= 0)
            return 0;

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float rangeSqr = range * range;
        float cosHalfAngle = Mathf.Cos(Mathf.Clamp(coneAngle, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
        bool useCone = coneAngle < 359.9f;

        while (results.Count < maxCount)
        {
            _excludeScratch.Clear();
            for (int r = 0; r < results.Count; r++)
                _excludeScratch.Add(results[r]);

            Transform best = null;
            float bestSqr = float.MaxValue;

            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                Transform candidate = _activeEnemies[i];
                if (candidate == null)
                {
                    _activeEnemies.RemoveAt(i);
                    continue;
                }

                if (_excludeScratch.Contains(candidate))
                    continue;

                Vector3 delta = candidate.position - from;
                float sqr = delta.sqrMagnitude;
                if (sqr <= 0.0001f || sqr > rangeSqr || sqr >= bestSqr)
                    continue;

                if (useCone && Vector3.Dot(forward, delta.normalized) < cosHalfAngle)
                    continue;

                best = candidate;
                bestSqr = sqr;
            }

            if (best == null)
                break;

            results.Add(best);
        }

        return results.Count;
    }

    // Fills results with active enemies within a radius of a world-space polyline.
    public static int CollectClosestNearPolyline(Vector3[] points, int pointCount, float radius, int maxCount, List<Transform> results, List<Vector3> closestPoints)
    {
        results.Clear();
        closestPoints?.Clear();

        if (points == null || pointCount <= 0 || radius <= 0f || maxCount <= 0)
            return 0;

        pointCount = Mathf.Min(pointCount, points.Length);
        if (pointCount <= 0)
            return 0;

        float radiusSqr = radius * radius;

        while (results.Count < maxCount)
        {
            _excludeScratch.Clear();
            for (int r = 0; r < results.Count; r++)
                _excludeScratch.Add(results[r]);

            Transform best = null;
            Vector3 bestClosestPoint = Vector3.zero;
            float bestDistanceSqr = float.MaxValue;

            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                Transform candidate = _activeEnemies[i];
                if (candidate == null)
                {
                    _activeEnemies.RemoveAt(i);
                    continue;
                }

                if (_excludeScratch.Contains(candidate))
                    continue;

                float distanceSqr = DistanceSqrToCandidate(candidate, points, pointCount, out Vector3 closestPoint);
                if (distanceSqr > radiusSqr || distanceSqr >= bestDistanceSqr)
                    continue;

                best = candidate;
                bestClosestPoint = closestPoint;
                bestDistanceSqr = distanceSqr;
            }

            if (best == null)
                break;

            results.Add(best);
            closestPoints?.Add(bestClosestPoint);
        }

        return results.Count;
    }

    private static float DistanceSqrToCandidate(Transform candidate, Vector3[] points, int pointCount, out Vector3 closestPoint)
    {
        float bestSqr = DistanceSqrToPolyline(candidate.position, points, pointCount, out closestPoint);

        _candidateColliders.Clear();
        candidate.GetComponentsInChildren(false, _candidateColliders);
        for (int i = 0; i < _candidateColliders.Count; i++)
        {
            Collider collider = _candidateColliders[i];
            if (collider == null || !collider.enabled)
                continue;

            Vector3 colliderAnchor = collider.bounds.center;
            DistanceSqrToPolyline(colliderAnchor, points, pointCount, out Vector3 linePoint);
            Vector3 colliderPoint = collider.ClosestPoint(linePoint);
            float sqr = (linePoint - colliderPoint).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            closestPoint = linePoint;
        }

        _candidateColliders.Clear();
        return bestSqr;
    }

    private static bool IsInsideHorizontalCone(Vector3 delta, Vector3 forward, float coneAngle)
    {
        if (coneAngle >= 359.9f)
            return true;

        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        if (delta.sqrMagnitude <= 0.0001f)
            return false;

        float cosHalfAngle = Mathf.Cos(Mathf.Clamp(coneAngle, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
        return Vector3.Dot(forward, delta.normalized) >= cosHalfAngle;
    }

    private static float DistanceSqrToPolyline(Vector3 point, Vector3[] points, int pointCount, out Vector3 closestPoint)
    {
        closestPoint = points[0];
        float bestSqr = (point - closestPoint).sqrMagnitude;

        for (int i = 1; i < pointCount; i++)
        {
            Vector3 segmentClosest = ClosestPointOnSegment(point, points[i - 1], points[i]);
            float sqr = (point - segmentClosest).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            closestPoint = segmentClosest;
        }

        return bestSqr;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float denominator = ab.sqrMagnitude;
        if (denominator <= 0.0001f)
            return a;

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denominator);
        return a + ab * t;
    }
}
