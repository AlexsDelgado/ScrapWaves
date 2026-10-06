#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Disposable cameras show the authored scene and real VFX; never saves or changes the scene.</summary>
public static class GeyserPreviewCapture
{
    public const string Output = ".utmp/geyser-preview";
    public static void Pilot() => Capture(true);
    public static void RenderLoops() => Capture(false);
    private static void Capture(bool pilot)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture outside Play Mode.");
        Directory.CreateDirectory(Output);
        Scene previous = SceneManager.GetActiveScene();
        var scene=EditorSceneManager.OpenScene(GeyserModelAuthoring.ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RenderTexture target=null;Texture2D image=null;GameObject cameraObject=null,actor=null;
        Mesh[] baked=null;
        try
        {
            foreach(GameObject root in scene.GetRootGameObjects())
            {
                foreach(Canvas canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.enabled=false;
                if(root.GetComponent<PlayerMovement>()!=null)
                {
                    foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
                    foreach(Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
                }
            }
            actor=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"),scene);
            actor.transform.localScale=Vector3.one*1.2f;
            foreach(MonoBehaviour script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled=false;
            foreach(Collider collider in actor.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
            actor.GetComponent<Rigidbody>().isKinematic=true;
            GameObject visual=actor.transform.Find("PlaceholderPlayerVisual").gameObject;
            visual.GetComponent<Animator>().runtimeAnimatorController=null;
            AnimationClip idle=AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath).OfType<AnimationClip>().Single(c=>c.name=="Idle");
            var skins=visual.GetComponentsInChildren<SkinnedMeshRenderer>();baked=new Mesh[skins.Length];
            for(int i=0;i<skins.Length;i++)
            {
                baked[i]=new Mesh();var go=new GameObject("Disposable posed skin");go.transform.SetParent(skins[i].transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=baked[i];go.AddComponent<MeshRenderer>().sharedMaterials=skins[i].sharedMaterials;skins[i].enabled=false;
            }
            cameraObject=new GameObject("Disposable scene geyser camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);
            Camera camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cameraType=CameraType.Preview;
            camera.fieldOfView=58f;camera.nearClipPlane=.05f;camera.farClipPlane=600f;camera.cullingMask=~(1<<5);
            var data=camera.GetUniversalAdditionalCameraData();data.requiresColorOption=CameraOverrideOption.On;data.requiresDepthOption=CameraOverrideOption.On;data.renderPostProcessing=true;
            target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
            image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var metrics=new StringBuilder("variant,frame,ambient_particles,entry_particles,total_particles\n");
            foreach(string name in new[]{"Jump floor","Jump sky"})
            {
                GameObject root=scene.GetRootGameObjects().Single(g=>g.name==name);
                GeyserVfx fx=root.GetComponentInChildren<GeyserVfx>();fx.ApplyTuning();
                var particles=fx.GetComponentsInChildren<ParticleSystem>();
                ParticleSystem burst=particles.Single(p=>p.name=="EntryBurst");
                string variant=name=="Jump floor"?"TrashGeyser":"HotAirGeyser";string folder=Output+"/"+variant;Directory.CreateDirectory(folder);
                Vector3 origin=root.transform.position;
                Physics.SyncTransforms();
                // Choose an unobstructed view in the authored blockout rather than clipping through its walls.
                Vector3 cameraOffset=new(-11f,8f,-12f);
                for(int angle=0;angle<12;angle++)
                {
                    Vector3 candidate=Quaternion.AngleAxis(angle*30f,Vector3.up)*new Vector3(-11f,8f,-12f);
                    Vector3 sight=candidate-Vector3.up*2.5f;
                    if(!Physics.Raycast(origin+Vector3.up*2.5f,sight.normalized,sight.magnitude,129,QueryTriggerInteraction.Ignore))
                    {cameraOffset=candidate;break;}
                }
                Vector3 towardCamera=Vector3.ProjectOnPlane(cameraOffset,Vector3.up).normalized;
                Vector3 near=origin+towardCamera*3.7f+Vector3.Cross(Vector3.up,towardCamera)*1.1f;
                Physics.SyncTransforms();float groundY=origin.y-1f;
                if(Physics.Raycast(near+Vector3.up*15,Vector3.down,out RaycastHit contact,30f,129,QueryTriggerInteraction.Ignore))groundY=contact.point.y;
                actor.transform.position=new Vector3(near.x,groundY+.9f,near.z);
                Vector3 actorFacing=origin-actor.transform.position;actorFacing.y=0;
                actor.transform.rotation=Quaternion.LookRotation(actorFacing,Vector3.up);
                camera.transform.position=origin+cameraOffset;camera.transform.LookAt(origin+Vector3.up*3.2f);
                foreach(ParticleSystem system in particles)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(!pilot)foreach(ParticleSystem system in particles.Where(p=>p!=burst))system.Simulate(3f,false,true,false);
                int frames=pilot?2:144;
                for(int frame=0;frame<frames;frame++)
                {
                    float time=pilot?(frame==0?1.5f:3.2f):frame/24f;
                    if(pilot)
                    {
                        foreach(ParticleSystem system in particles.Where(p=>p!=burst))system.Simulate(3f+time,false,true,false);
                        burst.Simulate(0,false,true,false);
                        if(time>=3f)
                        {
                            fx.PlayEntryBurst();burst.Simulate(time-3f,false,false,false);
                            typeof(GeyserVfx).GetMethod("TickPresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fx,new object[]{time-3f});
                        }
                    }
                    else
                    {
                        // Advance native particles continuously; emit exactly one real entry event per segment.
                        float delta=frame==0?0f:1f/24f;
                        typeof(GeyserVfx).GetMethod("TickPresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fx,new object[]{delta});
                        if(frame==72)fx.PlayEntryBurst();
                        foreach(ParticleSystem system in particles.Where(p=>p!=burst))system.Simulate(delta,false,false,false);
                        if(frame>=72)burst.Simulate(frame==72?0f:delta,false,false,false);
                    }
                    fx.SetPreviewClock(time);
                    idle.SampleAnimation(visual,time%idle.length);
                    for(int i=0;i<skins.Length;i++){skins[i].BakeMesh(baked[i]);baked[i].RecalculateBounds();}
                    RenderTexture previousTarget=RenderTexture.active;
                    camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                    File.WriteAllBytes(folder+"/"+(pilot?(frame==0?"pilot-idle.png":"pilot-burst.png"):$"frame-{frame:D4}.png"),image.EncodeToPNG());
                    camera.targetTexture=null;RenderTexture.active=previousTarget;
                    metrics.AppendLine($"{variant},{frame},{particles.Where(p=>p!=burst).Sum(p=>p.particleCount)},{burst.particleCount},{fx.ActiveParticleCount}");
                    if(frame%48==0)Debug.Log($"GEYSER_PREVIEW {variant} {frame}/{frames}");
                }
            }
            File.WriteAllText(Output+"/"+(pilot?"pilot-metrics.csv":"capture-metrics.csv"),metrics.ToString());
            File.WriteAllText(Output+"/limits.txt","Actual authored GameplayScene geometry, production player visual for scale, original lighting and URP materials.\nControlled VFX preview: persistent updraft at 0-3s, actual entry burst at 3s, then recovery. 24fps; six seconds per variant.\nPlayer is an in-place posed scale reference; this is not human gameplay, input or controller simulation. Native physics tests independently validate entry and both complete ballistic routes.\nNo scene, prefab or asset is saved by this capture.\n");
            Debug.Log("GEYSER_PREVIEW_COMPLETE "+Path.GetFullPath(Output));
        }
        finally
        {
            if(baked!=null)foreach(Mesh mesh in baked)if(mesh!=null)Object.DestroyImmediate(mesh);
            if(image!=null)Object.DestroyImmediate(image);if(target!=null)Object.DestroyImmediate(target);
            if(cameraObject!=null)Object.DestroyImmediate(cameraObject);if(actor!=null)Object.DestroyImmediate(actor);
            EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
        }
    }
}
#endif
