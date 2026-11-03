using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed class PlayerHealingTextTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject _player, _cameraObject, _runtime;
    private PlayerHealth _health;
    private Camera _camera;
    private CombatTextProfile _profile;
    private CombatTextDirector _director;
    private readonly List<int> _heals = new();

    [SetUp]
    public void SetUp()
    {
        _player = new GameObject("Healing text test player");
        _health = _player.AddComponent<PlayerHealth>();
        _health.FullHeal();
        _health.OnHealed += _heals.Add;
        _heals.Clear();
        _cameraObject = new GameObject("Healing text test camera", typeof(Camera));
        _camera = _cameraObject.GetComponent<Camera>();
        _camera.enabled = false;
        _camera.transform.position = new Vector3(0, 1.5f, -10);
        _runtime = new GameObject("Healing text test runtime");
        _profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatTextProfile>(
            "Assets/ScriptableObjects/GameFeel/CombatTextProfile.asset"));
        _director = new CombatTextDirector(_runtime.transform, _camera, _profile,
            new GameFeelRuntimeOptions(), PresentationAccessibilityState.Default);
    }

    [TearDown]
    public void TearDown()
    {
        _director.Dispose();
        Object.DestroyImmediate(_player);
        Object.DestroyImmediate(_runtime);
        Object.DestroyImmediate(_cameraObject);
        Object.DestroyImmediate(_profile);
    }

    [TestCase(65, 25, 90, 25)]
    [TestCase(90, 30, 100, 10)]
    [TestCase(90, int.MaxValue, 100, 10)]
    public void Heal_ReportsOnlyActualRestoredHealth(int before, int requested, int after, int shown)
    {
        SetHealth(before);
        _health.Heal(requested);
        Assert.That(_health.CurrentHealth, Is.EqualTo(after));
        Assert.That(_heals, Is.EqualTo(new[] { shown }));
    }

    [TestCase(100, 25, false)]
    [TestCase(70, 0, false)]
    [TestCase(70, -25, false)]
    [TestCase(0, 25, true)]
    public void NoRecovery_EmitsNothing(int before, int requested, bool dead)
    {
        SetHealth(before);
        typeof(PlayerHealth).GetField("_isDead", Hidden).SetValue(_health, dead);
        _health.Heal(requested);
        Assert.That(_heals, Is.Empty);
        Assert.That(_health.CurrentHealth, Is.EqualTo(before));
    }

    [Test]
    public void FullHealPickup_ReportsMissingHpOnce_AndStillRestoresShieldsAndClearsBurn()
    {
        SetHealth(65);
        _health.SetShieldConfig(3, 10);
        typeof(PlayerHealth).GetField("_shieldCharges", Hidden).SetValue(_health, 0);
        typeof(PlayerHealth).GetField("_burnRemaining", Hidden).SetValue(_health, 5f);
        _health.HealToFull();
        _health.HealToFull();
        Assert.That(_heals, Is.EqualTo(new[] { 35 }));
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Assert.That(_health.ShieldCharges, Is.EqualTo(3));
        Assert.That(_health.IsBurning, Is.False);
    }

    [Test]
    public void InitializationMaxHealthAndQaReset_DoNotPretendToBeHealingPickups()
    {
        SetHealth(65);
        _health.FullHeal(resetPhase: true);
        _health.ApplyMaxHealthIncrease(10);
        typeof(PlayerHealth).GetMethod("OnEnable", Hidden).Invoke(_health, null);
        Assert.That(_heals, Is.Empty);
    }

    [Test]
    public void ActualHealing_UsesSmallerGreenPlus_DriftsSidewaysFadesAndReturnsToPool()
    {
        _health.OnHealed += amount => _director.TryEmitHealing(amount, _player.transform, 0);
        SetHealth(75);
        _health.Heal(50);
        var view = ActiveView();
        var text = view.GetComponentInChildren<TextMeshPro>();
        Assert.That(text.text, Is.EqualTo("+25"));
        Assert.That(text.color.g, Is.GreaterThan(text.color.r));
        Assert.That(text.color.g, Is.GreaterThan(text.color.b));
        Assert.That(text.font, Is.EqualTo(_profile.FontAsset));
        Vector3 initialPosition = view.transform.position;
        Assert.That((float)typeof(CombatTextView).GetField("_resolvedScale", Hidden).GetValue(view),
            Is.EqualTo(_profile.Normal.BaseScale * .8f).Within(.001f));
        _director.Tick(.2f, .2f);
        Assert.That(view.transform.position.y, Is.GreaterThan(initialPosition.y));
        Assert.That(Vector3.Dot(view.transform.position - initialPosition, _camera.transform.right), Is.GreaterThan(.1f));
        _director.Tick(.65f, .45f);
        Assert.That(text.alpha, Is.LessThan(1));
        _director.Tick(1, .35f);
        Assert.That(_director.ActiveHealingTextCount, Is.Zero);
        Assert.That(_director.Metrics.SumAppliedDamageReceived, Is.Zero);
    }

    [Test]
    public void RapidRecovery_MergesExactAmounts_AndKeepsPoolBounded()
    {
        for (int i = 0; i < 100; i++) _director.TryEmitHealing(1, _player.transform, i * .001f);
        Assert.That(_director.ActiveHealingTextCount, Is.EqualTo(1));
        Assert.That(ActiveView().GetComponentInChildren<TextMeshPro>().text, Is.EqualTo("+100"));
        for (int i = 1; i < 100; i++) _director.TryEmitHealing(1, _player.transform, i * .25f);
        Assert.That(_director.ActiveHealingTextCount, Is.LessThanOrEqualTo(4));
        int viewCount = _director.WorldRoot.GetComponentsInChildren<CombatTextView>(true).Length;
        _director.StopAll();
        _director.TryEmitHealing(10, _player.transform, 30);
        Assert.That(_director.WorldRoot.GetComponentsInChildren<CombatTextView>(true).Length, Is.EqualTo(viewCount));
        Assert.That(ActiveView().GetComponentInChildren<TextMeshPro>().text, Is.EqualTo("+10"));
    }

    [TestCase(CombatTextMode.Off, false)]
    [TestCase(CombatTextMode.ImportantOnly, true)]
    public void AccessibilityMode_IsRespected(CombatTextMode mode, bool visible)
    {
        _director.ApplyAccessibility(new PresentationAccessibilityState(false, false, false, mode, 1f));
        Assert.That(_director.TryEmitHealing(10, _player.transform, 0), Is.EqualTo(visible));
        _director.ApplyAccessibility(new PresentationAccessibilityState(false, false, false, CombatTextMode.Off, 1f));
        Assert.That(_director.ActiveHealingTextCount, Is.Zero);
    }

    [Test]
    public void ReducedMotionAndScale_UseTheSameAuthoredAccessibilitySettingsAsDamage()
    {
        _director.ApplyAccessibility(new PresentationAccessibilityState(true, true, true, CombatTextMode.Full, 1.25f));
        _director.TryEmitHealing(25, _player.transform, 0);
        var view = ActiveView();
        Assert.That(typeof(CombatTextView).GetField("_motion", Hidden).GetValue(view), Is.SameAs(_profile.ReducedMotion));
        Assert.That((float)typeof(CombatTextView).GetField("_resolvedScale", Hidden).GetValue(view),
            Is.EqualTo(_profile.Normal.BaseScale * .8f * 1.25f).Within(.001f));
        _director.TryEmitHealing(5, _player.transform, .1f);
        Assert.That((float)typeof(CombatTextView).GetField("_rePunchAmount", Hidden).GetValue(view), Is.Zero);
    }

    [Test]
    public void InactivePlayerAndMissingCamera_CannotLeaveOrCreateOrphanNumbers()
    {
        _director.TryEmitHealing(10, _player.transform, 0);
        _player.SetActive(false);
        _director.Tick(.1f, .1f);
        Assert.That(_director.ActiveHealingTextCount, Is.Zero);
        _player.SetActive(true);
        _director.SetCamera(null);
        Assert.That(_director.TryEmitHealing(10, _player.transform, 1), Is.False);
    }

    [Test]
    public void ControllerSubscription_ReenableDoesNotDuplicate_AndResetClearsNumbers()
    {
        var controller = _player.AddComponent<WeaponPresentationController>();
        var type = typeof(WeaponPresentationController);
        type.GetMethod("OnDisable", Hidden).Invoke(controller, null);
        type.GetMethod("OnEnable", Hidden).Invoke(controller, null);
        type.GetField("_combatText", Hidden).SetValue(controller, _director);
        type.GetMethod("OnDisable", Hidden).Invoke(controller, null);
        type.GetMethod("OnEnable", Hidden).Invoke(controller, null);
        // The handler resolves the camera before emitting, just like gameplay.
        _camera.tag = "MainCamera";
        SetHealth(65);
        _health.Heal(25);
        Assert.That(ActiveView().GetComponentInChildren<TextMeshPro>().text, Is.EqualTo("+25"));
        _health.FullHeal(resetPhase: true);
        Assert.That(_director.ActiveHealingTextCount, Is.Zero);
        type.GetMethod("OnDisable", Hidden).Invoke(controller, null);
        SetHealth(65);
        _health.Heal(25);
        Assert.That(_director.ActiveHealingTextCount, Is.Zero);
    }

    [Test]
    public void DamageViewReusedAfterHealing_HasNoGreenTintOrPlusPrefix()
    {
        _director.TryEmitHealing(25, _player.transform, 0);
        var view = ActiveView();
        CombatTextPresentation damage = new(12, CombatTextStyleId.Normal,
            CombatTextPriority.AutomaticDirect, default, false, false, false, false,
            false, false, false, Vector3.up, 1, 1, _profile.NormalMotion);
        view.Play(in damage, _camera);
        var text = view.GetComponentInChildren<TextMeshPro>();
        Assert.That(text.text, Is.EqualTo("12"));
        Assert.That(text.color, Is.EqualTo(CombatTextStyleResolver.ResolveColor(CombatTextStyleId.Normal)));
    }

    [TestCase(.8f)]
    [TestCase(1.2f)]
    public void AuthoredPlayer_AnchorsAtTheTorsoRegardlessOfPickupRangeOrPhysicsRefresh(float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            foreach (var script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            actor.transform.position = Vector3.zero;
            actor.transform.localScale = Vector3.one * scale;
            var pickup = actor.AddComponent<SphereCollider>();
            pickup.isTrigger = true;
            pickup.radius = 20;
            Assert.That(_director.TryEmitHealing(25, actor.transform, 0), Is.True);
            Vector3 position = (Vector3)typeof(CombatTextView).GetField("_anchorPosition", Hidden).GetValue(ActiveView());
            Assert.That(position.y, Is.GreaterThan(scale * .2f));
            Assert.That(position.y, Is.LessThan(scale * .7f));
            Assert.That(Mathf.Abs(position.x), Is.LessThan(.1f));
        }
        finally { Object.DestroyImmediate(actor); }
    }

    private void SetHealth(int value) => typeof(PlayerHealth).GetField("_currentHealth", Hidden).SetValue(_health, value);
    private CombatTextView ActiveView() => _director.WorldRoot.GetComponentsInChildren<CombatTextView>()
        .First(value => value.IsActive);
}
