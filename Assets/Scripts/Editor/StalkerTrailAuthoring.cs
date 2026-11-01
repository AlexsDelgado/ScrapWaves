#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class StalkerTrailAuthoring
{
    public const string Folder = "Assets/Art/Enemies/Stalker/UndergroundTrail";
    [MenuItem("ScrapWaves/Enemies/Apply Stalker Open Arch Joint Limit")]
    public static void ApplyOpenArchJointLimit()
    {
        var boss = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Stalker.prefab");
        try
        {
            var settings = new SerializedObject(boss.GetComponent<StalkerRigAnimation>());
            var bend = settings.FindProperty("_maximumJointBend");
            if (Mathf.Approximately(bend.floatValue,45f)) bend.floatValue=50f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(boss,"Assets/Prefabs/Stalker.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(boss); }
    }

    [MenuItem("ScrapWaves/Enemies/Create Missing Stalker Ground Trail")]
    public static void CreateMissing()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        string earthPath = Folder + "/GroundMound_DryEarth.mat";
        var earth = AssetDatabase.LoadAssetAtPath<Material>(earthPath);
        if (earth == null)
        {
            earth = new Material(AssetDatabase.LoadAssetAtPath<Material>(GeyserModelAuthoring.Folder + "/Materials/Geyser_DarkEarth.mat"))
            { name = "GroundMound_DryEarth" };
            // Dry, exposed earth separates from the dark scrapyard floor without glowing.
            earth.SetColor("_BaseColor", new Color(.52f, .41f, .27f, 1));
            earth.SetColor("_Color", new Color(.52f, .41f, .27f, 1));
            AssetDatabase.CreateAsset(earth, earthPath);
        }
        string path = Folder + "/GroundMound.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            var mesh = new Mesh { name = "Small raised earth mound" };
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            const int sides = 12;
            Vector3 Point(int ring, int i)
            {
                float angle = i * Mathf.PI * 2 / sides;
                float radius = ring == 0 ? .5f : ring == 1 ? .34f : .1f;
                float height = ring == 0 ? 0 : ring == 1 ? .55f : .94f;
                radius *= 1f + .08f * Mathf.Sin(i * 2.7f + ring);
                return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            }
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int index = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            }
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                for (int ring = 0; ring < 2; ring++)
                {
                    Triangle(Point(ring,i), Point(ring+1,i), Point(ring+1,next));
                    Triangle(Point(ring,i), Point(ring+1,next), Point(ring,next));
                }
                Triangle(Point(2,i), new Vector3(.015f,1f,-.01f), Point(2,next));
                Triangle(Point(0,i), Point(0,next), Vector3.zero);
            }
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Folder + "/GroundMound.asset");
            var root = new GameObject("GroundMound",typeof(MeshFilter),typeof(MeshRenderer));
            try
            {
                root.GetComponent<MeshFilter>().sharedMesh = mesh;
                root.GetComponent<MeshRenderer>().sharedMaterial = earth;
                for (int i = 0; i < 2; i++)
                {
                    var scrap = new GameObject(i == 0 ? "BuriedCog" : "BuriedBolt",typeof(MeshFilter),typeof(MeshRenderer));
                    scrap.transform.SetParent(root.transform,false);
                    scrap.transform.localPosition = i == 0 ? new Vector3(-.16f,.82f,.03f) : new Vector3(.14f,.88f,-.04f);
                    scrap.transform.localRotation = Quaternion.Euler(i == 0 ? 25 : 80,i * 67,35);
                    scrap.transform.localScale = Vector3.one * (i == 0 ? .27f : .24f);
                    scrap.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Enemies/Stalker/ScrapBurst/Meshes/" + (i == 0 ? "Cog" : "Bolt") + ".asset");
                    scrap.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(GeyserModelAuthoring.Folder + "/Materials/" + (i == 0 ? "Geyser_RustScrap" : "Geyser_BareSteel") + ".mat");
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { Object.DestroyImmediate(root); }
        }
        var boss = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Stalker.prefab");
        try
        {
            var trail = boss.GetComponent<StalkerUndergroundTrail>();
            if (trail == null) trail = boss.AddComponent<StalkerUndergroundTrail>();
            var settings = new SerializedObject(trail);
            if (settings.FindProperty("_moundPrefab").objectReferenceValue == null)
                settings.FindProperty("_moundPrefab").objectReferenceValue = prefab;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(boss,"Assets/Prefabs/Stalker.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(boss); }
        Debug.Log("STALKER_GROUND_TRAIL_AUTHORED: bounded cosmetic pool; combat and balance fields preserved.");
    }
}
#endif
