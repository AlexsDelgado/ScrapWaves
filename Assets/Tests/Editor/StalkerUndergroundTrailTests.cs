using System.Linq;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class StalkerUndergroundTrailTests
{
    private GameObject _owner, _floor;
    private StalkerUndergroundTrail _trail;
    [SetUp]
    public void Setup()
    {
        if(Application.isPlaying)return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        CreateObjects();
    }
    private void CreateObjects()
    {
        _floor=GameObject.CreatePrimitive(PrimitiveType.Cube); _floor.name="Disposable mound floor";
        _floor.transform.position=Vector3.down*.5f;_floor.transform.localScale=new Vector3(400,1,400);
        _owner=new GameObject("Disposable underground mover"); _trail=_owner.AddComponent<StalkerUndergroundTrail>();
        var settings=new SerializedObject(_trail);
        settings.FindProperty("_moundPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(StalkerTrailAuthoring.Folder+"/GroundMound.prefab");
        settings.ApplyModifiedPropertiesWithoutUndo();Physics.SyncTransforms();
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if(Application.isPlaying)yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    }

    [Test]
    public void ActualDistanceSpacing_BoundedPool_AndLifecycleLeaveNoResidue()
    {
        _trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.ActiveCount,Is.EqualTo(1));
        _trail.Track(Vector3.zero,1,_floor.GetComponent<Collider>());
        _trail.Track(Vector3.right*2,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.EmittedCount,Is.EqualTo(1),"Stationary and sub-spacing samples must not emit.");
        _trail.Track(Vector3.right*7,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.EmittedCount,Is.EqualTo(3));
        Assert.That(_trail.LastEmissionPosition.x,Is.EqualTo(6).Within(.001));
        _trail.Tick(.12f);
        var root=GameObject.Find(_owner.name+" underground mound pool").transform;
        Assert.That(root.parent,Is.Null,"Boss burrow renderer hiding must not hide or move the trail.");
        Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
        Assert.That(root.GetComponentsInChildren<Rigidbody>(true),Is.Empty);
        var mound=root.Cast<Transform>().First(x=>x.gameObject.activeSelf);
        Vector3 full=mound.localScale;Assert.That(full.y,Is.GreaterThan(.4f));
        _trail.Tick(1.4f);Assert.That(mound.localScale,Is.EqualTo(full));
        _trail.Tick(.3f);Assert.That(mound.localScale.y,Is.InRange(.01f,full.y-.01f));
        _trail.Tick(.5f);Assert.That(_trail.ActiveCount,Is.Zero);
        _trail.Track(Vector3.right*190,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.ActiveCount,Is.LessThanOrEqualTo(32));
        Assert.That(root.childCount,Is.EqualTo(32));
        _trail.ResetTrail();Assert.That(_trail.ActiveCount,Is.Zero);
        Assert.That(root.Cast<Transform>().All(x=>!x.gameObject.activeSelf),Is.True);
        _trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.EmittedCount,Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator NativeDisableReuseAndDestroy_ClearOwnedPoolAndPreventDisabledEmission()
    {
        yield return new EnterPlayMode();
        CreateObjects();
        _trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        var pool=GameObject.Find(_owner.name+" underground mound pool");
        Assert.That(_trail.ActiveCount,Is.EqualTo(1));
        _trail.enabled=false;
        _trail.Track(Vector3.right*15,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.ActiveCount,Is.Zero,"A disabled component must not emit frozen mounds.");
        _trail.enabled=true;_trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        _owner.SetActive(false);Assert.That(_trail.ActiveCount,Is.Zero);
        _owner.SetActive(true);_trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.EmittedCount,Is.EqualTo(1));
        // Reproduce independent world-root destruction before owner disable during scene unload.
        Object.Destroy(pool);yield return null;yield return null;
        _owner.SetActive(false);_owner.SetActive(true);
        _trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        pool=GameObject.Find(_owner.name+" underground mound pool");
        Assert.That(_trail.ActiveCount,Is.EqualTo(1));
        Object.Destroy(_owner);yield return null;yield return null;
        Assert.That(pool==null,Is.True,"Owned world pool must be destroyed with its owner.");
        yield return new ExitPlayMode();
    }

    [Test]
    public void TelegraphBridge_FollowsProgressWithoutMovingBoss_AndSkipsMissingFloor()
    {
        Vector3 ownerPosition=_owner.transform.position;
        _trail.BeginBridge(Vector3.zero,Vector3.right*18,1,_floor.GetComponent<Collider>());
        _trail.AdvanceBridge(.5f);
        Assert.That(_trail.LastEmissionPosition.x,Is.EqualTo(9).Within(.001));
        Assert.That(_owner.transform.position,Is.EqualTo(ownerPosition));
        _trail.EndTravel(true);Assert.That(_trail.LastEmissionPosition.x,Is.EqualTo(18).Within(.001));
        _trail.ResetTrail();_floor.transform.localScale=new Vector3(8,1,8);Physics.SyncTransforms();
        _trail.BeginTrack(Vector3.zero,1,_floor.GetComponent<Collider>());
        _trail.Track(Vector3.right*30,1,_floor.GetComponent<Collider>());
        Assert.That(_trail.LastEmissionPosition.x,Is.LessThan(4),"No trail hovering over a ledge or tracing terrain far below.");
    }

    [Test]
    public void MoundsUseSurfaceNormal_AndRetainWorldScaleWithScaledBoss()
    {
        _floor.transform.rotation=Quaternion.Euler(0,0,15);_owner.transform.localScale=Vector3.one*4;Physics.SyncTransforms();
        var collider=_floor.GetComponent<Collider>();
        Assert.That(collider.Raycast(new Ray(Vector3.up*5,Vector3.down),out RaycastHit hit,10),Is.True);
        _trail.BeginTrack(hit.point,1,collider);_trail.Tick(.12f);
        var root=GameObject.Find(_owner.name+" underground mound pool").transform;
        var mound=root.Cast<Transform>().First(x=>x.gameObject.activeSelf);
        Assert.That(Vector3.Angle(mound.up,hit.normal),Is.LessThan(.05f));
        Assert.That(mound.lossyScale.x,Is.InRange(2.3f,3.3f));
        Assert.That(mound.lossyScale.y,Is.InRange(.5f,.7f));
    }
}
