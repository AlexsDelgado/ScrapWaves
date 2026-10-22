using System.Collections.Generic;
using UnityEngine;

/// <summary>Rigid links feeding successively through the head's recorded world-space route.</summary>
public sealed class StalkerBodyChain
{
    private readonly float[] _lengths;
    private readonly Vector3[] _points;
    private readonly Quaternion[] _rotations;
    private readonly List<Vector3> _trail = new();
    private readonly float _totalLength;
    private readonly float _sampleSpacing;
    private readonly int _capacity;
    private Vector3 _previousSocket;
    private Quaternion _previousHeadRotation;
    private float _distanceSinceSample;

    public StalkerBodyChain(float[] linkLengths)
    {
        _lengths = (float[])linkLengths.Clone();
        _points = new Vector3[_lengths.Length + 1];
        _rotations = new Quaternion[_lengths.Length];
        float minimumLength = float.PositiveInfinity;
        for (int i = 0; i < _lengths.Length; i++)
        {
            _lengths[i] = Mathf.Max(0.001f, _lengths[i]);
            _totalLength += _lengths[i];
            minimumLength = Mathf.Min(minimumLength, _lengths[i]);
        }
        _sampleSpacing = Mathf.Max(0.001f, minimumLength * 0.025f);
        _capacity = Mathf.CeilToInt(_totalLength * 3f / _sampleSpacing) + 8;
        // Reserve the largest non-teleport append before trimming, including a hitch.
        _trail.Capacity = _capacity + Mathf.CeilToInt(_totalLength * 2f / _sampleSpacing) + 2;
    }

    public Quaternion GetRotation(int link) => _rotations[link];
    public Vector3 GetPosition(int joint) => _points[joint];

    public void Reset(Vector3 socket, Quaternion headRotation)
    {
        _previousSocket = socket;
        _previousHeadRotation = headRotation;
        _distanceSinceSample = 0f;
        _trail.Clear();
        Vector3 direction = headRotation * Vector3.down;
        int seedCount = Mathf.CeilToInt(_totalLength * 2f / _sampleSpacing);
        for (int i = seedCount; i >= 0; i--)
            _trail.Add(socket + direction * (i * _sampleSpacing));
        _points[0] = socket;
        for (int i = 0; i < _lengths.Length; i++)
        {
            _points[i + 1] = _points[i] + direction * _lengths[i];
            _rotations[i] = headRotation;
        }
    }

    public void Step(Vector3 socket, Quaternion headRotation, float maximumBendDegrees)
    {
        float travel = Vector3.Distance(_previousSocket, socket);
        if (travel < 0.000001f && Quaternion.Angle(_previousHeadRotation, headRotation) < 0.0001f) return;
        // Only a teleport reseeds the route. Phase/speed changes retain the same trail.
        if (travel > _totalLength * 2f || _trail.Count < 2)
        {
            Reset(socket, headRotation);
            return;
        }
        RecordTravel(socket, travel);
        _points[0] = socket;
        int cursor = _trail.Count - 1;
        Vector3 cursorPoint = socket;
        Quaternion frame = headRotation;
        Vector3 precedingDirection = frame * Vector3.down;
        float bend = Mathf.Clamp(maximumBendDegrees, 0f, 75f);
        float minimumDot = Mathf.Cos(bend * Mathf.Deg2Rad);
        for (int i = 0; i < _lengths.Length; i++)
        {
            // A sphere/polyline intersection keeps Euclidean link length exact. Equal time
            // delays would stretch at speed changes; equal arc offsets would shorten chords.
            Vector3 target = PreviousPointAtLength(_points[i], _lengths[i], precedingDirection, ref cursor, ref cursorPoint);
            Vector3 desired = (target - _points[i]).normalized;
            if (desired.sqrMagnitude < 0.5f) desired = precedingDirection;
            if (Vector3.Dot(precedingDirection, desired) < minimumDot)
            {
                Vector3 axis = Vector3.Cross(precedingDirection, desired);
                if (axis.sqrMagnitude < 0.00000001f) axis = frame * Vector3.right;
                desired = Quaternion.AngleAxis(bend, axis.normalized) * precedingDirection;
            }
            _points[i + 1] = _points[i] + desired * _lengths[i];
            frame = Quaternion.FromToRotation(precedingDirection, desired) * frame;
            _rotations[i] = frame;
            precedingDirection = desired;
        }
        _previousSocket = socket;
        _previousHeadRotation = headRotation;
    }

    private void RecordTravel(Vector3 socket, float travel)
    {
        if (travel <= 0.000001f) return;
        Vector3 direction = (socket - _previousSocket) / travel;
        float next = _sampleSpacing - _distanceSinceSample;
        while (next <= travel)
        {
            _trail.Add(_previousSocket + direction * next);
            next += _sampleSpacing;
        }
        _distanceSinceSample = travel - (next - _sampleSpacing);
        if (_trail.Count > _capacity) _trail.RemoveRange(0, _trail.Count - _capacity);
    }

    private Vector3 PreviousPointAtLength(Vector3 center, float length, Vector3 fallbackDirection,
        ref int cursor, ref Vector3 cursorPoint)
    {
        float squaredLength = length * length;
        while (cursor >= 0)
        {
            Vector3 end = _trail[cursor];
            Vector3 edge = end - cursorPoint;
            float a = edge.sqrMagnitude;
            if (a > 0.00000001f)
            {
                Vector3 offset = cursorPoint - center;
                float b = Vector3.Dot(offset, edge);
                float discriminant = b * b - a * (offset.sqrMagnitude - squaredLength);
                if (discriminant >= 0f)
                {
                    // A clamped parent can put this search outside its sphere. In that
                    // case take the entering intersection, not the later exit crossing.
                    float root = Mathf.Sqrt(discriminant);
                    float enter = (-b - root) / a;
                    float leave = (-b + root) / a;
                    float fraction = enter >= 0f ? enter : leave;
                    if (fraction >= 0f && fraction <= 1.0001f)
                    {
                        cursorPoint += edge * Mathf.Clamp01(fraction);
                        return cursorPoint;
                    }
                }
            }
            cursorPoint = end;
            cursor--;
        }
        // Conservative fallback for an unusually tight/reversing external trajectory.
        cursorPoint = center + fallbackDirection * length;
        return cursorPoint;
    }
}
