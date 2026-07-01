# Manual Click Fire Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make manual weapons advance their fire cooldown while idle so a single click fires immediately whenever the cooldown is already clear.

**Architecture:** Keep the change local to manual fire cadence. Add a regression test around shared manual cooldown behavior first, then update the shared base manual fire path and each weapon-specific manual override that currently freezes `FireTimer` when the fire input is not held. Do not change automatic fire, ammo formulas, or ability behavior.

**Tech Stack:** Unity, C#, NUnit editor tests

---

### Task 1: Add a regression test for idle manual cooldown progression

**Files:**
- Create: `Assets/Tests/Editor/ManualWeaponFireCooldownTests.cs`
- Test: `Assets/Tests/Editor/ManualWeaponFireCooldownTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ManualWeaponFireCooldownTests
{
    [Test]
    public void TickManual_DecrementsFireTimer_WhenNotFiring()
    {
        var owner = new GameObject("Owner");
        var spawn = new GameObject("Spawn");
        spawn.transform.position = Vector3.zero;

        var weaponStats = owner.AddComponent<PlayerStats>();
        SetStatDefinitions(weaponStats, CreateAttackSpeedDefinitions());
        InvokePrivate(weaponStats, "Awake");

        WeaponData weaponData = ScriptableObject.CreateInstance<WeaponData>();
        weaponData.BaseAttackRate = 1f;
        weaponData.BaseManualAmmo = 10f;
        weaponData.BaseRange = 10f;

        WeaponInstance runtime = new()
        {
            Data = weaponData,
            State = WeaponState.Manual,
            CurrentAmmo = 10f
        };

        var weapon = new TestManualWeapon(spawn.transform);
        weapon.Setup(runtime, owner.transform, weaponStats, null);
        weapon.SetFireTimerForTest(0.5f);

        weapon.TickManual(0.2f, Vector3.forward, false);

        Assert.That(weapon.GetFireTimerForTest(), Is.EqualTo(0.3f).Within(0.0001f));

        Object.DestroyImmediate(weaponData);
        Object.DestroyImmediate(spawn);
        Object.DestroyImmediate(owner);
    }

    private static List<StatDefinition> CreateAttackSpeedDefinitions()
    {
        StatDefinition attackSpeed = ScriptableObject.CreateInstance<StatDefinition>();
        SetAutoProperty(attackSpeed, "<StatType>k__BackingField", StatType.AttackSpeedMultiplier);
        SetAutoProperty(attackSpeed, "<Category>k__BackingField", StatCategory.Offensive);
        SetAutoProperty(attackSpeed, "<BaseValue>k__BackingField", 1f);
        SetAutoProperty(attackSpeed, "<UpgradeableByLevel>k__BackingField", false);
        SetAutoProperty(attackSpeed, "<UpgradeableByItems>k__BackingField", false);
        SetAutoProperty(attackSpeed, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetAutoProperty(attackSpeed, "<IsPercentage>k__BackingField", false);
        SetAutoProperty(attackSpeed, "<IsInteger>k__BackingField", false);
        return new List<StatDefinition> { attackSpeed };
    }

    private static void SetStatDefinitions(PlayerStats stats, List<StatDefinition> definitions)
    {
        FieldInfo field = typeof(PlayerStats).GetField("_statDefinitions", BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(stats, definitions);
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(instance, null);
    }

    private static void SetAutoProperty(object instance, string backingFieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(instance, value);
    }

    private sealed class TestManualWeapon : BasicProjectileWeapon
    {
        public TestManualWeapon(Transform spawn)
            : base(null, null, spawn)
        {
        }

        public void SetFireTimerForTest(float value) => FireTimer = value;
        public float GetFireTimerForTest() => FireTimer;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: Unity editor test runner for `ManualWeaponFireCooldownTests.TickManual_DecrementsFireTimer_WhenNotFiring`
Expected: FAIL because `BasicProjectileWeapon.TickManual()` returns early on `!isFiring`, leaving `FireTimer` unchanged at `0.5`.

- [ ] **Step 3: Write minimal implementation**

Change the shared manual fire path in `Assets/Scripts/Weapon/Base/WeaponBehaviourBase.cs` so the cooldown timer always advances in manual state, then only gate the actual shot on `isFiring`.

```csharp
public virtual void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
{
    if (Runtime.State != WeaponState.Manual)
        return;

    FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
    if (!isFiring || FireTimer > 0f)
        return;

    FireTimer = GetFireInterval();
    if (!TrySpendManualAmmo(1f, requireFullAmount: false))
        return;

    FireAt(Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange, 1f, false);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: Unity editor test runner for `ManualWeaponFireCooldownTests.TickManual_DecrementsFireTimer_WhenNotFiring`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/Editor/ManualWeaponFireCooldownTests.cs Assets/Scripts/Weapon/Base/WeaponBehaviourBase.cs
git commit -m "test: lock manual fire cooldown behavior"
```

### Task 2: Apply the same cooldown behavior to weapon-specific manual overrides

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Types/MortarWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs`
- Review: `Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs`

- [ ] **Step 1: Update the cannon manual burst path**

Replace the current early return pattern with cooldown advancement first.

```csharp
public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
{
    if (Runtime.State != WeaponState.Manual)
        return;

    FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
    if (!isFiring || FireTimer > 0f)
        return;

    if (aimDirection.sqrMagnitude <= 0.0001f)
        return;

    AutomaticCannonTuning tuning = Runtime.Data.AutomaticCannon;
    int manualBurstCount = Mathf.Max(1, tuning.CannonManualBurstCount + GetContinuousFireBonus());
    int bulletsToFire = Mathf.Clamp(Mathf.CeilToInt(Runtime.CurrentAmmo), 1, manualBurstCount);
    if (!TrySpendManualAmmo(bulletsToFire, requireFullAmount: false))
        return;

    FireTimer = AutomaticCannonFireLogic.GetManualBurstInterval(
        tuning.CannonManualBurstsPerSecond,
        WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier),
        WeaponMath.GetAttackRateMultiplier(Runtime));
    FireLineBurst(
        aimDirection,
        bulletsToFire,
        1f,
        tuning.CannonManualLineSpacing,
        0f,
        tuning.CannonBurstProjectileScatterDegrees,
        false);
}
```

- [ ] **Step 2: Update the rocket launcher manual shot path**

```csharp
public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
{
    if (Runtime.State != WeaponState.Manual)
        return;

    FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
    if (!isFiring || FireTimer > 0f)
        return;

    if (aimDirection.sqrMagnitude <= 0.0001f)
        return;

    if (!TrySpendManualAmmo(1f, requireFullAmount: false))
        return;

    FireTimer = GetManualFireInterval();
    RocketLauncherTuning tuning = Runtime.Data.RocketLauncher;
    FireRocketAt(
        Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange,
        1f,
        GetPathAdjustedExplosionRadius(tuning.RocketManualExplosionRadius),
        GetPathAdjustedFalloff(tuning.RocketManualExplosionFalloff),
        tuning.RocketManualSpeedMultiplier);
}
```

- [ ] **Step 3: Update the mortar manual shot path**

```csharp
public override void TickManual(float deltaTime, Vector3 aimDirection, bool isFiring)
{
    if (Runtime.State != WeaponState.Manual)
        return;

    FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
    if (!isFiring || FireTimer > 0f)
        return;

    if (Spawn == null)
        return;

    if (aimDirection.sqrMagnitude <= 0.0001f)
        return;

    if (!TrySpendManualAmmo(1f, requireFullAmount: false))
        return;

    MortarTuning tuning = Runtime.Data.Mortar;
    FireTimer = GetManualFireInterval();
    Vector3 impact = Spawn.position + aimDirection.normalized * Runtime.Data.BaseRange;
    impact += RandomPlanarOffset(tuning.MortarManualAccuracyRadius);
    FireShell(
        Spawn.position,
        impact,
        1f,
        tuning.MortarManualExplosionRadius,
        tuning.MortarExplosionFalloff,
        GetManualTravelTime(tuning),
        tuning.MortarArcHeight,
        false);
}
```

- [ ] **Step 4: Keep the blade aligned with the same semantics**

`RotatingBladeWeapon` already decrements `FireTimer` while idle. Confirm it remains unchanged except for formatting if needed. No logic change unless the implementation diverges from:

```csharp
FireTimer = Mathf.Max(0f, FireTimer - deltaTime);
if (!isFiring || FireTimer > 0f)
    return;
```

- [ ] **Step 5: Explicitly do not change flamethrower hold behavior**

Document in code review notes and final summary: `FlamethrowerWeapon.TickManual()` is intentionally continuous-stream behavior and should still require holding fire. No production edit in this task unless a bug is discovered.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs Assets/Scripts/Weapon/Types/MortarWeapon.cs
git commit -m "fix: let manual fire cooldown progress between clicks"
```

### Task 3: Verify click-to-fire behavior and regression coverage

**Files:**
- Test: `Assets/Tests/Editor/ManualWeaponFireCooldownTests.cs`
- Verify: `Assets/Scripts/Weapon/Base/WeaponBehaviourBase.cs`
- Verify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Verify: `Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs`
- Verify: `Assets/Scripts/Weapon/Types/MortarWeapon.cs`
- Verify: `Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs`

- [ ] **Step 1: Add one more regression assertion for click-after-idle**

Extend the editor test to prove that after idle time clears the cooldown, a single click spends ammo and resets `FireTimer`.

```csharp
[Test]
public void TickManual_ClickAfterIdleCooldown_SpendsAmmoAndResetsTimer()
{
    var owner = new GameObject("Owner");
    var spawn = new GameObject("Spawn");
    spawn.transform.position = Vector3.zero;

    var weaponStats = owner.AddComponent<PlayerStats>();
    SetStatDefinitions(weaponStats, CreateAttackSpeedDefinitions());
    InvokePrivate(weaponStats, "Awake");

    WeaponData weaponData = ScriptableObject.CreateInstance<WeaponData>();
    weaponData.BaseAttackRate = 2f;
    weaponData.BaseManualAmmo = 10f;
    weaponData.BaseRange = 10f;

    WeaponInstance runtime = new()
    {
        Data = weaponData,
        State = WeaponState.Manual,
        CurrentAmmo = 5f
    };

    var weapon = new TestManualWeapon(spawn.transform);
    weapon.Setup(runtime, owner.transform, weaponStats, null);
    weapon.SetFireTimerForTest(0.1f);

    weapon.TickManual(0.2f, Vector3.forward, false);
    weapon.TickManual(0f, Vector3.forward, true);

    Assert.That(runtime.CurrentAmmo, Is.EqualTo(4f).Within(0.0001f));
    Assert.That(weapon.GetFireTimerForTest(), Is.EqualTo(0.5f).Within(0.0001f));

    Object.DestroyImmediate(weaponData);
    Object.DestroyImmediate(spawn);
    Object.DestroyImmediate(owner);
}
```

- [ ] **Step 2: Run the editor test file**

Run: Unity editor test runner for `ManualWeaponFireCooldownTests`
Expected: PASS for both manual cooldown tests

- [ ] **Step 3: Run the existing weapon editor tests**

Run: Unity editor test runner for:
- `AutomaticCannonFireLogicTests`
- `MortarTrajectoryTests`
- `MortarTerrainFilterTests`
- `ReticlePresentationLogicTests`

Expected: PASS, with no regressions from the manual fire cadence change

- [ ] **Step 4: Manual sandbox verification**

Run the weapon testing sandbox and verify:
- Cannon: one click fires a burst immediately when cooldown is already clear
- Rocket launcher: one click fires one rocket immediately when cooldown is clear
- Mortar: one click fires one shell immediately when cooldown is clear
- Flamethrower: still requires hold
- Rotating blade: repeated clicks work without needing to hold between cooldown windows

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/Editor/ManualWeaponFireCooldownTests.cs
git commit -m "test: cover click-to-fire manual cadence"
```
