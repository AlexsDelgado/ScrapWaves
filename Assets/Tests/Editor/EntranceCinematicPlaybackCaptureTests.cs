using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

/// <summary>Records the real startup coroutine in Play Mode; never samples the clip manually.</summary>
public sealed class EntranceCinematicPlaybackCaptureTests
{
    [UnityTest]
    public IEnumerator ActualStartupPlaybackShowsRecoveryBeforeWeaponSelection()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        new GameObject("Title settings playback fixture",typeof(UserSettingsService));
        yield return new EnterPlayMode();
        // Create all recording closures after Unity's domain reload. Compiler-generated
        // closure objects created before EnterPlayMode are not restored by the runner.
        yield return RecordLiveStartup();
        yield return new ExitPlayMode();
    }
    private IEnumerator RecordLiveStartup()
    {
        var intro=Object.FindFirstObjectByType<EntranceCinematic>();
        Assert.True(intro!=null,"Live entrance director missing after EnterPlayMode");
        var focusMethod=typeof(EntranceCinematic).GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        Assert.NotNull(focusMethod,"Live focus method missing");
        focusMethod.Invoke(intro,new object[]{true});
        var keyboard=Keyboard.current;
        if(keyboard!=null)InputSystem.ResetDevice(keyboard);
        if(Mouse.current!=null)InputSystem.ResetDevice(Mouse.current);
        var capturedField=typeof(EntranceCinematic).GetField("_captured",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        Assert.NotNull(capturedField,"Live capture field missing");
        while(!(bool)capturedField.GetValue(intro))yield return null;
        yield return null;yield return null;
        T Bound<T>(string field) where T:UnityEngine.Object
        {
            var member=typeof(EntranceCinematic).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
            Assert.NotNull(member,"Director field missing: "+field);
            var value=(T)member.GetValue(intro);Assert.True(value!=null,"Director reference missing/destroyed: "+field);
            return value;
        }
        var player=Bound<Transform>("_player").GetComponent<PlayerMovement>();Assert.NotNull(player,"Bound movement missing");
        var choice=player.GetComponent<LevelUpChoiceUI>();Assert.NotNull(choice,"Bound selector missing");
        var visual=Bound<Transform>("_visual");var camera=Bound<Camera>("_camera");
        var driver=player.GetComponent<PlayerAnimationDriver>();Assert.NotNull(driver);
        Debug.Log($"LIVE_CAPTURE_BOUND player={player.name} visual={visual.name} camera={camera.name} enabled={camera.enabled}");
        var dust=Bound<ParticleSystem>("_dust");
        var emitters=dust.GetComponentsInChildren<ParticleSystem>(true);
        bool sawDustAfterContact=false,sawScrapAfterContact=false;
        var mesh=new Mesh();
        float floor=0f;
        if(Physics.Raycast(player.transform.position+Vector3.up*5,Vector3.down,out var hit,30,~(1<<player.gameObject.layer),QueryTriggerInteraction.Ignore))floor=hit.point.y;
        var previousTarget=camera.targetTexture;
        var target=new RenderTexture(960,540,24);
        var pixels=new Texture2D(960,540,TextureFormat.RGB24,false);
        var previousActive=RenderTexture.active;
        string output=System.Environment.GetEnvironmentVariable("ENTRANCE_PREVIEW_OUTPUT") ?? Path.GetFullPath("../entrance-playback-frames");Directory.CreateDirectory(output);
        var trace=new List<string>{"frame,realtime,pelvis_y,head_y,left_hand_y,left_knee_y,right_knee_y,left_foot_y,right_foot_y,finished,choice_visible,player_y,visual_y,visual_local_y,cinematic_time,dust_count,scrap_count,entrance_idle_held,left_arm_drop,right_arm_drop"};
        Transform Bone(string name)=>Array.Find(visual.GetComponentsInChildren<Transform>(true),t=>t.name==name);
        double start=Time.realtimeSinceStartupAsDouble;int frame=0;
        camera.targetTexture=target;
        // Render the real overlay selector/HUD into the review video as well.
        foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+1f;}
        Vector3 lastVisual=visual.position,lastHead=Bone("spine.006").position,lastCamera=camera.transform.position;
        bool previousFinished=false,selected=false;float maxResumedVisualStep=0,maxResumedCameraStep=0,maxResumedHeadStep=0;
        int secondLandingEvents=0;player.OnLanded+=()=>secondLandingEvents++;
        var continuity=new List<string>{"realtime,stage,visual_world_step,head_world_step,camera_world_step,player_x,player_y,player_z"};
        try
        {
            while(frame<=358)
            {
                double elapsed=Time.realtimeSinceStartupAsDouble-start;
                int dustCount=emitters[0].particleCount,scrapCount=emitters.Length>1?emitters[1].particleCount:0;
                if(intro.PlaybackTime<EntranceCinematic.ImpactTime)
                {
                    Assert.AreEqual(0,dustCount,"Dust fired before ground contact.");
                    Assert.AreEqual(0,scrapCount,"Scrap fired before ground contact.");
                }
                else if(!intro.IsFinished && intro.PlaybackTime<EntranceCinematic.ImpactTime+.4f)
                {
                    sawDustAfterContact|=dustCount>0;sawScrapAfterContact|=scrapCount>0;
                    float minimum=float.PositiveInfinity;
                    foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        skin.BakeMesh(mesh);foreach(var vertex in mesh.vertices)minimum=Mathf.Min(minimum,skin.transform.TransformPoint(vertex).y);
                    }
                    Assert.Less(minimum-floor,.045f,"Impact effect began before the mesh reached the floor.");
                }
                if(intro.IsFinished && !previousFinished)
                {
                    float visualStep=Vector3.Distance(lastVisual,visual.position),headStep=Vector3.Distance(lastHead,Bone("spine.006").position),cameraStep=Vector3.Distance(lastCamera,camera.transform.position);
                    continuity.Add(FormattableString.Invariant($"{elapsed:F5},handoff,{visualStep:F6},{headStep:F6},{cameraStep:F6},{player.transform.position.x:F5},{player.transform.position.y:F5},{player.transform.position.z:F5}"));
                    Assert.Less(visualStep,.005f,"Standing visual must not teleport at the natural handoff.");
                    Assert.Less(headStep,.035f,"Standing rig must remain continuous at the natural handoff.");
                    Assert.Less(cameraStep,.035f,"Camera must remain continuous at the natural handoff.");
                }
                if(intro.PlaybackTime>=9.4f)
                {
                    foreach(string side in new[]{"L","R"})
                        Assert.Less(Bone("hand."+side).position.y,Bone("upper_arm."+side).position.y-.48f,"Live startup must keep relaxed arms: "+side);
                    if(intro.IsFinished)Assert.True(driver.IsEntranceIdleHeld,"Equipping a weapon must not force aim without new gameplay input.");
                }
                if(selected)
                {
                    maxResumedVisualStep=Mathf.Max(maxResumedVisualStep,Vector3.Distance(lastVisual,visual.position));
                    maxResumedHeadStep=Mathf.Max(maxResumedHeadStep,Vector3.Distance(lastHead,Bone("spine.006").position));
                    maxResumedCameraStep=Mathf.Max(maxResumedCameraStep,Vector3.Distance(lastCamera,camera.transform.position));
                }
                previousFinished=intro.IsFinished;lastVisual=visual.position;lastHead=Bone("spine.006").position;lastCamera=camera.transform.position;
                if(!selected && elapsed>=13.35 && choice.IsVisible)
                {
                    Assert.True(intro.IsFinished);Assert.True(EntranceCinematic.StartupHeld);Assert.AreEqual(0,Time.timeScale);
                    Assert.AreEqual(0,player.GetComponent<WeaponManager>().GetEquippedWeapons().Count);
                    typeof(LevelUpChoiceUI).GetMethod("OnOptionClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choice,new object[]{0});selected=true;
                }
                if(elapsed>=frame/24d)
                {
                    if(elapsed>=9.5 && elapsed<=12.5)
                    {
                        foreach(string name in new[]{"spine.006","foot.L","foot.R"})
                        {
                            Vector3 point=camera.WorldToViewportPoint(Bone(name).position);
                            Assert.That(point.x,Is.InRange(0f,1f),"Live camera return: "+name);Assert.That(point.y,Is.InRange(0f,1f),"Live camera return: "+name);
                        }
                    }
                    foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                        if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+1f;}
                    camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(output,$"entrance-{frame:0000}.png"),pixels.EncodeToPNG());
                    trace.Add(FormattableString.Invariant($"{frame},{elapsed:F4},{Bone("spine").position.y:F4},{Bone("spine.006").position.y:F4},{Bone("hand.L").position.y:F4},{Bone("shin.L").position.y:F4},{Bone("shin.R").position.y:F4},{Bone("foot.L").position.y:F4},{Bone("foot.R").position.y:F4},{intro.IsFinished},{choice.IsVisible},{player.transform.position.y:F5},{visual.position.y:F5},{visual.localPosition.y:F5},{intro.PlaybackTime:F5},{dustCount},{scrapCount},{driver.IsEntranceIdleHeld},{Bone("upper_arm.L").position.y-Bone("hand.L").position.y:F5},{Bone("upper_arm.R").position.y-Bone("hand.R").position.y:F5}"));
                    frame++;
                }
                yield return null;
            }
            Assert.True(sawDustAfterContact,"Dust burst missing after contact.");Assert.True(sawScrapAfterContact,"Scrap burst missing after contact.");
            Assert.True(intro.IsFinished);Assert.True(selected);Assert.False(choice.IsVisible);
            Assert.False(EntranceCinematic.StartupHeld);Assert.AreEqual(1,Time.timeScale);
            Assert.AreEqual(1,player.GetComponent<WeaponManager>().GetEquippedWeapons().Count);
            Assert.Less(maxResumedVisualStep,.035f,"First resumed gameplay must not produce a second visual drop.");
            Assert.AreEqual(0,secondLandingEvents,"The authored landing must not retrigger when gameplay resumes.");
            Assert.Less(maxResumedHeadStep,.05f,"The standing rig must not dip into a second landing animation.");
            Assert.Less(maxResumedCameraStep,.06f,"First resumed gameplay camera must stay continuous.");
            continuity.Add(FormattableString.Invariant($"{Time.realtimeSinceStartupAsDouble-start:F5},resumed_max,{maxResumedVisualStep:F6},{maxResumedHeadStep:F6},{maxResumedCameraStep:F6},{player.transform.position.x:F5},{player.transform.position.y:F5},{player.transform.position.z:F5}"));
            File.WriteAllLines(Path.Combine(output,"handoff-continuity.csv"),continuity);
            File.WriteAllLines(Path.Combine(output,"actual-playback-trace.csv"),trace);
            File.WriteAllText(Path.Combine(output,"capture-notes.txt"),"Actual Play Mode startup, live EntranceCinematic.PlayEntrance coroutine, 24 fps camera captures over 14.96 seconds, including selector resolution and first resumed gameplay. No manual Sample calls and no pose substitution. Real overlay canvases are routed to the capture camera for HUD/selection recording. Selection visibility and startup gating are verified in the test and trace. Audio in the delivered video uses the authored whistle/thud assets.");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(output,"actual-playback-trace.csv"),trace);
            File.WriteAllLines(Path.Combine(output,"handoff-continuity.csv"),continuity);
            camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
            Object.Destroy(target);Object.Destroy(pixels);Object.Destroy(mesh);
            foreach(var menu in Object.FindObjectsByType<PauseMenuUI>(FindObjectsInactive.Include,FindObjectsSortMode.None))menu.enabled=false;
        }
    }
}
