#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>Authors static pad visuals while preserving the existing gameplay roots and colliders.</summary>
public static class PadModelAuthoring
{
    public const string Folder="Assets/Art/Environment/Pads";
    private static readonly string[] Names={"PaleSafetyYellow","CharcoalMarkings","SalvagedSteel","EdgeRust","DarkOxideFrame","ReusedRubber","WornMustardMechanics","WarmServiceLight"};
    private static readonly Color[] Colors={new(.88f,.72f,.30f),new(.045f,.045f,.035f),new(.34f,.38f,.38f),new(.30f,.105f,.035f),new(.115f,.08f,.055f),new(.065f,.07f,.065f),new(.56f,.40f,.075f),new(.96f,.76f,.32f)};
    [MenuItem("ScrapWaves/Level/Author Approved Pad Models")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Author outside Play Mode.");
        Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Folder+"/Prefabs");Directory.CreateDirectory(Folder+"/Meshes");AssetDatabase.Refresh();
        var shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)throw new Exception("URP Lit shader unavailable.");
        var materials=new Material[Names.Length];
        for(int i=0;i<Names.Length;i++)
        {
            string path=Folder+"/Materials/Pad_"+Names[i]+".mat";materials[i]=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(materials[i]!=null)
            {
                if(i==0){materials[i].EnableKeyword("_EMISSION");materials[i].SetColor("_EmissionColor",Colors[i]*.12f);EditorUtility.SetDirty(materials[i]);}
                continue;
            }
            var m=new Material(shader){name="Pad_"+Names[i]};m.SetColor("_BaseColor",Colors[i]);
            m.SetFloat("_Metallic",i==2?.65f:i==6?.6f:i==4?.45f:i==3?.25f:i==0?.18f:0);
            m.SetFloat("_Smoothness",i==5?.14f:i==3?.17f:.35f);
            if(i==0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Colors[i]*.12f);}
            if(i==7){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Colors[i]*1.4f);}
            AssetDatabase.CreateAsset(m,path);materials[i]=m;
        }
        foreach(string name in new[]{"JumpPlatform","LandingPlatform"})
        {
            string path=Folder+"/Models/"+name+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.importAnimation=false;importer.addCollider=false;importer.globalScale=1;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
            var root=new GameObject(name+"_Visual");instance.transform.SetParent(root.transform,false);
            // FBX conversion maps Blender's painted +Y chevrons to Unity -Z.
            // Normalize the prefab so its forward (+Z) is the actual chevron direction.
            instance.transform.localRotation=Quaternion.Euler(0,180,0)*instance.transform.localRotation;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>materials[Array.FindIndex(Names,n=>m.name.EndsWith(n,StringComparison.Ordinal))]).ToArray();
            if(name=="JumpPlatform")SplitLaunchMarkings(root,instance,materials[1]);
            PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Prefabs/"+name+"_Visual.prefab");UnityEngine.Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        foreach(var pad in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("Jump ")||g.name.StartsWith("Land ")))
        {
            string name=pad.name.StartsWith("Jump ")?"JumpPlatform":"LandingPlatform";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Prefabs/"+name+"_Visual.prefab");
            var existing=pad.transform.Find("PadVisual");
            var visual=existing!=null?existing.gameObject:(GameObject)PrefabUtility.InstantiatePrefab(prefab,pad.transform);visual.name="PadVisual";
            visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;
            var launcher=pad.GetComponent<MapLaunchPad>();
            if(launcher!=null)
            {
                var landing=(Transform)new SerializedObject(launcher).FindProperty("_landing").objectReferenceValue;
                Vector3 direction=landing.GetComponent<Collider>().bounds.center-pad.GetComponent<Collider>().bounds.center;direction.y=0;
                var markings=visual.transform.Find("LaunchMarkings");markings.rotation=Quaternion.LookRotation(direction.normalized,Vector3.up);
                PrefabUtility.RecordPrefabInstancePropertyModifications(markings);
            }
            var scale=pad.transform.localScale;visual.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            pad.GetComponent<MeshRenderer>().enabled=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);
        }
        EditorSceneManager.SaveScene(scene);Debug.Log("Approved pad models authored; existing gameplay roots retained.");
    }
    private static void SplitLaunchMarkings(GameObject root,GameObject instance,Material markingMaterial)
    {
        var filter=instance.GetComponentInChildren<MeshFilter>();var source=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();
        var vertices=source.vertices.Select(v=>root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
        var body=new Mesh{name="JumpPlatform_Body"};body.vertices=vertices;body.uv=source.uv;body.subMeshCount=source.subMeshCount;
        var marks=new List<int>();
        for(int s=0;s<source.subMeshCount;s++)
        {
            var kept=new List<int>();var triangles=source.GetTriangles(s);bool black=renderer.sharedMaterials[s]==markingMaterial;
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                var destination=black&&vertices[a].y>.97f&&vertices[b].y>.97f&&vertices[c].y>.97f?marks:kept;
                if(destination==marks&&Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).y<0){int swap=b;b=c;c=swap;}
                destination.Add(a);destination.Add(b);destination.Add(c);
            }
            body.SetTriangles(kept,s);
        }
        body.RecalculateNormals();body.RecalculateBounds();
        var used=marks.Distinct().ToArray();var remap=used.Select((v,i)=>new{v,i}).ToDictionary(x=>x.v,x=>x.i);
        var marking=new Mesh{name="JumpPlatform_Chevrons"};marking.vertices=used.Select(i=>vertices[i]).ToArray();marking.uv=used.Select(i=>source.uv[i]).ToArray();marking.SetTriangles(marks.Select(i=>remap[i]).ToArray(),0);marking.RecalculateNormals();marking.RecalculateBounds();
        body=SaveMesh(body,"JumpPlatform_Body");marking=SaveMesh(marking,"JumpPlatform_Chevrons");
        var bodyObject=new GameObject("PlatformBody",typeof(MeshFilter),typeof(MeshRenderer));bodyObject.transform.SetParent(root.transform,false);bodyObject.GetComponent<MeshFilter>().sharedMesh=body;bodyObject.GetComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
        var arrow=new GameObject("LaunchMarkings",typeof(MeshFilter),typeof(MeshRenderer));arrow.transform.SetParent(root.transform,false);arrow.GetComponent<MeshFilter>().sharedMesh=marking;arrow.GetComponent<MeshRenderer>().sharedMaterial=markingMaterial;
        UnityEngine.Object.DestroyImmediate(instance);
    }
    private static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Folder+"/Meshes/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing!=null){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
}
#endif
