#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Isolated editor-only visual fixture. Never saves or changes gameplay scenes.</summary>
public static class CraftingReadoutPreview
{
    private static readonly string Output = Environment.GetEnvironmentVariable("SCRAPWAVES_CRAFTING_PREVIEW_OUTPUT")
        ?? Path.Combine(Path.GetTempPath(), "ScrapWavesCraftingPreview");
    private static void Set(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);
    public static void Capture()
    {
        Directory.CreateDirectory(Output);
        Scene scene=EditorSceneManager.NewPreviewScene();
        Camera camera=new GameObject("Preview camera",typeof(Camera)).GetComponent<Camera>();
        SceneManager.MoveGameObjectToScene(camera.gameObject,scene); camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.025f,0.035f,0.03f);camera.orthographic=true;camera.orthographicSize=5;camera.transform.position=new Vector3(0,0,-10);
        GameObject owner=new GameObject("In-memory preview economy");owner.SetActive(false);SceneManager.MoveGameObjectToScene(owner,scene);
        MaterialInventory inventory=owner.AddComponent<MaterialInventory>();WeaponManager manager=owner.AddComponent<WeaponManager>();WeaponCraftingService service=owner.AddComponent<WeaponCraftingService>();
        Set(service,"_inventory",inventory);Set(service,"_weaponManager",manager);service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
        WeaponData flame=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        WeaponData rocket=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");
        PlayerStats stats=owner.AddComponent<PlayerStats>();
        PlayerStats authoredStats=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab").GetComponent<PlayerStats>();
        Set(stats,"_statDefinitions",typeof(PlayerStats).GetField("_statDefinitions",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(authoredStats));
        typeof(PlayerStats).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(stats,null);
        WeaponInstance first=new WeaponInstance {Data=flame,Level=5};WeaponInstance second=new WeaponInstance {Data=rocket,Level=2};
        var equipped=(List<IWeaponBehaviour>)typeof(WeaponManager).GetField("_equipped",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);equipped.Add(new Stub(first));
        var rocketBehaviour=(BasicProjectileWeapon)WeaponBehaviourFactory.Create(rocket,null,null,null,null);
        rocketBehaviour.Setup(second,owner.transform,stats,null);equipped.Add(rocketBehaviour);
        var pool=new List<WeaponData>();foreach(string name in new[]{"Mortar","AutomaticCannon","RotatingBlade"})pool.Add(AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/ScriptableObjects/WeaponSO/{name}.asset"));Set(service,"_weaponPool",pool);
        foreach(MaterialType type in Enum.GetValues(typeof(MaterialType))) inventory.Add(type,100);
        GameObject menu=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RunMenuPrefabBuilder.CraftingPrefabPath));SceneManager.MoveGameObjectToScene(menu,scene);menu.SetActive(true);
        CraftingMenuView view=menu.GetComponent<CraftingMenuView>();PrepareCanvas(view.Canvas,camera);
        view.BalanceReadout.Bind(inventory,null);view.ShowPanel(view.UpgradePanel);view.UpgradeNameText.text=rocket.DisplayName;view.UpgradeLevelText.text="LV 2 → 3";
        var costs=service.GetUpgradeCost(rocket,WeaponUpgradePath.None,3);view.UpgradeReadout.Bind(inventory,costs);
        CraftingUI.BindUpgradePreview(view,rocketBehaviour);
        view.Slots[0].Bind(first,false,true);view.Slots[1].Bind(second,true,true);view.Slots[2].Bind(null,false,true);
        view.Slots[0].Availability.Bind(service.GetAvailableAction(first));view.Slots[1].Availability.Bind(service.GetAvailableAction(second));view.Slots[2].Availability.Bind(service.CanTinkerNewWeapon()?CraftingActionKind.TinkerNewWeapon:null);
        view.StatusText.text="Editor preview • Affordable upgrade, advanced tinkering and next empty slot";Render(camera,"crafting-affordable.png");
        second.Level=6;second.SelectedPath=WeaponUpgradePath.PathA;
        view.UpgradeLevelText.text="LV 6 → 7";view.UpgradeNameText.text=rocket.DisplayName+" · "+rocket.PathA.PathName;
        view.Slots[1].Bind(second,true,true);CraftingUI.BindUpgradePreview(view,rocketBehaviour);
        view.UpgradeReadout.Bind(inventory,service.GetUpgradeCost(rocket,WeaponUpgradePath.PathA,7));
        Render(camera,"crafting-upgrade-path.png");
        second.Level=2;second.SelectedPath=WeaponUpgradePath.None;view.UpgradeLevelText.text="LV 2 → 3";view.UpgradeNameText.text=rocket.DisplayName;
        view.Slots[1].Bind(second,true,true);CraftingUI.BindUpgradePreview(view,rocketBehaviour);view.UpgradeReadout.Bind(inventory,costs);
        CapturePulse(camera,menu,"crafting-pulse",false);
        inventory.TrySpend(new[]{new MaterialCost(MaterialType.SheetMetal,96),new MaterialCost(MaterialType.MetalPipe,95),new MaterialCost(MaterialType.Gears,92),new MaterialCost(MaterialType.JellifiedFuel,96),new MaterialCost(MaterialType.PlasticExplosive,95),new MaterialCost(MaterialType.Wiring,92)});
        view.BalanceReadout.Bind(inventory,null);view.ShowPanel(view.TinkerPanel);var tinker=service.GetTinkeringCost(3);view.TinkerReadout.Bind(inventory,tinker);view.TinkerCostLabel.text="COST • SLOT 3";view.TinkerButton.interactable=false;
        var offer=service.GetTinkeringOffer();for(int i=0;i<2;i++){view.Candidates[i].NameText.text=offer[i].DisplayName;view.Candidates[i].Icon.sprite=WeaponUiIcons.Resolve(offer[i],false);}
        foreach(var slot in view.Slots)slot.Availability.Bind(null);view.StatusText.text="Editor preview • Insufficient materials; no availability glow";Render(camera,"crafting-insufficient.png");
        view.ShowPanel(view.AdvancedPanel);view.AdvancedNameText.text=flame.DisplayName;view.AdvancedLevelText.text="LV 5 → 6";view.AdvancedPathText.text="Advanced tinkering";view.AdvancedDescriptionText.text="Preview of rare material affordability";view.AdvancedReadout.Bind(inventory,service.GetAdvancedTinkeringCost(flame));view.AcceptButton.interactable=false;view.DeclineButton.interactable=false;Render(camera,"crafting-advanced-mixed.png");menu.SetActive(false);
        foreach(MaterialType type in Enum.GetValues(typeof(MaterialType)))inventory.Add(type,100);
        GameObject hud=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CraftingReadoutAuthoring.HudPath));hud.SetActive(false);SceneManager.MoveGameObjectToScene(hud,scene);
        foreach(Canvas canvas in hud.GetComponentsInChildren<Canvas>(true))PrepareCanvas(canvas,camera);
        WeaponClusterHud cluster=hud.GetComponentInChildren<WeaponClusterHud>(true);Set(cluster,"_weaponManager",manager);Set(cluster,"_crafting",service);hud.SetActive(true);
        typeof(WeaponClusterHud).GetMethod("TryWireFromHierarchy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cluster,null);typeof(WeaponClusterHud).GetMethod("RefreshWeaponPanel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cluster,null);Render(camera,"hud-affordable.png");CapturePulse(camera,hud,"hud-pulse",true);
        Set(manager,"_currentManualIndex",1);typeof(WeaponClusterHud).GetMethod("RefreshWeaponPanel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cluster,null);Render(camera,"hud-rotated.png");hud.SetActive(false);
        GameObject arrow=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Arrow_Guide.prefab"));SceneManager.MoveGameObjectToScene(arrow,scene);
        GuideArrow guide=arrow.GetComponent<GuideArrow>()??arrow.AddComponent<GuideArrow>();
        GameObject station=new GameObject("Preview table target");station.SetActive(false);SceneManager.MoveGameObjectToScene(station,scene);CraftingStation table=station.AddComponent<CraftingStation>();station.transform.position=new Vector3(3,0,0);
        GuideArrowController controller=arrow.GetComponent<GuideArrowController>()??arrow.AddComponent<GuideArrowController>();Set(controller,"_guideArrow",guide);Set(controller,"_craftingStation",table);Set(controller,"_crafting",service);
        typeof(GuideArrow).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(guide,null);
        typeof(GuideArrowController).GetMethod("ShowCraftingGuide",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{10f});
        if(guide.Target!=table.transform)throw new Exception("Affordable table guide did not show");arrow.transform.position=new Vector3(-1,0,0);arrow.transform.rotation=Quaternion.Euler(0,0,90);arrow.transform.localScale=Vector3.one;Render(camera,"table-arrow-affordable.png");
        var spend=new List<MaterialCost>();foreach(MaterialType type in Enum.GetValues(typeof(MaterialType)))spend.Add(new MaterialCost(type,inventory.GetAmount(type)));inventory.TrySpend(spend);
        typeof(GuideArrowController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);if(guide.Target!=null)throw new Exception("Unaffordable table guide did not hide");Render(camera,"table-arrow-unaffordable.png");
        MakeContactSheet();
        EditorSceneManager.ClosePreviewScene(scene);
        Debug.Log("Crafting preview captures written to "+Output);
    }
    private static void CapturePulse(Camera camera,GameObject root,string prefix,bool hud)
    {
        float previous=Time.timeScale;Time.timeScale=0f;
        try
        {
            var views=root.GetComponentsInChildren<CraftingAvailabilityView>(true);
            const int frames=84;
            for(int i=0;i<frames;i++)
            {
                float timestamp=i*(4f*Mathf.PI/2.4f)/frames;
                foreach(var view in views)view.RefreshPulse(timestamp);
                Render(camera,$"{prefix}-{i:D3}.png");
                // Crop only the actual rendered pixels for a readable animated delivery.
                Texture2D source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes(Path.Combine(Output,$"{prefix}-{i:D3}.png")));
                RectInt rect=hud?new RectInt(1380,0,540,210):new RectInt(115,250,510,410);
                Texture2D crop=new Texture2D(rect.width,rect.height,TextureFormat.RGB24,false);
                crop.SetPixels(source.GetPixels(rect.x,rect.y,rect.width,rect.height));crop.Apply();
                File.WriteAllBytes(Path.Combine(Output,$"{prefix}-crop-{i:D3}.png"),crop.EncodeToPNG());
                Object.DestroyImmediate(crop);Object.DestroyImmediate(source);
            }
        }
        finally{Time.timeScale=previous;}
    }
    private static void MakeContactSheet()
    {
        Texture2D sheet=new Texture2D(1920,2880,TextureFormat.RGB24,false);
        Color[] background=new Color[1920*2880];for(int i=0;i<background.Length;i++)background[i]=new Color(0.025f,0.035f,0.03f);sheet.SetPixels(background);
        Copy(sheet,"crafting-affordable.png",new RectInt(0,0,1920,1080),0,1800);
        Copy(sheet,"crafting-advanced-mixed.png",new RectInt(0,0,1920,1080),0,720);
        Copy(sheet,"hud-affordable.png",new RectInt(1370,0,550,200),320,450);
        Copy(sheet,"hud-rotated.png",new RectInt(1370,0,550,200),1050,450);
        Copy(sheet,"table-arrow-affordable.png",new RectInt(650,350,620,350),250,30);
        Copy(sheet,"table-arrow-unaffordable.png",new RectInt(650,350,620,350),1050,30);
        sheet.Apply();File.WriteAllBytes(Path.Combine(Output,"crafting-implementation-preview.png"),sheet.EncodeToPNG());Object.DestroyImmediate(sheet);
    }
    private static void Copy(Texture2D sheet,string name,RectInt source,int x,int y)
    {
        Texture2D image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes(Path.Combine(Output,name)));
        sheet.SetPixels(x,y,source.width,source.height,image.GetPixels(source.x,source.y,source.width,source.height));Object.DestroyImmediate(image);
    }
    private static void PrepareCanvas(Canvas canvas,Camera camera){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
    private static void Render(Camera camera,string name)
    {
        RenderTexture target=new RenderTexture(1920,1080,24);camera.targetTexture=target;Canvas.ForceUpdateCanvases();foreach(TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))text.ForceMeshUpdate();camera.Render();
        RenderTexture previous=RenderTexture.active;RenderTexture.active=target;Texture2D image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(target);
    }
    private sealed class Stub:IWeaponBehaviour
    {
        public WeaponInstance Runtime{get;}public Stub(WeaponInstance runtime){Runtime=runtime;}
        public void Setup(WeaponInstance instance,Transform owner,PlayerStats stats,HeatManager heat){}public void TickAutomatic(float deltaTime,Vector3 aimDirection){}public void TickManual(float deltaTime,Vector3 aimDirection,bool isFiring){}public void UseActiveAbility(Vector3 aimDirection){}public bool CanCrit()=>false;
    }
}
#endif
