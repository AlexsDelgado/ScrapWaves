using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Editor-only, repeatable authoring. No UI or tutorial actors are constructed in play mode.</summary>
public static class TutorialAuthoring
{
    public const string Folder = "Assets/Art/Tutorial";
    public const string DefinitionPath = Folder + "/TutorialDefinition.asset";
    private static readonly Color Ink = new(.055f,.075f,.065f,1f);
    private static readonly Color Sage = new(.73f,.88f,.48f,1);
    private static readonly Color Paper = new(.94f,.96f,.90f,1);
    private static TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

    [MenuItem("Tools/ScrapWaves/Author Tutorial 168")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var definition = AssetDatabase.LoadAssetAtPath<TutorialDefinition>(DefinitionPath);
        if (!definition) { definition=ScriptableObject.CreateInstance<TutorialDefinition>(); AssetDatabase.CreateAsset(definition,DefinitionPath); }
        definition.StartingWeapon=Assets<WeaponData>().First(x=>x.WeaponId == "AutomaticCannon");
        definition.Pages=Pages();
        EditorUtility.SetDirty(definition);
        BuildGameplay(definition);
        BuildTitle();
        AssetDatabase.SaveAssets();
        Debug.Log("TUTORIAL_AUTHORING_COMPLETE");
    }

    private static void BuildGameplay(TutorialDefinition definition)
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        foreach(var name in new[]{"Tutorial168","TutorialWorld","TutorialHUD","TutorialLevelUpMenu"}) RemoveRoot(name);
        var player=Find<PlayerMovement>();
        var world=new GameObject("TutorialWorld");
        var root=new GameObject("Tutorial168");
        var director=root.AddComponent<TutorialDirector>();
        Vector3 spawnPosition=player.transform.position;
        Vector3 forward=Vector3.ProjectOnPlane(Find<ThirdPersonCamera>().transform.forward,Vector3.up).normalized;
        Quaternion facing=Quaternion.LookRotation(forward);
        var spawn=Anchor(world.transform,"Spawn",spawnPosition);
        spawn.rotation=facing;
        Vector3 center=spawnPosition+forward*9;center.y=Ground(center);
        var centerAnchor=Anchor(world.transform,"MovementTarget",center);
        var marker=new GameObject("Walk here - 5m fading cylinder",typeof(MeshFilter),typeof(MeshRenderer));
        marker.transform.SetParent(world.transform,false); marker.transform.position=center+Vector3.up*.025f;
        marker.GetComponent<MeshFilter>().sharedMesh=MarkerMesh();
        var markerMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Marker.mat");
        if(!markerMaterial){markerMaterial=new Material(Shader.Find("ScrapWaves/TutorialMarker"));AssetDatabase.CreateAsset(markerMaterial,Folder+"/Marker.mat");}
        marker.GetComponent<MeshRenderer>().sharedMaterial=markerMaterial;
        var sourceStation=Find<CraftingStation>();
        var station=Object.Instantiate(sourceStation,world.transform);
        station.name="Tutorial tinkering table";station.transform.position=center;
        Bind(station,"_craftingUi",player.GetComponent<CraftingUI>());
        Bind(station,"_craftingService",player.GetComponent<WeaponCraftingService>());
        Bind(station,"_inventory",player.GetComponent<MaterialInventory>());
        Bind(station,"_levelUpChoiceUi",player.GetComponent<LevelUpChoiceUI>());
        var config=Read<EnemySpawnRouletteConfig>(Find<OrbitalSpawner>(),"_config");
        var catalog=Assets<MaterialDropVisualCatalog>().First();
        var first=new[]{Actor(config.GetEntry(EnemySpawnKind.ChaserBot).Prefab,world.transform,"1 - Charger",center+facing*new Vector3(8,0,2),true,MaterialType.MetalPipe,catalog)};
        var second=new[]{
            Actor(config.GetEntry(EnemySpawnKind.JunkSlime).Prefab,world.transform,"2 - Junk Slime",center+facing*new Vector3(-8,0,2),true,MaterialType.SheetMetal,catalog),
            Actor(config.GetEntry(EnemySpawnKind.VigilanceDrone).Prefab,world.transform,"2 - Vigilance Drone",center+facing*new Vector3(-10,0,5),true,MaterialType.Gears,catalog),
            Actor(config.GetEntry(EnemySpawnKind.ChaserBot).Prefab,world.transform,"2 - Charger",center+facing*new Vector3(-6,0,5),true,MaterialType.MetalPipe,catalog)
        };
        var last=Enumerable.Range(0,4).Select(i=>Actor(config.GetEntry(EnemySpawnKind.JunkSlime).Prefab,world.transform,"3 - Moving slime "+(i+1),center+facing*new Vector3((i-1.5f)*3,0,12),false,MaterialType.SheetMetal,catalog)).ToArray();
        var originalVent=Object.FindObjectsByType<MapLaunchPad>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(x=>x.name=="Jump floor");
        var vent=Object.Instantiate(originalVent,world.transform);vent.name="Tutorial trash geyser";
        Vector3 ventPosition=center+forward*24;ventPosition.y=Ground(ventPosition)+.16f;vent.transform.position=ventPosition;
        // The normal floating island remains present after TutorialWorld is removed.
        Bind(vent,"_landing",GameObject.Find("Land floor").transform);
        Bind(vent,"_launchFromTrigger",true);
        var collider=vent.GetComponent<BoxCollider>();collider.isTrigger=true;
        vent.gameObject.SetActive(false);
        var canvas=CanvasRoot("TutorialHUD",null,35);
        var hud=BuildHud(canvas.transform);
        var choices=player.GetComponent<LevelUpChoiceUI>();
        var normalChoice=Read<ChoiceMenuView>(choices,"_levelUpView");
        var tutorialChoice=Object.Instantiate(normalChoice,normalChoice.transform.parent);tutorialChoice.name="TutorialLevelUpMenu";
        var choiceWindow=(RectTransform)tutorialChoice.TitleText.transform.parent;choiceWindow.sizeDelta=new Vector2(1400,850);
        tutorialChoice.TitleText.text="LEVEL UP / CHOOSE YOUR FIRST ITEM";
        tutorialChoice.SubtitleText.fontSize=23;tutorialChoice.SubtitleText.alignment=TextAlignmentOptions.TopLeft;
        tutorialChoice.SubtitleText.rectTransform.sizeDelta=new Vector2(1312,150);
        tutorialChoice.SubtitleText.text=definition.Page(TutorialStep.ItemChoice).Text;
        var cards=(RectTransform)tutorialChoice.Cards[0].transform.parent;cards.offsetMax=new Vector2(cards.offsetMax.x,-274);
        tutorialChoice.gameObject.SetActive(false);Bind(choices,"_tutorialLevelUpView",tutorialChoice);Bind(choices,"_tutorialDefinition",definition);
        Bind(director,"_definition",definition);Bind(director,"_hud",hud);Bind(director,"_world",world);
        Bind(director,"_spawn",spawn);Bind(director,"_markerCenter",centerAnchor);Bind(director,"_marker",marker);
        Bind(director,"_station",station);Bind(director,"_vent",vent);Bind(director,"_player",player);
        Bind(director,"_weapons",player.GetComponent<WeaponManager>());Bind(director,"_crafting",player.GetComponent<WeaponCraftingService>());
        Bind(director,"_craftingUi",player.GetComponent<CraftingUI>());Bind(director,"_inventory",player.GetComponent<MaterialInventory>());
        Bind(director,"_xp",player.GetComponent<PlayerXP>());Bind(director,"_levels",player.GetComponent<LevelUpOrchestrator>());
        Bind(director,"_arrow",Find<GuideArrow>());BindArray(director,"_firstEnemies",first);BindArray(director,"_secondEnemies",second);BindArray(director,"_lastEnemies",last);
        if(!director.HasReferences)throw new InvalidOperationException("Tutorial references are incomplete.");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    private static TutorialEnemy Actor(GameObject prefab,Transform parent,string name,Vector3 position,bool stationary,MaterialType material,MaterialDropVisualCatalog catalog)
    {
        var actor=Object.Instantiate(prefab,parent);actor.name=name;
        actor.SetActive(false);
        actor.transform.position=new Vector3(position.x,Ground(position)+1.1f,position.z);
        actor.transform.rotation=Quaternion.Euler(0,180,0);
        foreach(var component in actor.GetComponentsInChildren<Component>(true))
        {
            if(component is EnemyMaterialDrop || component is EnemyDropExperience || component is EnemyScrapDrop || component is EnemyTemporaryPowerupDrop || component is SwarmPooledEnemy)
                Object.DestroyImmediate(component);
        }
        if(stationary)
        {
            foreach(var ai in actor.GetComponentsInChildren<EnemyBehaviorBase>(true))ai.enabled=false;
            foreach(var follower in actor.GetComponentsInChildren<EnemyFollow>(true))follower.enabled=false;
            foreach(var follower in actor.GetComponentsInChildren<SimpleFollow>(true))follower.enabled=false;
            foreach(var damage in actor.GetComponentsInChildren<EnemyContactDamage>(true))Object.DestroyImmediate(damage);
            var rigid=actor.GetComponent<Rigidbody>();if(rigid)rigid.isKinematic=true;
        }
        if(!actor.GetComponent<EnemyOutgoingDamageScale>())actor.AddComponent<EnemyOutgoingDamageScale>();
        var lesson=actor.AddComponent<TutorialEnemy>();
        Bind(lesson,"_stationary",stationary);Bind(lesson,"_primaryMaterial",(int)material);Bind(lesson,"_damageMultiplier",.5f);
        var drops=new List<TutorialPickup>();
        foreach(MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            var drop=new GameObject(name+" - "+type+" pickup");drop.transform.SetParent(parent,false);
            var visualPrefab=catalog.GetVisualPrefab(type);
            var visual=visualPrefab?Object.Instantiate(visualPrefab,drop.transform):GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(drop.transform,false);visual.name="Material visual";
            foreach(var physics in visual.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(physics);
            foreach(var body in visual.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(body);
            foreach(var script in visual.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(script);
            // A stable, readable material model is saved for each grant; its amount is bound from the live recipe.
            var pickup=drop.AddComponent<TutorialPickup>();Bind(pickup,"_material",(int)type);Bind(pickup,"_visual",visual.transform);
            drops.Add(pickup);drop.SetActive(false);
        }
        BindArray(lesson,"_drops",drops.ToArray());return lesson;
    }

    private static TutorialHudView BuildHud(Transform parent)
    {
        var panel=Rect("Lesson",parent,new Vector2(.5f,1),new Vector2(1120,300),new Vector2(0,-32));panel.pivot=new Vector2(.5f,1);
        panel.gameObject.AddComponent<Image>().color=Ink;
        var group=panel.gameObject.AddComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;group.interactable=false;
        var stripe=Rect("Accent",panel,new Vector2(0,1),new Vector2(5,300),Vector2.zero);stripe.pivot=new Vector2(0,1);stripe.gameObject.AddComponent<Image>().color=Sage;
        var title=Label("Title",panel,"SCRAPYARD INDUCTION",25,new Vector2(32,-22),new Vector2(1040,35),Sage);
        title.fontStyle=FontStyles.Bold;
        var body=Label("Body",panel,"",23,new Vector2(32,-68),new Vector2(1056,164),Paper);
        body.textWrappingMode=TextWrappingModes.Normal;
        var footer=Label("Progress",panel,"",18,new Vector2(32,-264),new Vector2(770,30),Sage);
        var advance=Key(panel,"E  CONTINUE",new Vector2(872,-260),new Vector2(208,32));
        var controls=new GameObject[5];
        for(int i=0;i<5;i++)controls[i]=Rect(((TutorialControls)i)+" controls",panel,new Vector2(0,1),new Vector2(830,42),new Vector2(32,-214)).gameObject;
        Key(controls[1].transform,"W A S D   MOVE",new Vector2(0,0),new Vector2(190,32));
        Key(controls[1].transform,"SPACE   JUMP",new Vector2(200,0),new Vector2(180,32));
        Key(controls[1].transform,"SHIFT   DASH",new Vector2(390,0),new Vector2(180,32));
        Key(controls[1].transform,"CTRL   SLIDE",new Vector2(580,0),new Vector2(180,32));
        Key(controls[2].transform,"LEFT MOUSE   FIRE",Vector2.zero,new Vector2(270,32));
        Key(controls[3].transform,"E   TINKERING TABLE",Vector2.zero,new Vector2(290,32));
        Key(controls[4].transform,"Q   WEAPON ABILITY",Vector2.zero,new Vector2(285,32));
        foreach(var c in controls)c.SetActive(false);
        var view=panel.gameObject.AddComponent<TutorialHudView>();Bind(view,"_group",group);Bind(view,"_title",title);Bind(view,"_body",body);Bind(view,"_footer",footer);Bind(view,"_advance",advance);BindArray(view,"_controlGroups",controls);
        return view;
    }

    private static void BuildTitle()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity");
        RemoveRoot("TutorialMenuUI"); RemoveRoot("Tutorial pending");
        var title=Find<TitleScreenController>();var objectives=Find<ObjectivesMenuUI>();
        var previous=title.PlayButton.transform.parent.Find("TutorialButton");if(previous)Object.DestroyImmediate(previous.gameObject);
        var replay=Object.Instantiate(title.PlayButton,title.PlayButton.transform.parent);replay.name="TutorialButton";
        replay.onClick=new Button.ButtonClickedEvent();
        var replayRect=replay.GetComponent<RectTransform>();
        var quitRect=title.QuitButton.GetComponent<RectTransform>();
        replayRect.anchoredPosition=quitRect.anchoredPosition+new Vector2(28,-88);
        var item=replay.GetComponent<MainMenuItemView>();Read<TMP_Text>(item,"_label").text="PLAY TUTORIAL";
        var label=Read<TMP_Text>(item,"_label");label.enableAutoSizing=true;label.fontSizeMin=24;label.fontSizeMax=label.fontSize;
        Bind(title,"_tutorialButton",replay);
        var presentation=title.GetComponent<MainMenuPresentationController>();
        var items=presentation.OrderedItems.Where(x=>x && x.name!="TutorialButton").ToList();items.Insert(Mathf.Min(4,items.Count),item);BindArray(presentation,"_orderedItems",items.ToArray());
        var root=CanvasRoot("TutorialMenuUI",title.transform,1800);
        var presenter=root.gameObject.AddComponent<TutorialMenuPresenter>();
        var help=Rect("Meta guidance",root.transform,new Vector2(.5f,.5f),new Vector2(900,365),Vector2.zero);help.pivot=new Vector2(.5f,.5f);help.gameObject.AddComponent<Image>().color=Ink;
        Label("Eyebrow",help,"BACK AT THE SCRAPYARD",18,new Vector2(32,-24),new Vector2(830,30),Sage);
        var heading=Label("Heading",help,"MAKE THE NEXT RUN COUNT",30,new Vector2(32,-64),new Vector2(830,42),Paper);heading.fontStyle=FontStyles.Bold;
        Label("Body",help,"Every time you play, you complete objectives and collect Scrap. This scrap can be used to upgrade your starting stats, as well as upgrading your items and unlocking new items and weapon variants.",24,new Vector2(32,-128),new Vector2(830,146),Paper);
        var close=Button("Close",help,"E  CONTINUE",new Vector2(598,-302),new Vector2(270,42));
        var info=Button("Tutorial info",root.transform,"i   PROGRESSION HELP",new Vector2(0,0),new Vector2(275,44));
        var infoRt=info.GetComponent<RectTransform>();infoRt.anchorMin=infoRt.anchorMax=new Vector2(1,1);infoRt.pivot=new Vector2(1,1);infoRt.anchoredPosition=new Vector2(-36,-22);
        var accent=Rect("Tutorial pending",title.ObjectivesButton.transform,new Vector2(1,.5f),new Vector2(8,40),new Vector2(-52,0)); accent.pivot=new Vector2(1,.5f);accent.gameObject.AddComponent<Image>().color=Sage;

        var accentGroup=accent.gameObject.AddComponent<CanvasGroup>();accentGroup.alpha=0;accentGroup.blocksRaycasts=false;
        Bind(presenter,"_title",title);Bind(presenter,"_objectives",objectives);Bind(presenter,"_replay",replay);Bind(presenter,"_info",info);Bind(presenter,"_close",close);Bind(presenter,"_help",help.gameObject);Bind(presenter,"_pendingAccent",accentGroup);
        help.gameObject.SetActive(false);info.gameObject.SetActive(false);
        AuthorMetaContent(objectives);
        GameplayUiAuthoringAssets.PersistGraphics(title.transform);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }

    private static void AuthorMetaContent(ObjectivesMenuUI objectives)
    {
        Invoke(objectives,"EnsureFilterBars");
        Invoke(objectives,"EnsureDevResetProgressButton");Invoke(objectives,"EnsureDevMaxUpgradesButton");
        var shop=Read<MetaUpgradeShopUI>(objectives,"_metaUpgradeShop");
        Invoke(shop,"EnsureCosts");Invoke(shop,"EnsureLayout");Invoke(shop,"EnsureFilterBar");
        // Saved pools leave headroom for new catalogue entries, without constructing UI during a run.
        AuthorPool(objectives,"_authoredRows",Read<ObjectiveRowView>(objectives,"_objectiveRowPrefab"),Read<RectTransform>(objectives,"_objectivesContent"),96);
        AuthorPool(objectives,"_authoredUnlocks",Read<UnlockCardView>(objectives,"_unlockCardPrefab"),Read<RectTransform>(objectives,"_unlocksContent"),64);
        AuthorPool(shop,"_authoredCards",Read<UnlockCardView>(shop,"_cardPrefab"),Read<RectTransform>(shop,"_content"),64);
        EditorUtility.SetDirty(objectives);EditorUtility.SetDirty(shop);
    }
    private static void AuthorPool<T>(Object target,string field,T template,Transform parent,int count) where T:Component
    {
        foreach(var old in parent.GetComponentsInChildren<T>(true))if(old.transform!=parent)Object.DestroyImmediate(old.gameObject);
        var saved=new T[count];for(int i=0;i<count;i++){saved[i]=Object.Instantiate(template,parent);saved[i].name="Authored "+typeof(T).Name+" "+(i+1).ToString("00");saved[i].gameObject.SetActive(false);}
        BindArray(target,field,saved);
    }
    private static Canvas CanvasRoot(string name,Transform parent,int order)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));if(parent)go.transform.SetParent(parent,false);
        var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=order;
        var scale=go.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;
        return canvas;
    }
    private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 size,Vector2 position)
    {
        var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=anchor;rt.pivot=new Vector2(0,1);rt.sizeDelta=size;rt.anchoredPosition=position;return rt;
    }
    private static TextMeshProUGUI Label(string name,Transform parent,string text,float size,Vector2 pos,Vector2 dimensions,Color color)
    {
        var rt=Rect(name,parent,new Vector2(0,1),dimensions,pos);var tmp=rt.gameObject.AddComponent<TextMeshProUGUI>();tmp.font=Font;tmp.fontSize=size;tmp.text=text;tmp.color=color;tmp.raycastTarget=false;tmp.textWrappingMode=TextWrappingModes.Normal;return tmp;
    }
    private static GameObject Key(Transform parent,string text,Vector2 pos,Vector2 size)
    {
        var rt=Rect(text,parent,new Vector2(0,1),size,pos);rt.gameObject.AddComponent<Image>().color=new Color(.21f,.27f,.19f,1);
        var label=Label("Label",rt,text,17,new Vector2(8,-4),size-new Vector2(16,8),Paper);label.alignment=TextAlignmentOptions.Center;label.fontStyle=FontStyles.Bold;return rt.gameObject;
    }
    private static Button Button(string name,Transform parent,string text,Vector2 pos,Vector2 size)
    {var go=Key(parent,text,pos,size);go.name=name;var b=go.AddComponent<Button>();b.targetGraphic=go.GetComponent<Image>();return b;}
    private static Transform Anchor(Transform parent,string name,Vector3 point){var go=new GameObject(name);go.transform.SetParent(parent);go.transform.position=point;return go.transform;}
    private static float Ground(Vector3 position)
    {Physics.SyncTransforms();var hits=Physics.RaycastAll(new Vector3(position.x,6,position.z),Vector3.down,20);return hits.Where(x=>!x.collider.isTrigger && x.collider.GetComponentInParent<PlayerMovement>()==null && x.collider.GetComponentInParent<EnemyHealth>()==null).OrderByDescending(x=>x.point.y).Select(x=>x.point.y).DefaultIfEmpty(0).First();}
    private static Mesh MarkerMesh()
    {
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Marker.asset");if(mesh)return mesh;
        mesh=new Mesh{name="Fading lesson cylinder"};var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
        for(int i=0;i<=96;i++){float a=i/96f*Mathf.PI*2;v.Add(new Vector3(Mathf.Cos(a)*2.5f,0,Mathf.Sin(a)*2.5f));v.Add(new Vector3(Mathf.Cos(a)*2.5f,3,Mathf.Sin(a)*2.5f));uv.Add(new Vector2(i/96f,0));uv.Add(new Vector2(i/96f,1));if(i<96){int k=i*2;t.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});}}
        mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/Marker.asset");return mesh;
    }
    private static T Find<T>() where T:Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    private static IEnumerable<T> Assets<T>() where T:Object=>AssetDatabase.FindAssets("t:"+typeof(T).Name).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(x=>x);
    private static void RemoveRoot(string name){var all=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None);foreach(var t in all.Where(x=>x.name==name).ToArray())Object.DestroyImmediate(t.gameObject);}
    private static void Invoke(Object target,string method)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(target,null);
    public static T Read<T>(Object target,string field) where T:Object => new SerializedObject(target).FindProperty(field).objectReferenceValue as T;
    public static void Bind(Object target,string field,Object value){var so=new SerializedObject(target);var p=so.FindProperty(field)??throw new Exception(target+" missing "+field);p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    private static void Bind(Object target,string field,bool value){var so=new SerializedObject(target);so.FindProperty(field).boolValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    private static void Bind(Object target,string field,float value){var so=new SerializedObject(target);so.FindProperty(field).floatValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    private static void Bind(Object target,string field,int value){var so=new SerializedObject(target);so.FindProperty(field).intValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    public static void BindArray<T>(Object target,string field,T[] value) where T:Object{var so=new SerializedObject(target);var p=so.FindProperty(field);p.arraySize=value.Length;for(int i=0;i<value.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=value[i];so.ApplyModifiedPropertiesWithoutUndo();}
    private static TutorialPage P(TutorialStep step,string title,string text,TutorialControls controls=TutorialControls.None,bool advance=false)=>new TutorialPage{Step=step,Title=title,Text=text,Controls=controls,AdvanceWithE=advance};
    private static TutorialPage[] Pages()=>new[]{
        P(TutorialStep.Welcome,"WELCOME TO DUMPSTER FIRE","Welcome to Dumpster Fire! In this survivor game, you’ll face hordes of enemies and bosses while you improve your equipment and try to escape the scrapyard. Let’s start with some basics.",advance:true),
        P(TutorialStep.Movement,"01 / GET MOVING","You can move with W/A/S/D and jump with the space bar. You can dash with Left Shift, which consumes a dash charge that recharges over time. If you’re going quick enough, like after a dash, you can slide by holding Left Control. Walk to the highlighted area to continue.",TutorialControls.Movement),
        P(TutorialStep.FirstEnemy,"02 / FIRST CONTACT","An enemy has spawned! Shoot him with your left click until he dies.",TutorialControls.Fire),
        P(TutorialStep.FirstPickup,"02 / COLLECT THE MATERIALS","Walk over the dropped materials to collect them.",TutorialControls.Movement),
        P(TutorialStep.Upgrade,"02 / UPGRADE YOUR AUTOCANNON","Different enemies drop different materials. You can use these materials to upgrade your weapons and to craft new ones. Go to the tinkering table and upgrade your autocannon to level 2.",TutorialControls.Interact),
        P(TutorialStep.Heat,"03 / FEELING THE HEAT","You are starting to attract attention! Killing enemies fills the Heat gauge, and the fuller it is, the more enemies will spawn. Once it reaches its maximum, an Overheat event will begin, spawning elite enemies and bosses.",advance:true),
        P(TutorialStep.Ability,"03 / WEAPON ABILITIES","Luckily, you also have weapon abilities at your disposal. Each weapon has a unique ability, but they all consume ammo to use and have a timed cooldown. Press Q to use your ability to deal with this small horde.",TutorialControls.Ability),
        P(TutorialStep.HordePickup,"03 / MATERIALS FOR TINKERING","Collect the materials dropped by all three enemies, then return to the tinkering table.",TutorialControls.Movement),
        P(TutorialStep.Tinker,"03 / BUILD YOUR SECOND WEAPON","With enough materials, you can Tinker at a table to create a new semi-random weapon. Go to the table and choose your second weapon slot to tinker a new weapon. If you upgrade weapons enough, you will also be able to perform Advanced Tinkering on them, which will evolve their capabilities or change them drastically.",TutorialControls.Interact),
        P(TutorialStep.AutomaticCombat,"04 / YOUR WEAPONS WORK TOGETHER","We’re almost done. Your equipped weapons will cycle when you run out of ammo; however, unequipped weapons will still fire independently, although with limited range and damage. Kill the last horde of enemies with your new weapons to continue.",TutorialControls.Fire),
        P(TutorialStep.FinalPickup,"04 / COLLECT THE LAST MATERIALS","Collect the materials from all four enemies to level up.",TutorialControls.Movement),
        P(TutorialStep.ItemChoice,"04 / LEVEL UP","Apart from using them for crafting, picking materials up makes you level up. When you do, you will get small random stat boosts, as well as choosing one of three passive items. You have six item slots, differentiated by body parts, which can fit different items. If you already have items, you will also be given the choice to upgrade them up to level 6."),
        P(TutorialStep.Escape,"04 / YOUR WAY OUT","Your goal is to leave the scrapyard. To do this, you need to trigger enough Overheat cycles to attract the two bosses, collect their core batteries and use them to power a massive gate found somewhere between the trash piles.",advance:true),
        P(TutorialStep.Vent,"04 / TAKE TO THE SKY","To finish the tutorial, walk towards the trash geyser. It will make you jump up to the floating islands in the sky. These are ripe with opportunities, as well as being the only place you’ll find tinkering stations. Be warned, however, that during Overheat events, you will be forced to jump back down to the scrapyard.",TutorialControls.Movement)
    };
}
