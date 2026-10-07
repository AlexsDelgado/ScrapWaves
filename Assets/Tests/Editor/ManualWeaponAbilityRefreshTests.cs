using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ManualWeaponAbilityRefreshTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void Cleanup()
    {
        Time.timeScale = 1f;
        GameplayPause.Reset();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    [TestCase(WeaponType.RotatingBlade)]
    public void SingleWeaponReload_RefreshesQOnlyAfterSixSeconds(WeaponType type)
    {
        WeaponManager manager = CreateManager(type);
        WeaponInstance weapon = manager.GetCurrentManualWeapon();
        DepleteAndBeginCycle(manager, 12f);

        Assert.That(manager.GetPendingManualWeapon(), Is.SameAs(weapon));
        Assert.That(weapon.AbilityCooldownTimer, Is.EqualTo(12f));
        Assert.That(manager.CanUseAbility(), Is.False);
        Call(manager, "UpdateManualCycle", 5.99f);
        Assert.That(weapon.AbilityCooldownTimer, Is.EqualTo(12f));
        Assert.That(manager.CanUseAbility(), Is.False);

        Call(manager, "UpdateManualCycle", .02f);
        AssertReady(manager, weapon);
    }

    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    [TestCase(WeaponType.RotatingBlade)]
    public void Switch_RefreshesOnlyIncomingQAfterThreeSeconds(WeaponType incomingType)
    {
        WeaponManager manager = CreateManager(WeaponType.Flamethrower, incomingType, WeaponType.Mortar);
        WeaponInstance outgoing = manager.GetCurrentManualWeapon();
        WeaponInstance incoming = manager.GetEquippedWeapons()[1].Runtime;
        WeaponInstance third = manager.GetEquippedWeapons()[2].Runtime;
        incoming.AbilityCooldownTimer = 11f;
        third.AbilityCooldownTimer = 7f;
        DepleteAndBeginCycle(manager, 13f);

        Call(manager, "UpdateManualCycle", 2.99f);
        Assert.That(manager.GetCurrentManualWeapon(), Is.SameAs(outgoing));
        Assert.That(incoming.AbilityCooldownTimer, Is.EqualTo(11f));
        Assert.That(outgoing.AbilityCooldownTimer, Is.EqualTo(13f));
        Assert.That(manager.CanUseAbility(), Is.False);

        Call(manager, "UpdateManualCycle", .02f);
        AssertReady(manager, incoming);
        Assert.That(outgoing.State, Is.EqualTo(WeaponState.Automatic));
        Assert.That(outgoing.AbilityCooldownTimer, Is.EqualTo(13f));
        Assert.That(third.AbilityCooldownTimer, Is.EqualTo(7f));
    }

    [TestCase(1)]
    [TestCase(3)]
    public void ClearedLoadout_CancelsTransitionWithoutRefreshingAnyQ(int count)
    {
        WeaponManager manager = CreateManager(count);
        var weapons = new List<WeaponInstance>();
        foreach (IWeaponBehaviour behaviour in manager.GetEquippedWeapons())
        {
            behaviour.Runtime.AbilityCooldownTimer = 12f;
            weapons.Add(behaviour.Runtime);
        }
        DepleteAndBeginCycle(manager, 12f);
        Call(manager, "UpdateManualCycle", 1f);
        manager.ClearEquippedWeapons();
        Call(manager, "UpdateManualCycle", 100f);

        Assert.That(manager.IsManualCycleInProgress, Is.False);
        Assert.That(manager.GetPendingManualWeapon(), Is.Null);
        Assert.That(manager.GetCurrentManualWeapon(), Is.Null);
        Assert.That(manager.CanUseAbility(), Is.False);
        foreach (WeaponInstance weapon in weapons)
            Assert.That(weapon.AbilityCooldownTimer, Is.EqualTo(12f));

        Assert.That(manager.AddWeapon(CreateData(WeaponType.Flamethrower)), Is.True);
        Call(manager, "UpdateManualCycle", 100f);
        foreach (WeaponInstance weapon in weapons)
            Assert.That(weapon.AbilityCooldownTimer, Is.EqualTo(12f), "Cancelled weapons must never receive a delayed reset.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PausedTransition_DoesNotGrantEarlyQRefresh(bool uiPause)
    {
        WeaponManager manager = CreateManager(WeaponType.Flamethrower, WeaponType.RocketLauncher);
        WeaponInstance incoming = manager.GetEquippedWeapons()[1].Runtime;
        incoming.AbilityCooldownTimer = 10f;
        DepleteAndBeginCycle(manager, 12f);
        Call(manager, "UpdateManualCycle", 1f);
        if (uiPause) GameplayPause.Push();
        else Time.timeScale = 0f;

        Call(manager, "Update");
        Assert.That(manager.GetManualCooldownRemaining(), Is.EqualTo(2f));
        Assert.That(incoming.AbilityCooldownTimer, Is.EqualTo(10f));
        Assert.That(manager.CanUseAbility(), Is.False);

        GameplayPause.Reset();
        Time.timeScale = 1f;
        Call(manager, "UpdateManualCycle", 2f);
        AssertReady(manager, incoming);
    }

    [Test]
    public void AddingWeaponDuringReload_RefreshesCapturedTargetOnly()
    {
        WeaponManager manager = CreateManager(WeaponType.Flamethrower);
        WeaponInstance reloading = manager.GetCurrentManualWeapon();
        DepleteAndBeginCycle(manager, 12f);
        Call(manager, "UpdateManualCycle", 3f);
        manager.AddWeapon(CreateData(WeaponType.Mortar));
        WeaponInstance added = manager.GetEquippedWeapons()[1].Runtime;
        added.AbilityCooldownTimer = 9f;

        Assert.That(reloading.AbilityCooldownTimer, Is.EqualTo(12f));
        Call(manager, "UpdateManualCycle", 3f);
        AssertReady(manager, reloading);
        Assert.That(added.AbilityCooldownTimer, Is.EqualTo(9f));
    }

    [TestCase(1)]
    [TestCase(3)]
    public void RepeatedCycles_RefreshSelectedQOnceAtCompletion(int count)
    {
        WeaponManager manager = CreateManager(count);
        for (int cycle = 0; cycle < count * 3; cycle++)
        {
            foreach (IWeaponBehaviour behaviour in manager.GetEquippedWeapons())
                behaviour.Runtime.AbilityCooldownTimer = 10f + cycle;
            DepleteAndBeginCycle(manager, 10f + cycle);
            WeaponInstance pending = manager.GetPendingManualWeapon();
            float duration = manager.GetManualCycleCooldownDuration();
            Call(manager, "UpdateManualCycle", duration / 2f);
            Call(manager, "EndManualMode");
            Assert.That(manager.GetPendingManualWeapon(), Is.SameAs(pending));
            Assert.That(manager.GetManualCooldownRemaining(), Is.EqualTo(duration / 2f));
            Assert.That(pending.AbilityCooldownTimer, Is.EqualTo(10f + cycle));
            Call(manager, "UpdateManualCycle", duration / 2f);
            AssertReady(manager, pending);
            foreach (IWeaponBehaviour behaviour in manager.GetEquippedWeapons())
                if (behaviour.Runtime != pending)
                    Assert.That(behaviour.Runtime.AbilityCooldownTimer, Is.EqualTo(10f + cycle));

            pending.AbilityCooldownTimer = 4f;
            Call(manager, "UpdateManualCycle", 100f);
            Assert.That(pending.AbilityCooldownTimer, Is.EqualTo(4f), "Completion must not repeat once the transition has ended.");
        }
    }

    [TestCase(1)]
    [TestCase(3)]
    public void ZeroDurationCycle_RefreshesQWhenTransitionCompletesImmediately(int count)
    {
        WeaponManager manager = CreateManager(count);
        Set(manager, count == 1 ? "_singleWeaponCycleCooldown" : "_manualCycleCooldown", 0f);
        foreach (IWeaponBehaviour behaviour in manager.GetEquippedWeapons())
            behaviour.Runtime.AbilityCooldownTimer = 12f;
        WeaponInstance incoming = manager.GetEquippedWeapons()[count == 1 ? 0 : 1].Runtime;
        DepleteAndBeginCycle(manager, 12f);
        AssertReady(manager, incoming);
    }

    [TestCase(1)]
    [TestCase(3)]
    public void CooldownHud_ReflectsCompletionAndRetainsAmmoAndManualStateGates(int count)
    {
        WeaponManager manager = CreateManager(count);
        GameObject ui = Track(new GameObject("Q cooldown HUD test"));
        ui.SetActive(false);
        WeaponClusterHud hud = ui.AddComponent<WeaponClusterHud>();
        var fill = new GameObject("Q fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(ui.transform);
        var status = new GameObject("Q status", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        status.transform.SetParent(ui.transform);
        Set(hud, "_weaponManager", manager);
        Set(hud, "_abilityCooldownFill", fill);
        Set(hud, "_abilityStatusText", status);
        manager.GetCurrentManualWeapon().AbilityCooldownTimer = 12f;
        Call(hud, "RefreshWeaponPanel");
        Assert.That(fill.fillAmount, Is.LessThan(1f));
        Assert.That(status.text, Does.StartWith("Enfriando [Q]"));

        foreach (IWeaponBehaviour behaviour in manager.GetEquippedWeapons())
            behaviour.Runtime.AbilityCooldownTimer = 12f;
        DepleteAndBeginCycle(manager, 12f);
        Call(hud, "RefreshWeaponPanel");
        Assert.That(status.text, Is.Empty);
        Call(manager, "UpdateManualCycle", manager.GetManualCycleCooldownDuration());
        Call(hud, "RefreshWeaponPanel");
        Assert.That(fill.fillAmount, Is.EqualTo(1f));
        Assert.That(status.text, Is.EqualTo("Lista [Q]"));

        WeaponInstance ready = manager.GetCurrentManualWeapon();
        ready.CurrentAmmo = 0f;
        Call(hud, "RefreshWeaponPanel");
        Assert.That(fill.fillAmount, Is.EqualTo(1f));
        Assert.That(manager.CanUseAbility(), Is.False);
        Assert.That(status.text, Is.EqualTo("Sin munición [Q]"));
        ready.CurrentAmmo = 1f;
        Assert.That(manager.CanUseAbility(), Is.True, "Existing partial-ammo casting must still be allowed.");
        ready.State = WeaponState.Automatic;
        Assert.That(manager.CanUseAbility(), Is.False);
    }

    [Test]
    public void FlamethrowerQ_AfterReloadSpendsAmmoAndStartsOrdinaryCooldownAgain()
    {
        WeaponManager manager = CreateManager(WeaponType.Flamethrower);
        IWeaponBehaviour flame = manager.GetCurrentManualBehaviour();
        WeaponInstance runtime = flame.Runtime;
        flame.UseActiveAbility(Vector3.forward);
        Assert.That(runtime.AbilityCooldownTimer, Is.EqualTo(14f));
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(60f));
        DepleteAndBeginCycle(manager, runtime.AbilityCooldownTimer);
        Call(manager, "UpdateManualCycle", 6f);
        AssertReady(manager, runtime);

        flame.UseActiveAbility(Vector3.forward);
        Assert.That(runtime.CurrentAmmo, Is.EqualTo(60f));
        Assert.That(runtime.AbilityCooldownTimer, Is.EqualTo(14f));
        Assert.That(manager.CanUseAbility(), Is.False);
    }

    private WeaponManager CreateManager(int count)
    {
        WeaponType[] types = { WeaponType.Flamethrower, WeaponType.RocketLauncher, WeaponType.Mortar };
        var selected = new WeaponType[count];
        for (int i = 0; i < count; i++) selected[i] = types[i];
        return CreateManager(selected);
    }

    private WeaponManager CreateManager(params WeaponType[] types)
    {
        GameObject owner = Track(new GameObject("Q refresh test"));
        owner.SetActive(false);
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        Set(manager, "_stats", stats);
        foreach (WeaponType type in types)
            Assert.That(manager.AddWeapon(CreateData(type)), Is.True);
        return manager;
    }

    private WeaponData CreateData(WeaponType type)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponId = "q-refresh-" + type;
        data.WeaponType = type;
        data.BaseManualAmmo = 100f;
        data.ActiveAbilityAmmoCost = 40f;
        data.SkillCooldown = 14f;
        data.PresentationProfile = Track(ScriptableObject.CreateInstance<WeaponPresentationProfile>());
        return data;
    }

    private static void DepleteAndBeginCycle(WeaponManager manager, float cooldown)
    {
        WeaponInstance outgoing = manager.GetCurrentManualWeapon();
        outgoing.CurrentAmmo = 0f;
        outgoing.AbilityCooldownTimer = cooldown;
        Call(manager, "UpdateManualWeapon", 0f, Vector3.forward);
    }

    private static void AssertReady(WeaponManager manager, WeaponInstance weapon)
    {
        Assert.That(manager.GetCurrentManualWeapon(), Is.SameAs(weapon));
        Assert.That(manager.IsManualCycleInProgress, Is.False);
        Assert.That(weapon.State, Is.EqualTo(WeaponState.Manual));
        Assert.That(weapon.AbilityCooldownTimer, Is.Zero);
        Assert.That(weapon.CurrentAmmo, Is.EqualTo(100f));
        Assert.That(manager.GetAbilityCooldownNormalized(), Is.EqualTo(1f));
        Assert.That(manager.CanUseAbility(), Is.True);
    }

    private T Track<T>(T value) where T : Object { _cleanup.Add(value); return value; }
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
}
