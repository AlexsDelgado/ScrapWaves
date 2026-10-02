using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public sealed class CraftingGuideArrowTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private readonly List<Object> _cleanup=new();
    private float _previousRunStart;
    private UnityEngine.Random.State _previousRandom;
    [SetUp] public void Setup(){_previousRunStart=(float)typeof(RunSessionStats).GetField("_runStartTime",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);_previousRandom=UnityEngine.Random.state;}
    [TearDown] public void Cleanup(){Time.timeScale=1;GameplayPause.Reset();typeof(RunSessionStats).GetField("_runStartTime",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,_previousRunStart);UnityEngine.Random.state=_previousRandom;for(int i=_cleanup.Count-1;i>=0;i--)if(_cleanup[i]!=null)Object.DestroyImmediate(_cleanup[i]);_cleanup.Clear();}
    private T Track<T>(T o)where T:Object{_cleanup.Add(o);return o;}
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    private static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n,Flags).GetValue(o);
    private static void Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Flags).Invoke(o,args);
    private Harness Create(int level=1,bool basic=false)
    {
        var player=Track(new GameObject("Crafting eligibility"));player.SetActive(false);var inventory=player.AddComponent<MaterialInventory>();var manager=player.AddComponent<WeaponManager>();var service=player.AddComponent<WeaponCraftingService>();Set(service,"_inventory",inventory);Set(service,"_weaponManager",manager);service.SetMaterialBalance(AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>("Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset"));
        var data=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");var runtime=new WeaponInstance{Data=data,Level=level,State=WeaponState.Manual};Get<List<IWeaponBehaviour>>(manager,"_equipped").Add(new Probe(runtime));
        Set(service,"_weaponPool",new List<WeaponData>{AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset"),AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset")});
        var arrow=Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Arrow_Guide.prefab")));arrow.SetActive(false);var guide=arrow.AddComponent<GuideArrow>();var controller=arrow.AddComponent<GuideArrowController>();Call(guide,"Awake");
        var table=Track(new GameObject("Crafting target"));table.SetActive(false);var station=table.AddComponent<CraftingStation>();Set(controller,"_guideArrow",guide);Set(controller,"_craftingStation",station);Set(controller,"_crafting",service);Set(controller,"_dialogueDriven",true);
        var costs=basic?service.GetTinkeringCost(2):level==5?service.GetAdvancedTinkeringCost(data):service.GetUpgradeCost(data,WeaponUpgradePath.None,level+1);
        return new Harness{Player=player,Inventory=inventory,Manager=manager,Service=service,Guide=guide,Controller=controller,Station=station,Costs=costs};
    }
    private static void Fund(Harness h){foreach(var cost in h.Costs)h.Inventory.Add(cost.Material,cost.Amount);}
    [TestCase(1,false)] [TestCase(5,false)] [TestCase(1,true)]
    public void DialoguePresent_AffordableUpgradeAdvancedOrBasicCraftTriggersSpatialGuide(int level,bool basic)
    {
        var h=Create(level,basic);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);Fund(h);
        Assert.That(h.Service.HasAnyAvailableCraftingAction(),Is.True);var random=UnityEngine.Random.state;Call(h.Controller,"Update");
        Assert.That(h.Guide.Target,Is.SameAs(h.Station.transform));Assert.That(Get<bool>(h.Controller,"_craftingHintShown"),Is.True);Assert.That(UnityEngine.Random.state,Is.EqualTo(random),"Guide polling must not roll an offer.");
        foreach(var renderer in h.Guide.GetComponentsInChildren<Renderer>(true))Assert.That(renderer.enabled,Is.True);
    }
    [Test] public void SpendingThenRegainingAffordability_RearmsGuideWithoutAnotherDialogue()
    {
        var h=Create();Fund(h);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Not.Null);h.Inventory.TrySpend(new[]{new MaterialCost(h.Costs[0].Material,1)});Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);
        h.Inventory.Add(h.Costs[0].Material,1);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.SameAs(h.Station.transform));
    }
    [Test] public void GuideTimeout_DoesNotRestartEveryFrameWhileStillAffordable()
    {
        var h=Create();Fund(h);Call(h.Controller,"Update");Set(h.Controller,"_hideAtTime",0f);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);
    }
    [Test] public void StationInteraction_DismissesGuideWithoutImmediateRepeat()
    {
        var h=Create();Fund(h);Call(h.Controller,"Update");((Action)typeof(CraftingStation).GetField("OnInteracted",Flags).GetValue(h.Station))?.Invoke();Assert.That(h.Guide.Target,Is.Null);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);
    }
    [Test] public void ExitGuide_HasPriorityOverNewAffordabilityAndCraftingDialogue()
    {
        var h=Create();var door=Track(new GameObject("Exit"));door.SetActive(false);var exit=door.AddComponent<ExitDoor>();Set(h.Controller,"_exitDoor",exit);Call(h.Controller,"HandleAllKeysCollected");Assert.That(h.Guide.Target,Is.SameAs(exit.transform));
        Fund(h);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.SameAs(exit.transform));var entry=Track(ScriptableObject.CreateInstance<DialogueEntry>());Set(entry,"_showsCraftingGuide",true);Call(h.Controller,"HandleDialogueStarted",entry);Assert.That(h.Guide.Target,Is.SameAs(exit.transform));
        Set(h.Controller,"_hideAtTime",0f);Call(h.Controller,"Update");Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.SameAs(h.Station.transform));
    }
    [TestCase(false)] [TestCase(true)] public void PausedAffordability_DoesNotConsumeHint(bool uiPause)
    {
        var h=Create();Fund(h);if(uiPause)GameplayPause.Push();else Time.timeScale=0;Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);if(uiPause)GameplayPause.Pop();else Time.timeScale=1;Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.SameAs(h.Station.transform));
    }
    [Test] public void NoDialogue_PreservesTwentySecondFallbackAndDoesNotConsumeEarlyAffordability()
    {
        var h=Create();Set(h.Controller,"_dialogueDriven",false);Fund(h);typeof(RunSessionStats).GetField("_runStartTime",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Time.time-19f);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);
        typeof(RunSessionStats).GetField("_runStartTime",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Time.time-21f);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.SameAs(h.Station.transform));
    }
    [Test] public void StockWithoutAnEligibleAction_DoesNotShowGuide()
    {
        var h=Create(10);var list=Get<List<IWeaponBehaviour>>(h.Manager,"_equipped");list.Add(new Probe(new WeaponInstance{Data=list[0].Runtime.Data,Level=10}));list.Add(new Probe(new WeaponInstance{Data=list[0].Runtime.Data,Level=10}));foreach(MaterialType type in Enum.GetValues(typeof(MaterialType)))h.Inventory.Add(type,1000);
        Assert.That(h.Service.HasAnyAvailableCraftingAction(),Is.False);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.Null);
    }
    [Test] public void LateCraftingService_IsResolvedBeforeHintIsConsumed()
    {
        var h=Create();Fund(h);Set(h.Controller,"_crafting",null);h.Player.SetActive(true);Call(h.Controller,"Update");Assert.That(h.Guide.Target,Is.SameAs(h.Station.transform));
    }
    [Test] public void ActualGameplayScene_ResolvesAuthoredArrowStationPlayerAndDialogueWithAffordableUpgrade()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity",OpenSceneMode.Additive);
        try{
            GuideArrowController controller=null;WeaponCraftingService service=null;
            foreach(var root in scene.GetRootGameObjects()){if(controller==null)controller=root.GetComponentInChildren<GuideArrowController>(true);if(service==null)service=root.GetComponentInChildren<WeaponCraftingService>(true);}
            Assert.That(controller,Is.Not.Null);Assert.That(service,Is.Not.Null);Call(service,"Awake");var manager=service.GetComponent<WeaponManager>();var data=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");Get<List<IWeaponBehaviour>>(manager,"_equipped").Add(new Probe(new WeaponInstance{Data=data,Level=1}));var inventory=service.GetComponent<MaterialInventory>();foreach(var cost in service.GetUpgradeCost(data,WeaponUpgradePath.None,2))inventory.Add(cost.Material,cost.Amount);
            Call(controller,"Awake");var guide=Get<GuideArrow>(controller,"_guideArrow");Call(guide,"Awake");Assert.That(Get<bool>(controller,"_dialogueDriven"),Is.True);Call(controller,"Update");Assert.That(guide.Target,Is.SameAs(Get<CraftingStation>(controller,"_craftingStation").transform));foreach(var renderer in guide.GetComponentsInChildren<Renderer>(true))Assert.That(renderer.enabled,Is.True);
        }finally{EditorSceneManager.CloseScene(scene,true);}
    }
    private sealed class Harness{public GameObject Player;public MaterialInventory Inventory;public WeaponManager Manager;public WeaponCraftingService Service;public GuideArrow Guide;public GuideArrowController Controller;public CraftingStation Station;public IReadOnlyList<MaterialCost> Costs;}
    private sealed class Probe:IWeaponBehaviour{
        public WeaponInstance Runtime{get;}public Probe(WeaponInstance r){Runtime=r;}public void Setup(WeaponInstance r,Transform o,PlayerStats s,HeatManager h){}public void TickAutomatic(float d,Vector3 v){}public void TickManual(float d,Vector3 v,bool f){}public void UseActiveAbility(Vector3 v){}public bool CanCrit()=>false;
    }
}
