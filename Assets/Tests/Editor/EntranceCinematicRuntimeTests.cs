using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class EntranceCinematicRuntimeTests
{
    [UnityTest]
    public IEnumerator SkipAtAllBeatsPauseRetryAndNaturalEndReachOneWeaponChoice()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameplayScene.unity");
        new GameObject("Title settings service test fixture",typeof(UserSettingsService));
        yield return new EnterPlayMode();
        var keyboard=InputSystem.AddDevice<Keyboard>();
        var mouse=InputSystem.AddDevice<Mouse>();
        var originalSettings=InputSystem.settings;
        var testSettings=Object.Instantiate(originalSettings);
        testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings=testSettings;
        try
        {
        foreach(float beat in new[]{.2f,2.8f,7.4f,12f})
        {
            var intro=Object.FindFirstObjectByType<EntranceCinematic>();
            typeof(EntranceCinematic).GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(intro,new object[]{true});
            var player=Object.FindFirstObjectByType<PlayerMovement>();
            var health=player.GetComponent<PlayerHealth>();
            var choice=player.GetComponent<LevelUpChoiceUI>();
            yield return new WaitForSecondsRealtime(beat);
            Assert.True(EntranceCinematic.StartupHeld);Assert.AreEqual(0f,Time.timeScale);Assert.False(choice.IsVisible);
            int hp=health.CurrentHealth;health.TakeDamage(1000);health.ApplyBurn(10,1000);Assert.AreEqual(hp,health.CurrentHealth);
            Assert.Less(RunSessionStats.ElapsedSeconds,.02f);
            // Another pause owner freezes the cinematic and retains its own lock.
            var visual=player.transform.Find("PlaceholderPlayerVisual");var position=visual.localPosition;
            GameplayPause.Push();yield return new WaitForSecondsRealtime(.15f);Assert.AreEqual(position,visual.localPosition);GameplayPause.Pop();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));InputSystem.Update();
            yield return new WaitForSecondsRealtime(.1f);
            Assert.True(intro.IsFinished,$"Skip: held={EntranceCinematic.SkipHeld}, locks={GameplayPause.LockCount}");Assert.False(choice.IsVisible,"Held skip must not open or confirm selection.");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
            yield return new WaitForSecondsRealtime(.15f);
            Assert.True(choice.IsVisible);Assert.True(EntranceCinematic.StartupHeld);Assert.AreEqual(0f,Time.timeScale);
            typeof(LevelUpChoiceUI).GetMethod("OnOptionClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(choice,new object[]{0});
            yield return null;yield return null;
            Assert.False(EntranceCinematic.StartupHeld);Assert.AreEqual(1f,Time.timeScale);
            Assert.True(player.enabled);Assert.True(Camera.main.GetComponent<ThirdPersonCamera>().enabled);
            Assert.AreEqual(1,player.GetComponent<WeaponManager>().GetEquippedWeapons().Count);
            SceneManager.LoadScene("GameplayScene");yield return null;yield return null;
        }
        // Natural completion runs the same restoration and selection path.
        var naturalIntro=Object.FindFirstObjectByType<EntranceCinematic>();
        typeof(EntranceCinematic).GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(naturalIntro,new object[]{true});
        var naturalPlayer=Object.FindFirstObjectByType<PlayerMovement>();
        var naturalVisual=naturalPlayer.transform.Find("PlaceholderPlayerVisual");
        var naturalHead=System.Array.Find(naturalVisual.GetComponentsInChildren<Transform>(true),t=>t.name=="spine.006");
        var naturalCamera=Camera.main;
        Vector3 previousVisual=naturalVisual.position,previousHead=naturalHead.position,previousCamera=naturalCamera.transform.position;
        float deadline=Time.realtimeSinceStartup+16f;
        while(!naturalIntro.IsFinished && Time.realtimeSinceStartup<deadline)
        {
            previousVisual=naturalVisual.position;previousHead=naturalHead.position;previousCamera=naturalCamera.transform.position;
            yield return null;
        }
        Assert.True(naturalIntro.IsFinished,"Natural completion must reach the selector.");
        Assert.Less(Vector3.Distance(previousVisual,naturalVisual.position),.03f,"Visual must remain continuous at natural handoff.");
        Assert.Less(Vector3.Distance(previousHead,naturalHead.position),.03f,"Head must remain continuous at natural handoff.");
        Assert.Less(Vector3.Distance(previousCamera,naturalCamera.transform.position),.03f,"Camera must remain continuous at natural handoff.");
        yield return null;yield return null;
        var naturalChoice=naturalPlayer.GetComponent<LevelUpChoiceUI>();
        Assert.True(naturalChoice.IsVisible);
        Assert.Greater(Vector3.Dot(naturalCamera.transform.forward,naturalPlayer.transform.forward),.85f);
        Assert.Less(Vector3.Dot(naturalCamera.transform.position-naturalPlayer.transform.position,naturalPlayer.transform.forward),-1f);
        foreach(var presenter in Object.FindObjectsByType<RearThreatPresenter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            Assert.True(presenter.enabled,"Threat presentation must return after the cinematic.");
        Vector3 selectorVisual=naturalVisual.position,selectorHead=naturalHead.position,selectorCamera=naturalCamera.transform.position;
        typeof(LevelUpChoiceUI).GetMethod("OnOptionClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(naturalChoice,new object[]{0});
        yield return null;yield return new WaitForSecondsRealtime(.1f);
        Assert.False(EntranceCinematic.StartupHeld);Assert.AreEqual(1f,Time.timeScale);
        Assert.True(naturalPlayer.IsGroundedOnSurface,"The newer capsule support probe must accept the cinematic placement.");
        Assert.True(naturalPlayer.GetComponent<PlayerAnimationDriver>().IsEntranceIdleHeld);
        Assert.AreEqual(1,naturalPlayer.GetComponent<WeaponManager>().GetEquippedWeapons().Count);
        Assert.Less(Vector3.Distance(selectorVisual,naturalVisual.position),.03f,"Resumed body must not snap from selector placement.");
        Assert.Less(Vector3.Distance(selectorHead,naturalHead.position),.03f,"Resumed head must not snap from selector placement.");
        Assert.Less(Vector3.Distance(selectorCamera,naturalCamera.transform.position),.03f,"Live camera must continue the cinematic endpoint.");
        EntranceCinematic.PrepareRetry();SceneManager.LoadScene("GameplayScene");yield return null;yield return null;yield return null;yield return null;
        typeof(EntranceCinematic).GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Object.FindFirstObjectByType<EntranceCinematic>(),new object[]{true});
        yield return null;yield return null;yield return null;
        Assert.True(Object.FindFirstObjectByType<EntranceCinematic>().IsFinished);
        Assert.True(Object.FindFirstObjectByType<LevelUpChoiceUI>().IsVisible);
        var readyPlayer=Object.FindFirstObjectByType<PlayerMovement>();
        var driver=readyPlayer.GetComponent<PlayerAnimationDriver>();
        Assert.True(driver.IsEntranceIdleHeld,"Retry must enter the same relaxed idle.");
        var readyChoice=readyPlayer.GetComponent<LevelUpChoiceUI>();
        InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});InputSystem.Update();
        typeof(LevelUpChoiceUI).GetMethod("OnOptionClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(readyChoice,new object[]{0});
        yield return null;yield return null; // Selection equips via the startup coroutine.
        var selectedWeapon=readyPlayer.GetComponent<WeaponManager>().GetCurrentManualWeapon();
        Assert.NotNull(selectedWeapon,"Startup must equip the selected manual weapon.");
        float selectedAmmo=selectedWeapon.CurrentAmmo;
        yield return null;yield return null;yield return null;
        Assert.True(driver.IsEntranceIdleHeld,"A held selector click must preserve the relaxed idle.");
        Assert.AreEqual(selectedAmmo,selectedWeapon.CurrentAmmo,"The selector click must not spend firing ammo.");
        InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();
        yield return null;yield return null;yield return null;
        Assert.True(driver.IsEntranceIdleHeld,"Releasing the selector click alone must preserve the idle.");
        InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});InputSystem.Update();
        yield return null;yield return new WaitForSecondsRealtime(.15f);
        Assert.False(driver.IsEntranceIdleHeld,"Fresh primary fire must release the entrance idle.");
        Assert.Greater((float)typeof(PlayerAnimationDriver).GetField("_layerWeight",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(driver),.5f,"Normal manual-weapon aim must work after the player acts.");
        InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();
        }
        finally {InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);InputSystem.settings=originalSettings;Object.Destroy(testSettings);}
        // The title service persists normally. During test shutdown, unsubscribe scene menus
        // before destroying that fixture, matching normal scene-before-bootstrap teardown.
        foreach(var menu in Object.FindObjectsByType<PauseMenuUI>(FindObjectsInactive.Include,FindObjectsSortMode.None))menu.enabled=false;
        yield return new ExitPlayMode();
    }
}
