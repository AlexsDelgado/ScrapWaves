using UnityEngine;

/// <summary>A continuous worm flight, sampled by distance and extended along its ground-entry tangents.</summary>
public sealed class StalkerFlightPath
{
    private const int SamplesPerSpan = 128;
    private readonly float[] _distances = new float[SamplesPerSpan * 2 + 1];
    private Vector3 _origin, _riseControl, _beforeApex, _apex, _afterApex, _fallControl, _landing;
    private Vector3 _normal = Vector3.right;

    public float ApexDistance { get; private set; }
    public float Length { get; private set; }

    public void Configure(Vector3 origin, Vector3 apex, Vector3 landing, float bodyLength)
    {
        _origin = origin;
        _apex = apex;
        _landing = landing;
        Vector3 forward = Vector3.ProjectOnPlane(landing - origin, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
        _normal = Vector3.Cross(Vector3.up, forward).normalized;
        float rise = Mathf.Max(0.5f, apex.y - origin.y);
        float fall = Mathf.Max(0.5f, apex.y - landing.y);
        // A small outward overhang keeps a tall, narrow jump rounded at its crown.
        // Both spans meet with the same tangent, rather than switching steering targets.
        float handle = Mathf.Clamp(bodyLength * 0.45f, 2f, Mathf.Max(2f, Mathf.Min(rise, fall) * 0.55f));
        _riseControl = origin + Vector3.up * (rise * 0.7f) - forward * (handle * 0.25f);
        _beforeApex = apex - forward * handle;
        _afterApex = apex + forward * handle;
        _fallControl = landing + Vector3.up * (fall * 0.7f) + forward * (handle * 0.25f);
        _distances[0] = 0f;
        Vector3 previous = origin;
        for (int i = 1; i < _distances.Length; i++)
        {
            Vector3 current = PositionAtParameter((float)i / SamplesPerSpan);
            _distances[i] = _distances[i - 1] + Vector3.Distance(previous, current);
            previous = current;
        }
        ApexDistance = _distances[SamplesPerSpan];
        Length = _distances[_distances.Length - 1];
    }

    public Vector3 GetPosition(float distance)
    {
        if (distance <= 0f) return _origin + TangentAtParameter(0f) * distance;
        if (distance >= Length) return _landing + TangentAtParameter(2f) * (distance - Length);
        return PositionAtParameter(ParameterAtDistance(distance));
    }

    public Vector3 GetTangent(float distance) => TangentAtParameter(ParameterAtDistance(distance));

    public Quaternion GetRotation(float distance)
    {
        Vector3 up = GetTangent(distance);
        return Quaternion.LookRotation(Vector3.Cross(_normal, up), up);
    }

    private float ParameterAtDistance(float distance)
    {
        if (distance <= 0f) return 0f;
        if (distance >= Length) return 2f;
        int low = 0, high = _distances.Length - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (_distances[middle] < distance) low = middle;
            else high = middle;
        }
        float fraction = Mathf.InverseLerp(_distances[low], _distances[high], distance);
        return (low + fraction) / SamplesPerSpan;
    }

    private Vector3 PositionAtParameter(float t)
    {
        return t <= 1f ? Bezier(_origin, _riseControl, _beforeApex, _apex, t)
            : Bezier(_apex, _afterApex, _fallControl, _landing, t - 1f);
    }

    private Vector3 TangentAtParameter(float t)
    {
        Vector3 tangent = t <= 1f ? Derivative(_origin, _riseControl, _beforeApex, _apex, t)
            : Derivative(_apex, _afterApex, _fallControl, _landing, t - 1f);
        return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.up;
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float s = 1f - t;
        return s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d;
    }

    private static Vector3 Derivative(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float s = 1f - t;
        return 3f * (s * s * (b - a) + 2f * s * t * (c - b) + t * t * (d - c));
    }
}
