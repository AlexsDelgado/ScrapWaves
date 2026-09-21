using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps rigid wearables on the deforming player surface. The animation driver
/// evaluates these anchors after the final aimed/recoiling pose and before firing.
/// Only the authored anchor influences are sampled; the full mesh is never baked.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerWearableSurfaceFollower : MonoBehaviour
{
    [Serializable]
    public sealed class WeightedBonePoint
    {
        public Transform Bone;
        public Vector3 LocalPoint;
        public float Weight;
    }

    [Serializable]
    public sealed class SurfaceAnchor
    {
        public WeightedBonePoint[] Points = Array.Empty<WeightedBonePoint>();

        public bool TryEvaluate(out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            if (Points == null || Points.Length == 0) return false;
            float totalWeight = 0f;
            foreach (WeightedBonePoint point in Points)
            {
                if (point == null || point.Bone == null || !IsFinite(point.LocalPoint)
                    || !IsFinite(point.Weight) || point.Weight < 0f) return false;
                if (point.Weight == 0f) continue;
                worldPoint += point.Bone.TransformPoint(point.LocalPoint) * point.Weight;
                totalWeight += point.Weight;
            }
            if (!IsFinite(totalWeight) || totalWeight <= 0f || !IsFinite(worldPoint)) return false;
            worldPoint /= totalWeight;
            return IsFinite(worldPoint);
        }
    }

    [Serializable]
    public sealed class SurfaceAttachment
    {
        public WeaponType Type;
        public Transform Socket;
        public SurfaceAnchor A = new();
        public SurfaceAnchor B = new();
        public SurfaceAnchor C = new();
        [Tooltip("Socket position relative to the bind surface frame, in player-root units.")]
        public Vector3 FrameLocalPosition;
        [Tooltip("Socket rotation relative to the bind surface frame.")]
        public Quaternion FrameLocalRotation = Quaternion.identity;
        [Tooltip("Optional exact surface contact for a seated mount; independent of its stabilised rotation frame.")]
        public SurfaceAnchor Seat = new();
        [Tooltip("Contact offset in socket orientation, measured in player-root units (includes authored socket scale).")]
        public Vector3 SocketLocalSeatPoint;
        [Tooltip("Nearby body surface points used to retain the bind-pose contact depth as the skin bends.")]
        public SurfaceAnchor[] ContactProbes = Array.Empty<SurfaceAnchor>();
        [Tooltip("Contact frame relative to the socket. Its Z axis points outward from the body.")]
        public Quaternion ContactLocalRotation = Quaternion.identity;
        public Vector2 ContactMin;
        public Vector2 ContactMax;
        public float ContactInnerZ;
        public float BindContactDepth;
        [Min(0f)] public float MaximumContactCorrection = .08f;
    }

    [SerializeField] private SurfaceAttachment[] _attachments = Array.Empty<SurfaceAttachment>();

    public IReadOnlyList<SurfaceAttachment> Attachments => _attachments;

    public void Configure(SurfaceAttachment[] attachments) =>
        _attachments = attachments ?? Array.Empty<SurfaceAttachment>();

    /// <summary>
    /// Reconstructs absolute poses, so repeated evaluation never accumulates offsets.
    /// The frame is measured in this component's space: root scale scales the entire
    /// character, while local skin stretching does not deform the rigid artifacts.
    /// </summary>
    public void EvaluateAttachments()
    {
        if (_attachments == null) return;
        foreach (SurfaceAttachment attachment in _attachments)
        {
            if (attachment == null || attachment.Socket == null || attachment.A == null
                || attachment.B == null || attachment.C == null
                || !IsFinite(attachment.FrameLocalPosition) || !IsUsable(attachment.FrameLocalRotation)
                || !attachment.A.TryEvaluate(out Vector3 a)
                || !attachment.B.TryEvaluate(out Vector3 b)
                || !attachment.C.TryEvaluate(out Vector3 c)) continue;

            a = transform.InverseTransformPoint(a);
            b = transform.InverseTransformPoint(b);
            c = transform.InverseTransformPoint(c);
            if (!TryGetFrame(a, b, c, out Vector3 position, out Quaternion rotation)) continue;

            Vector3 socketPosition = position + rotation * attachment.FrameLocalPosition;
            Quaternion socketRotation = rotation * attachment.FrameLocalRotation;
            Vector3 seat = Vector3.zero;
            bool seated = attachment.Seat != null && IsFinite(attachment.SocketLocalSeatPoint)
                && attachment.Seat.TryEvaluate(out seat);
            if (seated)
                socketPosition = transform.InverseTransformPoint(seat) - socketRotation * attachment.SocketLocalSeatPoint;
            if (!seated && IsFinite(attachment.BindContactDepth) && IsFinite(attachment.MaximumContactCorrection)
                && attachment.MaximumContactCorrection > 0f && IsUsable(attachment.ContactLocalRotation))
            {
                float depth = MeasureContactDepth(socketPosition, socketRotation, attachment, transform);
                float correction = Mathf.Clamp(depth - attachment.BindContactDepth, 0f, attachment.MaximumContactCorrection);
                socketPosition += socketRotation * attachment.ContactLocalRotation * Vector3.forward * correction;
            }
            Vector3 worldPosition = transform.TransformPoint(socketPosition);
            Quaternion worldRotation = transform.rotation * socketRotation;
            if (!IsFinite(worldPosition) || !IsUsable(worldRotation)) continue;
            attachment.Socket.SetPositionAndRotation(worldPosition, worldRotation);
            // SetPositionAndRotation intentionally retains the authored local scale.
        }
    }

    /// <summary>
    /// Measures nearby skin beyond the artifact's inner contact plane. A narrow
    /// footprint fade avoids jumps as probes pass an edge. Builder calibration uses
    /// this same measurement, preserving the approved contact depth at rest.
    /// </summary>
    public static float MeasureContactDepth(Vector3 socketPosRoot, Quaternion socketRotRoot,
        SurfaceAttachment attachment, Transform root)
    {
        if (attachment == null || root == null || attachment.ContactProbes == null
            || attachment.ContactProbes.Length == 0 || !IsFinite(socketPosRoot) || !IsUsable(socketRotRoot)
            || !IsUsable(attachment.ContactLocalRotation) || !IsFinite(attachment.ContactInnerZ)
            || !IsFinite(attachment.ContactMin.x) || !IsFinite(attachment.ContactMin.y)
            || !IsFinite(attachment.ContactMax.x) || !IsFinite(attachment.ContactMax.y)
            || attachment.ContactMax.x < attachment.ContactMin.x
            || attachment.ContactMax.y < attachment.ContactMin.y) return 0f;

        const float edgeFade = .015f;
        Quaternion inverseContact = Quaternion.Inverse(socketRotRoot * attachment.ContactLocalRotation);
        float depth = 0f;
        foreach (SurfaceAnchor probe in attachment.ContactProbes)
        {
            if (probe == null || !probe.TryEvaluate(out Vector3 worldPoint)) continue;
            Vector3 point = inverseContact * (root.InverseTransformPoint(worldPoint) - socketPosRoot);
            float penetration = point.z - attachment.ContactInnerZ;
            // Remote surfaces on the other side of a limb or torso must not drive
            // the local mounting contact, even when their projected footprint overlaps.
            if (!IsFinite(point) || penetration <= 0f || penetration > .20f) continue;
            float xFade = Mathf.Clamp01((point.x - attachment.ContactMin.x + edgeFade) / edgeFade)
                * Mathf.Clamp01((attachment.ContactMax.x + edgeFade - point.x) / edgeFade);
            float yFade = Mathf.Clamp01((point.y - attachment.ContactMin.y + edgeFade) / edgeFade)
                * Mathf.Clamp01((attachment.ContactMax.y + edgeFade - point.y) / edgeFade);
            depth = Mathf.Max(depth, penetration * xFade * yFade);
        }
        return depth;
    }

    /// <summary>Frame origin is A, X follows A→B, and Z is the triangle normal.</summary>
    public static bool TryGetFrame(Vector3 a, Vector3 b, Vector3 c,
        out Vector3 position, out Quaternion rotation)
    {
        position = a;
        rotation = Quaternion.identity;
        if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)) return false;
        Vector3 tangent = b - a;
        Vector3 across = c - a;
        float tangentSquared = tangent.sqrMagnitude;
        float acrossSquared = across.sqrMagnitude;
        if (tangentSquared <= 1e-12f || acrossSquared <= 1e-12f) return false;
        Vector3 normal = Vector3.Cross(tangent, across);
        float normalSquared = normal.sqrMagnitude;
        if (!IsFinite(normalSquared) || normalSquared <= tangentSquared * acrossSquared * 1e-8f) return false;
        tangent /= Mathf.Sqrt(tangentSquared);
        normal /= Mathf.Sqrt(normalSquared);
        rotation = Quaternion.LookRotation(normal, Vector3.Cross(normal, tangent));
        return IsUsable(rotation);
    }

    private static bool IsUsable(Quaternion value) =>
        IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 1e-12f;

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
