using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GeyserIntegrationTests
{
    [SetUp] public void Setup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    [TestCase("TrashGeyser",GeyserVfx.Kind.Trash)]
    [TestCase("HotAirGeyser",GeyserVfx.Kind.HotAir)]
    public void ImportedMounds_KeepMeterScaleWalkableCollisionAndBudgets(string name, GeyserVfx.Kind kind)
    {
        GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GeyserModelAuthoring.Folder+"/Prefabs/"+name+"_Visual.prefab");
        Assert.That(prefab,Is.Not.Null);
        GameObject instance=Object.Instantiate(prefab);
        try
        {
            Physics.SyncTransforms();
            var visible=instance.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.transform.name!="HeatShimmer").ToArray();
            Assert.That(visible.Length,Is.EqualTo(1));
            Bounds visual=visible[0].bounds;
            MeshCollider collider=instance.GetComponentInChildren<MeshCollider>();
            Assert.That(visual.size.x,Is.InRange(9f,10.1f)); Assert.That(visual.size.z,Is.InRange(9f,10.1f));
            Assert.That(visual.size.y,Is.InRange(1.9f,2.2f));
            Assert.That(collider.isTrigger,Is.False);Assert.That(collider.convex,Is.False);
            Assert.That(Vector3.Distance(visual.center,collider.bounds.center),Is.LessThan(.15f));
            Assert.That(collider.sharedMesh.triangles.Length/3,Is.LessThan(400));
            foreach(MeshFilter filter in visible.Select(r=>r.GetComponent<MeshFilter>()))
            {
                ulong indices=0;for(int i=0;i<filter.sharedMesh.subMeshCount;i++)indices+=filter.sharedMesh.GetIndexCount(i);
                Assert.That(indices/3,Is.LessThan(1500));
            }
            // Walkable support exists away from the opening; the former invisible deck is gone.
            Assert.That(Physics.Raycast(new Vector3(3f,4f,0),Vector3.down,out RaycastHit hit,6f),Is.True);
            Assert.That(hit.collider,Is.EqualTo(collider));Assert.That(Vector3.Angle(hit.normal,Vector3.up),Is.LessThan(50f));
            GeyserVfx vfx=instance.GetComponent<GeyserVfx>();vfx.ApplyTuning();
            Assert.That(vfx.GeyserKind,Is.EqualTo(kind));Assert.That(vfx.MaximumParticleBudget,Is.LessThanOrEqualTo(108));
            foreach(ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>())
            {
                Assert.That(particles.main.startDelay.constant,Is.Zero);
                if(particles.name=="EntryBurst")continue;
                Assert.That(particles.main.loop,Is.True);Assert.That(particles.emission.rateOverTime.constant,Is.GreaterThan(0));
                particles.Simulate(12f,true,true,false);
                Assert.That(particles.particleCount,Is.GreaterThan(0));Assert.That(particles.particleCount,Is.LessThanOrEqualTo(particles.main.maxParticles));
            }
            vfx.PlayEntryBurst();
            var burst=instance.GetComponentsInChildren<ParticleSystem>().Single(p=>p.name=="EntryBurst");
            Assert.That(burst.particleCount,Is.GreaterThanOrEqualTo(20));
            Assert.That(vfx.ActiveParticleCount,Is.LessThanOrEqualTo(vfx.MaximumParticleBudget));
            Assert.That(instance.GetComponentsInChildren<Rigidbody>(),Is.Empty,"Debris must remain inexpensive presentation particles.");
        }
        finally {Object.DestroyImmediate(instance);}
    }

    [Test]
    public void Scene_RetainsAllFourRootTransformsDestinationsAndReceivingPads()
    {
        var scene=EditorSceneManager.OpenScene(GeyserModelAuthoring.ScenePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var expected=new (string name,Vector3 position)[]{("Jump floor",new(-45.7f,.16f,184.57f)),("Land floor",new(-12.36f,68.98f,191.75f)),("Jump sky",new(22.74f,67.9f,191.8f)),("Land sky",new(159.72f,97.9f,154.97f))};
            foreach(var item in expected)
            {
                GameObject root=scene.GetRootGameObjects().Single(g=>g.name==item.name);
                Assert.That(Vector3.Distance(root.transform.position,item.position),Is.LessThan(.001f));
                Assert.That(root.transform.localScale,Is.EqualTo(new Vector3(10,2,10)));
                if(item.name.StartsWith("Land"))
                {
                    Assert.That(root.GetComponent<BoxCollider>().isTrigger,Is.False);
                    Assert.That(root.GetComponentInChildren<GeyserVfx>(),Is.Null);Assert.That(root.transform.Find("PadVisual"),Is.Not.Null);
                }
                else
                {
                    var serialized=new SerializedObject(root.GetComponent<MapLaunchPad>());
                    var landing=(Transform)serialized.FindProperty("_landing").objectReferenceValue;
                    Assert.That(landing.name,Is.EqualTo(item.name.Replace("Jump","Land")));
                    Assert.That(serialized.FindProperty("_apexClearance").floatValue,Is.EqualTo(12));
                    Assert.That(root.transform.Find("PadVisual"),Is.Null);
                }
            }
        }
        finally{EditorSceneManager.CloseScene(scene,true);}
    }

    [Test]
    public void Shaders_CompileAndHotAirUsesAvailableOpaqueSceneColor()
    {
        foreach(string name in new[]{"ScrapWaves/Level/Geyser Wisp","ScrapWaves/Level/Geyser Heat Shimmer","ScrapWaves/Level/Geyser Debris"})
        {
            Shader shader=Shader.Find(name);Assert.That(shader,Is.Not.Null);Assert.That(ShaderUtil.ShaderHasError(shader),Is.False);
        }
        var pipeline=new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"));
        Assert.That(pipeline.FindProperty("m_RequireOpaqueTexture").boolValue,Is.True);
        Assert.That(pipeline.FindProperty("m_RequireDepthTexture").boolValue,Is.True);
    }
}
