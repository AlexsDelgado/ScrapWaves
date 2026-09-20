using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Anchor = PlayerWearableSurfaceFollower.SurfaceAnchor;
using Attachment = PlayerWearableSurfaceFollower.SurfaceAttachment;
using Influence = PlayerWearableSurfaceFollower.WeightedBonePoint;

public sealed class PlayerWearableSurfaceFollowerTests
{
    private readonly List<GameObject> _cleanup = new();

    [TearDown]
    public void Cleanup()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void WeightedAnchor_BlendsActualBoneDeformationAndNormalizesWeights()
    {
        Transform first = Create("First bone");
        Transform second = Create("Second bone");
        first.position = new Vector3(2f, 0f, 0f);
        second.SetPositionAndRotation(new Vector3(0f, 4f, 0f), Quaternion.Euler(0f, 0f, 90f));
        Anchor anchor = new()
        {
            Points = new[]
            {
                new Influence { Bone = first, LocalPoint = Vector3.up, Weight = 1f },
                new Influence { Bone = second, LocalPoint = Vector3.right, Weight = 3f }
            }
        };
        Assert.That(anchor.TryEvaluate(out Vector3 point), Is.True);
        AssertNear(point, new Vector3(.5f, 4f, 0f));
        second.position += Vector3.forward * 2f;
        Assert.That(anchor.TryEvaluate(out point), Is.True);
        AssertNear(point, new Vector3(.5f, 4f, 1.5f));
    }

    [Test]
    public void SurfaceFrame_FollowsBlendedBonesPreservesRigidSocketAndDoesNotAccumulate()
    {
        Transform root = Create("Player");
        PlayerWearableSurfaceFollower follower = root.gameObject.AddComponent<PlayerWearableSurfaceFollower>();
        Transform first = Create("Lower torso", root);
        Transform second = Create("Upper torso", root);
        Transform socket = Create("Automatic socket", first);
        Transform manual = Create("Manual muzzle", second);
        manual.localPosition = new Vector3(.3f, .5f, .7f);
        socket.localScale = new Vector3(.8f, 1.2f, .9f);
        Quaternion originalOffset = Quaternion.Euler(10f, 20f, 30f);
        Attachment attachment = new()
        {
            Socket = socket,
            A = Blended(first, second, Vector3.zero),
            B = Blended(first, second, Vector3.right),
            C = Blended(first, second, Vector3.up),
            FrameLocalPosition = new Vector3(.2f, .3f, .4f),
            FrameLocalRotation = originalOffset
        };
        follower.Configure(new[] { attachment });
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, attachment.FrameLocalPosition);
        AssertRotation(socket.localRotation, originalOffset);

        second.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Vector3 manualBefore = manual.position;
        Quaternion expectedFrame = Quaternion.Euler(0f, 0f, 45f);
        follower.EvaluateAttachments();
        AssertNear(socket.position, expectedFrame * attachment.FrameLocalPosition);
        AssertRotation(socket.rotation, expectedFrame * originalOffset);
        Vector3 firstPosition = socket.position;
        Quaternion firstRotation = socket.rotation;
        for (int i = 0; i < 8; i++) follower.EvaluateAttachments();
        AssertNear(socket.position, firstPosition);
        AssertRotation(socket.rotation, firstRotation);
        AssertNear(socket.localScale, new Vector3(.8f, 1.2f, .9f));
        AssertNear(manual.position, manualBefore);
    }

    [Test]
    public void SurfaceFrame_TracksTranslatedRotatedScaledPlayerWithoutStretchingArtifact()
    {
        Transform root = Create("Player");
        PlayerWearableSurfaceFollower follower = root.gameObject.AddComponent<PlayerWearableSurfaceFollower>();
        Transform bone = Create("Surface bone", root);
        Transform socket = Create("Socket", bone);
        Attachment attachment = new()
        {
            Socket = socket,
            A = Single(bone, Vector3.zero), B = Single(bone, Vector3.right), C = Single(bone, Vector3.up),
            FrameLocalPosition = new Vector3(.2f, .3f, .4f),
            FrameLocalRotation = Quaternion.Euler(10f, 20f, 30f)
        };
        follower.Configure(new[] { attachment });
        follower.EvaluateAttachments();
        Vector3 originalPosition = socket.position;
        Quaternion originalRotation = socket.rotation;
        root.SetPositionAndRotation(new Vector3(8f, -3f, 7f), Quaternion.Euler(18f, 61f, -14f));
        root.localScale = new Vector3(2f, 3f, 4f);
        follower.EvaluateAttachments();
        AssertNear(socket.position, root.TransformPoint(originalPosition));
        AssertRotation(socket.rotation, root.rotation * originalRotation);
        AssertNear(socket.localScale, Vector3.one);

        // Skin stretching affects the anchor triangle, not the artifact's dimensions
        // or the calibrated distance from its surface frame.
        attachment.B.Points[0].LocalPoint *= 2f;
        attachment.C.Points[0].LocalPoint *= 3f;
        follower.EvaluateAttachments();
        AssertNear(socket.position, root.TransformPoint(originalPosition));
        AssertNear(socket.localScale, Vector3.one);
    }

    [Test]
    public void DegenerateOrBrokenSurface_LeavesLastValidPoseAndRecovers()
    {
        Transform root = Create("Player");
        PlayerWearableSurfaceFollower follower = root.gameObject.AddComponent<PlayerWearableSurfaceFollower>();
        Transform bone = Create("Surface bone", root);
        Transform socket = Create("Socket", root);
        Attachment attachment = new()
        {
            Socket = socket,
            A = Single(bone, Vector3.zero), B = Single(bone, Vector3.right), C = Single(bone, Vector3.up),
            FrameLocalPosition = new Vector3(.2f, .3f, .4f)
        };
        follower.Configure(new[] { attachment });
        follower.EvaluateAttachments();
        Vector3 validPosition = socket.position;
        Quaternion validRotation = socket.rotation;
        attachment.C.Points[0].LocalPoint = Vector3.right * 2f;
        bone.position = Vector3.one;
        follower.EvaluateAttachments();
        AssertNear(socket.position, validPosition);
        AssertRotation(socket.rotation, validRotation);
        attachment.C.Points[0].LocalPoint = Vector3.up;
        attachment.B.Points[0].Bone = null;
        follower.EvaluateAttachments();
        AssertNear(socket.position, validPosition);
        attachment.B.Points[0].Bone = bone;
        follower.EvaluateAttachments();
        AssertNear(socket.position, validPosition + Vector3.one);
        Assert.That(PlayerWearableSurfaceFollower.TryGetFrame(Vector3.zero, Vector3.zero, Vector3.up,
            out _, out _), Is.False);
        Assert.That(PlayerWearableSurfaceFollower.TryGetFrame(Vector3.zero, Vector3.right,
            new Vector3(float.NaN, 1f, 0f), out _, out _), Is.False);
    }

    [Test]
    public void ContactCorrection_PreservesBindPlacementFollowsBodyBulgeAndReturnsAtRest()
    {
        Transform root = Create("Player");
        root.SetPositionAndRotation(new Vector3(2f, .5f, -3f), Quaternion.Euler(0f, 35f, 0f));
        PlayerWearableSurfaceFollower follower = root.gameObject.AddComponent<PlayerWearableSurfaceFollower>();
        Transform carrier = Create("Mounting surface", root);
        Transform bulgingSurface = Create("Independent skin bulge", root);
        Transform socket = Create("Rigid wearable", carrier);
        Transform muzzle = Create("Wearable muzzle", socket);
        muzzle.localPosition = Vector3.forward * .3f;
        Attachment attachment = new()
        {
            Socket = socket,
            A = Single(carrier, Vector3.zero), B = Single(carrier, Vector3.right), C = Single(carrier, Vector3.up),
            FrameLocalPosition = Vector3.forward * .1f,
            ContactProbes = new[] { Single(bulgingSurface, Vector3.forward * .12f) },
            ContactMin = new Vector2(-.1f, -.1f), ContactMax = new Vector2(.1f, .1f),
            ContactInnerZ = 0f,
            MaximumContactCorrection = .08f
        };
        attachment.BindContactDepth = PlayerWearableSurfaceFollower.MeasureContactDepth(
            attachment.FrameLocalPosition, Quaternion.identity, attachment, root);
        Assert.That(attachment.BindContactDepth, Is.EqualTo(.02f).Within(.00001f));
        follower.Configure(new[] { attachment });
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, new Vector3(0f, 0f, .1f));

        bulgingSurface.localPosition = Vector3.forward * .04f;
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, new Vector3(0f, 0f, .14f));
        AssertNear(muzzle.localPosition, Vector3.forward * .3f);
        AssertNear(socket.localScale, Vector3.one);
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, new Vector3(0f, 0f, .14f));

        bulgingSurface.localPosition = Vector3.forward * .12f;
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, new Vector3(0f, 0f, .18f));
        bulgingSurface.localPosition = Vector3.zero;
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, new Vector3(0f, 0f, .1f));

        // A bulge outside the local mounting footprint must not push the artifact.
        bulgingSurface.localPosition = new Vector3(.2f, 0f, .04f);
        follower.EvaluateAttachments();
        AssertNear(socket.localPosition, new Vector3(0f, 0f, .1f));
    }

    private Transform Create(string name, Transform parent = null)
    {
        var instance = new GameObject(name);
        _cleanup.Add(instance);
        instance.transform.SetParent(parent, false);
        return instance.transform;
    }

    private static Anchor Single(Transform bone, Vector3 point) => new()
    {
        Points = new[] { new Influence { Bone = bone, LocalPoint = point, Weight = 1f } }
    };

    private static Anchor Blended(Transform first, Transform second, Vector3 point) => new()
    {
        Points = new[]
        {
            new Influence { Bone = first, LocalPoint = point, Weight = .5f },
            new Influence { Bone = second, LocalPoint = point, Weight = .5f }
        }
    };

    private static void AssertNear(Vector3 actual, Vector3 expected) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00002f));

    private static void AssertRotation(Quaternion actual, Quaternion expected) =>
        Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(.05f));
}
