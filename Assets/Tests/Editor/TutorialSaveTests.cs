using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class TutorialSaveTests
{
    private GameObject _root;
    private SaveManager _save;
    private string _directory;
    private string _path;
    [SetUp] public void Setup()
    {
        _directory=Path.Combine(Path.GetTempPath(),"ScrapWavesTutorialTests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);_path=Path.Combine(_directory,"save.json");
        _root=new GameObject("Tutorial persistence test");_root.SetActive(false);_save=_root.AddComponent<SaveManager>();
        typeof(SaveManager).GetField("_path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(_save,_path);
    }
    [TearDown] public void Teardown(){Object.DestroyImmediate(_root);if(File.Exists(_path))File.Delete(_path);Directory.Delete(_directory);}
    private void Load()=>typeof(SaveManager).GetMethod("Load",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(_save,null);
    [Test] public void OldEmptyProfile_DoesNotAutoEnterTutorial()
    {File.WriteAllText(_path,"{\"Version\":3,\"Scrap\":0}");Load();Assert.That(_save.ShouldAutoPlayTutorial,Is.False);Assert.That(_save.MetaTutorialPending,Is.False);}
    [Test] public void OldProgress_MigratesWithoutLossOrForcedTutorial()
    {File.WriteAllText(_path,"{\"Version\":3,\"Scrap\":137,\"HighestPlayerLevel\":12}");Load();Assert.That(_save.Scrap,Is.EqualTo(137));Assert.That(_save.ShouldAutoPlayTutorial,Is.False);}
    [Test] public void FreshIncompleteLesson_RemainsEligibleAfterReload()
    {Load();Assert.That(_save.ShouldAutoPlayTutorial,Is.True);_save.SaveNow();Load();Assert.That(_save.ShouldAutoPlayTutorial,Is.True);}
    [Test] public void HandoffAndMetaAcknowledgement_PersistSeparately()
    {
        Load();_save.CompleteGameplayTutorial();Load();Assert.That(_save.ShouldAutoPlayTutorial,Is.False);Assert.That(_save.MetaTutorialPending,Is.True);
        _save.CompleteMetaTutorial();Load();Assert.That(_save.MetaTutorialPending,Is.False);
        _save.CompleteGameplayTutorial();Load();Assert.That(_save.MetaTutorialPending,Is.False,"Replaying must not re-arm already acknowledged meta guidance.");
    }
}
