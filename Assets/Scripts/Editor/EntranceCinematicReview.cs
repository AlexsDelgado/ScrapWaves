using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class EntranceCinematicReview
{
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var intro=Object.FindFirstObjectByType<EntranceCinematic>();
        T Field<T>(string name)=>(T)typeof(EntranceCinematic).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(intro);
        var visual=Field<Transform>("_visual");var camera=Field<Camera>("_camera");
        var bones=visual.GetComponentsInChildren<Transform>(true);var skins=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        string output=Environment.GetEnvironmentVariable("ENTRANCE_REVIEW_OUTPUT");Directory.CreateDirectory(output);
        var target=new RenderTexture(960,720,24);var pixels=new Texture2D(960,720,TextureFormat.RGB24,false);var mesh=new Mesh();
        camera.targetTexture=target;
        Debug.Log($"REVIEW_BASIS player={visual.parent.name} position={visual.parent.position} rotation={visual.parent.rotation} forward={visual.parent.forward}");
        var proxies=skins.Select(skin=>{var go=new GameObject("CPU skinned review "+skin.name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;return go;}).ToArray();
        var csv=new StringBuilder("time,bone,x,y,z,local_x,local_y,local_z,qx,qy,qz,qw\n");
        foreach(float time in new[]{4f,5f,5.25f,5.5f,5.75f,6f,6.2f,6.35f,6.55f,6.75f,7f,7.25f,7.45f,7.75f,8f,8.25f,8.5f,8.75f,9f,9.4f,13f})
        {
            intro.Sample(time);
            foreach(var b in bones){Vector3 p=b.position,lp=b.localPosition;Quaternion q=b.localRotation;csv.AppendLine(FormattableString.Invariant($"{time},{b.name},{p.x},{p.y},{p.z},{lp.x},{lp.y},{lp.z},{q.x},{q.y},{q.z},{q.w}"));}
            Vector3 min=Vector3.one*float.PositiveInfinity,max=Vector3.one*float.NegativeInfinity;
            for(int index=0;index<skins.Length;index++){var skin=skins[index];skin.enabled=true;var posed=new Mesh();skin.BakeMesh(posed);var previous=proxies[index].GetComponent<MeshFilter>().sharedMesh;if(previous!=null)Object.DestroyImmediate(previous);proxies[index].GetComponent<MeshFilter>().sharedMesh=posed;proxies[index].transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);proxies[index].transform.localScale=skin.transform.lossyScale;skin.enabled=false;foreach(var v in posed.vertices){var p=skin.transform.TransformPoint(v);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}}
            Vector3 centre=(min+max)*.5f;
            foreach(string view in new[]{"cinematic","front","side","left-side"})
            {
                if(view!="cinematic")
                {
                    camera.orthographic=true;camera.orthographicSize=1.5f;
                    Vector3 axis=view=="front"?visual.parent.forward:view=="left-side"?-visual.parent.right:visual.parent.right;
                    camera.transform.SetPositionAndRotation(centre+axis*5f,Quaternion.LookRotation(-axis));
                }
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,$"{view}-{time:00.00}.png"),pixels.EncodeToPNG());
                camera.orthographic=false;
                intro.Sample(time);
            }
        }
        File.WriteAllText(Path.Combine(output,"bones.csv"),csv.ToString());
        foreach(var proxy in proxies){Object.DestroyImmediate(proxy.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(proxy);}foreach(var skin in skins)skin.enabled=true;
        camera.targetTexture=null;RenderTexture.active=null;intro.RestorePresentation();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(mesh);
    }
    public static void RebuildAndCapture(){EntranceCinematicAuthoring.Build();Capture();}
}
