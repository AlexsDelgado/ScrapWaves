using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Attachment = PlayerWearableSurfaceFollower.SurfaceAttachment;
using Anchor = PlayerWearableSurfaceFollower.SurfaceAnchor;
using Point = PlayerWearableSurfaceFollower.WeightedBonePoint;

/// <summary>Calibrates skin-following attachments and seats the cannon's mounting plate on the shoulder.</summary>
public static class PlayerWearableSurfaceBuilder
{
    public static void Configure(GameObject player, GameObject visual, AnimatedWeaponSocket[] sockets)
    {
        var skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
        var mesh = skin.sharedMesh;
        var vertices = mesh.vertices;
        var weights = mesh.boneWeights;
        var binds = mesh.bindposes;
        var normals = mesh.normals;
        var normalMatrix = skin.transform.localToWorldMatrix.inverse.transpose;
        var anchors = new Anchor[vertices.Length];
        var positions = new Vector3[vertices.Length];
        var directions = new Vector3[vertices.Length];
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            var points = new List<Point>();
            BoneWeight weight = weights[vertex];
            void Add(int bone, float influence)
            {
                if (influence <= 0f) return;
                points.Add(new Point { Bone = skin.bones[bone], Weight = influence,
                    LocalPoint = binds[bone].MultiplyPoint3x4(vertices[vertex]) });
            }
            Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
            Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
            anchors[vertex] = new Anchor { Points = points.ToArray() };
            Vector3 world = Vector3.zero;
            foreach (Point point in points) world += point.Bone.TransformPoint(point.LocalPoint) * point.Weight;
            positions[vertex] = player.transform.InverseTransformPoint(world);
            directions[vertex] = player.transform.InverseTransformDirection(normalMatrix.MultiplyVector(normals[vertex])).normalized;
        }

        var attachments = new List<Attachment>();
        var report = new StringBuilder("Surface anchors use existing mesh skin weights; the cannon plate is fitted to its shoulder contact.\n");
        foreach (var binding in sockets)
        {
            Transform socket = binding.Socket;
            Vector3 center = player.transform.InverseTransformPoint(socket.position);
            string[] region;
            Vector3 normal, targetA, targetB, targetC;
            switch (binding.Type)
            {
                case WeaponType.AutomaticCannon:
                    region = new[] { "shoulder.R", "upper_arm.R" };
                    normal = Vector3.up;
                    targetA = center + Vector3.left * .05f;
                    targetB = center + Vector3.right * .08f;
                    targetC = center + Vector3.back * .09f;
                    break;
                case WeaponType.Flamethrower:
                    region = new[] { "forearm.L", "hand.L" };
                    normal = new Vector3(-.8f, .6f, 0f);
                    targetA = center;
                    targetB = center + new Vector3(-.07f, -.07f, 0f);
                    targetC = center + Vector3.back * .07f;
                    break;
                case WeaponType.RotatingBlade:
                    region = new[] { "spine", "thigh.L" };
                    normal = Vector3.back;
                    targetA = center;
                    targetB = center + Vector3.up * .09f;
                    targetC = center + Vector3.left * .08f;
                    break;
                default:
                    region = new[] { "spine.001", "spine.002", "spine.003" };
                    normal = Vector3.back;
                    targetA = center + Vector3.down * .17f;
                    targetB = center + Vector3.up * .17f;
                    targetC = center + (center.x < 0f ? Vector3.left : Vector3.right) * .09f;
                    break;
            }
            int[] candidates = Enumerable.Range(0, vertices.Length).Where(index =>
                Vector3.Dot(directions[index], normal) > .15f &&
                anchors[index].Points.Where(point => region.Contains(point.Bone.name)).Sum(point => point.Weight) >= .3f).ToArray();
            int Pick(Vector3 target, Func<int, bool> valid)
            {
                int[] eligible = candidates.Where(valid).OrderBy(index => (positions[index] - target).sqrMagnitude).ToArray();
                if (eligible.Length == 0) throw new InvalidOperationException("No usable skin patch for " + binding.Type);
                return eligible[0];
            }
            int a = Pick(targetA, _ => true);
            int b = Pick(targetB, index => Vector3.Distance(positions[index], positions[a]) > .035f);
            int c = Pick(targetC, index => Vector3.Cross((positions[b] - positions[a]).normalized,
                positions[index] - positions[a]).magnitude > .025f);
            if (!PlayerWearableSurfaceFollower.TryGetFrame(positions[a], positions[b], positions[c], out Vector3 origin, out Quaternion rotation))
                throw new InvalidOperationException("Degenerate surface patch for " + binding.Type);
            Quaternion inverse = Quaternion.Inverse(rotation);
            Anchor Supported(Anchor anchor)
            {
                if (binding.Type != WeaponType.AutomaticCannon) return anchor;
                // This tall shoulder housing has a clavicle support as well as
                // the moving armor base. Retain that support during large arm
                // raises so the housing does not tip inward toward the head.
                Transform support = skin.bones.Single(bone => bone.name == "shoulder.R");
                anchor.TryEvaluate(out Vector3 worldPoint);
                return new Anchor { Points = anchor.Points.Select(point => new Point
                { Bone = point.Bone, LocalPoint = point.LocalPoint, Weight = point.Weight * .45f })
                    .Concat(new[] { new Point { Bone = support, LocalPoint = support.InverseTransformPoint(worldPoint), Weight = .55f } }).ToArray() };
            }
            var attachment = new Attachment
            {
                Type = binding.Type, Socket = socket,
                A = Supported(anchors[a]), B = Supported(anchors[b]), C = Supported(anchors[c]),
                FrameLocalPosition = inverse * (center - origin),
                FrameLocalRotation = inverse * Quaternion.Inverse(player.transform.rotation) * socket.rotation
            };
            CalibrateContact(player, socket, binding.Type, normal, region, anchors, positions, attachment);
            if (binding.Type == WeaponType.AutomaticCannon)
                CalibrateShoulderSeat(player, socket, mesh.triangles, region, anchors, positions, attachment);
            attachments.Add(attachment);
            report.AppendLine($"{binding.Type}: vertices={a},{b},{c}, points={positions[a]:F4}; {positions[b]:F4}; {positions[c]:F4}");
            foreach (int index in new[] { a, b, c })
                report.AppendLine("  " + index + ": " + string.Join(", ", anchors[index].Points.Select(point => point.Bone.name + "=" + point.Weight.ToString("F3"))));
        }
        var follower = player.GetComponent<PlayerWearableSurfaceFollower>() ?? player.AddComponent<PlayerWearableSurfaceFollower>();
        follower.Configure(attachments.ToArray());
        follower.EvaluateAttachments();
        Directory.CreateDirectory(".utmp/player-animation/surface-follow");
        File.WriteAllText(".utmp/player-animation/surface-follow/calibration.txt", report.ToString());
    }

    private static void CalibrateShoulderSeat(GameObject player, Transform socket, int[] triangles,
        string[] region, Anchor[] anchors, Vector3[] positions, Attachment attachment)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>("Assets/Resources/WearableWeaponMounts.asset");
        catalog.TryGet(WeaponType.AutomaticCannon, out var definition);
        var instance = UnityEngine.Object.Instantiate(definition.Prefab, socket, false);
        Vector3 seatPoint;
        try
        {
            var plate = instance.GetComponentsInChildren<MeshFilter>().Single(filter => filter.name == "Shoulder mount top plate");
            var points = plate.sharedMesh.vertices.Select(point => socket.InverseTransformPoint(plate.transform.TransformPoint(point))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            seatPoint = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }

        Vector3 target = player.transform.InverseTransformPoint(socket.TransformPoint(seatPoint));
        float nearest = float.PositiveInfinity;
        Anchor seat = null;
        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            int a = triangles[triangle], b = triangles[triangle+1], c = triangles[triangle+2];
            if (new[] { a,b,c }.Any(index => anchors[index].Points.Where(point => region.Contains(point.Bone.name)).Sum(point => point.Weight) < .3f)) continue;
            Vector3 ab = positions[b]-positions[a], ac = positions[c]-positions[a];
            Vector3 normal = Vector3.Cross(ab,ac);
            if (normal.sqrMagnitude < 1e-12f || normal.normalized.y < .15f) continue;
            Vector3 projected = target - normal * (Vector3.Dot(target-positions[a],normal)/normal.sqrMagnitude);
            float d00 = Vector3.Dot(ab,ab), d01 = Vector3.Dot(ab,ac), d11 = Vector3.Dot(ac,ac);
            float d20 = Vector3.Dot(projected-positions[a],ab), d21 = Vector3.Dot(projected-positions[a],ac);
            float denominator = d00*d11-d01*d01;
            float v = (d11*d20-d01*d21)/denominator, w = (d00*d21-d01*d20)/denominator;
            void Consider(Vector3 barycentric)
            {
                Vector3 point = positions[a]*barycentric.x+positions[b]*barycentric.y+positions[c]*barycentric.z;
                float distance = (point-target).sqrMagnitude;
                if (distance >= nearest) return;
                nearest = distance;
                seat = new Anchor { Points = new[] { (a,barycentric.x),(b,barycentric.y),(c,barycentric.z) }
                    .SelectMany(pair => anchors[pair.Item1].Points.Select(influence => new Point
                    { Bone=influence.Bone, LocalPoint=influence.LocalPoint, Weight=influence.Weight*pair.Item2 })).ToArray() };
            }
            if (v>=0 && w>=0 && v+w<=1) Consider(new Vector3(1-v-w,v,w));
            float t = Mathf.Clamp01(Vector3.Dot(target-positions[a],ab)/d00); Consider(new Vector3(1-t,t,0));
            t = Mathf.Clamp01(Vector3.Dot(target-positions[a],ac)/d11); Consider(new Vector3(1-t,0,t));
            Vector3 bc = positions[c]-positions[b];
            t = Mathf.Clamp01(Vector3.Dot(target-positions[b],bc)/bc.sqrMagnitude); Consider(new Vector3(0,1-t,t));
        }
        if (seat == null) throw new InvalidOperationException("No shoulder armor surface under cannon mount.");
        attachment.Seat = seat;
        Quaternion rootRotation = Quaternion.Inverse(player.transform.rotation) * socket.rotation;
        Vector3 center = player.transform.InverseTransformPoint(socket.position);
        attachment.SocketLocalSeatPoint = Quaternion.Inverse(rootRotation) * (target - center);
        attachment.MaximumContactCorrection = 0f;
        seat.TryEvaluate(out Vector3 worldSeat);
        socket.position = player.transform.TransformPoint(player.transform.InverseTransformPoint(worldSeat)
            - rootRotation * attachment.SocketLocalSeatPoint);
        // Persist the fitted bind position so subsequent socket rebuilds and the
        // catalog fallback use the same shoulder seating.
        definition.LocalPosition = player.transform.InverseTransformPoint(socket.position);
        EditorUtility.SetDirty(catalog);
    }

    private static void CalibrateContact(GameObject player, Transform socket, WeaponType type, Vector3 normal,
        string[] region, Anchor[] anchors, Vector3[] positions, Attachment attachment)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>("Assets/Resources/WearableWeaponMounts.asset");
        if (!catalog.TryGet(type, out var definition)) throw new InvalidOperationException("Missing wearable " + type);
        Quaternion rootRotation = Quaternion.Inverse(player.transform.rotation) * socket.rotation;
        Quaternion contactRotation = Quaternion.LookRotation(normal,
            Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .9f ? Vector3.forward : Vector3.up);
        Quaternion inverseContact = Quaternion.Inverse(contactRotation);
        Vector3 center = player.transform.InverseTransformPoint(socket.position);
        Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
        var instance = UnityEngine.Object.Instantiate(definition.Prefab, socket, false);
        try
        {
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 point in filter.sharedMesh.vertices)
                {
                    Vector3 local = inverseContact * (player.transform.InverseTransformPoint(filter.transform.TransformPoint(point)) - center);
                    min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                }
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        attachment.ContactLocalRotation = Quaternion.Inverse(rootRotation) * contactRotation;
        attachment.ContactMin = new Vector2(min.x, min.y);
        attachment.ContactMax = new Vector2(max.x, max.y);
        attachment.ContactInnerZ = min.z;
        attachment.MaximumContactCorrection = .055f;
        float radius = (max - min).magnitude * .5f + .18f;
        // Retain a sparse, welded set of nearby skin points. This is an offline
        // selection; runtime only skins these small samples, never a full mesh.
        var seen = new HashSet<Vector3Int>();
        attachment.ContactProbes = Enumerable.Range(0, positions.Length).Where(index =>
            Vector3.Distance(positions[index], center) < radius &&
            anchors[index].Points.Where(point => region.Contains(point.Bone.name)).Sum(point => point.Weight) > .15f &&
            seen.Add(Vector3Int.RoundToInt(positions[index] * 100f)))
            .Select(index => anchors[index]).ToArray();
        attachment.BindContactDepth = PlayerWearableSurfaceFollower.MeasureContactDepth(center, rootRotation, attachment, player.transform);
    }
}
