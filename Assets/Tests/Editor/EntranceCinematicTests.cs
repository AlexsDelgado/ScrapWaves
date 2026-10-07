using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class EntranceCinematicTests
{
    private EntranceCinematic _intro;
    private Camera _camera;
    private Transform _visual;
    [SetUp] public void Setup()
    {
        GameplayPause.Reset();Time.timeScale=1f;
        if(UnityEngine.InputSystem.Keyboard.current!=null)UnityEngine.InputSystem.InputSystem.ResetDevice(UnityEngine.InputSystem.Keyboard.current);
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        _intro=Object.FindFirstObjectByType<EntranceCinematic>();
        Assert.NotNull(_intro,"Scene must contain the authored entrance.");
        _camera=Get<Camera>("_camera");_visual=Get<Transform>("_visual");
        var gameplayCamera=_camera.GetComponent<ThirdPersonCamera>();
        gameplayCamera.SetLookBlockedByUi(true);
        foreach(string method in new[]{"OnEnable","Start","Update","LateUpdate"})typeof(ThirdPersonCamera).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplayCamera,null);
    }
    private T Get<T>(string field)=>(T)typeof(EntranceCinematic).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(_intro);
    [TearDown] public void Cleanup(){_intro.RestorePresentation();Object.DestroyImmediate(_intro.gameObject);GameplayPause.Reset();Time.timeScale=1f;}
    [TestCase(.5f)][TestCase(2.8f)][TestCase(7.4f)][TestCase(12.8f)]
    public void InterruptedAtEachBeatRestoresExactCameraAndRig(float time)
    {
        var position=_camera.transform.position;var rotation=_camera.transform.rotation;float fov=_camera.fieldOfView;
        var bones=_visual.GetComponentsInChildren<Transform>(true);var rotations=bones.Select(t=>t.localRotation).ToArray();var positions=bones.Select(t=>t.localPosition).ToArray();
        var movement=Get<Transform>("_player").GetComponent<PlayerMovement>();bool enabled=movement.enabled;
        _intro.Sample(time);Assert.False(movement.enabled);
        _intro.RestorePresentation();_intro.RestorePresentation();
        Assert.AreEqual(position,_camera.transform.position);Assert.AreEqual(rotation,_camera.transform.rotation);Assert.AreEqual(fov,_camera.fieldOfView);Assert.AreEqual(enabled,movement.enabled);
        for(int i=0;i<bones.Length;i++){Assert.AreEqual(positions[i],bones[i].localPosition);Assert.Less(Quaternion.Angle(rotations[i],bones[i].localRotation),.01f);}
    }
    [Test] public void RetryBypassesTheSequenceAndReachesSameReadyPose()
    {
        var clip=Get<AnimationClip>("_entrance");var local=_visual.localPosition;
        for(int axis=0;axis<3;axis++){var binding=EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition."+"xyz"[axis]);local[axis]=AnimationUtility.GetEditorCurve(clip,binding).Evaluate(_intro.Duration);}
        var position=_visual.parent.TransformPoint(local);EntranceCinematic.PrepareRetry();var routine=_intro.PlayEntrance();
        Assert.True(routine.MoveNext());Assert.True(_intro.IsFinished);Assert.Less(Vector3.Distance(position,_visual.position),.001f);
        Assert.False(routine.MoveNext());
    }
    [Test] public void AssetsAreAuthoredWithBoundedDurationAndNoImpactCollision()
    {
        Assert.That(_intro.Duration,Is.InRange(8f,15f));
        Assert.That(Get<AnimationClip>("_entrance").length,Is.EqualTo(13f).Within(.01f));
        Assert.NotNull(Get<AudioClip>("_whistle"));Assert.NotNull(Get<AudioClip>("_thud"));
        Assert.IsEmpty(Get<GameObject>("_impact").GetComponentsInChildren<Collider>());
    }
    private Transform Bone(string name)=>_visual.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    [Test] public void ReferencePoseHasBothFoldedKneesAndForearmsNearTuckedHead()
    {
        _intro.Sample(4f);
        foreach(string side in new[]{"L","R"})
        {
            Vector3 thigh=Bone("shin."+side).position-Bone("thigh."+side).position;
            Vector3 calf=Bone("foot."+side).position-Bone("shin."+side).position;
            Assert.Less(Vector3.Dot(thigh.normalized,calf.normalized),.25f,"Both legs remain folded and overlap the curled torso.");
            Assert.Less(Vector3.Distance(Bone("hand."+side).position,Bone("spine.006").position),.6f,"Neither arm should stretch away from the head.");
        }
        Assert.Less(Mathf.Abs(Vector3.Dot(_visual.up,Vector3.up)),.15f,"The whole body rests on its side.");
    }
    [Test] public void ReferenceCameraKeepsHeadHandsKneesAndFeetInsideFrame()
    {
        _intro.Sample(4f);
        foreach(string name in new[]{"spine.006","hand.L","hand.R","shin.L","shin.R","foot.L","foot.R"})
        {
            Vector3 viewport=_camera.WorldToViewportPoint(Bone(name).position);
            Assert.Greater(viewport.z,0);Assert.That(viewport.x,Is.InRange(.05f,.95f),name);Assert.That(viewport.y,Is.InRange(.05f,.95f),name);
        }
    }
    [TestCase(9.4f)][TestCase(12.8f)] public void GetupEndsWithLoweredArmsAndExistingIdleBody(float time)
    {
        _intro.Sample(time);
        foreach(string side in new[]{"L","R"})
        {
            Assert.Less(Bone("hand."+side).position.y,Bone("upper_arm."+side).position.y-.5f,"Hands should hang alongside the body: "+side);
            Assert.Less(Bone("hand."+side).position.y,Bone("forearm."+side).position.y-.2f,"Forearms should point down: "+side);
        }
        var bones=_visual.GetComponentsInChildren<Transform>(true);var rotations=bones.Select(t=>t.localRotation).ToArray();
        var idle=AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath).OfType<AnimationClip>().First(c=>c.name=="Idle");
        idle.SampleAnimation(_visual.gameObject,0f);
        for(int i=0;i<bones.Length;i++)
            if(!bones[i].name.StartsWith("upper_arm.")&&!bones[i].name.StartsWith("forearm.")&&!bones[i].name.StartsWith("hand."))
                Assert.Less(Quaternion.Angle(rotations[i],bones[i].localRotation),.1f,bones[i].name);
    }
    [Test] public void LateRecoveryElbowsFlexForwardInsteadOfBackwards()
    {
        var forward=Get<Transform>("_player").forward;
        for(int frame=480;frame<=780;frame++)
        {
            _intro.Sample(frame/60f);
            foreach(string side in new[]{"L","R"})
            {
                Vector3 upper=(Bone("forearm."+side).position-Bone("upper_arm."+side).position).normalized;
                Vector3 forearm=(Bone("hand."+side).position-Bone("forearm."+side).position).normalized;
                Assert.Greater(Vector3.Dot(forearm-upper,forward),.015f,$"Backwards elbow bend at {frame/60f:F3}s on {side}");
            }
        }
    }
    [Test] public void RecoveryRisesThroughGroundSupportAndOneKneeBeforeStanding()
    {
        _intro.Sample(6.35f);
        float pushedHead=Bone("spine.006").position.y;
        Assert.Less(Bone("hand.R").position.y,.25f,"The supporting hand must reach the ground.");
        Assert.Less(Bone("shin.R").position.y,.30f,"The initial push must have a grounded knee.");
        _intro.Sample(7.4f);
        Assert.Greater(Bone("shin.L").position.y-Bone("shin.R").position.y,.25f,"One knee is raised, the other remains down.");
        _intro.Sample(9.4f);
        Assert.Greater(Bone("spine.006").position.y-pushedHead,.75f,"Recovery visibly raises the head, rather than only rotating the body.");
        foreach(string name in new[]{"spine.006","hand.L","hand.R","foot.L","foot.R"})
        {
            Vector3 point=_camera.WorldToViewportPoint(Bone(name).position);
            Assert.That(point.x,Is.InRange(.03f,.97f),name);Assert.That(point.y,Is.InRange(.03f,.97f),name);
        }
        _intro.Sample(12f);
        foreach(string name in new[]{"spine.006","foot.L","foot.R"})
        {
            Vector3 point=_camera.WorldToViewportPoint(Bone(name).position);
            Assert.That(point.x,Is.InRange(.02f,.98f),"Camera return: "+name);Assert.That(point.y,Is.InRange(.02f,.98f),"Camera return: "+name);
        }
    }
    [Test] public void SideRollKeepsPelvisLowWithoutAnIntermediateLiftAndDrop()
    {
        _intro.Sample(5f);float start=Bone("spine").position.y;
        _intro.Sample(6.35f);float end=Bone("spine").position.y;
        for(float time=5.2f;time<=6.35f;time+=.025f)
        {
            _intro.Sample(time);
            Assert.That(Bone("spine").position.y,Is.InRange(Mathf.Min(start,end)-.015f,Mathf.Max(start,end)+.015f),$"The roll raised the pelvis then dropped it at {time:F3}s");
        }
    }
    [Test] public void PlantedHandsAndRearFootDoNotSlideDuringPushOff()
    {
        _intro.Sample(6.35f);Vector3 hand=Bone("hand.R").position,foot=Bone("foot.R").position;
        for(float time=6.35f;time<=6.55f;time+=.02f)
        {
            _intro.Sample(time);Assert.Less(Vector3.Distance(hand,Bone("hand.R").position),.005f);
            Assert.Less(Vector3.Distance(foot,Bone("foot.R").position),.005f);
        }
        _intro.Sample(7.45f);foot=Bone("foot.L").position;
        for(float time=7.45f;time<=8.35f;time+=.03f){_intro.Sample(time);Assert.Less(Vector3.Distance(foot,Bone("foot.L").position),.015f);}
    }
    [Test] public void LandingArmsRestBesideHeadWithNeutralWrists()
    {
        var idle=AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath).OfType<AnimationClip>().First(c=>c.name=="Idle");
        idle.SampleAnimation(_visual.gameObject,0f);
        var wrists=new[]{Bone("hand.L").localRotation,Bone("hand.R").localRotation};
        _intro.Sample(4f);
        for(int i=0;i<2;i++)
        {
            string side=i==0?"L":"R";
            Assert.Less(Quaternion.Angle(wrists[i],Bone("hand."+side).localRotation),12f,"Landing wrist twist: "+side);
            Vector3 shoulder=_visual.InverseTransformPoint(Bone("upper_arm."+side).position);
            Vector3 elbow=_visual.InverseTransformPoint(Bone("forearm."+side).position);
            Assert.Less(elbow.y,shoulder.y-.04f,"The elbow should tuck toward the ribs, not flare above the head: "+side);
        }
    }
    [Test] public void RecoveryUsesOneSidePalmAndHasNoJointJump()
    {
        for(float time=6.2f;time<=6.6f;time+=.025f)
        {
            _intro.Sample(time);
            Assert.Greater(Bone("hand.L").position.y-Bone("hand.R").position.y,.3f,"The free arm must stay off the ground at "+time);
            Assert.Greater(Bone("shin.L").position.y-Bone("shin.R").position.y,.18f,"The forward leg must already leave the symmetrical kneel at "+time);
        }
        var joints=new[]{"upper_arm.L","forearm.L","hand.L","upper_arm.R","forearm.R","hand.R","shin.L","shin.R","spine","spine.006"}.Select(Bone).ToArray();
        _intro.Sample(5f);var rotations=joints.Select(t=>t.rotation).ToArray();var positions=joints.Select(t=>t.position).ToArray();
        var maxRotation=new float[joints.Length];var maxMovement=new float[joints.Length];var atTime=new float[joints.Length];
        for(int frame=301;frame<=564;frame++)
        {
            _intro.Sample(frame/60f);
            for(int i=0;i<joints.Length;i++)
            {
                float angle=Quaternion.Angle(rotations[i],joints[i].rotation);
                if(angle>maxRotation[i]){maxRotation[i]=angle;atTime[i]=frame/60f;}
                maxMovement[i]=Mathf.Max(maxMovement[i],Vector3.Distance(positions[i],joints[i].position));
                rotations[i]=joints[i].rotation;positions[i]=joints[i].position;
            }
        }
        var errors=new System.Collections.Generic.List<string>();
        for(int i=0;i<joints.Length;i++)
        {
            if(maxRotation[i]>=14f)errors.Add($"Abrupt recovery joint rotation {maxRotation[i]:F3} at {atTime[i]:F3}: {joints[i].name}");
            if(maxMovement[i]>=.10f)errors.Add($"Abrupt recovery joint movement {maxMovement[i]:F3}: {joints[i].name}");
        }
        Assert.IsEmpty(errors,string.Join("; ",errors));
    }
    [Test] public void RecoveryMeshDoesNotSinkThroughGround()
    {
        var player=Get<Transform>("_player");
        Assert.True(Physics.Raycast(player.position+Vector3.up*5,Vector3.down,out var hit,30,~(1<<player.gameObject.layer),QueryTriggerInteraction.Ignore));
        var mesh=new Mesh();
        try
        {
            for(int frame=150;frame<=282;frame++)
            {
                _intro.Sample(frame/30f);float minimum=float.PositiveInfinity;
                foreach(var skin in _visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.BakeMesh(mesh);foreach(var v in mesh.vertices)minimum=Mathf.Min(minimum,skin.transform.TransformPoint(v).y);
                }
                Assert.Greater(minimum,hit.point.y-.005f,$"Recovery mesh penetrates the floor at {frame/30f:F3}s");
            }
        }
        finally {Object.DestroyImmediate(mesh);}
    }
    [Test] public void ImpactEmittersAreStoppedAndBurstOnly()
    {
        var dust=Get<ParticleSystem>("_dust");
        _intro.Sample(0f);
        foreach(var emitter in dust.GetComponentsInChildren<ParticleSystem>(true))
        {
            Assert.False(emitter.main.playOnAwake);Assert.False(emitter.main.prewarm);Assert.False(emitter.main.loop);
            Assert.AreEqual(0,emitter.emission.rateOverTime.constant);Assert.AreEqual(0,emitter.emission.rateOverDistance.constant);
            Assert.AreEqual(1,emitter.emission.burstCount);Assert.AreEqual(0,emitter.emission.GetBurst(0).time);
            Assert.AreEqual(0,emitter.particleCount,"No stale particles may survive before impact.");
        }
    }
    [Test] public void ThreatPresenterIsHiddenDuringEntranceAndRestoredAfterInterruption()
    {
        var presenters=Object.FindObjectsByType<RearThreatPresenter>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        Assert.IsNotEmpty(presenters);var enabled=presenters.Select(p=>p.enabled).ToArray();
        _intro.Sample(4);foreach(var presenter in presenters)Assert.False(presenter.enabled);
        _intro.RestorePresentation();for(int i=0;i<presenters.Length;i++)Assert.AreEqual(enabled[i],presenters[i].enabled);
    }
    [Test] public void ApprovedLandingRemainsStillDuringItsHold()
    {
        _intro.Sample(3f);var bones=_visual.GetComponentsInChildren<Transform>(true);var rotations=bones.Select(t=>t.localRotation).ToArray();var positions=bones.Select(t=>t.localPosition).ToArray();
        _intro.Sample(4.9f);
        for(int i=0;i<bones.Length;i++){Assert.Less(Quaternion.Angle(rotations[i],bones[i].localRotation),.01f);Assert.Less(Vector3.Distance(positions[i],bones[i].localPosition),.001f);}
    }
    [Test] public void CameraFinishesBehindFinalFacingAndMatchesLiveOrbit()
    {
        var player=Get<Transform>("_player");_intro.Sample(_intro.Duration);
        Assert.Greater(Vector3.Dot(_camera.transform.forward,player.forward),.85f);
        Assert.Less(Vector3.Dot(_camera.transform.position-player.position,player.forward),-1f);
        var camera=_camera.GetComponent<ThirdPersonCamera>();
        camera.GetFollowPose(Get<Vector3>("_readyPlayerPosition"),_camera.transform.rotation,out var position,out var rotation);
        Assert.Less(Vector3.Distance(_camera.transform.position,position),.005f);
        Assert.Less(Quaternion.Angle(_camera.transform.rotation,rotation),.01f);
    }
    [Test] public void CraterDustAndShadowShareThePosedGroundContactAnchor()
    {
        _intro.Sample(4f);Vector3 crater=Get<GameObject>("_impact").transform.position;
        foreach(var position in new[]{Get<Transform>("_shadow").position,Get<ParticleSystem>("_dust").transform.position})
            Assert.Less(Vector2.Distance(new Vector2(crater.x,crater.z),new Vector2(position.x,position.z)),.001f);
        float min=float.PositiveInfinity;var mesh=new Mesh();
        var points=new System.Collections.Generic.List<Vector3>();
        foreach(var skin in _visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            skin.BakeMesh(mesh);foreach(var vertex in mesh.vertices){var p=skin.transform.TransformPoint(vertex);points.Add(p);min=Mathf.Min(min,p.y);}
        }
        Object.DestroyImmediate(mesh);
        var contact=points.Where(p=>p.y<min+.10f).ToArray();Assert.IsNotEmpty(contact);
        Assert.That(crater.x,Is.InRange(contact.Min(p=>p.x),contact.Max(p=>p.x)));
        Assert.That(crater.z,Is.InRange(contact.Min(p=>p.z),contact.Max(p=>p.z)));
    }

    [Test] public void SupportingPalmActuallyTouchesTerrainThroughoutPush()
    {
        var player=Get<Transform>("_player");Assert.True(Physics.Raycast(player.position+Vector3.up*5,Vector3.down,out var hit,30,~(1<<player.gameObject.layer)));
        var mesh=new Mesh();
        try
        {
            for(int frame=186;frame<=199;frame++)
            {
                _intro.Sample(frame/30f);float minimum=float.PositiveInfinity;
                foreach(var skin in _visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.BakeMesh(mesh);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
                    for(int vertex=0;vertex<vertices.Length;vertex++)
                        if(weights[vertex].weight0>.7f && skin.bones[weights[vertex].boneIndex0].name=="hand.R")minimum=Mathf.Min(minimum,skin.transform.TransformPoint(vertices[vertex]).y);
                }
                Assert.That(minimum-hit.point.y,Is.InRange(-.005f,.035f),$"The supporting palm must touch terrain at {frame/30f:F3}s");
            }
        }
        finally{Object.DestroyImmediate(mesh);}
    }

}

