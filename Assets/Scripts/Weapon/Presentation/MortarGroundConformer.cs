using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drapes copies of authored marker meshes onto nearby ground. The source meshes,
/// target anchor, and coordinates in the target's tangent plane never change.
/// </summary>
public sealed class MortarGroundConformer : IDisposable
{
    private readonly struct Sample
    {
        public readonly bool Supported;
        public readonly Vector3 Point;
        public readonly int BoxFace;
        public Sample(bool supported, Vector3 point, int boxFace = 0)
        { Supported = supported; Point = point; BoxFace = boxFace; }
    }

    private sealed class Layer
    {
        public MeshFilter Filter;
        public Mesh Source, Copy;
        public Vector3[] Rest, Vertices, TangentPoints;
        public int[] Indices;
        public Sample[] Samples;
        public bool[] TopFaces;
        public readonly List<int> VisibleIndices = new();
        public int[] RetainedVertices, TriangleEdgeSlots;
        public Sample[] EdgeSamples;
        public bool[] EdgeSampled, RetainedVertexMask;
    }

    private readonly List<Layer> _layers = new();
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly Collider[] _nearby = new Collider[32];
    private readonly Dictionary<Transform, bool> _terrainValidity = new();
    private readonly bool _enableBoxFastPath;
    private BoxCollider _singleBox;
    private Vector3 _contact, _normal;
    private Transform _ignoredRoot;
    private float _offset, _probeDistance, _maximumDeviation;
#if UNITY_EDITOR
    public long RaycastCount { get; private set; }
    public long RaycastFallbackCount { get; private set; }
    public long BroadphaseCount { get; private set; }
    public long BoxFastPathCount { get; private set; }
    public long BoxRaycastCount { get; private set; }
    public long TriangleShortcutCount { get; private set; }
#endif

    public MortarGroundConformer(Renderer[] renderers, Transform blastRadiusLayer)
        : this(renderers, blastRadiusLayer, true) { }

    internal MortarGroundConformer(Renderer[] renderers, Transform blastRadiusLayer, bool enableBoxFastPath)
    {
        _enableBoxFastPath = enableBoxFastPath;
        if (renderers == null) return;
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null || !source.isReadable) continue;
            var layer = new Layer
            {
                Filter = filter,
                Source = source
            };
            // The wide blast annulus needs one extra sampling level to follow
            // small ground curvature at production radii. Refine once, retaining
            // its original vertices first; the small pulse and solid layers keep
            // their authored density and all shared assets remain unchanged.
            bool planar = source.bounds.size.y <= 0.0001f;
            if (planar && filter.transform == blastRadiusLayer)
                layer.Copy = SubdivideAnnulus(source);
            Mesh restMesh = layer.Copy != null ? layer.Copy : source;
            layer.Rest = restMesh.vertices;
            layer.Indices = restMesh.triangles;
            layer.Vertices = new Vector3[layer.Rest.Length];
            layer.TangentPoints = new Vector3[layer.Rest.Length];
            layer.Samples = new Sample[layer.Rest.Length];
            layer.TopFaces = new bool[layer.Indices.Length / 3];
            layer.VisibleIndices.Capacity = layer.Indices.Length;
            for (int t = 0; t < layer.TopFaces.Length; t++)
            {
                int at = t * 3;
                Vector3 a = layer.Rest[layer.Indices[at]], b = layer.Rest[layer.Indices[at + 1]], c = layer.Rest[layer.Indices[at + 2]];
                // Thin primitive crosses/cores become their authored top surface.
                // Keeping bottom faces too would double additive brightness after flattening.
                layer.TopFaces[t] = planar || Vector3.Cross(b - a, c - a).normalized.y > 0.2f;
            }
            CacheProjectionTopology(layer);
            _layers.Add(layer);
        }
    }

    private static void CacheProjectionTopology(Layer layer)
    {
        var retained = new bool[layer.Rest.Length];
        var vertices = new List<int>(layer.Rest.Length);
        var edgeSlots = new Dictionary<ulong, int>();
        layer.TriangleEdgeSlots = new int[layer.Indices.Length];
        for (int t = 0; t < layer.TopFaces.Length; t++)
        {
            if (!layer.TopFaces[t]) continue;
            int at = t * 3;
            for (int corner = 0; corner < 3; corner++)
            {
                int a = layer.Indices[at + corner];
                int b = layer.Indices[at + (corner + 1) % 3];
                retained[a] = true;
                ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (!edgeSlots.TryGetValue(key, out int slot))
                {
                    slot = edgeSlots.Count;
                    edgeSlots.Add(key, slot);
                }
                layer.TriangleEdgeSlots[at + corner] = slot;
            }
        }
        // Keep original vertex order, including the same probe ordering among
        // visible vertices. Culled primitive side/bottom vertices need no ray.
        for (int i = 0; i < retained.Length; i++)
            if (retained[i]) vertices.Add(i);
        layer.RetainedVertices = vertices.ToArray();
        layer.RetainedVertexMask = retained;
        layer.EdgeSamples = new Sample[edgeSlots.Count];
        layer.EdgeSampled = new bool[edgeSlots.Count];
    }

    private static Mesh SubdivideAnnulus(Mesh source)
    {
        Mesh copy = UnityEngine.Object.Instantiate(source);
        copy.name = source.name + " (ground marker instance)";
        copy.hideFlags = HideFlags.DontSave;
        copy.MarkDynamic();
        var vertices = new List<Vector3>(source.vertices);
        int originalCount = vertices.Count;
        var normals = new List<Vector3>(source.normals);
        var tangents = new List<Vector4>(source.tangents);
        var colors = new List<Color>(source.colors);
        var uvChannels = new List<Vector4>[8];
        for (int channel = 0; channel < uvChannels.Length; channel++)
        {
            var uv = new List<Vector4>();
            source.GetUVs(channel, uv);
            if (uv.Count == originalCount) uvChannels[channel] = uv;
        }
        bool hasNormals = normals.Count == originalCount;
        bool hasTangents = tangents.Count == originalCount;
        bool hasColors = colors.Count == originalCount;
        int[] sourceIndices = source.triangles;
        var indices = new List<int>(sourceIndices.Length * 4);
        var midpointIndices = new Dictionary<ulong, int>();

        int Midpoint(int a, int b)
        {
            ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            if (midpointIndices.TryGetValue(key, out int existing)) return existing;
            int index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            if (hasNormals) normals.Add((normals[a] + normals[b]).normalized);
            if (hasTangents) tangents.Add((tangents[a] + tangents[b]) * 0.5f);
            if (hasColors) colors.Add(Color.Lerp(colors[a], colors[b], 0.5f));
            foreach (List<Vector4> uv in uvChannels)
                if (uv != null) uv.Add((uv[a] + uv[b]) * 0.5f);
            midpointIndices.Add(key, index);
            return index;
        }

        for (int at = 0; at < sourceIndices.Length; at += 3)
        {
            int a = sourceIndices[at], b = sourceIndices[at + 1], c = sourceIndices[at + 2];
            int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
            indices.Add(a); indices.Add(ab); indices.Add(ca);
            indices.Add(ab); indices.Add(b); indices.Add(bc);
            indices.Add(ca); indices.Add(bc); indices.Add(c);
            indices.Add(ab); indices.Add(bc); indices.Add(ca);
        }
        copy.SetVertices(vertices);
        if (hasNormals) copy.SetNormals(normals);
        if (hasTangents) copy.SetTangents(tangents);
        if (hasColors) copy.SetColors(colors);
        for (int channel = 0; channel < uvChannels.Length; channel++)
            if (uvChannels[channel] != null) copy.SetUVs(channel, uvChannels[channel]);
        copy.SetTriangles(indices, 0, false);
        copy.RecalculateBounds();
        return copy;
    }

    public void Conform(Vector3 contact, Vector3 normal, Transform ignoredRoot,
        float surfaceOffset, float projectionDistance, float maximumDeviation)
    {
        _contact = contact;
        _normal = normal.normalized;
        _ignoredRoot = ignoredRoot;
        _offset = Mathf.Max(0.001f, surfaceOffset);
        _probeDistance = Mathf.Max(0.05f, projectionDistance);
        _maximumDeviation = Mathf.Max(0.001f, maximumDeviation);
        // Adjacent samples repeatedly hit the same terrain. Cache only for this
        // refresh so hierarchy/component changes are reflected on the next one.
        _terrainValidity.Clear();
        PrepareProjectionQueries();
        foreach (Layer layer in _layers)
        {
            if (layer.Filter == null || layer.Source == null) continue;
            if (layer.Copy == null)
            {
                layer.Copy = UnityEngine.Object.Instantiate(layer.Source);
                layer.Copy.name = layer.Source.name + " (ground marker instance)";
                layer.Copy.hideFlags = HideFlags.DontSave;
                layer.Copy.MarkDynamic();
            }
            Matrix4x4 toLocal = layer.Filter.transform.worldToLocalMatrix;
            for (int i = 0; i < layer.Rest.Length; i++)
            {
                // Unreferenced vertices remain at the tangent plane. Their
                // coordinates never participate in the retained triangles.
                if (!layer.RetainedVertexMask[i])
                    layer.Vertices[i] = toLocal.MultiplyPoint3x4(layer.TangentPoints[i] + _normal * _offset);
            }
            foreach (int i in layer.RetainedVertices)
            {
                Vector3 tangentPoint = layer.TangentPoints[i];
                Sample sample = SampleGround(tangentPoint);
                layer.Samples[i] = sample;
                layer.Vertices[i] = toLocal.MultiplyPoint3x4(
                    (sample.Supported ? sample.Point : tangentPoint) + _normal * _offset);
            }
            layer.VisibleIndices.Clear();
            Array.Clear(layer.EdgeSampled, 0, layer.EdgeSampled.Length);
            for (int t = 0; t < layer.TopFaces.Length; t++)
            {
                if (!layer.TopFaces[t]) continue;
                int at = t * 3;
                int a = layer.Indices[at], b = layer.Indices[at + 1], c = layer.Indices[at + 2];
                if (!layer.Samples[a].Supported || !layer.Samples[b].Supported || !layer.Samples[c].Supported)
                    continue;
                if (_singleBox != null && layer.Samples[a].BoxFace != 0
                    && layer.Samples[a].BoxFace == layer.Samples[b].BoxFace
                    && layer.Samples[a].BoxFace == layer.Samples[c].BoxFace)
                {
                    // A finite box face is convex and planar. All three hits
                    // on that face prove its edges and interior are supported.
#if UNITY_EDITOR
                    TriangleShortcutCount++;
#endif
                }
                else
                {
                    if (!EdgeIsSupported(layer, a, b, layer.TriangleEdgeSlots[at])
                        || !EdgeIsSupported(layer, b, c, layer.TriangleEdgeSlots[at + 1])
                        || !EdgeIsSupported(layer, c, a, layer.TriangleEdgeSlots[at + 2]))
                        continue;
                    Sample center = SampleGround((layer.TangentPoints[a] + layer.TangentPoints[b] + layer.TangentPoints[c]) / 3f);
                    Vector3 interpolatedCenter = (layer.Samples[a].Point + layer.Samples[b].Point + layer.Samples[c].Point) / 3f;
                    if (!MatchesSurface(center, interpolatedCenter)) continue;
                }
                layer.VisibleIndices.Add(a);
                layer.VisibleIndices.Add(b);
                layer.VisibleIndices.Add(c);
            }
            layer.Copy.vertices = layer.Vertices;
            layer.Copy.SetTriangles(layer.VisibleIndices, 0, false);
            layer.Copy.RecalculateNormals();
            layer.Copy.RecalculateBounds();
            layer.Filter.sharedMesh = layer.Copy;
        }
    }

    private void PrepareProjectionQueries()
    {
        _singleBox = null;
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool hasVertices = false;
        foreach (Layer layer in _layers)
        {
            if (layer.Filter == null || layer.Source == null) continue;
            Matrix4x4 toWorld = layer.Filter.transform.localToWorldMatrix;
            for (int i = 0; i < layer.Rest.Length; i++)
            {
                Vector3 restWorld = toWorld.MultiplyPoint3x4(layer.Rest[i]);
                Vector3 tangentPoint = _contact + Vector3.ProjectOnPlane(restWorld - _contact, _normal);
                layer.TangentPoints[i] = tangentPoint;
                minimum = Vector3.Min(minimum, tangentPoint);
                maximum = Vector3.Max(maximum, tangentPoint);
                hasVertices = true;
            }
        }
        if (!_enableBoxFastPath || !hasVertices) return;
        // Every edge/centroid probe lies in the vertex convex hull. Expanding
        // its AABB by the full ray reach contains every sample ray, including
        // tilted contacts, animated layers and transformed parents.
        Vector3 reach = new Vector3(Mathf.Abs(_normal.x), Mathf.Abs(_normal.y), Mathf.Abs(_normal.z)) * _probeDistance;
        Vector3 center = (minimum + maximum) * .5f;
        Vector3 extents = (maximum - minimum) * .5f + reach + Vector3.one * .002f;
#if UNITY_EDITOR
        BroadphaseCount++;
#endif
        int count = Physics.OverlapBoxNonAlloc(center, extents, _nearby, Quaternion.identity,
            ~0, QueryTriggerInteraction.Ignore);
        // An incomplete candidate set cannot prove the single-collider case.
        if (count == _nearby.Length) return;
        Collider only = null;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = _nearby[i];
            if (candidate == null || !IsValidTerrain(candidate.transform)
                || (candidate.attachedRigidbody != null && !IsValidTerrain(candidate.attachedRigidbody.transform)))
                continue;
            if (only != null) return;
            only = candidate;
        }
        _singleBox = only as BoxCollider;
#if UNITY_EDITOR
        if (_singleBox != null) BoxFastPathCount++;
#endif
    }

    private bool EdgeIsSupported(Layer layer, int a, int b, int slot)
    {
        if (!layer.EdgeSampled[slot])
        {
            layer.EdgeSamples[slot] = SampleGround((layer.TangentPoints[a] + layer.TangentPoints[b]) * 0.5f);
            layer.EdgeSampled[slot] = true;
        }
        Sample middle = layer.EdgeSamples[slot];
        return MatchesSurface(middle, (layer.Samples[a].Point + layer.Samples[b].Point) * 0.5f);
    }

    private bool MatchesSurface(Sample sample, Vector3 interpolatedPoint)
    {
        return sample.Supported && Vector3.Distance(sample.Point, interpolatedPoint) <= _maximumDeviation;
    }

    private Sample SampleGround(Vector3 tangentPoint)
    {
        Vector3 origin = tangentPoint + _normal * _probeDistance;
#if UNITY_EDITOR
        RaycastCount++;
#endif
        if (_singleBox != null)
        {
#if UNITY_EDITOR
            BoxRaycastCount++;
#endif
            if (!_singleBox.Raycast(new Ray(origin, -_normal), out RaycastHit hit, _probeDistance * 2f)
                || hit.normal.y < .2f || Vector3.Dot(hit.normal, _normal) < .2f)
                return new Sample(false, default);
            Vector3 faceNormal = _singleBox.transform.InverseTransformDirection(hit.normal);
            int face = Mathf.Abs(faceNormal.x) > .999f ? (faceNormal.x > 0f ? 1 : 2)
                : Mathf.Abs(faceNormal.y) > .999f ? (faceNormal.y > 0f ? 3 : 4)
                : Mathf.Abs(faceNormal.z) > .999f ? (faceNormal.z > 0f ? 5 : 6) : 0;
            return new Sample(true, hit.point, face);
        }
        int count = Physics.RaycastNonAlloc(origin, -_normal, _hits,
            _probeDistance * 2f, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] candidates = _hits;
        if (count == _hits.Length)
        {
#if UNITY_EDITOR
            RaycastFallbackCount++;
#endif
            candidates = Physics.RaycastAll(origin, -_normal, _probeDistance * 2f,
                ~0, QueryTriggerInteraction.Ignore);
            count = candidates.Length;
        }
        float closest = float.PositiveInfinity;
        Vector3 point = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = candidates[i];
            if (hit.normal.y < 0.2f || Vector3.Dot(hit.normal, _normal) < 0.2f
                || !IsValidTerrain(hit.transform)
                || (hit.rigidbody != null && !IsValidTerrain(hit.rigidbody.transform)))
                continue;
            float distance = Mathf.Abs(Vector3.Dot(hit.point - tangentPoint, _normal));
            if (distance >= closest) continue;
            closest = distance;
            point = hit.point;
        }
        return new Sample(!float.IsPositiveInfinity(closest), point);
    }

    private bool IsValidTerrain(Transform candidate)
    {
        if (candidate == null) return false;
        if (_terrainValidity.TryGetValue(candidate, out bool valid)) return valid;
        valid = ReticleAimProvider.IsValidMortarTerrainTransform(candidate, _ignoredRoot);
        _terrainValidity.Add(candidate, valid);
        return valid;
    }

    public void Restore()
    {
        foreach (Layer layer in _layers)
            if (layer.Filter != null && layer.Filter.sharedMesh == layer.Copy)
                layer.Filter.sharedMesh = layer.Source;
    }

    public void Dispose()
    {
        Restore();
        foreach (Layer layer in _layers)
        {
            if (layer.Copy == null) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(layer.Copy);
            else UnityEngine.Object.DestroyImmediate(layer.Copy);
            layer.Copy = null;
        }
        _layers.Clear();
    }
}
