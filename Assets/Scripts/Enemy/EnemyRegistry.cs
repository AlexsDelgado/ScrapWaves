using System.Collections.Generic;
using UnityEngine;

public static class EnemyRegistry
{
    private static readonly List<Transform> _activeEnemies = new List<Transform>(256);

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

                if (results.Contains(candidate))
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
}
