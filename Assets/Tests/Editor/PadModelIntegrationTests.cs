using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PadModelIntegrationTests
{
    [Test] public void ImportedModels_AreMeterScaledUrpMeshesWithoutColliders()
    {
        foreach(string name in new[]{"JumpPlatform","LandingPlatform"})
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(PadModelAuthoring.Folder+"/Prefabs/"+name+"_Visual.prefab");
            Assert.That(source,Is.Not.Null);var o=UnityEngine.Object.Instantiate(source);
            try
            {
                Assert.That(o.GetComponentsInChildren<Collider>(true),Is.Empty);
                var renderers=o.GetComponentsInChildren<Renderer>();Assert.That(renderers.Length,Is.EqualTo(name=="JumpPlatform"?2:1));
                var bounds=renderers[0].bounds;Assert.That(bounds.size.x,Is.EqualTo(10).Within(.01));Assert.That(bounds.size.y,Is.EqualTo(1.998).Within(.01));Assert.That(bounds.size.z,Is.EqualTo(10).Within(.01));
                foreach(var material in renderers[0].sharedMaterials){Assert.That(material,Is.Not.Null);Assert.That(material.shader.name,Is.EqualTo("Universal Render Pipeline/Lit"));Assert.That(AssetDatabase.GetAssetPath(material),Does.StartWith(PadModelAuthoring.Folder+"/Materials/"));}
            }finally{UnityEngine.Object.DestroyImmediate(o);}
        }
    }
    [Test] public void GameplayPads_PreserveCollidersDestinationsAndAutomaticContactLaunch()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity",OpenSceneMode.Additive);
        try
        {
            var roots=scene.GetRootGameObjects();var pads=roots.Where(g=>g.name.StartsWith("Jump ")||g.name.StartsWith("Land ")).ToArray();Assert.That(pads.Length,Is.EqualTo(4));
            foreach(var pad in pads)
            {
                Assert.That(pad.GetComponent<MeshRenderer>().enabled,Is.False);var visual=pad.transform.Find("PadVisual");Assert.That(visual,Is.Not.Null);
                Assert.That(pad.GetComponentsInChildren<Collider>().Length,Is.EqualTo(1));var collider=pad.GetComponent<BoxCollider>();Assert.That(collider.enabled,Is.True);Assert.That(collider.isTrigger,Is.False);Assert.That(collider.size,Is.EqualTo(Vector3.one));Assert.That(collider.center,Is.EqualTo(Vector3.zero));Assert.That(pad.transform.localScale,Is.EqualTo(new Vector3(10,2,10)));
                Assert.That(visual.GetComponentsInChildren<Renderer>().All(r=>r.enabled&&r.gameObject.activeInHierarchy),Is.True);
                if(!pad.name.StartsWith("Jump ")){Assert.That(pad.GetComponent<MapLaunchPad>(),Is.Null);continue;}
                var launcher=pad.GetComponent<MapLaunchPad>();Assert.That(launcher,Is.Not.Null);
                var serialized=new SerializedObject(launcher);var landing=(Transform)serialized.FindProperty("_landing").objectReferenceValue;
                Assert.That(landing.name,Is.EqualTo(pad.name.Replace("Jump ","Land ")));Assert.That(serialized.FindProperty("_apexClearance").floatValue,Is.EqualTo(12));
                var direction=landing.GetComponent<Collider>().bounds.center-collider.bounds.center;direction.y=0;
                var markings=visual.Find("LaunchMarkings");Assert.That(markings,Is.Not.Null);
                Assert.That(Vector3.Dot(markings.forward,direction.normalized),Is.GreaterThan(.9999f),"Chevrons must point along this pad's horizontal trajectory.");
                Assert.That(visual.localRotation,Is.EqualTo(Quaternion.identity),"Keep the square deck aligned with the collision footprint.");
                Vector3 start=collider.bounds.center+Vector3.up*2;Vector3 target=landing.GetComponent<Collider>().bounds.center+Vector3.up*2;
                Assert.That(MapLaunchPad.TryComputeLaunchVelocity(start,target,12,out var velocity,out float time),Is.True);
                Assert.That(Vector3.Distance(start+velocity*time+.5f*Physics.gravity*time*time,target),Is.LessThan(.01));
                Assert.That(typeof(MapLaunchPad).GetMethod("OnCollisionStay",BindingFlags.Instance|BindingFlags.NonPublic),Is.Not.Null);
            }
        }finally{EditorSceneManager.CloseScene(scene,true);}
    }
    [Test] public void ImportedChevronVertices_ConfirmForwardDirection()
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(PadModelAuthoring.Folder+"/Prefabs/JumpPlatform_Visual.prefab");var o=UnityEngine.Object.Instantiate(source);
        try
        {
            var filter=o.transform.Find("LaunchMarkings").GetComponent<MeshFilter>();var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();int index=0;
            var centers=mesh.GetTriangles(index).Select(i=>filter.transform.TransformPoint(mesh.vertices[i])).Where(p=>Mathf.Abs(p.x)<.001f&&p.y>.97f).ToArray();
            Assert.That(mesh.triangles.Length,Is.EqualTo(36),"All three chevrons must survive import.");
            Assert.That(mesh.normals.All(n=>n.y>.99f),Is.True,"Every painted chevron must face upward and remain visible in Unity.");
            Assert.That(mesh.vertexCount,Is.EqualTo(18),"Keep the small painted marking mesh compact.");
            // Middle chevron's two apex points were authored at Blender +Y=.35 and +1.02.
            // Confirm those are Unity +Z, rather than assuming the FBX axis conversion.
            string evidence=string.Join("; ",centers.Distinct().Select(p=>p.ToString("F3")));
            Assert.That(centers.Any(p=>Mathf.Abs(p.z-.35f)<.01f),Is.True,evidence);Assert.That(centers.Any(p=>Mathf.Abs(p.z-1.02f)<.01f),Is.True,evidence);
        }finally{UnityEngine.Object.DestroyImmediate(o);}
    }
}
