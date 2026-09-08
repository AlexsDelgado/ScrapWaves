using UnityEngine;

/// <summary>A vertical pipe exit followed by a smooth bend toward the acquired aim point.</summary>
public sealed class AutomaticRocketTrajectory
{
    private const int CurveSegments = 48;
    private readonly float[] _curveDistances = new float[CurveSegments + 1];
    private Vector3 _start;
    private Vector3 _bendStart;
    private Vector3 _control;
    private Vector3 _target;
    private float _riseDistance;

    public float TotalLength { get; private set; }
    public Vector3 Target => _target;
    public float ClearanceHeight => _bendStart.y;

    public void Configure(Vector3 start, Vector3 target, float clearanceHeight)
    {
        _start = start;
        _target = target;
        _bendStart = new Vector3(start.x, Mathf.Max(start.y + 0.6f, clearanceHeight), start.z);
        _riseDistance = _bendStart.y - start.y;
        // The shared upward tangent keeps the transition out of the vertical pipe smooth.
        float bendRise = Mathf.Clamp(Vector3.Distance(_bendStart, target) * 0.2f, 0.75f, 3f);
        _control = _bendStart + Vector3.up * bendRise;

        _curveDistances[0] = 0f;
        Vector3 previous = _bendStart;
        for (int i = 1; i <= CurveSegments; i++)
        {
            Vector3 point = EvaluateCurve(i / (float)CurveSegments);
            _curveDistances[i] = _curveDistances[i - 1] + Vector3.Distance(previous, point);
            previous = point;
        }
        TotalLength = _riseDistance + _curveDistances[CurveSegments];
    }

    public Vector3 EvaluatePosition(float distance)
    {
        distance = Mathf.Max(0f, distance);
        if (distance <= _riseDistance)
            return _start + Vector3.up * distance;
        if (distance >= TotalLength)
            return _target + GetFinalDirection() * (distance - TotalLength);
        return EvaluateCurve(GetCurveTime(distance - _riseDistance));
    }

    public Vector3 EvaluateDirection(float distance)
    {
        if (distance <= _riseDistance)
            return Vector3.up;
        if (distance >= TotalLength)
            return GetFinalDirection();
        float t = GetCurveTime(distance - _riseDistance);
        return ((1f - t) * (_control - _bendStart) + t * (_target - _control)).normalized;
    }

    private Vector3 EvaluateCurve(float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * _bendStart + 2f * inverse * t * _control + t * t * _target;
    }

    private float GetCurveTime(float distance)
    {
        int low = 1;
        int high = CurveSegments;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_curveDistances[middle] < distance)
                low = middle + 1;
            else
                high = middle;
        }
        float fraction = Mathf.InverseLerp(_curveDistances[low - 1], _curveDistances[low], distance);
        return (low - 1 + fraction) / CurveSegments;
    }

    private Vector3 GetFinalDirection()
    {
        Vector3 direction = _target - _control;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.down;
    }
}
