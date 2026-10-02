using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ManualWeaponTransitionTests
{
    private readonly List<Object> _cleanup = new();
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    [TearDown] public void Cleanup() { Time.timeScale = 1f; GameplayPause.Reset(); foreach(Object o in _cleanup) if(o != null) Object.DestroyImmediate(o); _cleanup.Clear(); }
    private T Track<T>(T value) where T:Object { _cleanup.Add(value); return value; }
    private static void Set(object value,string name,object data)=>value.GetType().GetField(name,Flags).SetValue(value,data);
    private static T Get<T>(object value,string name)=>(T)value.GetType().GetField(name,Flags).GetValue(value);
    private static void Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
    private WeaponManager Create(int count)
    {
        var owner=Track(new GameObject("Transition test"));owner.SetActive(false);
        var stats=owner.AddComponent<PlayerStats>();Set(stats,"_statDefinitions",Get<List<StatDefinition>>(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab").GetComponent<PlayerStats>(),"_statDefinitions"));Call(stats,"Awake");
        var manager=owner.AddComponent<WeaponManager>();Set(manager,"_stats",stats);
        var equipped=Get<List<IWeaponBehaviour>>(manager,"_equipped");
        for(int i=0;i<count;i++){
            var data=Track(ScriptableObject.CreateInstance<WeaponData>());data.BaseManualAmmo=10;data.ActiveAbilityAmmoCost=1;data.WeaponId="transition"+i;data.WeaponType=i==0?WeaponType.Mortar:WeaponType.Flamethrower;
            equipped.Add(new Probe(new WeaponInstance{Data=data,CurrentAmmo=0,State=i==0?WeaponState.Manual:WeaponState.Automatic}));
        }
        return manager;
    }
    [TestCase(1,6f)] [TestCase(2,3f)] [TestCase(3,3f)]
    public void Depletion_BlocksManualAndQUntilExactConfiguredDuration(int count,float seconds)
    {
        var manager=Create(count);var equipped=manager.GetEquippedWeapons();
        Call(manager,"UpdateManualWeapon",0f,Vector3.forward);
        Assert.That(manager.IsManualCycleInProgress,Is.True);Assert.That(manager.GetManualCooldownRemaining(),Is.EqualTo(seconds));
        Assert.That(manager.GetManualCooldownNormalized(),Is.Zero);
        Assert.That(equipped[0].Runtime.State,Is.EqualTo(WeaponState.Cooldown));
        Call(manager,"UpdateAutomaticWeapons",0.5f,Vector3.forward);
        Assert.That(((Probe)equipped[0]).AutomaticTicks,Is.Zero,"Outgoing weapon must not auto-fire during reload/switch.");
        for(int i=1;i<count;i++)Assert.That(((Probe)equipped[i]).AutomaticTicks,Is.EqualTo(1),"Existing automatic weapons keep firing.");
        int ticks=((Probe)equipped[0]).ManualTicks;
        Call(manager,"UpdateManualWeapon",0.5f,Vector3.forward);
        Assert.That(((Probe)equipped[0]).ManualTicks,Is.EqualTo(ticks));Assert.That(manager.CanUseAbility(),Is.False);
        Call(manager,"UpdateManualCycle",seconds/2);Assert.That(manager.GetManualCooldownNormalized(),Is.EqualTo(0.5f).Within(0.0001f));
        Call(manager,"UpdateManualCycle",seconds/2-0.01f);Assert.That(manager.IsManualCycleInProgress,Is.True);
        Call(manager,"UpdateAutomaticWeapons",0.01f,Vector3.forward);Assert.That(((Probe)equipped[0]).AutomaticTicks,Is.Zero);
        Call(manager,"UpdateManualCycle",0.011f);Assert.That(manager.IsManualCycleInProgress,Is.False);
        var ready=manager.GetCurrentManualWeapon();Assert.That(ready,Is.SameAs(equipped[count==1?0:1].Runtime));
        Assert.That(ready.State,Is.EqualTo(WeaponState.Manual));Assert.That(ready.CurrentAmmo,Is.EqualTo(10));Assert.That(manager.CanUseAbility(),Is.True);
        Call(manager,"UpdateManualWeapon",0f,Vector3.forward);Assert.That(((Probe)equipped[count==1?0:1]).ManualTicks,Is.GreaterThan(0));
        Call(manager,"UpdateAutomaticWeapons",0f,Vector3.forward);
        Assert.That(((Probe)equipped[0]).AutomaticTicks,Is.EqualTo(count==1?0:1));
        if(count>1)Assert.That(equipped[0].Runtime.State,Is.EqualTo(WeaponState.Automatic));
    }
    [TestCase(false)] [TestCase(true)] public void Pause_FreezesTransition(bool uiLock)
    {
        var manager=Create(2);Call(manager,"UpdateManualWeapon",0f,Vector3.forward);
        if(uiLock)GameplayPause.Push();else Time.timeScale=0;
        Call(manager,"Update");Assert.That(manager.GetManualCooldownRemaining(),Is.EqualTo(3));Assert.That(manager.GetManualCooldownNormalized(),Is.Zero);
    }
    [Test] public void AddedWeapon_DoesNotChangeCapturedSingleWeaponWaitOrTarget()
    {
        var manager=Create(1);Call(manager,"UpdateManualWeapon",0f,Vector3.forward);var pending=manager.GetPendingManualWeapon();
        Get<List<IWeaponBehaviour>>(manager,"_equipped").Add(new Probe(new WeaponInstance{State=WeaponState.Automatic}));
        Call(manager,"UpdateManualCycle",3f);Assert.That(manager.GetManualCooldownNormalized(),Is.EqualTo(0.5f));Assert.That(manager.GetPendingManualWeapon(),Is.SameAs(pending));
        Call(manager,"UpdateManualCycle",3f);Assert.That(manager.GetCurrentManualWeapon(),Is.SameAs(pending));
    }
    [Test] public void FullHeal_CompletesPendingTransitionWithoutLeavingEveryWeaponAutomatic()
    {
        var manager=Create(2);Call(manager,"UpdateManualWeapon",0f,Vector3.forward);var pending=manager.GetPendingManualWeapon();
        manager.RefillManualAmmoAndResetActiveCooldown();Assert.That(manager.IsManualCycleInProgress,Is.False);Assert.That(manager.GetCurrentManualWeapon(),Is.SameAs(pending));Assert.That(pending.State,Is.EqualTo(WeaponState.Manual));
    }
    [Test] public void ClearLoadout_CancelsTransitionAndHeldAbility()
    {
        var manager=Create(2);var probe=(Probe)manager.GetEquippedWeapons()[0];Call(manager,"UpdateManualWeapon",0f,Vector3.forward);
        Assert.That(probe.Cancels,Is.GreaterThan(0));manager.ClearEquippedWeapons();Call(manager,"UpdateManualCycle",10f);
        Assert.That(manager.GetPendingManualWeapon(),Is.Null);Assert.That(manager.IsManualCycleInProgress,Is.False);Assert.That(manager.GetCurrentManualWeapon(),Is.Null);
    }
    [Test] public void AuthoredReticle_FollowsIncomingShapeTimerAndPauseWithoutCreatingRuntimeUi()
    {
        var manager=Create(2);var ui=Track(new GameObject("Authored UI",typeof(RectTransform)));var hud=manager.gameObject.AddComponent<ReticleHud>();hud.AuthorUi(ui.transform);Set(hud,"_weaponManager",manager);Call(hud,"Awake");
        int children=ui.GetComponentsInChildren<Transform>(true).Length;Call(hud,"LateUpdate");
        var mortar=Get<RectTransform>(hud,"_mortarVRoot");var brackets=Get<RectTransform>(hud,"_wideBracketRoot");var progress=Get<Image>(hud,"_manualCycleProgress");
        Assert.That(mortar.gameObject.activeSelf,Is.True);Assert.That(progress.gameObject.activeSelf,Is.False);
        Call(manager,"UpdateManualWeapon",0f,Vector3.forward);Call(manager,"UpdateManualCycle",1.5f);Call(hud,"LateUpdate");
        Assert.That(mortar.gameObject.activeSelf,Is.False);Assert.That(brackets.gameObject.activeSelf,Is.True);Assert.That(brackets.GetComponent<CanvasGroup>().alpha,Is.EqualTo(0.22f));Assert.That(progress.fillAmount,Is.EqualTo(0.5f));Assert.That(progress.rectTransform.parent.GetComponent<RectTransform>().anchoredPosition,Is.EqualTo(Vector2.zero));
        Time.timeScale=0;Call(manager,"Update");Call(hud,"LateUpdate");Assert.That(progress.fillAmount,Is.EqualTo(0.5f));Time.timeScale=1;
        Call(manager,"UpdateManualCycle",1.5f);Call(hud,"LateUpdate");Assert.That(progress.gameObject.activeSelf,Is.False);Assert.That(brackets.GetComponent<CanvasGroup>().alpha,Is.EqualTo(1));Assert.That(ui.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(children));
    }
    [Test] public void PlayerPrefab_AuthorsThreeAndSixSecondSettingsAndReadinessViews()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");var manager=prefab.GetComponent<WeaponManager>();Assert.That(Get<float>(manager,"_manualCycleCooldown"),Is.EqualTo(3));Assert.That(Get<float>(manager,"_singleWeaponCycleCooldown"),Is.EqualTo(6));
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity",OpenSceneMode.Additive);
        try{ReticleHud hud=null;foreach(var root in scene.GetRootGameObjects()){hud=root.GetComponentInChildren<ReticleHud>(true);if(hud!=null)break;}Assert.That(hud,Is.Not.Null);Assert.That(Get<Image>(hud,"_manualCycleProgress"),Is.Not.Null);Assert.That(Get<CanvasGroup[]>(hud,"_reticleGroups").Length,Is.EqualTo(4));}
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
    [Test] public void RepeatedDepletion_DoesNotRestartCountdown_AndNextTransitionBlocksNewOutgoing()
    {
        var manager=Create(2);Call(manager,"EndManualMode");Call(manager,"UpdateManualCycle",1f);
        var pending=manager.GetPendingManualWeapon();Call(manager,"EndManualMode");
        Assert.That(manager.GetManualCooldownRemaining(),Is.EqualTo(2f));Assert.That(manager.GetPendingManualWeapon(),Is.SameAs(pending));
        Call(manager,"UpdateManualCycle",2f);manager.GetCurrentManualWeapon().CurrentAmmo=0;
        Call(manager,"UpdateManualWeapon",0f,Vector3.forward);Call(manager,"UpdateAutomaticWeapons",0f,Vector3.forward);
        Assert.That(manager.GetCurrentManualWeapon().State,Is.EqualTo(WeaponState.Cooldown));
        Assert.That(((Probe)manager.GetEquippedWeapons()[1]).AutomaticTicks,Is.Zero);
        Assert.That(((Probe)manager.GetEquippedWeapons()[0]).AutomaticTicks,Is.EqualTo(1));
    }
    [TestCase("Assets/Scenes/GameplayScene.unity")]
    [TestCase("Assets/Scenes/SampleScene.unity")]
    [TestCase("Assets/Scenes/Testing/enemiesTesting.unity")]
    [TestCase("Assets/Scenes/Testing/test_balance.unity")]
    public void ActualScene_ReadinessBindingsRenderDuringTransitionAndHideOnCompletion(string path)
    {
        var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            ReticleHud hud=null;
            foreach(var root in scene.GetRootGameObjects()){hud=root.GetComponentInChildren<ReticleHud>(true);if(hud!=null)break;}
            Assert.That(hud,Is.Not.Null);Assert.That(hud.HasAuthoredUi,Is.True);
            var manager=hud.GetComponent<WeaponManager>();Assert.That(manager,Is.Not.Null);
            var equipped=Get<List<IWeaponBehaviour>>(manager,"_equipped");equipped.Clear();
            foreach(string weapon in new[]{"Mortar","Flamethrower"})
                equipped.Add(new Probe(new WeaponInstance{Data=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/"+weapon+".asset"),State=equipped.Count==0?WeaponState.Manual:WeaponState.Automatic}));
            Call(hud,"Awake");Call(manager,"EndManualMode");Call(manager,"UpdateManualCycle",1.5f);Call(hud,"LateUpdate");
            var progress=Get<Image>(hud,"_manualCycleProgress");var track=Get<Image>(hud,"_manualCycleTrack");
            Assert.That(progress,Is.Not.Null);Assert.That(track,Is.Not.Null);
            Assert.That(progress.gameObject.activeInHierarchy,Is.True);Assert.That(progress.enabled,Is.True);
            Assert.That(progress.sprite,Is.Not.Null);Assert.That(progress.color.a,Is.GreaterThan(0.8f));
            Assert.That(progress.type,Is.EqualTo(Image.Type.Filled));Assert.That(progress.fillAmount,Is.EqualTo(0.5f));
            Assert.That(track.gameObject.activeInHierarchy,Is.True);Assert.That(progress.rectTransform.rect.width,Is.GreaterThanOrEqualTo(64));
            Assert.That(progress.GetComponentInParent<Canvas>().enabled,Is.True);
            Assert.That(progress.GetComponentInParent<Canvas>().renderMode,Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(progress.GetComponentInParent<Canvas>().sortingOrder,Is.EqualTo(650));
            foreach(var group in progress.GetComponentsInParent<CanvasGroup>())Assert.That(group.alpha,Is.GreaterThan(0));
            Call(manager,"UpdateManualCycle",1.5f);Call(hud,"LateUpdate");Assert.That(progress.gameObject.activeSelf,Is.False);
        }
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
    private sealed class Probe:IWeaponBehaviour,IHoldActiveAbilityBehaviour
    {
        public WeaponInstance Runtime{get;} public int ManualTicks;public int AutomaticTicks;public int Cancels;public bool IsActiveAbilityCharging=>false;
        public Probe(WeaponInstance runtime){Runtime=runtime;}
        public void Setup(WeaponInstance i,Transform o,PlayerStats s,HeatManager h){} public void TickAutomatic(float d,Vector3 v){AutomaticTicks++;}public void TickManual(float d,Vector3 v,bool f){ManualTicks++;}public void UseActiveAbility(Vector3 v){}public bool CanCrit()=>false;
        public void BeginActiveAbility(Vector3 v){}public void TickActiveAbility(float d,Vector3 v){}public void ReleaseActiveAbility(Vector3 v){}public void CancelActiveAbility(){Cancels++;}
    }
}
