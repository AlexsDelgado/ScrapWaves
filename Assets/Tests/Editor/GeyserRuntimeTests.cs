using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class GeyserRuntimeTests
{
    private sealed class PhysicsTicks : MonoBehaviour { public int Count; private void FixedUpdate()=>Count++; }

    [UnityTest]
    public IEnumerator BothOpenings_LaunchImmediatelyCompleteOriginalRoutesAndRearmOnExit()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        CopyAuthoredWorldCollision();
        yield return new EnterPlayMode();
        // Acquire runtime references after the test runner's domain reload.
        yield return ExerciseRoutes();
        yield return new ExitPlayMode();
    }
    private static IEnumerator ExerciseRoutes()
    {
        float oldCapture=Time.captureDeltaTime;
        Keyboard keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();
        GameObject actor=null,source=null,landing=null,floor=null,camera=null;
        try
        {
            Time.timeScale=1f;Time.captureDeltaTime=.02f;GameplayPause.Reset();
            foreach(bool hotAir in new[]{false,true})
            {
                Vector3 origin=hotAir?new Vector3(22.74f,67.9f,191.8f):new Vector3(-45.7f,.16f,184.57f);
                Vector3 destination=hotAir?new Vector3(159.72f,97.9f,154.97f):new Vector3(-12.36f,68.98f,191.75f);
                source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.name=hotAir?"Jump sky":"Jump floor";
                source.transform.position=origin;source.transform.localScale=new Vector3(10,2,10);source.GetComponent<Renderer>().enabled=false;
                BoxCollider trigger=source.GetComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(.29f,1.3f,.29f);trigger.center=new Vector3(0,.35f,0);
                landing=GameObject.CreatePrimitive(PrimitiveType.Cube);landing.transform.position=destination;landing.transform.localScale=new Vector3(10,2,10);
                if(hotAir)landing.transform.rotation=new Quaternion(0,.39269605f,0,.9196684f);
                MapLaunchPad launcher=source.AddComponent<MapLaunchPad>();
                Set(launcher,"_landing",landing.transform);Set(launcher,"_launchFromTrigger",true);Call(launcher,"Awake");
                var visualPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(GeyserModelAuthoring.Folder+"/Prefabs/"+(hotAir?"HotAirGeyser":"TrashGeyser")+"_Visual.prefab");
                GameObject visual=Object.Instantiate(visualPrefab,source.transform);visual.transform.localScale=new Vector3(.1f,.5f,.1f);
                visual.GetComponent<GeyserVfx>().BindLauncher(launcher);
                floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=origin+Vector3.down*1.5f;floor.transform.localScale=new Vector3(100,1,100);
                actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"));actor.transform.localScale=Vector3.one*1.2f;
                foreach(MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                PlayerMovement movement=actor.GetComponent<PlayerMovement>();Rigidbody body=actor.GetComponent<Rigidbody>();
                camera=new GameObject("Physics validation camera",typeof(Camera));Set(movement,"_cameraTransform",camera.transform);movement.enabled=true;
                PhysicsTicks ticks=camera.AddComponent<PhysicsTicks>();
                Physics.SyncTransforms();
                float halfHeight=actor.GetComponent<Collider>().bounds.extents.y;
                Vector3 entry=origin+Vector3.up*(1f+halfHeight);
                body.position=entry;actor.transform.position=entry;body.linearVelocity=Vector3.zero;
                int launches=0,launchTick=0;Vector3 launchStart=default,launchVelocity=default;float flightTime=0;
                launcher.OnLaunched+=(player)=>
                {
                    launches++;launchTick=ticks.Count;launchStart=player.transform.position;launchVelocity=player.CurrentVelocity;
                    Bounds land=landing.GetComponent<Collider>().bounds;
                    halfHeight=player.GetComponent<Collider>().bounds.extents.y;
                    Vector3 target=new(land.center.x,land.max.y+halfHeight+.05f,land.center.z);
                    MapLaunchPad.TryComputeLaunchVelocity(launchStart,target,12f,out _,out flightTime);
                };
                Physics.SyncTransforms();int enteredAt=ticks.Count;
                yield return Until(()=>launches>0,3f);
                Assert.That(launches,Is.EqualTo(1));Assert.That(ticks.Count-enteredAt,Is.LessThanOrEqualTo(3),"The opening has no eruption readiness cycle.");
                Assert.That(movement.IsLaunching,Is.True);
                Assert.That(Array.Find(visual.GetComponentsInChildren<ParticleSystem>(),p=>p.name=="EntryBurst").particleCount,Is.GreaterThan(0),"A real launch drives the authored entry burst.");
                Bounds targetBounds=landing.GetComponent<Collider>().bounds;
                Vector3 targetPoint=new(targetBounds.center.x,targetBounds.max.y+halfHeight+.05f,targetBounds.center.z);
                Vector3 analytical=launchStart+launchVelocity*flightTime+.5f*Physics.gravity*flightTime*flightTime;
                Assert.That(Vector3.Distance(analytical,targetPoint),Is.LessThan(.015f));
                Vector3 planar=Vector3.ProjectOnPlane(destination-origin,Vector3.up);
                Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(launchVelocity,Vector3.up).normalized,planar.normalized),Is.GreaterThan(.9999f));
                int startTicks=ticks.Count;
                yield return Until(()=>!movement.IsLaunching,12f);
                Assert.That(launches,Is.EqualTo(1));Assert.That(movement.PlanarSpeed,Is.LessThan(.02f),"Preserve the pad landing dead stop.");
                Vector3 actualPlanar=Vector3.ProjectOnPlane(body.position,Vector3.up);
                Assert.That(Vector3.Distance(actualPlanar,Vector3.ProjectOnPlane(destination,Vector3.up)),Is.LessThan(1.1f));
                Assert.That(Mathf.Abs(body.position.y-targetPoint.y),Is.LessThan(.2f));
                TestContext.WriteLine($"{source.name}: entry latency={launchTick-enteredAt} physics ticks; launch={launchVelocity}; flight={flightTime:F3}s; simulated ticks={ticks.Count-startTicks}; landing={body.position}; residual speed={movement.PlanarSpeed:F4}; particle budget={visual.GetComponent<GeyserVfx>().MaximumParticleBudget}");
                body.position=entry;actor.transform.position=entry;body.linearVelocity=Vector3.zero;Physics.SyncTransforms();
                yield return Until(()=>launches==2,3f);
                Assert.That(launches,Is.EqualTo(2),"Exit rearms the same continuously available opening.");
                Object.Destroy(actor);Object.Destroy(source);Object.Destroy(landing);Object.Destroy(floor);Object.Destroy(camera);
                actor=source=landing=floor=camera=null;yield return null;
            }
        }
        finally
        {
            if(actor!=null)Object.Destroy(actor);if(source!=null)Object.Destroy(source);if(landing!=null)Object.Destroy(landing);
            if(floor!=null)Object.Destroy(floor);if(camera!=null)Object.Destroy(camera);
            InputSystem.RemoveDevice(keyboard);Time.captureDeltaTime=oldCapture;Time.timeScale=1f;GameplayPause.Reset();
        }
    }
    private static void CopyAuthoredWorldCollision()
    {
        var targetScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var authored=EditorSceneManager.OpenScene(GeyserModelAuthoring.ScenePath,OpenSceneMode.Additive);
        int copied=0;
        try
        {
            foreach(GameObject root in authored.GetRootGameObjects())
            {
                // Launch/landing roots use their tested production prefabs below. Retain every other active map solid.
                if(root.name.StartsWith("Jump ")||root.name.StartsWith("Land ")||root.GetComponent<PlayerMovement>()!=null)continue;
                foreach(Collider source in root.GetComponentsInChildren<Collider>())
                {
                    if(!source.enabled||source.isTrigger||source.GetComponentInParent<PlayerMovement>()!=null)continue;
                    var go=new GameObject("Authored map collision: "+source.name);go.layer=source.gameObject.layer;
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,targetScene);
                    go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.transform.localScale=source.transform.lossyScale;
                    Collider copy;
                    switch(source)
                    {
                        case BoxCollider box: var b=go.AddComponent<BoxCollider>();b.center=box.center;b.size=box.size;copy=b;break;
                        case SphereCollider sphere: var s=go.AddComponent<SphereCollider>();s.center=sphere.center;s.radius=sphere.radius;copy=s;break;
                        case CapsuleCollider capsule: var c=go.AddComponent<CapsuleCollider>();c.center=capsule.center;c.radius=capsule.radius;c.height=capsule.height;c.direction=capsule.direction;copy=c;break;
                        case CharacterController character: var cc=go.AddComponent<CapsuleCollider>();cc.center=character.center;cc.radius=character.radius;cc.height=character.height;copy=cc;break;
                        case MeshCollider mesh: var m=go.AddComponent<MeshCollider>();m.sharedMesh=mesh.sharedMesh;m.convex=mesh.convex;m.cookingOptions=mesh.cookingOptions;copy=m;break;
                        case TerrainCollider terrain: var t=go.AddComponent<TerrainCollider>();t.terrainData=terrain.terrainData;copy=t;break;
                        default: throw new NotSupportedException("Unhandled authored map solid: "+source.GetType());
                    }
                    copy.sharedMaterial=source.sharedMaterial;copied++;
                }
            }
            Assert.That(copied,Is.GreaterThan(20));TestContext.WriteLine($"Native validation retains {copied} active solids from GameplayScene without starting unrelated game systems.");
        }
        finally {EditorSceneManager.CloseScene(authored,true);}
    }
    private static IEnumerator Until(Func<bool> predicate,float timeout)
    {
        float deadline=Time.realtimeSinceStartup+timeout;
        while(!predicate()&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.That(predicate(),Is.True,"Native physics condition timed out.");
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    private static void Call(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if(Application.isPlaying){Time.captureDeltaTime=0;Time.timeScale=1;yield return new ExitPlayMode();}
        // ExitPlayMode restores the Editor scene, including the authored collider copies.
        // Release that disposable world so later trajectory tests start without its map solids.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    }
}
