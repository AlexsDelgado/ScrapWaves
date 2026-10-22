using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks the saved rig, authored motion, and the boundary between presentation and gameplay.</summary>
public sealed class StalkerRigAnimationTests
{
    private const string PrefabPath = "Assets/Prefabs/Stalker.prefab";
    private const string RigPath = "Assets/Art/Enemies/Stalker/Rigged/";
    private const string PiecePath = "Assets/Art/Enemies/Stalker/ProceduralMeshes/";
    private static readonly string[] ClipNames = { "Emergence", "Ascent", "ApexDive", "Dive", "ImpactReburrow", "Death" };
    private static readonly string[] BoneNames = { "Head", "Body01", "Body02", "Body03", "Body04", "Body05", "Body06", "Tail" };
    private readonly List<Object> _created = new();

    [TearDown]
    public void Cleanup()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _created.Clear();
    }

    [Test]
    public void SavedRig_PreservesSourceGeometryMaterialsAndRigidMetalWeights()
    {
        Assert.That(StalkerRigAuthoring.Validate(), Is.Not.Null.And.Not.Empty);
        Assert.That(AssetDatabase.AssetPathToGUID("Assets/Arte/Gusano.fbx"),
            Is.EqualTo("0b07ce85773ccb74ba2ee374ff9f05f2"), "The original imported source must be retained.");
        Fixture rig = CreateFixture();
        Mesh mesh = rig.Skin.sharedMesh;
        Assert.That(rig.Skin.bones, Is.EqualTo(rig.Bones));
        Assert.That(rig.Bones.Select(t => t.name), Is.EqualTo(BoneNames));
        Assert.That(mesh.bindposes.Length, Is.EqualTo(8));
        Assert.That(mesh.subMeshCount, Is.EqualTo(rig.Skin.sharedMaterials.Length));
        Assert.That(rig.Skin.sharedMaterials.All(m => m != null), Is.True);
        Assert.That(rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.EqualTo(1));
        Assert.That(rig.Root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);

        Mesh[] sourcePieces = BoneNames.Select(name => AssetDatabase.LoadAssetAtPath<Mesh>(PiecePath + name + ".asset")).ToArray();
        Assert.That(sourcePieces.All(m => m != null), Is.True, "Keep the verified source islands for reproducible authoring.");
        Assert.That(mesh.vertexCount, Is.EqualTo(sourcePieces.Sum(m => m.vertexCount)));
        using var counts = mesh.GetBonesPerVertex();
        using var weights = mesh.GetAllBoneWeights();
        Assert.That(counts.Length, Is.EqualTo(mesh.vertexCount));
        Assert.That(weights.Length, Is.EqualTo(mesh.vertexCount));
        int[] verticesPerBone = new int[8];
        for (int vertex = 0; vertex < counts.Length; vertex++)
        {
            Assert.That(counts[vertex], Is.EqualTo(1), "Metal sections must not deform through blended skin weights.");
            Assert.That(weights[vertex].weight, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(weights[vertex].boneIndex, Is.InRange(0, 7));
            verticesPerBone[weights[vertex].boneIndex]++;
        }
        for (int bone = 0; bone < 8; bone++)
        {
            Assert.That(verticesPerBone[bone], Is.EqualTo(sourcePieces[bone].vertexCount), BoneNames[bone]);
            if (bone > 0) Assert.That(rig.Bones[bone].parent, Is.SameAs(rig.Bones[bone - 1]));
        }
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            Assert.That(mesh.GetIndexCount(subMesh), Is.EqualTo(sourcePieces.Sum(m => (long)m.GetIndexCount(subMesh))),
                "Material submesh " + subMesh + " lost source triangles.");

        // Bake the actual bind pose: invalid bind matrices can pass weight/count checks but distort the model.
        Mesh baked = Track(new Mesh());
        // Unity 6's true option compensates for the renderer's inherited root scale.
        // Compare authored local vertices, rather than a second application of the boss's 4x scale.
        rig.Skin.BakeMesh(baked, true);
        Assert.That(baked.vertexCount, Is.EqualTo(mesh.vertexCount));
        Vector3[] expected = mesh.vertices;
        Vector3[] actual = baked.vertices;
        for (int vertex = 0; vertex < actual.Length; vertex++)
            AssertVector(actual[vertex], expected[vertex], "Bind vertex " + vertex, 0.001f);
    }

    [Test]
    public void Animator_HasSixGenericClipsAndDormantStateWithoutGameplayEventsOrRootCurves()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Animator animator = prefab.GetComponentsInChildren<Animator>(true).Single();
        Assert.That(animator.avatar, Is.Not.Null);
        Assert.That(animator.avatar.isValid, Is.True);
        Assert.That(animator.avatar.isHuman, Is.False);
        Assert.That(animator.applyRootMotion, Is.False);
        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.layers.Length, Is.EqualTo(1));
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        Assert.That(machine.states.Select(s => s.state.name), Is.EquivalentTo(ClipNames.Concat(new[] { "Dormant" })));
        Assert.That(machine.defaultState.name, Is.EqualTo("Dormant"));
        Assert.That(machine.defaultState.motion, Is.Null);
        Assert.That(machine.anyStateTransitions, Is.Empty, "The AI chooses phases; a trigger must not start an extra attack.");
        Assert.That(machine.stateMachines, Is.Empty);
        Assert.That(controller.animationClips.Select(c => c.name).Distinct(), Is.EquivalentTo(ClipNames));
        Assert.That(controller.animationClips.Any(c => c.name.IndexOf("Spit", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);

        foreach (ChildAnimatorState child in machine.states)
        {
            Assert.That(child.state.transitions, Is.Empty, "Gameplay chooses when " + child.state.name + " finishes.");
            if (child.state.name == "Dormant") continue;
            AnimationClip clip = child.state.motion as AnimationClip;
            Assert.That(clip, Is.SameAs(Clip(child.state.name)));
            Assert.That(clip.length, Is.GreaterThan(0f));
            Assert.That(clip.legacy, Is.False);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty, "No animation-driven damage, spit, or rewards.");
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                Is.EqualTo(clip.name == "Ascent" || clip.name == "Dive"), clip.name);
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings, Is.Not.Empty, clip.name + " must contain authored motion.");
            Assert.That(bindings.Any(binding => AnimationUtility.GetEditorCurve(clip, binding).keys.Length > 1), Is.True);
            foreach (EditorCurveBinding binding in bindings)
            {
                Assert.That(binding.type, Is.EqualTo(typeof(Transform)), clip.name + ": " + binding.propertyName);
                Assert.That(binding.path.StartsWith("Skeleton/Head", StringComparison.Ordinal), Is.True,
                    "Only the skeleton may be animated: " + clip.name + ": " + binding.path);
                Assert.That(binding.propertyName.IndexOf("Scale", StringComparison.OrdinalIgnoreCase), Is.EqualTo(-1),
                    "Metal pieces must preserve their authored dimensions.");
            }
        }
    }

    [Test]
    public void SavedPrefab_HasSevenBoneProxiesOneHealthAndTheOriginalHeadZone()
    {
        Fixture rig = CreateFixture();
        Assert.That(rig.Root.GetComponent<CapsuleCollider>(), Is.Null);
        Assert.That(rig.Root.GetComponent<CharacterController>(), Is.Not.Null);
        Assert.That(rig.Root.GetComponentsInChildren<EnemyEmergence>(true), Is.Empty);
        Assert.That(rig.Root.GetComponentsInChildren<EnemyDeathFeedback>(true), Is.Empty,
            "This boss has an animated detached death; generic snapshot feedback would duplicate it.");
        Assert.That(rig.Root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && c.GetType().Name == "StalkerProceduralBody"), Is.False);
        EnemyHealth health = rig.Root.GetComponent<EnemyHealth>();
        Assert.That(rig.Root.GetComponentsInChildren<EnemyHealth>(true).Length, Is.EqualTo(1));
        Assert.That(rig.Skin.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(rig.Animator.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(rig.Proxies[0], Is.Null);
        Transform collisionRoot = rig.Root.transform.Find("SegmentColliders");
        Assert.That(collisionRoot.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(7));
        for (int i = 1; i < 8; i++)
        {
            Assert.That(rig.Proxies[i].parent, Is.SameAs(collisionRoot));
            Assert.That(rig.Proxies[i].GetComponentInParent<EnemyHealth>(true), Is.SameAs(health));
            Assert.That(rig.Proxies[i].GetComponent<EnemyDamageHitZone>(), Is.Null);
            Assert.That(rig.Proxies[i].GetComponents<Collider>().Length, Is.EqualTo(1));
            BoxCollider box = rig.Proxies[i].GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null);
            Assert.That(box.isTrigger, Is.False);
            Assert.That(box.gameObject.layer, Is.EqualTo(rig.Root.layer));
            Mesh piece = AssetDatabase.LoadAssetAtPath<Mesh>(PiecePath + BoneNames[i] + ".asset");
            AssertVector(box.center, piece.bounds.center);
            AssertVector(box.size, piece.bounds.size);
        }
        AssertProxyAlignment(rig);
        Transform hitZone = rig.Root.transform.Find("Head");
        AssertVector(hitZone.localPosition, new Vector3(0f, 0.27f, 0.023f));
        Assert.That(hitZone.GetComponent<SphereCollider>().radius, Is.EqualTo(1.2f));
        Assert.That(new SerializedObject(hitZone.GetComponent<EnemyDamageHitZone>()).FindProperty("_damageMultiplier").floatValue,
            Is.EqualTo(1.2f));
        AssertVector(rig.Root.transform.localScale, Vector3.one * 4f);
    }

    [TestCase("Emergence")]
    [TestCase("Ascent")]
    [TestCase("ApexDive")]
    [TestCase("Dive")]
    [TestCase("ImpactReburrow")]
    [TestCase("Death")]
    public void AuthoredClip_BendsRigidSkinAndKeepsGameplayRootAndHitZoneFixed(string clipName)
    {
        Fixture rig = CreateFixture();
        rig.Animator.enabled = false;
        Transform root = rig.Root.transform;
        root.SetPositionAndRotation(new Vector3(23f, 45f, -17f), Quaternion.Euler(15f, 30f, 80f));
        Transform hitZone = root.Find("Head");
        Pose rootPose = new(root.position, root.rotation);
        Pose hitPose = new(hitZone.localPosition, hitZone.localRotation);
        Pose headPose = new(rig.Bones[0].localPosition, rig.Bones[0].localRotation);
        Quaternion[] bindRotations = rig.Bones.Select(b => b.localRotation).ToArray();
        Mesh baked = Track(new Mesh());
        AnimationClip clip = Clip(clipName);
        float largestBend = 0f;
        for (int sample = 0; sample <= 12; sample++)
        {
            clip.SampleAnimation(rig.Animator.gameObject, clip.length * sample / 12f);
            rig.Driver.SyncCollisionPose();
            AssertVector(root.position, rootPose.position);
            Assert.That(Quaternion.Angle(root.rotation, rootPose.rotation), Is.LessThan(0.01f));
            AssertVector(hitZone.localPosition, hitPose.position);
            Assert.That(Quaternion.Angle(hitZone.localRotation, hitPose.rotation), Is.LessThan(0.01f));
            if (clipName != "Death")
            {
                AssertVector(rig.Bones[0].localPosition, headPose.position, "Head must remain inside its existing damage zone.");
                Assert.That(Quaternion.Angle(rig.Bones[0].localRotation, headPose.rotation), Is.LessThan(0.01f));
            }
            for (int bone = 0; bone < 8; bone++)
            {
                AssertVector(rig.Bones[bone].localScale, Vector3.one);
                largestBend = Mathf.Max(largestBend, Quaternion.Angle(rig.Bones[bone].localRotation, bindRotations[bone]));
            }
            AssertProxyAlignment(rig);
            rig.Skin.BakeMesh(baked, true);
            Assert.That(baked.vertices.All(v => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z)), Is.True);
            AssertRigidDistances(rig.Skin.sharedMesh, baked);
        }
        Assert.That(largestBend, Is.GreaterThan(0.5f), clipName + " must visibly articulate the authored skeleton.");
    }

    [Test]
    public void HeadDrivenChain_SameHeadPoseKeepsDifferentRealMovementHistories()
    {
        Fixture left = CreateFixture();
        Fixture right = CreateFixture();
        Pose space = new(Vector3.zero, Quaternion.identity);
        Pose[] fromLeft = RunHeadTrace(left, -1f, 60, 1f, space).Last();
        Pose[] fromRight = RunHeadTrace(right, 1f, 60, 1f, space).Last();
        AssertVector(left.Root.transform.position, right.Root.transform.position);
        Assert.That(Quaternion.Angle(left.Root.transform.rotation, right.Root.transform.rotation), Is.LessThan(0.05f));
        AssertVector(fromLeft[0].position, fromRight[0].position);
        Assert.That(Quaternion.Angle(fromLeft[0].rotation, fromRight[0].rotation), Is.LessThan(0.05f));
        Assert.That(fromLeft[7].position.x, Is.LessThan(-2.5f), "The tail lost the head's left-side approach.");
        Assert.That(fromRight[7].position.x, Is.GreaterThan(2.5f), "The tail lost the head's right-side approach.");
        Assert.That(Vector3.Distance(fromLeft[7].position, fromRight[7].position), Is.GreaterThan(5f),
            "Identical head/clip time with different real histories must not produce a preset body arch.");
        Assert.That(Vector3.Distance(fromLeft[7].position, fromRight[7].position),
            Is.GreaterThan(Vector3.Distance(fromLeft[1].position, fromRight[1].position) + 2f),
            "Trailing sections must retain more of the earlier approach than the section beside the head.");
        TestContext.WriteLine("Actual head history: tail separation " +
            Vector3.Distance(fromLeft[7].position, fromRight[7].position).ToString("F3") + " m at the same final head pose.");
    }

    [TestCase(30, 0.5f)]
    [TestCase(30, 2f)]
    [TestCase(120, 0.5f)]
    [TestCase(120, 2f)]
    public void HeadDrivenChain_FrameRateAndSpeedPreserveTheSameTrailingShape(int frameRate, float speed)
    {
        Fixture reference = CreateFixture();
        Fixture candidate = CreateFixture();
        Pose space = new(new Vector3(15f, 7f, -20f), Quaternion.Euler(12f, 67f, -20f));
        List<Pose[]> expected = RunHeadTrace(reference, 1f, 60, 1f, space);
        // EditMode does not activate DebugSpeedTool's lifecycle singleton. The trace
        // itself moves the head over 8/speed seconds, at dt=1/frameRate; this changes
        // physical head speed and frame subdivision without relying on that singleton.
        // Actual DebugSpeedTool integration is covered by the live PlayMode flights.
        List<Pose[]> actual = RunHeadTrace(candidate, 1f, frameRate, speed, space);
        Assert.That(actual.Count, Is.EqualTo(expected.Count));
        float greatestPositionError = 0f;
        float greatestRotationError = 0f;
        for (int sample = 0; sample < expected.Count; sample++)
        for (int bone = 0; bone < 8; bone++)
        {
            float positionError = Vector3.Distance(actual[sample][bone].position, expected[sample][bone].position);
            float rotationError = Quaternion.Angle(actual[sample][bone].rotation, expected[sample][bone].rotation);
            greatestPositionError = Mathf.Max(greatestPositionError, positionError);
            greatestRotationError = Mathf.Max(greatestRotationError, rotationError);
            Assert.That(positionError, Is.LessThan(0.2f), "Frame subdivision changed the trailing position: sample " + sample + ", bone " + bone);
            Assert.That(rotationError, Is.LessThan(2f), "Frame subdivision changed the trailing direction: sample " + sample + ", bone " + bone);
        }
        TestContext.WriteLine(frameRate + " Hz / " + speed + "x: maximum pose difference " +
            greatestPositionError.ToString("F4") + " m / " + greatestRotationError.ToString("F3") + " degrees.");
    }

    [Test]
    public void HeadDrivenChain_CrestTravelsFromDescendingHeadThroughMiddleToRisingTail()
    {
        Fixture rig = CreateFixture();
        Pose[] bind = BonePoses(rig);
        float[] lengths = rig.Bones.Skip(1).Select(bone => bone.localPosition.magnitude * 4f).ToArray();
        float trailLength = lengths.Sum();
        // A broad semicircle permits a real turn within the configured joint guard.
        // Its curvature is deliberately strong enough that the full body spans the
        // ascending and descending sides at once; a rigid head-pivot swing cannot pass.
        float radius = Mathf.Max(lengths.Max() * 2.5f, trailLength * 0.45f);
        float warmup = trailLength * 2f;
        float end = warmup + Mathf.PI * radius + trailLength * 1.25f;
        const float speed = 25f;
        const float dt = 1f / 60f;
        Pose world = new(Vector3.zero, Quaternion.identity);
        rig.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        rig.Driver.BeginJump();
        rig.Animator.Update(0f);
        rig.Driver.ApplyFlightPose();
        rig.Driver.SyncCollisionPose();
        Vector3[] previous = rig.Bones.Select(bone => bone.position).ToArray();
        float[] previousVerticalSpeed = new float[8];
        float[] crestDistance = Enumerable.Repeat(float.NaN, 8).ToArray();
        bool sawTravelingCrest = false;
        int crestSamples = 0;
        float greatestCrestSpread = 0f;
        float greatestOverallSpread = 0f;
        Vector3 crestSpeedsAtGreatestSpread = Vector3.zero;
        bool beganDive = false;
        bool beganReburrow = false;
        for (float distance = speed * dt; distance <= end; distance += speed * dt)
        {
            float arcDistance = distance - warmup;
            Vector3 position;
            Vector3 direction;
            if (arcDistance < 0f)
            {
                position = Vector3.up * distance;
                direction = Vector3.up;
            }
            else if (arcDistance <= Mathf.PI * radius)
            {
                float angle = arcDistance / radius;
                position = new Vector3(radius * (1f - Mathf.Cos(angle)), warmup + radius * Mathf.Sin(angle), 0f);
                direction = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
            }
            else
            {
                position = new Vector3(radius * 2f, warmup - (arcDistance - Mathf.PI * radius), 0f);
                direction = Vector3.down;
            }
            // Keep roll continuous through the exact 180-degree heading; FromToRotation's
            // ambiguous antiparallel axis would introduce a fixture-only roll snap.
            Quaternion rotation = Quaternion.LookRotation(Vector3.forward, direction);
            rig.Root.transform.SetPositionAndRotation(position, rotation);
            if (!beganDive && arcDistance >= Mathf.PI * radius * 0.5f)
            {
                rig.Driver.BeginDive();
                beganDive = true;
            }
            if (!beganReburrow && arcDistance > Mathf.PI * radius + radius)
            {
                rig.Driver.BeginReburrow();
                beganReburrow = true;
            }
            rig.Driver.Step(dt);
            rig.Animator.Update(dt);
            rig.Driver.ApplyFlightPose();
            rig.Driver.SyncCollisionPose();
            AssertVector(rig.Root.transform.position, position, "The traveling bend displaced the gameplay root.");
            Assert.That(Quaternion.Angle(rig.Bones[0].rotation, rotation), Is.LessThan(0.05f));
            AssertProxyAlignment(rig);
            float[] verticalSpeed = new float[8];
            for (int bone = 0; bone < 8; bone++)
            {
                Vector3 current = rig.Bones[bone].position;
                verticalSpeed[bone] = (current.y - previous[bone].y) / dt;
                if (distance > warmup && float.IsNaN(crestDistance[bone]) &&
                    previousVerticalSpeed[bone] > 0f && verticalSpeed[bone] <= 0f)
                    crestDistance[bone] = distance;
                if (bone > 0)
                {
                    AssertVector(rig.Bones[bone].localPosition, bind[bone].position, "The traveling bend stretched link " + bone);
                    Assert.That(Vector3.Distance(current, rig.Bones[bone - 1].position),
                        Is.EqualTo(lengths[bone - 1]).Within(0.001f), "World link length " + bone);
                    Assert.That(Quaternion.Angle(rig.Bones[bone].localRotation, bind[bone].rotation),
                        Is.LessThanOrEqualTo(rig.Driver.MaximumJointBend + 0.1f), "Configured joint guard " + bone);
                    AssertVector(rig.Bones[bone].localScale, Vector3.one);
                }
                previous[bone] = current;
                previousVerticalSpeed[bone] = verticalSpeed[bone];
            }
            float spread = Vector3.Angle(rig.Bones[0].up, rig.Bones[7].up);
            greatestOverallSpread = Mathf.Max(greatestOverallSpread, spread);
            if (verticalSpeed[0] < -speed * 0.2f &&
                Mathf.Abs(verticalSpeed[4]) < speed * 0.15f && verticalSpeed[7] > speed * 0.2f &&
                rig.Bones[4].position.y > rig.Bones[0].position.y + 1f &&
                rig.Bones[4].position.y > rig.Bones[7].position.y + 1f)
            {
                // Entering the middle's crest window need not be its widest bend.
                // Observe the whole window, but count spread only while all three
                // independent motion/height conditions hold simultaneously.
                crestSamples++;
                if (spread > greatestCrestSpread)
                {
                    greatestCrestSpread = spread;
                    crestSpeedsAtGreatestSpread = new Vector3(verticalSpeed[0], verticalSpeed[4], verticalSpeed[7]);
                }
                if (!sawTravelingCrest)
                {
                    Pose[] held = WorldBonePoses(rig, world);
                    Advance(rig, 0.25f);
                    AssertWorldBonePoses(rig, world, held, "Pausing the head let the traveling bend creep through the body.");
                    sawTravelingCrest = true;
                }
            }
        }
        TestContext.WriteLine("Traveling crest: " + crestSamples + " qualifying frames; peak head-tail spread " +
            greatestCrestSpread.ToString("F3") + " degrees (whole trace " + greatestOverallSpread.ToString("F3") +
            "); head/middle/tail vertical speeds at peak " + crestSpeedsAtGreatestSpread + " m/s; joint guard " +
            rig.Driver.MaximumJointBend.ToString("F2") + " degrees, radius " + radius.ToString("F3") + " m.");
        TestContext.WriteLine("Head-to-tail crest distances: " + string.Join(", ", crestDistance.Select(value => value.ToString("F3"))));
        Assert.That(sawTravelingCrest, Is.True,
            "Never observed the head descending while the middle crested and the rear was still rising.");
        Assert.That(greatestCrestSpread, Is.GreaterThan(90f),
            "During the simultaneous descending-head/cresting-middle/rising-tail interval, the body must span the crest instead of sharing one whole-body rotation.");
        Assert.That(crestDistance.All(value => !float.IsNaN(value)), Is.True, "Every section must pass through its own crest.");
        for (int bone = 1; bone < 8; bone++)
            Assert.That(crestDistance[bone], Is.GreaterThan(crestDistance[bone - 1] + speed * dt),
                "The crest failed to travel in head-to-tail order at bone " + bone);
        Assert.That(beganDive && beganReburrow, Is.True);
    }

    [Test]
    public void HeadDrivenChain_BurrowResetAndTeleportRemovePreviousHistoryWithoutPoseSnap()
    {
        Fixture rig = CreateFixture();
        Pose[] bind = BonePoses(rig);
        Pose origin = new(Vector3.zero, Quaternion.identity);
        List<Pose[]> expected = RunHeadTrace(rig, 1f, 60, 1f, origin);
        RunHeadTrace(rig, -1f, 60, 1f, origin);

        Pose[] beforePhaseChange = WorldBonePoses(rig, origin);
        rig.Driver.BeginDive();
        Advance(rig, 0.2f);
        AssertWorldBonePoses(rig, origin, beforePhaseChange, "Beginning the dive reset the active chain.");
        rig.Driver.BeginReburrow();
        Advance(rig, 0.2f);
        AssertWorldBonePoses(rig, origin, beforePhaseChange, "Beginning reburrow reset the active chain.");
        rig.Driver.EnterBurrow();
        Assert.That(rig.Driver.CurrentPhase.ToString(), Is.EqualTo("Hidden"));
        AssertBonePoses(rig, bind);
        Pose relocated = new(new Vector3(400f, -12f, -250f), Quaternion.Euler(20f, 70f, -120f));
        List<Pose[]> replay = RunHeadTrace(rig, 1f, 60, 1f, relocated);
        for (int sample = 0; sample < expected.Count; sample++)
        for (int bone = 0; bone < 8; bone++)
        {
            AssertVector(replay[sample][bone].position, expected[sample][bone].position,
                "A previous launch/teleport polluted the new chain: sample " + sample + ", bone " + bone, 0.003f);
            Assert.That(Quaternion.Angle(replay[sample][bone].rotation, expected[sample][bone].rotation), Is.LessThan(0.1f));
        }
        rig.Driver.ResetPose();
        AssertBonePoses(rig, bind);
        AssertProxyAlignment(rig);
    }

    [Test]
    public void CollisionPose_IgnoresCosmeticFeedbackWrapperAndDoesNotOwnVisibility()
    {
        Fixture rig = CreateFixture();
        rig.Animator.enabled = false;
        AnimationClip clip = Clip("ApexDive");
        clip.SampleAnimation(rig.Animator.gameObject, clip.length * 0.5f);
        rig.Driver.SyncCollisionPose();
        Pose[] expected = rig.Proxies.Skip(1).Select(t => new Pose(t.localPosition, t.localRotation)).ToArray();
        rig.Proxies[3].GetComponent<Collider>().enabled = false;
        rig.Skin.enabled = false;
        rig.VisualRoot.localPosition = new Vector3(0.2f, -0.1f, 0.15f);
        rig.VisualRoot.localRotation = Quaternion.Euler(4f, 8f, -3f);
        rig.VisualRoot.localScale = new Vector3(0.92f, 1.03f, 0.94f);
        rig.Root.transform.SetPositionAndRotation(new Vector3(40f, 30f, -10f), Quaternion.Euler(0f, 70f, 120f));
        rig.Driver.SyncCollisionPose();
        for (int i = 1; i < 8; i++)
        {
            AssertVector(rig.Proxies[i].localPosition, expected[i - 1].position, "Feedback displaced body collision.");
            Assert.That(Quaternion.Angle(rig.Proxies[i].localRotation, expected[i - 1].rotation), Is.LessThan(0.05f));
            AssertVector(rig.Proxies[i].localScale, Vector3.one);
        }
        Assert.That(rig.Proxies[3].GetComponent<Collider>().enabled, Is.False);
        Assert.That(rig.Skin.enabled, Is.False);
        Assert.That(Vector3.Distance(rig.Proxies[3].position, rig.Bones[3].position), Is.GreaterThan(0.1f),
            "The fixture must actually perturb the cosmetic hierarchy.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FlightPath_JoinsAtHorizontalApexAndExtendsUndergroundWithoutOrbiting(bool rotatePlane)
    {
        Vector3 origin = new(7f, 4f, -2f);
        Vector3 forward = rotatePlane ? Vector3.forward : Vector3.right;
        Vector3 landing = origin + forward * 30f;
        Vector3 apex = origin + forward * 19.5f + Vector3.up * 50f;
        StalkerFlightPath path = new();
        path.Configure(origin, apex, landing, 45f);
        Assert.That(path.ApexDistance, Is.GreaterThan(0f));
        Assert.That(path.Length, Is.GreaterThan(path.ApexDistance));
        AssertVector(path.GetPosition(0f), origin, "Launch point", 0.001f);
        AssertVector(path.GetPosition(path.ApexDistance), apex, "Apex point", 0.001f);
        AssertVector(path.GetPosition(path.Length), landing, "Impact point", 0.001f);
        Assert.That(Vector3.Angle(path.GetTangent(path.ApexDistance), forward), Is.LessThan(0.1f));
        Assert.That(Vector3.Angle(path.GetTangent(path.ApexDistance - 0.01f),
            path.GetTangent(path.ApexDistance + 0.01f)), Is.LessThan(1f), "The two curve spans must share a tangent.");
        AssertVector(path.GetPosition(-5f), origin - path.GetTangent(0f) * 5f, "Entry extension", 0.001f);
        AssertVector(path.GetPosition(path.Length + 5f), landing + path.GetTangent(path.Length) * 5f,
            "Exit extension", 0.001f);

        Vector3 previous = origin;
        const float step = 0.1f;
        for (float distance = step; distance < path.Length; distance += step)
        {
            Vector3 current = path.GetPosition(distance);
            Assert.That(Vector3.Distance(previous, current), Is.InRange(0.08f, 0.12f),
                "Distance sampling stalled or jumped at " + distance);
            Assert.That(current.y, Is.LessThanOrEqualTo(apex.y + 0.001f));
            Assert.That(Mathf.Abs(Vector3.Dot(current - origin, Vector3.Cross(forward, Vector3.up))),
                Is.LessThan(0.001f), "The arch left its flight plane.");
            Assert.That(Vector3.Angle(current - previous, path.GetTangent(distance - step * 0.5f)),
                Is.LessThan(2f));
            Assert.That(Vector3.Angle(path.GetRotation(distance) * Vector3.up, path.GetTangent(distance)),
                Is.LessThan(0.05f), "Head +Y must follow the path tangent.");
            previous = current;
        }
    }

    [Test]
    public void BurialClearance_UsesEntirePosedSkinInsteadOfHeadOrRootDepth()
    {
        Fixture rig = CreateFixture();
        rig.Animator.enabled = false;
        Clip("Dive").SampleAnimation(rig.Animator.gameObject, 0.4f);
        rig.Root.transform.SetPositionAndRotation(new Vector3(0f, -8f, 0f), Quaternion.FromToRotation(Vector3.up, Vector3.down));
        rig.Driver.SyncCollisionPose();
        SphereCollider head = rig.Root.transform.Find("Head").GetComponent<SphereCollider>();
        Physics.SyncTransforms();
        Assert.That(head.bounds.max.y, Is.LessThan(0f), "The fixture's whole head damage zone must already be underground.");
        Mesh baked = Track(new Mesh());
        rig.Skin.BakeMesh(baked, true);
        float visibleTop = baked.vertices.Max(v => rig.Skin.transform.TransformPoint(v).y);
        Assert.That(visibleTop, Is.GreaterThan(15f), "Most of the long body is still above ground.");
        Assert.That(rig.Driver.GetHighestBodyPoint(), Is.GreaterThanOrEqualTo(visibleTop - 0.002f),
            "A head-only/root-depth test would hide a still-visible tail.");

        rig.Root.transform.position += Vector3.down * 60f;
        rig.Driver.SyncCollisionPose();
        rig.Skin.BakeMesh(baked, true);
        visibleTop = baked.vertices.Max(v => rig.Skin.transform.TransformPoint(v).y);
        float clearance = rig.Driver.GetHighestBodyPoint();
        Assert.That(clearance, Is.GreaterThanOrEqualTo(visibleTop - 0.002f));
        Assert.That(clearance, Is.LessThan(0f), "The gate must eventually admit a completely buried body.");
    }

    private List<Pose[]> RunHeadTrace(Fixture rig, float side, int frameRate, float speed, Pose space)
    {
        rig.Driver.ResetPose();
        Pose[] bind = BonePoses(rig);
        Transform hitZone = rig.Root.transform.Find("Head");
        Pose hitPose = new(hitZone.localPosition, hitZone.localRotation);
        SetTraceHead(rig, side, 0f, space);
        rig.Driver.BeginJump();
        rig.Animator.Update(0f);
        rig.Driver.ApplyFlightPose();
        rig.Driver.SyncCollisionPose();
        AssertBonePoses(rig, bind);
        Pose[] previous = WorldBonePoses(rig, space);
        Vector3 previousHeadPosition = rig.Root.transform.position;
        Quaternion previousHeadRotation = rig.Root.transform.rotation;
        List<Pose[]> samples = new();
        int frames = Mathf.RoundToInt(8f * frameRate / speed);
        float dt = 1f / frameRate;
        int pausedSamples = 0;
        for (int frame = 1; frame <= frames; frame++)
        {
            float time = (float)frame / frames;
            // The head changes direction in three dimensions, stops for 10% of the
            // sequence, then resumes. No authored flight path is supplied to the body.
            float progress = time <= 0.4f ? time / 0.9f
                : time <= 0.5f ? 0.4f / 0.9f : (time - 0.1f) / 0.9f;
            SetTraceHead(rig, side, progress, space);
            Pose rootPose = new(rig.Root.transform.position, rig.Root.transform.rotation);
            rig.Driver.Step(dt);
            rig.Animator.Update(dt);
            rig.Driver.ApplyFlightPose();
            rig.Driver.SyncCollisionPose();
            AssertVector(rig.Root.transform.position, rootPose.position, "Body solving moved the gameplay head.");
            Assert.That(Quaternion.Angle(rig.Root.transform.rotation, rootPose.rotation), Is.LessThan(0.05f));
            AssertVector(hitZone.localPosition, hitPose.position);
            Assert.That(Quaternion.Angle(hitZone.localRotation, hitPose.rotation), Is.LessThan(0.05f));
            AssertVector(rig.Bones[0].localPosition, bind[0].position);
            Assert.That(Quaternion.Angle(rig.Bones[0].rotation, rootPose.rotation), Is.LessThan(0.05f),
                "The fixed head bone diverged from its gameplay hit zone.");
            for (int bone = 1; bone < 8; bone++)
            {
                AssertVector(rig.Bones[bone].localPosition, bind[bone].position,
                    "Fixed-length link changed at frame " + frame + ", bone " + bone);
                Assert.That(Vector3.Distance(rig.Bones[bone].position, rig.Bones[bone - 1].position),
                    Is.EqualTo(bind[bone].position.magnitude * 4f).Within(0.001f), "World link stretched: " + bone);
                Assert.That(Quaternion.Angle(rig.Bones[bone].localRotation, bind[bone].rotation),
                    Is.LessThanOrEqualTo(rig.Driver.MaximumJointBend + 0.1f), "Joint bend limit: " + bone);
                AssertVector(rig.Bones[bone].localScale, Vector3.one);
                Vector3 position = rig.Bones[bone].position;
                Assert.That(IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z), Is.True);
            }
            AssertProxyAlignment(rig);
            if (Vector3.Distance(previousHeadPosition, rootPose.position) < 0.00001f &&
                Quaternion.Angle(previousHeadRotation, rootPose.rotation) < 0.001f)
            {
                AssertWorldBonePoses(rig, space, previous, "The stationary head's body drifted with the clip clock.");
                pausedSamples++;
            }
            previous = WorldBonePoses(rig, space);
            previousHeadPosition = rootPose.position;
            previousHeadRotation = rootPose.rotation;
            if (frame % (frames / 4) == 0) samples.Add(previous);
        }
        Assert.That(pausedSamples, Is.GreaterThan(3), "The fixture must exercise a real stop and restart.");
        // Rendering the same frozen pose a second time must not integrate the chain twice.
        rig.Driver.ApplyFlightPose();
        rig.Driver.SyncCollisionPose();
        AssertWorldBonePoses(rig, space, previous, "Repeated rendering changed a frozen chain.");
        Mesh baked = Track(new Mesh());
        rig.Skin.BakeMesh(baked, true);
        AssertRigidDistances(rig.Skin.sharedMesh, baked);
        return samples;
    }

    private static void SetTraceHead(Fixture rig, float side, float progress, Pose space)
    {
        float u = Mathf.Clamp01(progress);
        float smooth = u * u * (3f - 2f * u);
        float wave = Mathf.Sin(Mathf.PI * u);
        Vector3 position = new(side * 20f * (1f - smooth), 100f * u, 8f * wave * wave);
        Vector3 direction = new(-side * 120f * u * (1f - u), 100f, 8f * Mathf.PI * Mathf.Sin(2f * Mathf.PI * u));
        rig.Root.transform.SetPositionAndRotation(space.position + space.rotation * position,
            space.rotation * Quaternion.FromToRotation(Vector3.up, direction.normalized));
    }

    private static Pose[] WorldBonePoses(Fixture rig, Pose space)
    {
        Quaternion inverse = Quaternion.Inverse(space.rotation);
        return rig.Bones.Select(bone => new Pose(inverse * (bone.position - space.position), inverse * bone.rotation)).ToArray();
    }

    private static void AssertWorldBonePoses(Fixture rig, Pose space, Pose[] expected, string message)
    {
        Pose[] actual = WorldBonePoses(rig, space);
        for (int bone = 0; bone < 8; bone++)
        {
            AssertVector(actual[bone].position, expected[bone].position, message + " Bone " + bone, 0.001f);
            Assert.That(Quaternion.Angle(actual[bone].rotation, expected[bone].rotation), Is.LessThan(0.05f), message);
        }
    }

    private Fixture CreateFixture()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        GameObject root = Track(Object.Instantiate(prefab));
        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null) component.enabled = false;
        StalkerRigAnimation driver = root.GetComponent<StalkerRigAnimation>();
        Assert.That(driver, Is.Not.Null);
        SerializedObject serialized = new(driver);
        Animator animator = (Animator)serialized.FindProperty("_animator").objectReferenceValue;
        Transform visual = (Transform)serialized.FindProperty("_visualRoot").objectReferenceValue;
        SkinnedMeshRenderer skin = (SkinnedMeshRenderer)serialized.FindProperty("_bodyRenderer").objectReferenceValue;
        Transform[] bones = ReadTransforms(serialized, "_bones");
        Transform[] proxies = ReadTransforms(serialized, "_collisionSegments");
        Assert.That(bones.Length, Is.EqualTo(8));
        Assert.That(proxies.Length, Is.EqualTo(8));
        Assert.That(bones.All(b => b != null), Is.True);
        Assert.That(proxies.Skip(1).All(b => b != null), Is.True);
        driver.Configure(animator, visual, skin, bones, proxies);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return new Fixture(root, driver, animator, visual, skin, bones, proxies);
    }

    private T Track<T>(T value) where T : Object { _created.Add(value); return value; }
    private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(RigPath + name + ".anim");
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Transform[] ReadTransforms(SerializedObject serialized, string name)
    {
        SerializedProperty property = serialized.FindProperty(name);
        Assert.That(property, Is.Not.Null, name);
        return Enumerable.Range(0, property.arraySize)
            .Select(i => (Transform)property.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }
    private static void Advance(Fixture rig, float duration)
    {
        for (float elapsed = 0f; elapsed < duration - 0.00001f;)
        {
            float dt = Mathf.Min(1f / 60f, duration - elapsed);
            rig.Driver.Step(dt);
            rig.Animator.Update(dt);
            rig.Driver.ApplyFlightPose();
            rig.Driver.SyncCollisionPose();
            elapsed += dt;
        }
    }
    private static Pose[] BonePoses(Fixture rig) => rig.Bones.Select(b => new Pose(b.localPosition, b.localRotation)).ToArray();
    private static void AssertBonePoses(Fixture rig, Pose[] poses)
    {
        for (int i = 0; i < 8; i++)
        {
            AssertVector(rig.Bones[i].localPosition, poses[i].position, "Replay position " + i);
            Assert.That(Quaternion.Angle(rig.Bones[i].localRotation, poses[i].rotation), Is.LessThan(0.05f), "Replay rotation " + i);
        }
    }
    private static void AssertProxyAlignment(Fixture rig)
    {
        for (int i = 1; i < 8; i++)
        {
            AssertVector(rig.Proxies[i].position, rig.Bones[i].position, "Collision position " + i, 0.001f);
            Assert.That(Quaternion.Angle(rig.Proxies[i].rotation, rig.Bones[i].rotation), Is.LessThan(0.05f), "Collision rotation " + i);
            AssertVector(rig.Proxies[i].lossyScale, rig.Bones[i].lossyScale, "Collision scale " + i, 0.001f);
        }
    }
    private static void AssertRigidDistances(Mesh source, Mesh posed)
    {
        using var weights = source.GetAllBoneWeights();
        Vector3[] rest = source.vertices;
        Vector3[] actual = posed.vertices;
        int[] anchors = Enumerable.Repeat(-1, 8).ToArray();
        for (int vertex = 0; vertex < rest.Length; vertex++)
        {
            int bone = weights[vertex].boneIndex;
            if (anchors[bone] < 0) anchors[bone] = vertex;
            int anchor = anchors[bone];
            Assert.That(Vector3.Distance(actual[vertex], actual[anchor]),
                Is.EqualTo(Vector3.Distance(rest[vertex], rest[anchor])).Within(0.002f), "Metal section stretched: " + bone);
        }
    }
    private static void AssertVector(Vector3 actual, Vector3 expected, string message = null, float tolerance = 0.0002f) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance), message);

    private readonly struct Fixture
    {
        public readonly GameObject Root;
        public readonly StalkerRigAnimation Driver;
        public readonly Animator Animator;
        public readonly Transform VisualRoot;
        public readonly SkinnedMeshRenderer Skin;
        public readonly Transform[] Bones;
        public readonly Transform[] Proxies;
        public Fixture(GameObject root, StalkerRigAnimation driver, Animator animator, Transform visualRoot,
            SkinnedMeshRenderer skin, Transform[] bones, Transform[] proxies)
        { Root = root; Driver = driver; Animator = animator; VisualRoot = visualRoot; Skin = skin; Bones = bones; Proxies = proxies; }
    }
}
