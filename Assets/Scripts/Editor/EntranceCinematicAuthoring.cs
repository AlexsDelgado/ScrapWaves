using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EntranceCinematicAuthoring
{
    public const string Folder = "Assets/Art/Player/EntranceCinematic";
    [MenuItem("Tools/ScrapWaves/Author Entrance Cinematic")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        var player=Object.FindFirstObjectByType<PlayerMovement>();
        var camera=Object.FindFirstObjectByType<ThirdPersonCamera>().GetComponent<Camera>();
        Transform visual=player.transform.Find("PlaceholderPlayerVisual");
        if(visual==null)throw new InvalidOperationException("Authored player rig is missing.");
        var old=GameObject.Find("EntranceCinematic");
        var approvedClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/ScrapyardEntrance.anim");
        var transforms=visual.GetComponentsInChildren<Transform>(true);
        var originalRotations=transforms.Select(t=>t.localRotation).ToArray();
        var originalPositions=transforms.Select(t=>t.localPosition).ToArray();
        var landingClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/ApprovedLandingCurl.anim");
        if(landingClip!=null)landingClip.SampleAnimation(visual.gameObject,0f);
        else
        {
            approvedClip.SampleAnimation(visual.gameObject,4f);
            ReconstructApprovedLegs(visual);
            landingClip=new AnimationClip{name="ApprovedLandingCurl",legacy=true};
            foreach(var bone in transforms)
            {
                string binding=AnimationUtility.CalculateTransformPath(bone,visual);
                for(int c=0;c<4;c++)landingClip.SetCurve(binding,typeof(Transform),"m_LocalRotation."+"xyzw"[c],AnimationCurve.Constant(0,1,bone.localRotation[c]));
                for(int c=0;c<3;c++)landingClip.SetCurve(binding,typeof(Transform),"m_LocalPosition."+"xyz"[c],AnimationCurve.Constant(0,1,bone.localPosition[c]));
            }
            AssetDatabase.CreateAsset(landingClip,Folder+"/ApprovedLandingCurl.anim");
        }
        RefineLandingArms(visual);
        foreach(var bone in transforms.Where(t=>t.name.StartsWith("upper_arm.")||t.name.StartsWith("forearm.")||t.name.StartsWith("hand.")))
        {
            string binding=AnimationUtility.CalculateTransformPath(bone,visual);
            for(int c=0;c<4;c++)landingClip.SetCurve(binding,typeof(Transform),"m_LocalRotation."+"xyzw"[c],AnimationCurve.Constant(0,1,bone.localRotation[c]));
        }
        EditorUtility.SetDirty(landingClip);
        var approvedRotations=transforms.Select(t=>t.localRotation).ToArray();
        var approvedPositions=transforms.Select(t=>t.localPosition).ToArray();
        for(int i=0;i<transforms.Length;i++){transforms[i].localRotation=originalRotations[i];transforms[i].localPosition=originalPositions[i];}
        if(old!=null)Object.DestroyImmediate(old);
        var root=new GameObject("EntranceCinematic");
        var director=root.AddComponent<EntranceCinematic>();
        var clip=new AnimationClip {name="ScrapyardEntrance",legacy=true};
        Vector3 originalPosition=visual.localPosition;
        var idle=AssetDatabase.LoadAllAssetsAtPath(PlaceholderPlayerAnimationBuilder.ModelPath).OfType<AnimationClip>().First(c=>c.name=="Idle");
        idle.SampleAnimation(visual.gameObject,0f);
        AuthorReferencePose(clip,visual,originalPosition,approvedRotations,approvedPositions);
        clip.EnsureQuaternionContinuity();
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve=AnimationUtility.GetEditorCurve(clip,binding);
            for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            AnimationUtility.SetEditorCurve(clip,binding,curve);
        }
        string path=Folder+"/ScrapyardEntrance.anim";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(clip,path);
        Vector3 ground=player.transform.position;
        if(Physics.Raycast(ground+Vector3.up*5,Vector3.down,out var hit,30,~(1<<player.gameObject.layer)))ground.y=hit.point.y;
        else ground.y-=.75f;
        clip.SampleAnimation(visual.gameObject,4f);
        // Centre all impact effects on the posed mesh's actual contact footprint.
        // Triangle area avoids bias from dense head/glove topology.
        var contactMesh=new Mesh();float lowest=float.PositiveInfinity;
        var contactSkins=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach(var skin in contactSkins){skin.BakeMesh(contactMesh);foreach(var vertex in contactMesh.vertices)lowest=Mathf.Min(lowest,skin.transform.TransformPoint(vertex).y);}
        Vector3 contact=Vector3.zero;float contactArea=0f;
        foreach(var skin in contactSkins)
        {
            skin.BakeMesh(contactMesh);var vertices=contactMesh.vertices;var triangles=contactMesh.triangles;
            for(int index=0;index<triangles.Length;index+=3)
            {
                Vector3 a=skin.transform.TransformPoint(vertices[triangles[index]]),b=skin.transform.TransformPoint(vertices[triangles[index+1]]),c=skin.transform.TransformPoint(vertices[triangles[index+2]]);
                if(Mathf.Min(a.y,Mathf.Min(b.y,c.y))>lowest+.10f)continue;
                float area=Vector3.Cross(b-a,c-a).magnitude*.5f;contact+=(a+b+c)*(area/3f);contactArea+=area;
            }
        }
        Object.DestroyImmediate(contactMesh);
        if(contactArea<=0f)throw new InvalidOperationException("Landing mesh has no ground-contact footprint.");
        contact/=contactArea;ground.x=contact.x;ground.z=contact.z;
        Debug.Log($"IMPACT_CONTACT centre={ground} lowest={lowest} area={contactArea}");
        clip.SampleAnimation(visual.gameObject,0f);
        var groundShader=Shader.Find("ScrapWaves/EntranceSoftGround");
        var shadowMaterial=Material("ImpactCoal",groundShader,new Color(.045f,.034f,.025f,.55f));
        var soft=SoftTexture("SoftContact",false);shadowMaterial.SetTexture("_BaseMap",soft);
        var shadow=new GameObject("SoftFallingShadow",typeof(MeshFilter),typeof(MeshRenderer));shadow.transform.SetParent(root.transform);
        shadow.transform.position=ground+Vector3.up*.025f;shadow.GetComponent<MeshFilter>().sharedMesh=SaveMesh(Quad(2.1f,1.55f),"ContactShadow");shadow.GetComponent<MeshRenderer>().sharedMaterial=shadowMaterial;shadow.SetActive(false);
        var impact=new GameObject("ShallowImpactCrater");impact.transform.SetParent(root.transform);impact.transform.position=ground;
        var crater=new GameObject("BrokenEarthRim",typeof(MeshFilter),typeof(MeshRenderer));crater.transform.SetParent(impact.transform,false);
        crater.GetComponent<MeshFilter>().sharedMesh=SaveMesh(Crater(),"CraterRim");crater.GetComponent<MeshRenderer>().sharedMaterial=Material("CraterEarth",groundShader,Color.white);
        var debrisMat=Material("ImpactDebris",Shader.Find("Universal Render Pipeline/Lit"),new Color(.22f,.16f,.10f));
        for(int i=0;i<18;i++)
        {
            float angle=i*2.39996f,radius=.72f+(i%5)*.10f;
            var chip=GameObject.CreatePrimitive(PrimitiveType.Cube);chip.name="Rim fragment "+i;chip.transform.SetParent(impact.transform,false);
            Object.DestroyImmediate(chip.GetComponent<Collider>());chip.transform.localPosition=new Vector3(Mathf.Sin(angle)*radius,.025f,Mathf.Cos(angle)*radius);
            chip.transform.localScale=new Vector3(.04f+(i%4)*.016f,.035f,.07f);chip.transform.localRotation=Quaternion.Euler(0,i*37,12);
            chip.GetComponent<Renderer>().sharedMaterial=debrisMat;
        }
        impact.SetActive(false);
        var dustObject=new GameObject("ImpactDust",typeof(ParticleSystem));dustObject.transform.SetParent(root.transform);dustObject.transform.position=ground+Vector3.up*.10f;dustObject.transform.rotation=Quaternion.Euler(-90,0,0);
        var dust=dustObject.GetComponent<ParticleSystem>();var main=dust.main;main.playOnAwake=false;main.prewarm=false;main.startDelay=0;main.loop=false;main.duration=2.3f;main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,2.3f);main.startSpeed=new ParticleSystem.MinMaxCurve(1.2f,2.1f);main.startSize=new ParticleSystem.MinMaxCurve(.20f,.55f);main.startColor=new Color(.62f,.45f,.29f,.42f);main.gravityModifier=.12f;main.simulationSpace=ParticleSystemSimulationSpace.World;main.useUnscaledTime=false;
        var emission=dust.emission;emission.rateOverTime=0;emission.rateOverDistance=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,46)});var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=76;shape.radius=.58f;
        var fade=dust.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.7f,.66f,.58f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.08f),new GradientAlphaKey(0,1)});fade.color=gradient;
        var size=dust.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,.5f,1,1.6f));
        var dustMat=Material("ScrapyardDust",groundShader,Color.white);dustMat.SetTexture("_BaseMap",SoftTexture("DustCloud",true));dust.GetComponent<ParticleSystemRenderer>().sharedMaterial=dustMat;
        var scraps=new GameObject("FlyingScrap",typeof(ParticleSystem));scraps.transform.SetParent(dustObject.transform,false);
        var fragments=scraps.GetComponent<ParticleSystem>();var fm=fragments.main;fm.playOnAwake=false;fm.prewarm=false;fm.startDelay=0;fm.useUnscaledTime=false;fm.loop=false;fm.duration=1.3f;fm.startLifetime=new ParticleSystem.MinMaxCurve(.5f,1.2f);fm.startSpeed=new ParticleSystem.MinMaxCurve(1.8f,3.5f);fm.startSize=new ParticleSystem.MinMaxCurve(.025f,.075f);fm.gravityModifier=.9f;fm.simulationSpace=ParticleSystemSimulationSpace.World;
        var fe=fragments.emission;fe.rateOverTime=0;fe.rateOverDistance=0;fe.SetBursts(new[]{new ParticleSystem.Burst(0,12)});var fs=fragments.shape;fs.shapeType=ParticleSystemShapeType.Cone;fs.angle=65;fs.radius=.45f;
        var fr=fragments.GetComponent<ParticleSystemRenderer>();fr.renderMode=ParticleSystemRenderMode.Mesh;
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);fr.mesh=cube.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(cube);fr.sharedMaterial=debrisMat;
        dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var audio=root.AddComponent<AudioSource>();audio.playOnAwake=false;audio.spatialBlend=0;audio.volume=.45f;
        var lampObject=new GameObject("EntranceWarmFill",typeof(Light));lampObject.transform.SetParent(root.transform);lampObject.transform.position=player.transform.position+new Vector3(-2,3,-2);
        var lamp=lampObject.GetComponent<Light>();lamp.type=LightType.Point;lamp.color=new Color(1f,.8f,.58f);lamp.intensity=4;lamp.range=10;lamp.shadows=LightShadows.None;lamp.enabled=false;
        Sound(Folder+"/FallingWhistle.wav",2.7f,false);Sound(Folder+"/ScrapImpact.wav",.45f,true);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var serialized=new SerializedObject(director);
        serialized.FindProperty("_cameraOffset").vector3Value=new Vector3(1.2f,3.7f,1.2f);
        serialized.FindProperty("_duration").floatValue=13f;
        serialized.FindProperty("_landingLookOffset").vector3Value=new Vector3(-.25f,-1.35f,-.9f);
        Set("_player",player.transform);Set("_visual",visual);Set("_camera",camera);Set("_entrance",clip);Set("_dust",dust);Set("_shadow",shadow.transform);Set("_impact",impact);Set("_audio",audio);
        Set("_fill",lamp);
        Set("_whistle",AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"/FallingWhistle.wav"));Set("_thud",AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"/ScrapImpact.wav"));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        void Set(string field,Object value)=>serialized.FindProperty(field).objectReferenceValue=value;
    }
    private static void ReconstructApprovedLegs(Transform visual)
    {
        // Reconstruction from the preserved approved video frame: folded orange
        // thighs overlap the torso and both boots point down-right in the overhead view.
        var bones=visual.GetComponentsInChildren<Transform>(true);
        Transform Bone(string n)=>bones.First(t=>t.name==n);
        foreach(string side in new[]{"L","R"})
        {
            float x=side=="L"?-.16f:.16f;
            var a=Bone("thigh."+side);var b=Bone("shin."+side);var c=Bone("foot."+side);
            Vector3 target=visual.TransformPoint(new Vector3(x,.55f,.35f)),bend=visual.TransformPoint(new Vector3(x,1.28f,.35f))-a.position;
            float first=Vector3.Distance(a.position,b.position),second=Vector3.Distance(b.position,c.position);Vector3 direction=(target-a.position).normalized;
            float distance=Mathf.Clamp(Vector3.Distance(a.position,target),Mathf.Abs(first-second)+.001f,first+second-.001f);bend=Vector3.ProjectOnPlane(bend,direction).normalized;
            float along=(first*first+distance*distance-second*second)/(2*distance);Vector3 knee=a.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            a.rotation=Quaternion.FromToRotation(b.position-a.position,knee-a.position)*a.rotation;b.rotation=Quaternion.FromToRotation(c.position-b.position,a.position+direction*distance-b.position)*b.rotation;
            var toe=Bone("toe."+side);c.rotation=Quaternion.FromToRotation(toe.position-c.position,visual.TransformVector(new Vector3(0,-.08f,.11f)))*c.rotation;
        }
    }
    private static void RefineLandingArms(Transform visual)
    {
        // One forearm cushions the tucked face; the upper arm rests across the
        // chest. Elbows follow the ribcage instead of pointing above the head.
        var bones=visual.GetComponentsInChildren<Transform>(true);
        Transform Bone(string n)=>bones.First(t=>t.name==n);
        Arm("L",new Vector3(-.10f,1.04f,.70f),new Vector3(-.34f,.87f,.43f));
        Arm("R",new Vector3(.13f,.97f,.54f),new Vector3(.34f,.86f,.28f));
        void Arm(string side,Vector3 destination,Vector3 pole)
        {
            var a=Bone("upper_arm."+side);var b=Bone("forearm."+side);var c=Bone("hand."+side);
            Vector3 target=visual.TransformPoint(destination),bend=visual.TransformPoint(pole)-a.position;
            float first=Vector3.Distance(a.position,b.position),second=Vector3.Distance(b.position,c.position);
            Vector3 direction=(target-a.position).normalized;float distance=Mathf.Clamp(Vector3.Distance(a.position,target),Mathf.Abs(first-second)+.001f,first+second-.001f);
            bend=Vector3.ProjectOnPlane(bend,direction).normalized;float along=(first*first+distance*distance-second*second)/(2*distance);
            Vector3 elbow=a.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            a.rotation=Quaternion.FromToRotation(b.position-a.position,elbow-a.position)*a.rotation;
            b.rotation=Quaternion.FromToRotation(c.position-b.position,a.position+direction*distance-b.position)*b.rotation;
            c.localRotation=Quaternion.Euler(-9f,0f,0f);
        }
    }
    private static Material Material(string name,Shader shader,Color color)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
        material.shader=shader;material.SetColor("_BaseColor",color);EditorUtility.SetDirty(material);return material;
    }
    private static Texture2D SoftTexture(string name,bool cloud)
    {
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)
        {
            float px=(x-63.5f)/63.5f,py=(y-63.5f)/63.5f,r=Mathf.Sqrt(px*px+py*py);
            float alpha=Mathf.Exp(-r*r*5.5f)*Mathf.SmoothStep(0,1,Mathf.Clamp01((1-r)*5));
            if(cloud)alpha*=Mathf.Lerp(.55f,1f,Mathf.PerlinNoise(x*.075f,y*.075f));
            texture.SetPixel(x,y,new Color(1,1,1,alpha));
        }
        texture.Apply();string path=Folder+"/"+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    private static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Folder+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,saved);EditorUtility.SetDirty(saved);Object.DestroyImmediate(mesh);return saved;
    }
    private static Mesh Quad(float width,float depth)
    {
        var mesh=new Mesh{name="Soft contact shadow"};mesh.vertices=new[]{new Vector3(-width/2,0,-depth/2),new Vector3(width/2,0,-depth/2),new Vector3(-width/2,0,depth/2),new Vector3(width/2,0,depth/2)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.colors=Enumerable.Repeat(Color.white,4).ToArray();mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateNormals();return mesh;
    }
    private static Mesh Crater()
    {
        const int count=96;float[] radii={0f,.43f,.65f,.82f,.95f,1.15f};float[] heights={.012f,.015f,.035f,.13f,.035f,.009f};float[] opacity={.35f,.44f,.65f,.82f,.42f,0};
        var vertices=new Vector3[count*radii.Length];var colors=new Color[vertices.Length];var indices=new System.Collections.Generic.List<int>();
        for(int ring=0;ring<radii.Length;ring++)for(int i=0;i<count;i++)
        {
            float angle=i*Mathf.PI*2/count;float uneven=1+.045f*Mathf.Sin(angle*7)+.025f*Mathf.Cos(angle*11);
            int index=ring*count+i;vertices[index]=new Vector3(Mathf.Sin(angle)*radii[ring]*uneven,heights[ring]*(1+.28f*Mathf.Sin(angle*13)),Mathf.Cos(angle)*radii[ring]*uneven);
            float groove=.92f+.08f*Mathf.Sin(angle*24);var earth=Color.Lerp(new Color(.012f,.009f,.005f),new Color(.07f,.045f,.025f),ring/5f);earth*=groove;earth.a=opacity[ring];colors[index]=earth;
            if(ring>0){int a=(ring-1)*count+i,b=(ring-1)*count+(i+1)%count,c=ring*count+i,d=ring*count+(i+1)%count;indices.AddRange(new[]{a,b,c,b,d,c});}
        }
        var mesh=new Mesh{name="Irregular shallow impact rim"};mesh.vertices=vertices;mesh.colors=colors;mesh.uv=new Vector2[vertices.Length];mesh.triangles=indices.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    private static void AuthorReferencePose(AnimationClip clip,Transform visual,Vector3 originalPosition,Quaternion[] approvedRotations,Vector3[] approvedPositions)
    {
        var bones=visual.GetComponentsInChildren<Transform>(true);
        var idle=bones.Select(t=>t.localRotation).ToArray();var idlePositions=bones.Select(t=>t.localPosition).ToArray();
        Transform Bone(string name)=>bones.First(t=>t.name==name);
        var skins=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var mesh=new Mesh();
        float floor=Physics.Raycast(visual.parent.position+Vector3.up*5,Vector3.down,out var hit,30,~(1<<visual.parent.gameObject.layer))?hit.point.y:0f;
        Quaternion basis=visual.parent.rotation*idle[0];
        // The imported Idle keeps both hands forward. The entrance instead ends
        // with loose, nearly straight arms alongside the body. Keep the accepted
        // recovery's original bind frames and body/leg pose unchanged.
        foreach(string side in new[]{"L","R"})
        {
            var upper=Bone("upper_arm."+side);var elbow=Bone("forearm."+side);var hand=Bone("hand."+side);
            float upperLength=Vector3.Distance(upper.position,elbow.position),forearmLength=Vector3.Distance(elbow.position,hand.position);
            float sign=side=="L"?-1f:1f;
            Vector3 direction=(basis*new Vector3(sign*.14f,-1f,.07f)).normalized;
            float distance=upperLength+forearmLength-.025f;
            Vector3 target=upper.position+direction*distance;
            // Elbows rest behind the shoulder-to-wrist line so the forearm
            // flexes forward. A forward pole makes this lowered pose hyperextend.
            Vector3 bend=Vector3.ProjectOnPlane(basis*new Vector3(sign*.15f,-.4f,-.3f),direction).normalized;
            float along=(upperLength*upperLength+distance*distance-forearmLength*forearmLength)/(2f*distance);
            Vector3 joint=upper.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0f,upperLength*upperLength-along*along));
            upper.rotation=Quaternion.FromToRotation(elbow.position-upper.position,joint-upper.position)*upper.rotation;
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,target-elbow.position)*elbow.rotation;
        }
        var relaxedIdle=bones.Select(t=>t.localRotation).ToArray();
        for(int index=0;index<bones.Length;index++)bones[index].localRotation=idle[index];
        Vector3 basePoint=new Vector3(visual.parent.position.x,floor+.025f,visual.parent.position.z);
        Vector3 World(Vector3 point)=>basePoint+basis*point;
        // Define each limb's bind-frame correction once. FromToRotation alone
        // leaves twist unconstrained and can flip the mesh around a smooth bone.
        var frames=new System.Collections.Generic.Dictionary<string,Quaternion>();
        foreach(var chain in new[]{new[]{"thigh.L","shin.L","foot.L"},new[]{"thigh.R","shin.R","foot.R"},new[]{"upper_arm.L","forearm.L","hand.L"},new[]{"upper_arm.R","forearm.R","hand.R"}})
        {
            var upper=Bone(chain[0]);var joint=Bone(chain[1]);var end=Bone(chain[2]);
            Vector3 normal=Vector3.Cross(end.position-upper.position,joint.position-upper.position).normalized;
            if(normal.sqrMagnitude<.5f)normal=basis*Vector3.right;
            frames[chain[0]]=Quaternion.Inverse(Quaternion.LookRotation(joint.position-upper.position,normal))*upper.rotation;
            frames[chain[1]]=Quaternion.Inverse(Quaternion.LookRotation(end.position-joint.position,normal))*joint.rotation;
        }
        foreach(string side in new[]{"L","R"})
        {
            var foot=Bone("foot."+side);var toe=Bone("toe."+side);
            frames[foot.name]=Quaternion.Inverse(Quaternion.LookRotation(toe.position-foot.position,basis*Vector3.up))*foot.rotation;
        }
        // Fit the support wrist to this model's actual glove direction. A
        // vertical idle glove extends below the wrist even when its IK target
        // is above terrain; the planted palm needs a horizontal orientation.
        Vector3 handCenter=Vector3.zero;int handVertices=0;
        foreach(var skin in skins)
        {
            skin.BakeMesh(mesh);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
            for(int vertex=0;vertex<vertices.Length;vertex++)
            {
                var weight=weights[vertex];
                if(weight.weight0>.7f && skin.bones[weight.boneIndex0].name=="hand.R")
                {handCenter+=skin.transform.TransformPoint(vertices[vertex]);handVertices++;}
            }
        }
        Vector3 gloveDirection=(handCenter/Mathf.Max(1,handVertices)-Bone("hand.R").position).normalized;
        Quaternion palmFrame=Quaternion.Inverse(Quaternion.LookRotation(gloveDirection,basis*Vector3.up))*Bone("hand.R").rotation;
        Quaternion plantedPalm=Quaternion.LookRotation(basis*Vector3.forward,basis*Vector3.up)*palmFrame;
        float supportWristHeight=.065f;
        Quaternion palmTurn=plantedPalm*Quaternion.Inverse(Bone("hand.R").rotation);
        foreach(var skin in skins)
        {
            skin.BakeMesh(mesh);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
            for(int vertex=0;vertex<vertices.Length;vertex++)
            {
                var weight=weights[vertex];if(weight.weight0<=.7f || skin.bones[weight.boneIndex0].name!="hand.R")continue;
                Vector3 relative=palmTurn*(skin.transform.TransformPoint(vertices[vertex])-Bone("hand.R").position);
                supportWristHeight=Mathf.Max(supportWristHeight,-relative.y+.025f);
            }
        }
        Debug.Log($"SUPPORT_WRIST_HEIGHT {supportWristHeight}");
        float kneeBlend=1f;
        // Save the approved curled articulation before any recovery solving. Its
        // camera stays steep overhead during the hold; only the contact root is settled.
        Restore(approvedRotations,approvedPositions);GroundSurface();
        var landed=bones.Select(t=>t.localRotation).ToArray();var landedPositions=bones.Select(t=>t.localPosition).ToArray();
        var landedContacts=new[]{"hand.R","hand.L","foot.L","foot.R"}.Select(n=>Quaternion.Inverse(basis)*(Bone(n).position-basePoint)).ToArray();
        Vector3 landedHip=Quaternion.Inverse(basis)*(Bone("spine").position-basePoint);
        var landedKnees=new[]{"shin.L","shin.R"}.Select(n=>Quaternion.Inverse(basis)*(Bone(n).position-basePoint)).ToArray();
        var landedBends=new System.Collections.Generic.Dictionary<string,Vector3>();
        foreach(string side in new[]{"L","R"})
        {
            var thigh=Bone("thigh."+side);var shin=Bone("shin."+side);var foot=Bone("foot."+side);
            landedBends[thigh.name]=Vector3.ProjectOnPlane(shin.position-thigh.position,foot.position-thigh.position).normalized;
        }
        var landedArmFrames=new System.Collections.Generic.Dictionary<string,Quaternion>();
        var landedArmBends=new System.Collections.Generic.Dictionary<string,Vector3>();
        foreach(string side in new[]{"L","R"})
        {
            var upper=Bone("upper_arm."+side);var joint=Bone("forearm."+side);var end=Bone("hand."+side);
            Vector3 direction=end.position-upper.position,bend=Vector3.ProjectOnPlane(joint.position-upper.position,direction).normalized;
            Vector3 normal=Vector3.Cross(direction,bend).normalized;
            landedArmFrames[upper.name]=Quaternion.Inverse(Quaternion.LookRotation(joint.position-upper.position,normal))*upper.rotation;
            landedArmFrames[joint.name]=Quaternion.Inverse(Quaternion.LookRotation(end.position-joint.position,normal))*joint.rotation;
            landedArmBends[upper.name]=bend;
        }
        var previousArmBends=new System.Collections.Generic.Dictionary<string,Vector3>(landedArmBends);
        bool baking=false;
        var landedFeet=new[]{Bone("foot.L").rotation,Bone("foot.R").rotation};
        Quaternion landedHand=Bone("hand.R").rotation;
        var previousLegBends=new System.Collections.Generic.Dictionary<string,Vector3>(landedBends);
        var chest=Bone("spine.004");
        Vector3 supportHandLocal=chest.InverseTransformPoint(Bone("hand.R").position);
        Vector3 supportElbowLocal=chest.InverseTransformPoint(Bone("forearm.R").position);
        Vector3 freeHandLocal=chest.InverseTransformPoint(Bone("hand.L").position);
        Vector3 freeElbowLocal=chest.InverseTransformPoint(Bone("forearm.L").position);
        Pose(.765f,81f,.25f,-42f);
        Contacts(new Vector3(.55f,supportWristHeight,.70f),new Vector3(-.16f,.66f,.22f),new Vector3(-.18f,.18f,.20f),new Vector3(.18f,.18f,-.29f),1f);
        Bone("hand.R").rotation=plantedPalm;
        float plantedMinimum=float.PositiveInfinity;
        foreach(var skin in skins)
        {
            skin.BakeMesh(mesh);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
            for(int vertex=0;vertex<vertices.Length;vertex++)
                if(weights[vertex].weight0>.7f && skin.bones[weights[vertex].boneIndex0].name=="hand.R")plantedMinimum=Mathf.Min(plantedMinimum,skin.transform.TransformPoint(vertices[vertex]).y);
        }
        supportWristHeight-=plantedMinimum-floor-.015f;
        Contacts(new Vector3(.55f,supportWristHeight,.70f),new Vector3(-.16f,.66f,.22f),new Vector3(-.18f,.18f,.20f),new Vector3(.18f,.18f,-.29f),1f);
        Bone("hand.R").rotation=plantedPalm;
        var low=bones.Select(t=>t.localRotation).ToArray();var lowPositions=bones.Select(t=>t.localPosition).ToArray();
        previousLegBends=new System.Collections.Generic.Dictionary<string,Vector3>(landedBends);
        var baked=new System.Collections.Generic.List<(float time,Quaternion[] rotations,Vector3[] positions)>();
        float supportMinimum=float.PositiveInfinity,supportMaximum=float.NegativeInfinity;
        float recoveryMinimum=float.PositiveInfinity,recoveryMinimumTime=0f;string recoveryContact="";
        baking=true;previousArmBends=new System.Collections.Generic.Dictionary<string,Vector3>(landedArmBends);
        for(int frame=0;frame<=780;frame++)
        {
            float time=frame/60f;kneeBlend=Ease(5.15f,6.20f,time);
            float supportLift=time<6.2f?.07f*Mathf.Sin(Ease(5.15f,6.2f,time)*Mathf.PI):0f;
            if(time<2.35f)Restore(idle,idlePositions);
            else if(time<2.7f)Blend(idle,idlePositions,landed,landedPositions,Ease(2.35f,2.7f,time));
            else if(time<=5.15f)Restore(landed,landedPositions);
            else if(time<6.20f)
            {
                float roll=Ease(5.15f,6.20f,time);Blend(landed,landedPositions,low,lowPositions,roll);
                // Rotate around a low pelvis path, rather than lifting the entire
                // body until a moving boot happens to become its lowest vertex.
                Vector3 hip=World(Vector3.Lerp(landedHip,new Vector3(.25f,.765f,-.037f),roll));
                hip.y=basePoint.y+Mathf.Lerp(landedHip.y,.765f,Ease(5.15f,5.80f,time));
                visual.position+=hip-Bone("spine").position;
                // Reach the near palm first; once planted its world target does not
                // slide with quaternion interpolation of the torso and pelvis.
                float left=Ease(5.15f,6.05f,time);
                Vector3 lh=Quaternion.Inverse(basis)*(Vector3.Lerp(chest.TransformPoint(supportHandLocal),World(new Vector3(.55f,supportWristHeight,.70f)),left)-basePoint);lh.y+=supportLift;
                Vector3 le=Quaternion.Inverse(basis)*(Vector3.Lerp(chest.TransformPoint(supportElbowLocal),World(lh+new Vector3(.2f,.3f,-.12f)),left)-basePoint);
                Vector3 rh=Quaternion.Inverse(basis)*(Vector3.Lerp(chest.TransformPoint(freeHandLocal),World(new Vector3(-.16f,.66f,.22f)),roll)-basePoint);
                Vector3 re=Quaternion.Inverse(basis)*(Vector3.Lerp(chest.TransformPoint(freeElbowLocal),World(new Vector3(-.36f,.41f,.10f)),roll)-basePoint);
                Solve("upper_arm.R","forearm.R","hand.R",lh,le);
                Solve("upper_arm.L","forearm.L","hand.L",rh,re);
                if(time>5.2f)
                {
                    Solve("thigh.L","shin.L","foot.L",Vector3.Lerp(landedContacts[2],new Vector3(-.18f,.18f,.20f),roll)+Vector3.up*(Mathf.Sin(roll*Mathf.PI)*.12f),Vector3.Lerp(landedKnees[0],new Vector3(-.18f,.53f,.52f),roll)+Vector3.up*(Mathf.Sin(roll*Mathf.PI)*.45f));
                    Solve("thigh.R","shin.R","foot.R",Vector3.Lerp(landedContacts[3],new Vector3(.18f,.18f,-.29f),roll),Vector3.Lerp(landedKnees[1],new Vector3(.18f,.12f,.20f),roll));
                    var feet=new[]{Bone("foot.L"),Bone("foot.R")};Feet();
                    for(int i=0;i<feet.Length;i++)feet[i].rotation=Quaternion.Slerp(landedFeet[i],feet[i].rotation,roll);
                }
            }
            else if(time<7.45f)
            {
                // Keep a single side palm down while the planted forward leg
                // takes the weight. The free arm stays near the chest, never
                // forming the former symmetrical hands-and-knees pose.
                float step=Ease(6.20f,6.765f,time),lift=Ease(6.55f,7.45f,time);
                Pose( .765f,Mathf.Lerp(81f,24f,lift),Mathf.Lerp(.25f,.004f,lift),Mathf.Lerp(-42f,0f,lift));
                Vector3 lf=Vector3.Lerp(new Vector3(-.18f,.18f,.20f),new Vector3(-.18f,.18f,.38f),step);lf.y+=Mathf.Sin(step*Mathf.PI)*.10f;
                Vector3 lh=Vector3.Lerp(new Vector3(.55f,supportWristHeight,.70f),new Vector3(.32f,.53f,.39f),Ease(6.65f,7.45f,time));lh.y+=supportLift;
                Vector3 rh=Vector3.Lerp(new Vector3(-.16f,.66f,.22f),new Vector3(-.31f,.68f,.27f),lift);
                Contacts(lh,rh,lf,new Vector3(.18f,.18f,-.29f),1f);
            }
            else if(time<8.85f)
            {
                float rise=Ease(7.7f,8.85f,time),step=Ease(8f,8.65f,time);
                Pose(Mathf.Lerp(.765f,.876f,rise),Mathf.Lerp(24f,0f,rise));
                Vector3 lf=Vector3.Lerp(new Vector3(-.18f,.18f,.38f),new Vector3(-.17f,.18f,.035f),Ease(8.4f,8.85f,time));
                Vector3 rf=Vector3.Lerp(new Vector3(.18f,.18f,-.29f),new Vector3(.177f,.18f,-.008f),step);rf.y+=Mathf.Sin(step*Mathf.PI)*.07f;
                Solve("thigh.L","shin.L","foot.L",lf,new Vector3(-.18f,.53f,.52f));
                Solve("thigh.R","shin.R","foot.R",rf,new Vector3(.18f,.10f,.18f));Feet();
                // The push is over. Carry the released arm articulation with the
                // chest, then relax into idle without moving world-space elbow poles.
                var released=baked[446].rotations;
                for(int index=0;index<bones.Length;index++)
                    if(bones[index].name.StartsWith("upper_arm.")||bones[index].name.StartsWith("forearm.")||bones[index].name.StartsWith("hand."))
                        bones[index].localRotation=Quaternion.Slerp(released[index],relaxedIdle[index],Ease(7.45f,8.85f,time));
            }
            else
            {
                Restore(relaxedIdle,idlePositions);visual.position=basePoint;
                GroundSurface();
            }
            if(time>5f && time<7.45f)
            {
                float reach=Ease(5.15f,6.20f,time),release=Ease(6.65f,7.45f,time);
                Quaternion hand=Quaternion.Slerp(landedHand,plantedPalm,reach);
                Bone("hand.R").rotation=Quaternion.Slerp(hand,Bone("hand.R").rotation,release);
            }
            if(time>=5f && time<=9.4f)
            {
                foreach(var skin in skins)
                {
                    skin.BakeMesh(mesh);
                    var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
                    for(int vertex=0;vertex<vertices.Length;vertex++)
                    {
                        float height=skin.transform.TransformPoint(vertices[vertex]).y;
                        if(height<recoveryMinimum)
                        {
                            recoveryMinimum=height;recoveryMinimumTime=time;
                            var weight=weights[vertex];var bone=skin.bones[weight.boneIndex0];
                            recoveryContact=$"bone={bone.name} weight={weight.weight0} position={bone.position} wrist={Bone("hand.R").position} footL={Bone("foot.L").position} footR={Bone("foot.R").position}";
                        }
                    }
                }
            }
            if(time>=6.2f && time<=6.65f)
            {
                float minimum=float.PositiveInfinity;
                foreach(var skin in skins){skin.BakeMesh(mesh);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;for(int vertex=0;vertex<vertices.Length;vertex++){var weight=weights[vertex];if(weight.weight0>.7f && skin.bones[weight.boneIndex0].name=="hand.R")minimum=Mathf.Min(minimum,skin.transform.TransformPoint(vertices[vertex]).y);}}
                supportMinimum=Mathf.Min(supportMinimum,minimum);supportMaximum=Mathf.Max(supportMaximum,minimum);
            }
            baked.Add((time,bones.Select(t=>t.localRotation).ToArray(),bones.Select(t=>t.localPosition).ToArray()));
        }
        Debug.Log($"SUPPORT_CONTACT minimum={supportMinimum} maximum={supportMaximum} floor={floor}");
        Debug.Log($"RECOVERY_CLEARANCE minimum={recoveryMinimum} floor={floor} time={recoveryMinimumTime} {recoveryContact}");
        // Blend the last push into the exact gameplay idle placement over half a
        // second. This correction happens visibly before the camera return.
        int first=528,last=564;var a=baked[first];var b=baked[last];
        for(int frame=first+1;frame<last;frame++)
        {
            float blend=Ease(first/60f,last/60f,frame/60f);
            var rotations=a.rotations.Select((q,i)=>Quaternion.Slerp(q,b.rotations[i],blend)).ToArray();
            var positions=a.positions.Select((p,i)=>Vector3.Lerp(p,b.positions[i],blend)).ToArray();
            Restore(rotations,positions);GroundSurface();
            baked[frame]=(frame/60f,rotations,bones.Select(t=>t.localPosition).ToArray());
        }
        for(int bone=0;bone<bones.Length;bone++)
        {
            string path=AnimationUtility.CalculateTransformPath(bones[bone],visual);
            for(int c=0;c<4;c++)clip.SetCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[c],Curve(baked.Select(key=>new Keyframe(key.time,key.rotations[bone][c])).ToArray()));
            for(int c=0;c<3;c++)clip.SetCurve(path,typeof(Transform),"m_LocalPosition."+"xyz"[c],Curve(baked.Select(key=>new Keyframe(key.time,key.positions[bone][c])).ToArray()));
        }
        Restore(idle,idlePositions);visual.localPosition=originalPosition;Object.DestroyImmediate(mesh);
        AnimationCurve Curve(Keyframe[] keys)=>new AnimationCurve(keys.Where((key,i)=>i==0||i==keys.Length-1||key.value!=keys[i-1].value||key.value!=keys[i+1].value).ToArray());
        float Ease(float start,float end,float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,end,t));
        void Restore(Quaternion[] rotations,Vector3[] positions){for(int i=0;i<bones.Length;i++){bones[i].localRotation=rotations[i];bones[i].localPosition=positions[i];}}
        void Blend(Quaternion[] ar,Vector3[] ap,Quaternion[] br,Vector3[] bp,float t){for(int i=0;i<bones.Length;i++){bones[i].localRotation=Quaternion.Slerp(ar[i],br[i],t);bones[i].localPosition=Vector3.Lerp(ap[i],bp[i],t);}}
        void Pose(float hips,float lean,float lateral=.004f,float sideLean=0f)
        {
            Restore(idle,idlePositions);visual.position=basePoint;
            Bone("spine").position=World(new Vector3(lateral,hips,-.037f));
            Bone("spine").rotation=Quaternion.AngleAxis(sideLean,basis*Vector3.forward)*Quaternion.AngleAxis(lean,basis*Vector3.right)*Bone("spine").rotation;
            Bone("spine.004").rotation=Quaternion.AngleAxis(-lean*.10f,basis*Vector3.right)*Bone("spine.004").rotation;
        }
        void Contacts(Vector3 leftHand,Vector3 rightHand,Vector3 leftFoot,Vector3 rightFoot,float kneeUp)
        {
            Solve("thigh.L","shin.L","foot.L",leftFoot,Vector3.Lerp(new Vector3(-.18f,.10f,.18f),new Vector3(-.18f,.53f,.52f),kneeUp));
            Solve("thigh.R","shin.R","foot.R",rightFoot,new Vector3(.18f,.10f,.18f));
            Solve("upper_arm.R","forearm.R","hand.R",leftHand,leftHand+new Vector3(.20f,.30f,-.12f));
            Solve("upper_arm.L","forearm.L","hand.L",rightHand,rightHand+new Vector3(-.20f,-.25f,-.12f));Feet();
        }
        void Solve(string upper,string joint,string end,Vector3 destination,Vector3 pole)
        {
            var a=Bone(upper);var b=Bone(joint);var c=Bone(end);Vector3 target=World(destination),bend=World(pole)-a.position;
            float first=Vector3.Distance(a.position,b.position),second=Vector3.Distance(b.position,c.position);
            Vector3 direction=(target-a.position).normalized;float distance=Mathf.Clamp(Vector3.Distance(a.position,target),Mathf.Abs(first-second)+.001f,first+second-.001f);
            if(upper.StartsWith("thigh."))
            {
                bend=Vector3.Slerp(landedBends[upper],bend.normalized,kneeBlend);
            }
            bend=Vector3.ProjectOnPlane(bend,direction).normalized;
            if(upper.StartsWith("thigh."))
            {
                // Carry the knee plane continuously when the moving ankle ray
                // crosses a pole direction. A projected pole can otherwise swap
                // sides even though the ankle follows a smooth trajectory.
                Vector3 previous=Vector3.ProjectOnPlane(previousLegBends[upper],direction).normalized;
                bend=Vector3.RotateTowards(previous,bend,6f*Mathf.Deg2Rad,0f).normalized;
                previousLegBends[upper]=bend;
            }
            if(baking && upper.StartsWith("upper_arm."))
            {
                Vector3 previous=Vector3.ProjectOnPlane(previousArmBends[upper],direction).normalized;
                bend=Vector3.RotateTowards(previous,bend,3f*Mathf.Deg2Rad,0f).normalized;
                previousArmBends[upper]=bend;
            }
            float along=(first*first+distance*distance-second*second)/(2*distance);
            Vector3 knee=a.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Vector3 normal=Vector3.Cross(direction,bend).normalized;
            Quaternion upperFrame=frames[upper],jointFrame=frames[joint];
            if(upper.StartsWith("upper_arm."))
            {
                upperFrame=Quaternion.Slerp(landedArmFrames[upper],upperFrame,kneeBlend);
                jointFrame=Quaternion.Slerp(landedArmFrames[joint],jointFrame,kneeBlend);
            }
            a.rotation=Quaternion.LookRotation(knee-a.position,normal)*upperFrame;
            b.rotation=Quaternion.LookRotation(a.position+direction*distance-knee,normal)*jointFrame;
        }
        void Feet(){foreach(string side in new[]{"L","R"}){var a=Bone("foot."+side);var b=Bone("toe."+side);a.rotation=Quaternion.LookRotation(basis*new Vector3(0,-.035f,.12f),basis*Vector3.up)*frames[a.name];}}
        void GroundSurface()
        {
            float minimum=float.PositiveInfinity;foreach(var skin in skins){if(!skin.enabled)continue;skin.BakeMesh(mesh);foreach(var vertex in mesh.vertices)minimum=Mathf.Min(minimum,skin.transform.TransformPoint(vertex).y);}
            if(!float.IsPositiveInfinity(minimum))visual.position+=Vector3.up*(floor+.025f-minimum);
        }
    }
    private static void Sound(string path,float duration,bool impact)
    {
        const int rate=22050;int count=(int)(rate*duration);var random=new System.Random(17);double phase=0;
        using var writer=new BinaryWriter(File.Create(path));writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
        for(int i=0;i<count;i++){double t=(double)i/count;phase+=2*Math.PI*(impact?75:900-700*t)/rate;double envelope=impact?Math.Exp(-9*t):Math.Sin(Math.PI*t)*.5;double sample=impact?(Math.Sin(phase)*.55+(random.NextDouble()*2-1)*.45):Math.Sin(phase);writer.Write((short)(sample*envelope*18000));}
    }
}
