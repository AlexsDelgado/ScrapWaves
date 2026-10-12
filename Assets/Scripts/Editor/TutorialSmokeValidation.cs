using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in integration harness for an isolated project and save directory.</summary>
[InitializeOnLoad]
public static class TutorialSmokeValidation
{
    private const string Running="Tutorial168.Smoke";
    private static double _deadline;
    private static IEnumerator _run;
    private static string Output=>Path.GetFullPath("../TutorialQA");
    static TutorialSmokeValidation()
    {
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running,false))
            {
                if(UnityEngine.InputSystem.Mouse.current != null) UnityEngine.InputSystem.InputSystem.DisableDevice(UnityEngine.InputSystem.Mouse.current);
                _run=Walkthrough();_deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;
            }
        };
    }
    public static void Run()
    {
        string saves=Environment.GetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY");
        if(string.IsNullOrWhiteSpace(saves))throw new Exception("Smoke validation requires an isolated save directory.");
        Directory.CreateDirectory(saves);Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(saves,"scrapwaves_save.json"),"{\"Version\":4,\"FreshSaveTutorialEligible\":true}");
        File.WriteAllText(Path.Combine(Output,"walkthrough.txt"),"Tutorial integration validation\n");
        EditorSceneManager.OpenScene("Assets/Scenes/TitleScreen.unity");
        SessionState.SetBool(Running,true);EditorApplication.isPlaying=true;
    }
    public static void AuthorAndRun() { TutorialAuthoring.Build(); Run(); }
    private static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>_deadline)throw new Exception("Walkthrough timed out at "+TutorialDirector.Instance?.Step);
            Application.runInBackground=true;
            var entrance=Object.FindFirstObjectByType<EntranceCinematic>();
            if(entrance)typeof(EntranceCinematic).GetField("_focused",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(entrance,true);
            if(_run.MoveNext())return;
            SessionState.SetBool(Running,false);EditorApplication.update-=Tick;Note("PASS: complete walkthrough");EditorApplication.Exit(0);
        }
        catch(Exception e){SessionState.SetBool(Running,false);EditorApplication.update-=Tick;Note("FAIL: "+e);Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static IEnumerator Walkthrough()
    {
        var initialTitle=Object.FindFirstObjectByType<TitleScreenController>();
        while(!initialTitle.GetComponent<MainMenuPresentationController>().IsInteractive)yield return null;
        initialTitle.PlayButton.onClick.Invoke();
        while(SceneManager.GetActiveScene().name!="GameplayScene")yield return null;
        yield return null;
        // Allow the actual cinematic and run-start orchestrator to finish naturally.
        var director=Object.FindFirstObjectByType<TutorialDirector>();
        Check(director && director.HasReferences,"Authored gameplay references resolve");
        while(!Get<bool>(director,"_begun"))yield return null;
        var player=Object.FindFirstObjectByType<PlayerMovement>();
        var weapons=player.GetComponent<WeaponManager>();var crafting=player.GetComponent<WeaponCraftingService>();var xp=player.GetComponent<PlayerXP>();
        var definition=Get<TutorialDefinition>(director,"_definition");
        Check(TutorialDirector.IsActive && director.Step==TutorialStep.Welcome,"Fresh save enters welcome after cinematic");
        Check(weapons.GetEquippedWeapons().Count==1 && weapons.TryGetEquippedWeapon(definition.StartingWeapon,out var cannon),"Autocannon is the only starting weapon");
        Check(!TutorialDirector.AbilityAllowed,"Q remains locked in stage 1");
        xp.AddExperience(999);Check(xp.CurrentLevel==1 && xp.XpTowardsNext==0,"Natural XP is suppressed");
        Check(RunSessionStats.ElapsedSeconds==0,"Run clock is held at zero");
        Capture("01-welcome");for(int i=0;i<20;i++)yield return null;
        director.AdvancePage();Capture("02-movement");for(int i=0;i<20;i++)yield return null;
        Note($"Starting camera {Camera.main.transform.position} forward {Camera.main.transform.forward}, marker {Get<Transform>(director,"_markerCenter").position}, player {player.transform.position}");
        Check(Vector3.Dot(Camera.main.transform.forward,Get<Transform>(director,"_markerCenter").position-Camera.main.transform.position)>0,"Movement target is in front of the starting camera");
        Teleport(player,Get<Transform>(director,"_markerCenter").position+Vector3.up*1.7f);
        while(director.Step!=TutorialStep.FirstEnemy)yield return null;
        Check(!TutorialDirector.AbilityAllowed,"Q remains locked in stage 2");
        var first=Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None);
        Check(first.Length==1,"Exactly one lesson enemy is active");
        Check(first[0].GetComponentsInChildren<EnemyContactDamage>(true).Length==0,"Stationary charger has no contact damage");
        KillActive();Check(director.Step==TutorialStep.FirstPickup,"Death waits for material collection");
        Collect();Check(director.Step==TutorialStep.Upgrade,"Guaranteed pickup unlocks the upgrade instruction");
        Check(xp.CurrentLevel==1,"Early pickups grant no XP");
        var station=Get<CraftingStation>(director,"_station");station.OpenCrafting();yield return null;
        Capture("03-upgrade");for(int i=0;i<20;i++)yield return null;
        Check(!crafting.TryUpgradeWeapon(definition.StartingWeapon,3).Success,"Wrong crafting action is blocked");
        Check(crafting.TryUpgradeWeapon(definition.StartingWeapon,2).Success,"Live level-2 price is covered and real upgrade succeeds");
        while(director.Step!=TutorialStep.Heat)yield return null;
        Check(TutorialDirector.AbilityAllowed,"Q unlocks with stage 3");
        Check(Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None).Length==3,"Three basic enemy types are active");
        Capture("04-heat");for(int i=0;i<20;i++)yield return null;
        KillActive();Check(director.Step==TutorialStep.HordePickup,"Kills count before dismissing the heat explanation and without Q");
        Collect();Check(director.Step==TutorialStep.Tinker,"All three material drops unlock tinkering");
        station.OpenCrafting();yield return null;
        Check(crafting.TryBeginTinkering().Success,"Live slot-2 recipe is fully funded");
        var offer=crafting.GetTinkeringOffer().ToArray();Check(offer.Length==2,"Tinkering offers exactly two valid weapons");
        Check(crafting.TryTinkerWeapon(offer[0]).Success,"The selected offered weapon is really equipped");
        Check(!crafting.BuildUnequippedWeapons().Contains(offer[1]),"Rejected weapon is excluded from later offers");
        while(director.Step!=TutorialStep.AutomaticCombat)yield return null;
        var last=Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None);
        Check(last.Length==4 && last.All(x=>Mathf.Approximately(x.GetComponent<EnemyOutgoingDamageScale>().Multiplier,.5f)),"Four moving slimes have half outgoing damage");
        Capture("05-final-horde");for(int i=0;i<20;i++)yield return null;
        KillActive();Collect();
        Check(xp.CurrentLevel==2,"Exactly one scripted level is awarded");
        var choice=player.GetComponent<LevelUpChoiceUI>();while(!choice.IsVisible)yield return null;
        var view=Get<ChoiceMenuView>(choice,"_activeView");Check(view.Cards.Count(x=>x.gameObject.activeSelf)==3,"Level-up presents three passive choices");
        Capture("06-item-choice");for(int i=0;i<20;i++)yield return null;
        view.Cards[0].SelectButton.onClick.Invoke();while(director.Step!=TutorialStep.Escape)yield return null;
        director.AdvancePage();Check(director.Step==TutorialStep.Vent,"Item application unlocks the escape and vent guidance");
        Capture("07-geyser");for(int i=0;i<20;i++)yield return null;
        var vent=Get<MapLaunchPad>(director,"_vent");Teleport(player,vent.transform.position+Vector3.up*1.2f);
        // Exercise the launch pad's real collision route and completion callback.
        typeof(MapLaunchPad).GetMethod("TryLaunch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vent,new object[]{player.GetComponent<Collider>(),false});
        Check(!TutorialDirector.IsActive && player.IsLaunching,"Geyser launches the same player and ends the lesson");
        Check(HeatManager.GetInstance().CurrentHeat==0 && RunSessionStats.ElapsedSeconds<1,"Heat and normal run time restart at zero");
        Check(weapons.GetEquippedWeapons().Count==2 && xp.CurrentLevel==2,"Weapons and chosen level survive handoff");
        Check(!SaveManager.Instance.ShouldAutoPlayTutorial && SaveManager.Instance.MetaTutorialPending,"Completion and pending meta guidance are persisted at handoff");
        Check(!Get<GameObject>(director,"_world").activeSelf,"Tutorial table, marker, actors and pickups are cleaned up");
        Check(Object.FindFirstObjectByType<OrbitalSpawner>().enabled,"Normal spawning resumes");
        Note("Persistence snapshot: "+File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("SCRAPWAVES_TEST_SAVE_DIRECTORY"),"scrapwaves_save.json")));
        SceneManager.LoadScene("TitleScreen");yield return null;yield return null;
        var menu=Object.FindFirstObjectByType<TutorialMenuPresenter>();var objectives=Object.FindFirstObjectByType<ObjectivesMenuUI>(FindObjectsInactive.Include);
        Check(menu!=null && !objectives.IsVisible,"Returning to title does not force Objectives open");
        int objectsBefore=Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
        Capture("08-title");for(int i=0;i<30;i++)yield return null;
        var title=Object.FindFirstObjectByType<TitleScreenController>();title.ObjectivesButton.onClick.Invoke();
        while(!objectives.IsVisible)yield return null;for(int i=0;i<30;i++)yield return null;
        Check(objectives.ActiveTab==ObjectivesMenuTab.Upgrades,"Pending lesson routes a voluntary Objectives visit to Upgrades");
        Check(Get<GameObject>(menu,"_help").activeSelf,"Saved meta guidance opens");
        Check(Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objectsBefore,"Opening Objectives creates zero UI objects");
        Capture("09-meta-guidance");for(int i=0;i<30;i++)yield return null;
        menu.CloseHelp();Check(!SaveManager.Instance.MetaTutorialPending,"Acknowledgement clears the persistent pending flag");
        menu.ShowHelp();Check(Get<GameObject>(menu,"_help").activeSelf,"Info control can reopen guidance");
        menu.CloseHelp();objectives.ShowObjectivesTab();objectives.ShowUnlocksTab();objectives.ShowUpgradesTab();
        Check(Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objectsBefore,"Tab switches bind authored UI without hierarchy growth");
        SceneManager.LoadScene("GameplayScene");yield return null;yield return null;
        Check(!TutorialDirector.IsActive,"Completed saves enter a normal run without forcing the tutorial");
        TutorialDirector.RequestReplay();SceneManager.LoadScene("GameplayScene");yield return null;yield return null;
        Check(TutorialDirector.IsActive,"Explicit replay works after completion");
        typeof(RunEndScreenUI).GetMethod("Retry",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Object.FindFirstObjectByType<RunEndScreenUI>(),null);
        yield return null;yield return null;
        Check(TutorialDirector.IsActive,"Retrying an incomplete explicit replay restarts the lesson");
    }
    private static void Teleport(PlayerMovement p,Vector3 point){p.transform.position=point;var b=p.GetComponent<Rigidbody>();b.position=point;b.linearVelocity=Vector3.zero;Physics.SyncTransforms();}
    private static void KillActive(){foreach(var actor in Object.FindObjectsByType<TutorialEnemy>(FindObjectsSortMode.None))actor.GetComponent<EnemyHealth>().ApplyDamage(100000);}
    private static void Collect(){foreach(var drop in Object.FindObjectsByType<TutorialPickup>(FindObjectsSortMode.None))drop.Collect();}
    private static T Get<T>(Object target,string field)=>(T)target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Note("PASS: "+message);}
    private static void Note(string text){File.AppendAllText(Path.Combine(Output,"walkthrough.txt"),text+"\n");Debug.Log(text);}
    private static void Capture(string name)
    {
        // Explicit render targets work in hidden batch editors; ordinary ScreenCapture waits for a visible Game view.
        var camera=Camera.main;
        bool temporary=!camera;
        if(temporary){camera=new GameObject("QA render camera",typeof(Camera)).GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;}
        var target=new RenderTexture(1920,1080,24);
        var oldTarget=camera.targetTexture;float oldAspect=camera.aspect;
        var canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var oldCameras=canvases.Select(c=>c.worldCamera).ToArray();var oldPlanes=canvases.Select(c=>c.planeDistance).ToArray();
        var previous=RenderTexture.active;
        Texture2D pixels=null;
        try
        {
            camera.targetTexture=target;camera.aspect=1920f/1080;
            foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            Canvas.ForceUpdateCanvases();
            foreach(var text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))text.ForceMeshUpdate();
            camera.Render();RenderTexture.active=target;pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),pixels.EncodeToPNG());
        }
        finally
        {
            for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=RenderMode.ScreenSpaceOverlay;canvases[i].worldCamera=oldCameras[i];canvases[i].planeDistance=oldPlanes[i];}
            camera.targetTexture=oldTarget;camera.aspect=oldAspect;RenderTexture.active=previous;
            Object.DestroyImmediate(target);if(pixels)Object.DestroyImmediate(pixels);if(temporary)Object.DestroyImmediate(camera.gameObject);
        }
    }
}
