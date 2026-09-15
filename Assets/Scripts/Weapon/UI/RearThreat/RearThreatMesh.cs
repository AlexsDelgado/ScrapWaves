using UnityEngine;

/// <summary>Reusable radial strip, with spike tips inserted into its continuous outer edge.</summary>
public sealed class RearThreatMesh
{
    public const int MaximumSpikes = 5;
    public const int ArcSegments = 128;
    public const int MaximumSamples = ArcSegments + 1 + MaximumSpikes * 3;
    private readonly float[] _angles = new float[MaximumSamples];
    private readonly Vector3[] _fillVertices = new Vector3[MaximumSamples * 2];
    private readonly Vector3[] _outlineVertices = new Vector3[MaximumSamples * 4 + 8];
    private readonly int[] _fillIndices = new int[(MaximumSamples - 1) * 6];
    private readonly int[] _outlineIndices = new int[(MaximumSamples - 1) * 12 + 12];

    public RearThreatMesh()
    {
        for (int i = 0; i < MaximumSamples - 1; i++)
        {
            Quad(_fillIndices, i * 6, i * 2, i * 2 + 1, i * 2 + 2, i * 2 + 3);
            Quad(_outlineIndices, i * 12, i * 4, i * 4 + 1, i * 4 + 4, i * 4 + 5);
            Quad(_outlineIndices, i * 12 + 6, i * 4 + 2, i * 4 + 3, i * 4 + 6, i * 4 + 7);
        }
    }

    // Angles are degrees relative to backward. Lengths are already smoothed world units.
    public void Update(Mesh fill, Mesh outline, float innerRadius, float outerRadius,
        float halfArc, float spikeWidth, float outlineWidth, float maximumExtent,
        float[] spikeAngles, float[] spikeLengths, float localFlex = 0f,
        float idleOuterRadius = 0f, float shoulderWidth = 44f, float maximumSpikeLength = 0.65f)
    {
        int count = 0;
        for (int i = 0; i <= ArcSegments; i++)
            _angles[count++] = Mathf.Lerp(-halfArc, halfArc, i / (float)ArcSegments);
        float halfWidth = Mathf.Max(0.1f, spikeWidth * 0.5f);
        for (int i = 0; i < MaximumSpikes; i++)
        {
            if (spikeLengths[i] <= 0f || Mathf.Abs(spikeAngles[i]) > halfArc) continue;
            _angles[count++] = Mathf.Clamp(spikeAngles[i] - halfWidth, -halfArc, halfArc);
            _angles[count++] = spikeAngles[i];
            _angles[count++] = Mathf.Clamp(spikeAngles[i] + halfWidth, -halfArc, halfArc);
        }
        // Tiny bounded buffer; insertion sorting avoids comparer/delegate allocation.
        for (int i = 1; i < count; i++)
        {
            float angle = _angles[i];
            int j = i - 1;
            while (j >= 0 && _angles[j] > angle) { _angles[j + 1] = _angles[j]; j--; }
            _angles[j + 1] = angle;
        }
        int uniqueCount = 1;
        for (int i = 1; i < count; i++)
            if (_angles[i] - _angles[uniqueCount - 1] > 0.0001f) _angles[uniqueCount++] = _angles[i];
        count = uniqueCount;

        float flex = Mathf.Clamp01(localFlex);
        float idleRadius = idleOuterRadius > 0f ? Mathf.Clamp(idleOuterRadius, innerRadius, outerRadius) : outerRadius;
        float localizedGrowth = Mathf.Max(0f, outerRadius - idleRadius) * flex;
        float shoulderHalfWidth = Mathf.Max(halfWidth, shoulderWidth * 0.5f);
        float referenceLength = Mathf.Max(0.0001f, maximumSpikeLength);
        float stroke = Mathf.Min(outlineWidth, (outerRadius - innerRadius) * 0.49f);
        for (int i = 0; i < count; i++)
        {
            if (i < count - 1)
            {
                Quad(_outlineIndices, i * 12, i * 4, i * 4 + 1, i * 4 + 4, i * 4 + 5);
                Quad(_outlineIndices, i * 12 + 6, i * 4 + 2, i * 4 + 3, i * 4 + 6, i * 4 + 7);
            }
            float length = 0f;
            float uncoveredShoulder = 1f;
            for (int s = 0; s < MaximumSpikes; s++)
            {
                if (spikeLengths[s] <= 0f || Mathf.Abs(spikeAngles[s]) > halfArc) continue;
                float difference = halfArc >= 180f ? Mathf.Abs(Mathf.DeltaAngle(_angles[i], spikeAngles[s])) : Mathf.Abs(_angles[i] - spikeAngles[s]);
                float weight = Mathf.Clamp01(1f - difference / halfWidth);
                if (flex > 0f)
                {
                    float strength = Mathf.Clamp01(spikeLengths[s] / referenceLength);
                    float shoulder = Mathf.Clamp01(1f - difference / shoulderHalfWidth);
                    shoulder = SmoothStep(shoulder);
                    // A bounded union joins nearby shoulders without adding their heights.
                    uncoveredShoulder *= 1f - strength * shoulder;
                    // Short spikes rise as rounded bulges; mature tips sharpen while their bases stay smooth.
                    weight = Mathf.Lerp(shoulder, weight * weight, SmoothStep(strength));
                }
                length = Mathf.Max(length, spikeLengths[s] * weight);
            }
            // Reuse the existing growth budget: unthreatened portions retain only its global share.
            float outer = outerRadius - localizedGrowth * uncoveredShoulder + length;
            float localStroke = Mathf.Min(stroke, (outer - innerRadius) * 0.49f);
            Vector3 radial = Direction(_angles[i]);
            _fillVertices[i * 2] = radial * innerRadius;
            _fillVertices[i * 2 + 1] = radial * outer;
            _outlineVertices[i * 4] = radial * innerRadius;
            _outlineVertices[i * 4 + 1] = radial * (innerRadius + localStroke);
            _outlineVertices[i * 4 + 2] = radial * (outer - localStroke);
            _outlineVertices[i * 4 + 3] = radial * outer;
        }
        // End caps close the outline without extending into the player's clearance.
        int cap = count * 4;
        float capAngle = Mathf.Min(halfArc, stroke / innerRadius * Mathf.Rad2Deg);
        AddCap(cap, -halfArc, -halfArc + capAngle, innerRadius, _fillVertices[1].magnitude);
        AddCap(cap + 4, halfArc - capAngle, halfArc, innerRadius, _fillVertices[(count - 1) * 2 + 1].magnitude);
        int outlineIndexCount = (count - 1) * 12;
        Quad(_outlineIndices, outlineIndexCount, cap, cap + 1, cap + 2, cap + 3);
        Quad(_outlineIndices, outlineIndexCount + 6, cap + 4, cap + 5, cap + 6, cap + 7);
        fill.Clear(false);
        fill.SetVertices(_fillVertices, 0, count * 2);
        fill.SetTriangles(_fillIndices, 0, (count - 1) * 6, 0, false);
        outline.Clear(false);
        bool closedCircle = halfArc >= 180f;
        outline.SetVertices(_outlineVertices, 0, cap + (closedCircle ? 0 : 8));
        outline.SetTriangles(_outlineIndices, 0, outlineIndexCount + (closedCircle ? 0 : 12), 0, false);
        var bounds = new Bounds(Vector3.zero, new Vector3(maximumExtent * 2f, 0.1f, maximumExtent * 2f));
        fill.bounds = bounds;
        outline.bounds = bounds;
    }

    private static float SmoothStep(float value) => value * value * (3f - 2f * value);

    private void AddCap(int start, float from, float to, float inner, float outer)
    {
        _outlineVertices[start] = Direction(from) * inner;
        _outlineVertices[start + 1] = Direction(from) * outer;
        _outlineVertices[start + 2] = Direction(to) * inner;
        _outlineVertices[start + 3] = Direction(to) * outer;
    }

    public static Vector3 Direction(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(radians), 0f, -Mathf.Cos(radians));
    }

    private static void Quad(int[] indices, int start, int a, int b, int c, int d)
    {
        indices[start] = a; indices[start + 1] = c; indices[start + 2] = b;
        indices[start + 3] = b; indices[start + 4] = c; indices[start + 5] = d;
    }
}
