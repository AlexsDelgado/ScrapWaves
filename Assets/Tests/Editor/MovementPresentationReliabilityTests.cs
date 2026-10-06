using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

public sealed class MovementPresentationReliabilityTests
{
    private GameObject _actor;
    private PlayerAnimationDriver _driver;
    private PlayerMovement _movement;
    private Rigidbody _body;

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"));
        foreach (MonoBehaviour script in _actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        _movement = _actor.GetComponent<PlayerMovement>();
        _body = _actor.GetComponent<Rigidbody>();
        Call(_movement, "Awake");
        Set(_movement, "_isGrounded", true);
        _driver = _actor.GetComponent<PlayerAnimationDriver>();
        Call(_driver, "ResolveReferences");
        Assert.That((bool)Call(_driver, "EnsureGraph"), Is.True);
    }

    [TearDown] public void Cleanup() { if (_actor != null) Object.DestroyImmediate(_actor); }

    [TestCase(1f, 1.36f)] [TestCase(1.2f, 1.133333f)]
    public void GaitRate_AccountsForAuthoredRootScale(float scale, float expectedRate)
    {
        _actor.transform.localScale = Vector3.one * scale;
        _body.linearVelocity = Vector3.forward * 6.8f;
        Call(_driver, "UpdateLocomotion", 1f);
        AnimatorControllerPlayable graph = Get<AnimatorControllerPlayable>(_driver, "_controller");
        Assert.That(graph.GetFloat(Animator.StringToHash("LocomotionRate")), Is.EqualTo(expectedRate).Within(.001f));
    }

    [Test]
    public void MovingLanding_CuesChestWithoutMovingFeetRootOrVelocity()
    {
        _body.linearVelocity = Vector3.forward * 6.8f;
        Set(_driver, "_baseState", Animator.StringToHash("Base Layer.Locomotion"));
        Set(_driver, "_landRemaining", .11f);
        Transform chest = Get<Transform>(_driver, "_chest");
        Transform[] feet = _driver.RigAnimator.GetComponentsInChildren<Transform>().Where(t => t.name == "foot.L" || t.name == "foot.R").ToArray();
        Assert.That(feet.Length, Is.EqualTo(2));
        Vector3[] positions = feet.Select(t => t.position).ToArray();
        Quaternion before = chest.rotation;
        Vector3 root = _actor.transform.position, velocity = _body.linearVelocity;
        Call(_driver, "ApplyMovingLandingFeedback");
        Assert.That(Quaternion.Angle(before, chest.rotation), Is.EqualTo(4f).Within(.01f));
        for (int i = 0; i < feet.Length; i++) Assert.That(Vector3.Distance(feet[i].position, positions[i]), Is.LessThan(.00001f));
        Assert.That(_actor.transform.position, Is.EqualTo(root));
        Assert.That(_body.linearVelocity, Is.EqualTo(velocity));
    }

    [Test]
    public void JumpPose_RemainsDuringAscentThenFallsOnDescent()
    {
        Set(_movement, "_isGrounded", false);
        Set(_driver, "_jumpState", Animator.StringToHash("Base Layer.Jump"));
        Set(_driver, "_jumpRemaining", 0f);
        _body.linearVelocity = Vector3.up * 6f;
        Call(_driver, "UpdateLocomotion", .02f);
        Assert.That(Get<int>(_driver, "_baseState"), Is.EqualTo(Animator.StringToHash("Base Layer.Jump")));
        _body.linearVelocity = Vector3.down;
        Call(_driver, "UpdateLocomotion", .02f);
        Assert.That(Get<int>(_driver, "_baseState"), Is.EqualTo(Animator.StringToHash("Base Layer.Fall")));
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
