using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Deterministic scene preview. It samples the same authored clip/camera without starting a run.</summary>
public static class EntranceCinematicPreview
{
    public static void InspectSurface()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var intro=Object.FindFirstObjectByType<EntranceCinematic>();
        var visual=(Transform)typeof(EntranceCinematic).GetField("_visual",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro);
        var player=Object.FindFirstObjectByType<PlayerMovement>();
        Physics.Raycast(player.transform.position+Vector3.up*5,Vector3.down,out var hit,30,~(1<<player.gameObject.layer));
        Debug.Log($"RECOVERY_SURFACE floor={hit.point} collider={hit.collider?.name} player={player.transform.position}");
        foreach(float time in new[]{4f,5f,5.25f,5.5f,5.85f,6.05f,6.35f,7.4f,8.45f,9.35f})
        {
            intro.Sample(time);float minimum=float.PositiveInfinity;
            foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=new Mesh();skin.BakeMesh(mesh);
                foreach(var v in mesh.vertices)minimum=Mathf.Min(minimum,skin.transform.TransformPoint(v).y);
                Object.DestroyImmediate(mesh);
            }
            Debug.Log($"RECOVERY_SURFACE time={time} minimum={minimum} visual={visual.position} rotation={visual.localEulerAngles}");
        }
        intro.RestorePresentation();
    }
    public static void InspectRecovery()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var intro=Object.FindFirstObjectByType<EntranceCinematic>();
        var visual=(Transform)typeof(EntranceCinematic).GetField("_visual",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro);
        foreach(float time in new[]{4f,5f,5.8f,6.3f,7.2f,7.8f,9f})
        {
            intro.Sample(time);
            foreach(var bone in visual.GetComponentsInChildren<Transform>(true))
                if(bone.name=="spine"||bone.name=="spine.006"||bone.name=="shin.L"||bone.name=="shin.R"||bone.name=="foot.L"||bone.name=="foot.R"||bone.name=="hand.L"||bone.name=="hand.R")
                    Debug.Log($"RECOVERY {time:F2} {bone.name}: model={visual.InverseTransformPoint(bone.position).ToString("F3")} world={bone.position.ToString("F3")} rootRotation={visual.localEulerAngles}");
        }
        intro.RestorePresentation();
    }
    public static void RebuildAndCapture(){EntranceCinematicAuthoring.Build();Capture();}
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var intro=Object.FindFirstObjectByType<EntranceCinematic>();
        var camera=(Camera)typeof(EntranceCinematic).GetField("_camera",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro);
        var dust=(ParticleSystem)typeof(EntranceCinematic).GetField("_dust",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro);
        var visual=(Transform)typeof(EntranceCinematic).GetField("_visual",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro);
        var skins=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach(var skin in skins){skin.updateWhenOffscreen=true;var property=typeof(SkinnedMeshRenderer).GetProperty("forceMatrixRecalculationPerRender");property?.SetValue(skin,true);}
        var gameplayCamera=camera.GetComponent<ThirdPersonCamera>();
        gameplayCamera.SetLookBlockedByUi(true);
        foreach(string method in new[]{"OnEnable","Start","Update","LateUpdate"})typeof(ThirdPersonCamera).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplayCamera,null);
        string output=System.Environment.GetEnvironmentVariable("ENTRANCE_PREVIEW_OUTPUT") ?? Path.GetFullPath("../entrance-preview-frames");Directory.CreateDirectory(output);
        var rt=new RenderTexture(960,540,24);var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
        camera.targetTexture=rt;
        dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        for(int frame=0;frame<=156;frame++)
        {
            float time=frame/12f;intro.Sample(time);

            foreach(var skin in skins){var bake=new Mesh();skin.BakeMesh(bake);Object.DestroyImmediate(bake);}
            if(frame==44)Debug.Log("ENTRANCE_BOUNDS player="+((Transform)typeof(EntranceCinematic).GetField("_player",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro)).position+" visual="+((Transform)typeof(EntranceCinematic).GetField("_visual",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro)).position+" mesh="+Object.FindFirstObjectByType<PlayerMovement>().GetComponentInChildren<SkinnedMeshRenderer>().bounds);
            if(time>=EntranceCinematic.ImpactTime)dust.Simulate(time-EntranceCinematic.ImpactTime,true,true,false);
            else dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();
            File.WriteAllBytes(Path.Combine(output,$"entrance-{frame:0000}.png"),texture.EncodeToPNG());
        }
        RenderTexture.active=null;camera.targetTexture=null;intro.RestorePresentation();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);
    }
}
