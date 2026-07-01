# Sandbox Weapon Upgrades Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and verify sandbox-only upgraded weapon behavior for all five existing weapons without adding materials, inventory, crafting stations, save/load, or production level-up path selection.

**Architecture:** Keep `WeaponInstance.Level` and `WeaponInstance.SelectedPath` as the runtime source of truth. Add small reusable weapon effect helpers for status, damage amplification, radial damage, and repeated effects, then keep weapon-specific upgrade branches inside each existing weapon behavior class. The weapon testing sandbox remains the only player-facing integration point for this pass.

**Tech Stack:** Unity, C#, NUnit editor tests, existing weapon sandbox scene

---

### Task 1: Shared Level And Path Math Tests

**Files:**
- Create: `Assets/Tests/Editor/WeaponUpgradeMathTests.cs`
- Verify: `Assets/Scripts/Weapon/Managers/WeaponMath.cs`
- Verify: `Assets/Scripts/Weapon/Managers/WeaponDamageResolver.cs`

- [ ] **Step 1: Write failing editor tests for shared upgrade math**

Create `Assets/Tests/Editor/WeaponUpgradeMathTests.cs`:

```csharp
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class WeaponUpgradeMathTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _cleanup.Count; i++)
            Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [Test]
    public void GetLevelData_ReturnsExactConfiguredLevel()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 4, WeaponUpgradePath.None);

        WeaponLevelData levelData = WeaponMath.GetLevelData(weapon);

        Assert.That(levelData, Is.Not.Null);
        Assert.That(levelData.Level, Is.EqualTo(4));
        Assert.That(levelData.DamageMultiplier, Is.EqualTo(1.3f).Within(0.0001f));
    }

    [Test]
    public void GetPathData_IgnoresSelectedPathBelowLevelSix()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 5, WeaponUpgradePath.PathA);

        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);

        Assert.That(pathData, Is.Null);
    }

    [Test]
    public void GetPathData_ReturnsSelectedPathAtLevelSix()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);

        WeaponUpgradePathData pathData = WeaponMath.GetPathData(weapon);

        Assert.That(pathData, Is.Not.Null);
        Assert.That(pathData.PathName, Is.EqualTo("Path B"));
        Assert.That(pathData.DamageMultiplier, Is.EqualTo(1.4f).Within(0.0001f));
    }

    [Test]
    public void GetMaxManualAmmo_UsesLevelMultiplierAndPathOverride()
    {
        PlayerStats stats = CreateStats();
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathA);
        weapon.Data.PathA.ManualAmmoOverride = 400f;

        float ammo = WeaponMath.GetMaxManualAmmo(weapon, stats);

        Assert.That(ammo, Is.EqualTo(400f).Within(0.0001f));
    }

    [Test]
    public void GetAttackRateMultiplier_CombinesLevelAndPath()
    {
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);

        float multiplier = WeaponMath.GetAttackRateMultiplier(weapon);

        Assert.That(multiplier, Is.EqualTo(1.5f * 0.8f).Within(0.0001f));
    }

    [Test]
    public void CalculateDamage_AppliesLevelAndPathDamage()
    {
        PlayerStats stats = CreateStats();
        WeaponInstance weapon = CreateWeaponInstance(level: 6, WeaponUpgradePath.PathB);
        weapon.Data.BaseDamage = 10f;

        float damage = WeaponDamageResolver.CalculateDamage(stats, weapon, eliteOrBoss: false, canCrit: false);

        Assert.That(damage, Is.EqualTo(10f * 1.5f * 1.4f).Within(0.0001f));
    }

    private WeaponInstance CreateWeaponInstance(int level, WeaponUpgradePath path)
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(data);
        data.WeaponId = "TestWeapon";
        data.DisplayName = "Test Weapon";
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseManualAmmo = 100f;
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 4, DamageMultiplier = 1.3f, AttackRateMultiplier = 1.2f, ManualAmmoMultiplier = 1.1f },
            new() { Level = 6, DamageMultiplier = 1.5f, AttackRateMultiplier = 1.5f, ManualAmmoMultiplier = 1.2f }
        };
        data.PathA = new WeaponUpgradePathData
        {
            PathName = "Path A",
            DamageMultiplier = 1.2f,
            AttackRateMultiplier = 1.1f,
            ManualAmmoOverride = -1f
        };
        data.PathB = new WeaponUpgradePathData
        {
            PathName = "Path B",
            DamageMultiplier = 1.4f,
            AttackRateMultiplier = 0.8f,
            ManualAmmoOverride = -1f
        };

        return new WeaponInstance
        {
            Data = data,
            Level = level,
            SelectedPath = path,
            State = WeaponState.Manual
        };
    }

    private PlayerStats CreateStats()
    {
        GameObject owner = new("StatsOwner");
        _cleanup.Add(owner);
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        SetPrivateField(stats, "_statDefinitions", CreateStatDefinitions());
        InvokePrivate(stats, "Awake");
        return stats;
    }

    private List<StatDefinition> CreateStatDefinitions()
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.Knockback, 1f),
            CreateDefinition(StatType.ProjectileAreaSize, 1f)
        };
    }

    private StatDefinition CreateDefinition(StatType type, float baseValue)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        _cleanup.Add(definition);
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Offensive);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", false);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", false);
        SetPrivateField(definition, "<IsInteger>k__BackingField", false);
        return definition;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }
}
```

- [ ] **Step 2: Run tests and confirm baseline**

Run: Unity editor tests for `WeaponUpgradeMathTests`

Expected: PASS. If any test fails, inspect the shared math before changing behavior code. These tests document the existing contract.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/Editor/WeaponUpgradeMathTests.cs
git commit -m "test: cover weapon level and path math"
```

### Task 2: Shared Upgrade Effect Primitives

**Files:**
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponDamageApplier.cs`
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponDamageAmplifierStatus.cs`
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponMovementFreezeStatus.cs`
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponRadialDamage.cs`
- Create: `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`

- [ ] **Step 1: Write failing tests for damage amplification and radial damage**

Create `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class WeaponUpgradeEffectTests
{
    [Test]
    public void DamageAmplifierStatus_IncreasesDamageAppliedThroughWeaponDamageApplier()
    {
        GameObject target = new("Target");
        var damageable = target.AddComponent<TestDamageable>();
        target.AddComponent<WeaponDamageAmplifierStatus>().Refresh(1.5f, 3f);

        bool applied = WeaponDamageApplier.TryApplyDamage(damageable, 10);

        Assert.That(applied, Is.True);
        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void RadialDamage_DamagesEachDamageableOnlyOnce()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();

        int hits = WeaponRadialDamage.Apply(
            Vector3.zero,
            2f,
            12,
            falloff: 0f,
            knockback: 0f,
            maxTargets: 32);

        Assert.That(hits, Is.EqualTo(1));
        Assert.That(damageable.TotalDamage, Is.EqualTo(12));
        Object.DestroyImmediate(target);
    }

    private sealed class TestDamageable : MonoBehaviour, IDamageable
    {
        public int LastDamage { get; private set; }
        public int TotalDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            LastDamage = amount;
            TotalDamage += amount;
            return true;
        }
    }
}
```

- [ ] **Step 2: Run tests and verify compile failure**

Run: Unity editor tests for `WeaponUpgradeEffectTests`

Expected: FAIL to compile because the helper classes do not exist.

- [ ] **Step 3: Add damage applier**

Create `Assets/Scripts/Weapon/Projectiles/WeaponDamageApplier.cs`:

```csharp
using UnityEngine;

public static class WeaponDamageApplier
{
    public static bool TryApplyDamage(IDamageable damageable, int damage)
    {
        if (damageable == null || damage <= 0)
            return false;

        int modifiedDamage = WeaponDamageAmplifierStatus.ModifyDamage(damageable, damage);
        return damageable.ApplyDamage(Mathf.Max(1, modifiedDamage));
    }
}
```

- [ ] **Step 4: Add damage amplification status**

Create `Assets/Scripts/Weapon/Projectiles/WeaponDamageAmplifierStatus.cs`:

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponDamageAmplifierStatus : MonoBehaviour
{
    private float _multiplier = 1f;
    private float _remainingDuration;

    public float Multiplier => _remainingDuration > 0f ? Mathf.Max(1f, _multiplier) : 1f;

    public void Refresh(float multiplier, float duration)
    {
        _multiplier = Mathf.Max(_multiplier, multiplier);
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        TryApplyDummyStatus(duration);
    }

    public static void Apply(IDamageable damageable, float multiplier, float duration)
    {
        if (damageable is not Component component || duration <= 0f)
            return;

        WeaponDamageAmplifierStatus status = component.GetComponent<WeaponDamageAmplifierStatus>();
        if (status == null)
            status = component.gameObject.AddComponent<WeaponDamageAmplifierStatus>();

        status.Refresh(multiplier, duration);
    }

    public static int ModifyDamage(IDamageable damageable, int damage)
    {
        if (damageable is not Component component)
            return damage;

        WeaponDamageAmplifierStatus status = component.GetComponent<WeaponDamageAmplifierStatus>();
        if (status == null)
            return damage;

        return Mathf.Max(1, Mathf.RoundToInt(damage * status.Multiplier));
    }

    private void Update()
    {
        if (_remainingDuration <= 0f)
        {
            Destroy(this);
            return;
        }

        _remainingDuration -= Time.deltaTime;
        if (_remainingDuration <= 0f)
            Destroy(this);
    }

    private void TryApplyDummyStatus(float duration)
    {
        WeaponDummyEnemy dummy = GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
            dummy.ApplyStatus("Vulnerable", duration);
    }
}
```

- [ ] **Step 5: Add freeze status**

Create `Assets/Scripts/Weapon/Projectiles/WeaponMovementFreezeStatus.cs`:

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponMovementFreezeStatus : MonoBehaviour
{
    private SimpleFollow _simpleFollow;
    private EnemyFollow _enemyFollow;
    private bool _simpleWasEnabled;
    private bool _enemyWasEnabled;
    private bool _hasCachedState;
    private float _remainingDuration;

    public void Refresh(float duration)
    {
        if (duration <= 0f)
            return;

        CacheState();
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        SetMovement(false);
        TryApplyDummyStatus(duration);
    }

    public static void Apply(Transform target, float duration)
    {
        if (target == null || duration <= 0f)
            return;

        Transform root = target.root != null ? target.root : target;
        WeaponMovementFreezeStatus status = root.GetComponent<WeaponMovementFreezeStatus>();
        if (status == null)
            status = root.gameObject.AddComponent<WeaponMovementFreezeStatus>();

        status.Refresh(duration);
    }

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        if (_remainingDuration > 0f)
            return;

        SetMovement(true);
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (_hasCachedState)
            SetMovement(true);
    }

    private void CacheState()
    {
        if (_hasCachedState)
            return;

        _simpleFollow = GetComponent<SimpleFollow>();
        _enemyFollow = GetComponent<EnemyFollow>();
        _simpleWasEnabled = _simpleFollow != null && _simpleFollow.enabled;
        _enemyWasEnabled = _enemyFollow != null && _enemyFollow.enabled;
        _hasCachedState = true;
    }

    private void SetMovement(bool enabled)
    {
        if (_simpleFollow != null)
            _simpleFollow.enabled = enabled && _simpleWasEnabled;
        if (_enemyFollow != null)
            _enemyFollow.enabled = enabled && _enemyWasEnabled;
    }

    private void TryApplyDummyStatus(float duration)
    {
        WeaponDummyEnemy dummy = GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
            dummy.ApplyStatus("Freeze", duration);
    }
}
```

- [ ] **Step 6: Add radial damage helper**

Create `Assets/Scripts/Weapon/Projectiles/WeaponRadialDamage.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class WeaponRadialDamage
{
    private static readonly List<IDamageable> Damaged = new(128);

    public static int Apply(Vector3 center, float radius, int damage, float falloff, float knockback, int maxTargets = 128)
    {
        if (radius <= 0f || damage <= 0)
            return 0;

        ExplosionRadiusVfx.Spawn(center, radius);
        Damaged.Clear();
        Collider[] hits = Physics.OverlapSphere(center, radius);
        int applied = 0;

        for (int i = 0; i < hits.Length && applied < maxTargets; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null || Damaged.Contains(damageable))
                continue;

            Damaged.Add(damageable);
            float distance = Vector3.Distance(center, hits[i].transform.position);
            float t = Mathf.Clamp01(distance / radius);
            float falloffScale = Mathf.Lerp(1f, 1f - Mathf.Clamp01(falloff), t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * falloffScale));
            if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
            {
                EnemyKnockbackReceiver.TryApply(damageable, center, knockback * falloffScale);
                applied++;
            }
        }

        return applied;
    }
}
```

- [ ] **Step 7: Run tests**

Run: Unity editor tests for `WeaponUpgradeEffectTests`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Weapon/Projectiles/WeaponDamageApplier.cs Assets/Scripts/Weapon/Projectiles/WeaponDamageAmplifierStatus.cs Assets/Scripts/Weapon/Projectiles/WeaponMovementFreezeStatus.cs Assets/Scripts/Weapon/Projectiles/WeaponRadialDamage.cs Assets/Tests/Editor/WeaponUpgradeEffectTests.cs
git commit -m "feat: add shared weapon upgrade effects"
```

### Task 3: Route Weapon Damage Through Shared Applier

**Files:**
- Modify: `Assets/Scripts/Weapon/Projectiles/Projectile.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/FlamethrowerBurnStatus.cs`
- Modify: `Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs`

- [ ] **Step 1: Update direct projectile impact damage**

In `Assets/Scripts/Weapon/Projectiles/Projectile.cs`, replace:

```csharp
if (damageable.ApplyDamage(_damage))
    EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback);
```

with:

```csharp
if (WeaponDamageApplier.TryApplyDamage(damageable, _damage))
    EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback);
```

- [ ] **Step 2: Update projectile explosion damage**

In `Projectile.ApplyExplosionDamage`, replace:

```csharp
if (damageable.ApplyDamage(finalDamage))
    EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback * falloffScale);
```

with:

```csharp
if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
    EnemyKnockbackReceiver.TryApply(damageable, transform.position, _knockback * falloffScale);
```

- [ ] **Step 3: Update mortar explosion damage**

In `Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs`, replace:

```csharp
if (damageable.ApplyDamage(finalDamage))
    EnemyKnockbackReceiver.TryApply(damageable, explosionCenter, _knockback * falloffScale);
```

with:

```csharp
if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
    EnemyKnockbackReceiver.TryApply(damageable, explosionCenter, _knockback * falloffScale);
```

- [ ] **Step 4: Update burn status damage**

In `Assets/Scripts/Weapon/Projectiles/FlamethrowerBurnStatus.cs`, replace:

```csharp
_target.ApplyDamage(_damagePerTick);
```

with:

```csharp
WeaponDamageApplier.TryApplyDamage(_target, _damagePerTick);
```

- [ ] **Step 5: Update flamethrower direct damage**

In `FlamethrowerWeapon.ApplyDamageToTarget`, replace:

```csharp
if (damageable != null && damageable.ApplyDamage(damage))
    ApplyKnockback(damageable, impactOrigin, damage, knockbackScale);
```

with:

```csharp
if (damageable != null && WeaponDamageApplier.TryApplyDamage(damageable, damage))
    ApplyKnockback(damageable, impactOrigin, damage, knockbackScale);
```

- [ ] **Step 6: Update rotating blade damage**

In `RotatingBladeWeapon.ApplyBladeDamage`, replace:

```csharp
if (damageable.ApplyDamage(finalDamage))
    ApplyKnockback(damageable, impactOrigin, finalDamage, knockbackScale);
```

with:

```csharp
if (WeaponDamageApplier.TryApplyDamage(damageable, finalDamage))
    ApplyKnockback(damageable, impactOrigin, finalDamage, knockbackScale);
```

- [ ] **Step 7: Run regression tests**

Run Unity editor tests:

- `WeaponUpgradeEffectTests`
- `WeaponUpgradeMathTests`
- `ManualWeaponFireCooldownTests`
- `AutomaticCannonFireLogicTests`
- `MortarTrajectoryTests`
- `MortarTerrainFilterTests`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Weapon/Projectiles/Projectile.cs Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs Assets/Scripts/Weapon/Projectiles/FlamethrowerBurnStatus.cs Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs
git commit -m "refactor: route weapon damage through upgrade applier"
```

### Task 4: Sandbox Upgrade Data Verification

**Files:**
- Create: `Assets/Tests/Editor/SandboxWeaponUpgradeDataTests.cs`
- Verify: `Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset`
- Verify: `Assets/Scripts/Weapon/Testing/SO/Sandbox_RocketLauncher.asset`
- Verify: `Assets/Scripts/Weapon/Testing/SO/Sandbox_Flamethrower.asset`
- Verify: `Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset`
- Verify: `Assets/Scripts/Weapon/Testing/SO/Sandbox_RotatingBlade.asset`

- [ ] **Step 1: Add asset verification tests**

Create `Assets/Tests/Editor/SandboxWeaponUpgradeDataTests.cs`:

```csharp
using NUnit.Framework;
using UnityEditor;

public class SandboxWeaponUpgradeDataTests
{
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset", "Continuous Fire", "Head Hunter")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_RocketLauncher.asset", "Kinetic Explosion", "Fragmentation Cap")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Flamethrower.asset", "Jellified Fuel", "Liquid Nitrogen")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_Mortar.asset", "Grapeshot", "Multi-Charged Shells")]
    [TestCase("Assets/Scripts/Weapon/Testing/SO/Sandbox_RotatingBlade.asset", "Multi-Blade", "Atomic Sharpness")]
    public void SandboxWeapon_HasLevelAndPathData(string path, string expectedPathA, string expectedPathB)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);

        Assert.That(data, Is.Not.Null, path);
        Assert.That(data.LevelData, Has.Count.EqualTo(10));
        Assert.That(data.LevelData[0].Level, Is.EqualTo(1));
        Assert.That(data.LevelData[9].Level, Is.EqualTo(10));
        Assert.That(data.PathA, Is.Not.Null);
        Assert.That(data.PathB, Is.Not.Null);
        Assert.That(data.PathA.PathName, Is.EqualTo(expectedPathA));
        Assert.That(data.PathB.PathName, Is.EqualTo(expectedPathB));
        Assert.That(data.PathA.DamageMultiplier, Is.GreaterThan(1f));
        Assert.That(data.PathB.DamageMultiplier, Is.GreaterThan(1f));
    }
}
```

- [ ] **Step 2: Run tests**

Run: Unity editor tests for `SandboxWeaponUpgradeDataTests`

Expected: PASS. If they fail because assets are stale, run `Tools/ScrapWaves/Build Weapon Testing Sandbox` once in the editor, then rerun tests.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/Editor/SandboxWeaponUpgradeDataTests.cs
git commit -m "test: verify sandbox weapon upgrade data"
```

### Task 5: Automatic Cannon Upgrade Behavior

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Test: `Assets/Tests/Editor/WeaponUpgradeMathTests.cs`

- [ ] **Step 1: Add deterministic helper methods to cannon**

In `AutomaticCannonWeapon`, add helper methods near the existing path helpers:

```csharp
private bool IsContinuousFirePath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

private bool IsHeadHunterPath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

private float GetContinuousFireAttackSpeedMultiplier()
{
    if (!IsContinuousFirePath())
        return 1f;

    float heatPercent = Heat != null ? Heat.NormalizedHeat * 100f : 0f;
    float heatBonus = Mathf.Floor(heatPercent / 2f) * 0.01f;
    return 1.25f + heatBonus;
}

private float GetHeadHunterWeakPointScale()
{
    if (!IsHeadHunterPath())
        return 1f;

    float heat = Heat != null ? Heat.NormalizedHeat : 0f;
    int extraSteps = Mathf.FloorToInt(heat / 0.2f);
    return Mathf.Clamp(5f + extraSteps, 5f, 10f);
}
```

- [ ] **Step 2: Use continuous-fire attack speed in manual burst interval**

In `TickManual`, multiply the existing weapon rate by `GetContinuousFireAttackSpeedMultiplier()`:

```csharp
FireTimer = AutomaticCannonFireLogic.GetManualBurstInterval(
    tuning.CannonManualBurstsPerSecond,
    WeaponMath.GetStatScale(Stats, StatType.AttackSpeedMultiplier),
    WeaponMath.GetAttackRateMultiplier(Runtime) * GetContinuousFireAttackSpeedMultiplier());
```

- [ ] **Step 3: Give Continuous Fire the spec ammo through sandbox data**

In the sandbox asset setup in `WeaponTestingSandboxSceneBuilder.ConfigurePathData`, after generic path setup, branch for Automatic Cannon:

```csharp
if (data.WeaponType == WeaponType.AutomaticCannon)
{
    data.PathA.ManualAmmoOverride = 400f;
    data.PathB.ManualAmmoOverride = 40f;
}
```

- [ ] **Step 4: Extend Head Hunter scaling**

Replace `GetHeadHunterScale` with:

```csharp
private float GetHeadHunterScale(Transform target)
{
    if (!IsHeadHunterPath())
        return 1f;

    return WeaponEnemyClassifier.GetKind(target) switch
    {
        WeaponEnemyKind.Boss => 3f,
        WeaponEnemyKind.Elite => 2f,
        _ => 1.15f
    };
}
```

- [ ] **Step 5: Add a path-specific active ability branch**

At the start of `UseActiveAbility`, after validation and ammo spending, branch:

```csharp
if (IsHeadHunterPath())
{
    FirePiercingHeadHunterShot(aimDirection);
    CompleteActiveAbility();
    return;
}
```

Add:

```csharp
private void FirePiercingHeadHunterShot(Vector3 aimDirection)
{
    if (Spawn == null || aimDirection.sqrMagnitude <= 0.0001f)
        return;

    Vector3 origin = Spawn.position;
    Vector3 direction = aimDirection.normalized;
    int hitCount = EnemyRegistry.CollectClosestNearPolyline(
        new[] { origin, origin + direction * Runtime.Data.BaseRange },
        2,
        0.45f,
        128,
        new System.Collections.Generic.List<Transform>(),
        new System.Collections.Generic.List<Vector3>());

    // If allocation becomes visible in profiling, move these lists to fields.
    var targets = new System.Collections.Generic.List<Transform>(hitCount);
    var hitOrigins = new System.Collections.Generic.List<Vector3>(hitCount);
    hitCount = EnemyRegistry.CollectClosestNearPolyline(
        new[] { origin, origin + direction * Runtime.Data.BaseRange },
        2,
        0.45f,
        128,
        targets,
        hitOrigins);

    for (int i = 0; i < hitCount; i++)
    {
        IDamageable damageable = targets[i].GetComponentInParent<IDamageable>();
        if (damageable == null)
            continue;

        bool eliteOrBoss = WeaponEnemyClassifier.CountsAsEliteOrBoss(targets[i]);
        float damage = WeaponDamageResolver.CalculateDamage(Stats, Runtime, eliteOrBoss, CanCrit(), GetCritMultiplierOverride());
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * GetHeadHunterWeakPointScale()));
        WeaponDamageApplier.TryApplyDamage(damageable, finalDamage);
    }
}
```

Then refactor to field lists after the test passes:

```csharp
private readonly System.Collections.Generic.List<Transform> _piercingTargets = new();
private readonly System.Collections.Generic.List<Vector3> _piercingHitOrigins = new();
private readonly Vector3[] _piercingLine = new Vector3[2];
```

- [ ] **Step 6: Run tests and sandbox smoke**

Run Unity editor tests:

- `WeaponUpgradeMathTests`
- `SandboxWeaponUpgradeDataTests`
- `AutomaticCannonFireLogicTests`

Expected: PASS.

Manual sandbox checks:

- Automatic Cannon level 1 no path behaves as before.
- Level 6 Path A has larger manual ammo and faster manual cadence.
- Level 6 Path B hits elites/bosses harder.
- Level 10 Path A and Path B do not throw console errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs Assets/Scripts/Weapon/Testing/Editor/WeaponTestingSandboxSceneBuilder.cs
git commit -m "feat: add automatic cannon upgrade behavior"
```

### Task 6: Rocket Launcher Upgrade Behavior

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/Projectile.cs`

- [ ] **Step 1: Add path helper methods**

In `RocketLauncherWeapon`, add:

```csharp
private bool IsKineticExplosionPath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

private bool IsFragmentationCapPath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;
```

- [ ] **Step 2: Apply vulnerability on Kinetic Explosion hits**

After each rocket hit is not directly observable from `RocketLauncherWeapon`, use the shared projectile path. Extend `Projectile` with optional impact status fields:

```csharp
private bool _applyDamageAmplifierOnExplosion;
private float _damageAmplifierMultiplier;
private float _damageAmplifierDuration;

public void ConfigureDamageAmplifierOnExplosion(float multiplier, float duration)
{
    _applyDamageAmplifierOnExplosion = duration > 0f && multiplier > 1f;
    _damageAmplifierMultiplier = Mathf.Max(1f, multiplier);
    _damageAmplifierDuration = Mathf.Max(0f, duration);
}
```

Reset the fields in `Launch`:

```csharp
_applyDamageAmplifierOnExplosion = false;
_damageAmplifierMultiplier = 1f;
_damageAmplifierDuration = 0f;
```

Inside `ApplyExplosionDamage`, before damage application:

```csharp
if (_applyDamageAmplifierOnExplosion)
    WeaponDamageAmplifierStatus.Apply(damageable, _damageAmplifierMultiplier, _damageAmplifierDuration);
```

- [ ] **Step 3: Add projectile pool overload for amplifier shots**

In `ProjectilePool`, add:

```csharp
public bool TrySpawnExplosiveProjectileWithAmplifier(
    Vector3 position,
    Quaternion rotation,
    Vector3 fireDirection,
    int damage,
    float explosionRadius,
    float falloff,
    float knockback,
    float speedMultiplier,
    float maxTravelDistance,
    bool explodeOnMaxTravel,
    float amplifierMultiplier,
    float amplifierDuration)
{
    GameObject go = TryGet();
    if (go == null)
        return false;

    go.transform.SetPositionAndRotation(position, rotation);
    Projectile projectile = go.GetComponent<Projectile>();
    if (projectile == null)
    {
        Release(go);
        return false;
    }

    projectile.ConfigurePooled(_projectileLifetime, damage, knockback);
    projectile.Launch(fireDirection);
    projectile.ConfigureExplosion(explosionRadius, falloff);
    projectile.ConfigureSpeedMultiplier(speedMultiplier);
    projectile.ConfigureMaxTravel(maxTravelDistance, explodeOnMaxTravel);
    projectile.ConfigureDamageAmplifierOnExplosion(amplifierMultiplier, amplifierDuration);
    return true;
}
```

- [ ] **Step 4: Use kinetic amplifier and knockback scaling**

In `RocketLauncherWeapon.FireRocketAt`, if `IsKineticExplosionPath()` is true, call the new pool overload with:

```csharp
float pathKnockback = IsKineticExplosionPath() ? 3f : 1f;
float amplifier = IsKineticExplosionPath() ? 1.2f : 1f;
float amplifierDuration = IsKineticExplosionPath() ? 5f : 0f;
```

Then calculate knockback with the path scale.

- [ ] **Step 5: Add fragmentation cone damage**

Add secondary cone damage to `Projectile` so rocket behavior does not need impact callbacks. Add these fields:

```csharp
private bool _useFragmentCone;
private float _fragmentConeAngle;
private float _fragmentConeRange;
private float _fragmentDamageScale;
```

Reset the fields in `Launch`:

```csharp
_useFragmentCone = false;
_fragmentConeAngle = 0f;
_fragmentConeRange = 0f;
_fragmentDamageScale = 0f;
```

Add configuration:

```csharp
public void ConfigureFragmentCone(float angle, float range, float damageScale)
{
    _useFragmentCone = angle > 0f && range > 0f && damageScale > 0f;
    _fragmentConeAngle = Mathf.Clamp(angle, 1f, 180f);
    _fragmentConeRange = Mathf.Max(0f, range);
    _fragmentDamageScale = Mathf.Max(0f, damageScale);
}
```

At the end of `ApplyExplosionDamage`, call:

```csharp
ApplyFragmentConeDamage();
```

Add:

```csharp
private void ApplyFragmentConeDamage()
{
    if (!_useFragmentCone)
        return;

    Collider[] hits = Physics.OverlapSphere(transform.position, _fragmentConeRange);
    for (int i = 0; i < hits.Length; i++)
    {
        IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
        if (damageable == null)
            continue;

        Vector3 toTarget = hits[i].transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            continue;

        Vector3 forward = _direction;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = transform.forward;

        float angle = Vector3.Angle(forward.normalized, toTarget.normalized);
        if (angle > _fragmentConeAngle * 0.5f)
            continue;

        int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * _fragmentDamageScale));
        WeaponDamageApplier.TryApplyDamage(damageable, damage);
    }
}
```

In `ProjectilePool`, add `float fragmentConeAngle`, `float fragmentConeRange`, and `float fragmentDamageScale` parameters to the Path B overload from Step 3, and call `projectile.ConfigureFragmentCone(...)` before returning.

In `RocketLauncherWeapon.FireRocketAt`, pass `45f`, `explosionRadius * 2f`, and `0.5f` when `IsFragmentationCapPath()` is true; otherwise pass `0f`, `0f`, and `0f`.

- [ ] **Step 6: Run tests and sandbox smoke**

Run Unity editor tests:

- `WeaponUpgradeEffectTests`
- `WeaponUpgradeMathTests`
- `SandboxWeaponUpgradeDataTests`

Expected: PASS.

Manual sandbox checks:

- Path A applies `Vulnerable` status to dummies and increases follow-up damage.
- Path B produces extra fragmentation damage in groups.
- Rocket lock active ability still starts, holds, releases, and clears markers.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs Assets/Scripts/Weapon/Projectiles/Projectile.cs Assets/Scripts/Weapon/Projectiles/ProjectilePool.cs
git commit -m "feat: add rocket launcher upgrade behavior"
```

### Task 7: Flamethrower Upgrade Behavior

**Files:**
- Create: `Assets/Scripts/Weapon/Projectiles/FlamethrowerFuelPuddle.cs`
- Modify: `Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs`

- [ ] **Step 1: Add fuel puddle component**

Create `Assets/Scripts/Weapon/Projectiles/FlamethrowerFuelPuddle.cs`:

```csharp
using UnityEngine;

public sealed class FlamethrowerFuelPuddle : MonoBehaviour
{
    private Vector3 _center;
    private float _radius;
    private int _damagePerTick;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;

    public static FlamethrowerFuelPuddle Spawn(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        GameObject go = new("FlamethrowerFuelPuddle");
        FlamethrowerFuelPuddle puddle = go.AddComponent<FlamethrowerFuelPuddle>();
        puddle.Configure(center, radius, damagePerTick, duration, tickInterval);
        return puddle;
    }

    private void Configure(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        _center = center;
        _radius = Mathf.Max(0.1f, radius);
        _damagePerTick = Mathf.Max(1, damagePerTick);
        _remainingDuration = Mathf.Max(0.1f, duration);
        _tickInterval = Mathf.Max(0.05f, tickInterval);
        _tickTimer = 0f;
        transform.position = center;
        ExplosionRadiusVfx.Spawn(center, _radius);
    }

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        _tickTimer -= Time.deltaTime;

        while (_tickTimer <= 0f && _remainingDuration > 0f)
        {
            WeaponRadialDamage.Apply(_center, _radius, _damagePerTick, falloff: 0f, knockback: 0f, maxTargets: 64);
            _tickTimer += _tickInterval;
        }

        if (_remainingDuration <= 0f)
            Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
```

- [ ] **Step 2: Add flamethrower path helpers**

In `FlamethrowerWeapon`, add:

```csharp
private bool IsJellifiedFuelPath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

private bool IsLiquidNitrogenPath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;
```

- [ ] **Step 3: Spawn puddles for Jellified Fuel**

In `ApplyBurnToTarget`, after refreshing burn:

```csharp
if (IsJellifiedFuelPath())
{
    float levelScale = Mathf.Max(1f, Runtime.Level / 6f);
    float radius = GetScaledHoseRadius(tuning) * levelScale;
    float duration = GetPathAdjustedBurnDuration(tuning);
    FlamethrowerFuelPuddle.Spawn(target.position, radius, damagePerTick, duration, tuning.FlameBurnTickInterval);
}
```

- [ ] **Step 4: Apply Liquid Nitrogen slow/freeze**

In `ApplyBurnToTarget`, replace the existing Path B dummy-only status with:

```csharp
if (IsLiquidNitrogenPath())
{
    float duration = activeAbility ? 2f : 3f;
    WeaponDummyEnemy dummy = damageComponent.GetComponent<WeaponDummyEnemy>();
    if (dummy != null)
        dummy.ApplyStatus(activeAbility ? "Freeze" : "Liquid Nitrogen", duration);
    if (activeAbility)
        WeaponMovementFreezeStatus.Apply(target, duration);
}
```

- [ ] **Step 5: Run tests and sandbox smoke**

Run Unity editor tests:

- `WeaponUpgradeEffectTests`
- `WeaponUpgradeMathTests`

Expected: PASS.

Manual sandbox checks:

- Path A creates puddles in auto/manual/active use and they damage grouped dummies.
- Path B slows moving dummy targets and freezes on active ability.
- Base flamethrower behavior remains unchanged with no path.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Weapon/Projectiles/FlamethrowerFuelPuddle.cs Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs
git commit -m "feat: add flamethrower upgrade behavior"
```

### Task 8: Mortar Upgrade Behavior

**Files:**
- Modify: `Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs`
- Modify: `Assets/Scripts/Weapon/Types/MortarWeapon.cs`

- [ ] **Step 1: Add mortar payload model**

At the top of `MortarShellImpact.cs`, before `MortarShellImpact`, add:

```csharp
public readonly struct MortarUpgradePayload
{
    public readonly bool UseGrapeshot;
    public readonly int GrapeshotCount;
    public readonly float GrapeshotConeAngle;
    public readonly float GrapeshotDamageScale;
    public readonly int RepeatExplosionCount;
    public readonly float RepeatExplosionDelay;

    public MortarUpgradePayload(
        bool useGrapeshot,
        int grapeshotCount,
        float grapeshotConeAngle,
        float grapeshotDamageScale,
        int repeatExplosionCount,
        float repeatExplosionDelay)
    {
        UseGrapeshot = useGrapeshot;
        GrapeshotCount = grapeshotCount;
        GrapeshotConeAngle = grapeshotConeAngle;
        GrapeshotDamageScale = grapeshotDamageScale;
        RepeatExplosionCount = repeatExplosionCount;
        RepeatExplosionDelay = repeatExplosionDelay;
    }

    public static MortarUpgradePayload None => new(false, 0, 0f, 0f, 1, 0f);
}
```

- [ ] **Step 2: Add a launch overload with payload**

Add overload:

```csharp
public static MortarShellImpact Launch(
    Vector3 start,
    Vector3 target,
    float travelTime,
    float arcHeight,
    int damage,
    float explosionRadius,
    float falloff,
    float knockback,
    float collisionRadius,
    Transform ignoredRoot,
    MortarUpgradePayload payload)
{
    GameObject go = new GameObject("MortarShellImpact");
    MortarShellImpact shell = go.AddComponent<MortarShellImpact>();
    shell.Configure(start, target, travelTime, arcHeight, damage, explosionRadius, falloff, knockback, collisionRadius, ignoredRoot);
    shell._payload = payload;
    return shell;
}
```

Add field:

```csharp
private MortarUpgradePayload _payload = MortarUpgradePayload.None;
private int _remainingRepeatExplosions;
private float _repeatExplosionTimer;
private bool _detonated;
```

- [ ] **Step 3: Support repeated explosions**

In `Detonate(Vector3 explosionCenter)`, replace the final `Destroy(gameObject);` with:

```csharp
if (!_detonated)
{
    _detonated = true;
    _remainingRepeatExplosions = Mathf.Max(1, _payload.RepeatExplosionCount) - 1;
    _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
    SpawnGrapeshot(explosionCenter);
}

if (_remainingRepeatExplosions <= 0)
    Destroy(gameObject);
```

In `Update`, add at the top:

```csharp
if (_detonated)
{
    TickRepeatExplosions();
    return;
}
```

Add:

```csharp
private void TickRepeatExplosions()
{
    if (_remainingRepeatExplosions <= 0)
    {
        Destroy(gameObject);
        return;
    }

    _repeatExplosionTimer -= Time.deltaTime;
    if (_repeatExplosionTimer > 0f)
        return;

    _remainingRepeatExplosions--;
    _repeatExplosionTimer = Mathf.Max(0.01f, _payload.RepeatExplosionDelay);
    ApplyExplosionDamageAt(_target);
}
```

Move the existing radial damage loop into `ApplyExplosionDamageAt(Vector3 explosionCenter)` so repeat calls reuse it.

- [ ] **Step 4: Support grapeshot**

Add:

```csharp
private void SpawnGrapeshot(Vector3 center)
{
    if (!_payload.UseGrapeshot || _payload.GrapeshotCount <= 0)
        return;

    Vector3 forward = (_target - _start).sqrMagnitude > 0.0001f ? (_target - _start).normalized : Vector3.forward;
    Quaternion baseRotation = Quaternion.LookRotation(forward, Vector3.up);
    for (int i = 0; i < _payload.GrapeshotCount; i++)
    {
        float yaw = Random.Range(-_payload.GrapeshotConeAngle * 0.5f, _payload.GrapeshotConeAngle * 0.5f);
        Vector3 direction = baseRotation * Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 hitCenter = center + direction.normalized * Mathf.Max(0.5f, _explosionRadius);
        int damage = Mathf.Max(1, Mathf.RoundToInt(_damage * Mathf.Max(0f, _payload.GrapeshotDamageScale)));
        WeaponRadialDamage.Apply(hitCenter, Mathf.Max(0.25f, _explosionRadius * 0.35f), damage, 0.2f, _knockback * 0.35f, 16);
    }
}
```

- [ ] **Step 5: Pass payload from MortarWeapon**

In `MortarWeapon`, add:

```csharp
private MortarUpgradePayload GetUpgradePayload(bool activeAbility)
{
    if (!Runtime.HasAdvancedPath)
        return MortarUpgradePayload.None;

    if (Runtime.SelectedPath == WeaponUpgradePath.PathA)
    {
        int heatBonus = Heat != null ? Mathf.FloorToInt(Heat.NormalizedHeat * 10f) : 0;
        return new MortarUpgradePayload(true, activeAbility ? 10 + heatBonus : 15, 70f, 1f, 1, 0f);
    }

    if (Runtime.SelectedPath == WeaponUpgradePath.PathB)
        return new MortarUpgradePayload(false, 0, 0f, 0f, 3, 2f);

    return MortarUpgradePayload.None;
}
```

Update `FireShell` to accept `bool activeAbility = false` and pass `GetUpgradePayload(activeAbility)` into the new `Launch` overload.

- [ ] **Step 6: Run tests and sandbox smoke**

Run Unity editor tests:

- `MortarTrajectoryTests`
- `MortarTerrainFilterTests`
- `WeaponUpgradeEffectTests`

Expected: PASS.

Manual sandbox checks:

- Path A produces visible grapeshot damage in groups.
- Path B repeats explosions after delay.
- Base mortar still fires and detonates normally.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs Assets/Scripts/Weapon/Types/MortarWeapon.cs
git commit -m "feat: add mortar upgrade behavior"
```

### Task 9: Rotating Blade Upgrade Behavior

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs`

- [ ] **Step 1: Add path helpers and multi-blade count**

Add:

```csharp
private bool IsMultiBladePath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathA;

private bool IsAtomicSharpnessPath() =>
    Runtime != null && Runtime.HasAdvancedPath && Runtime.SelectedPath == WeaponUpgradePath.PathB;

private int GetBladeCount()
{
    if (!IsMultiBladePath())
        return 1;

    return Mathf.Clamp(1 + Mathf.FloorToInt((Runtime.Level - 6) / 2f), 2, 4);
}
```

- [ ] **Step 2: Tick multiple automatic blades**

In `TickAutomatic`, replace single `bladeCenter` damage with a loop:

```csharp
int bladeCount = GetBladeCount();
for (int bladeIndex = 0; bladeIndex < bladeCount; bladeIndex++)
{
    Vector3 bladeCenter = GetBladeCenter(tuning, bladeIndex, bladeCount);
    float hitRadius = GetScaledHitRadius(tuning);
    ShowOrbit(bladeCenter, hitRadius, tuning);

    if (_autoDamageTimer <= 0f)
    {
        int hitCount = EnemyRegistry.CollectClosestOnPlane(bladeCenter, hitRadius, MaxContactTargets, _targets);
        float knockbackScale = GetAutomaticKnockbackScale(tuning);
        for (int i = 0; i < hitCount; i++)
            ApplyBladeDamage(_targets[i], GetAtomicSharpnessDamageScale(), bladeCenter, knockbackScale);
    }
}
```

Change `GetBladeCenter` signature:

```csharp
private Vector3 GetBladeCenter(RotatingBladeTuning tuning, int bladeIndex = 0, int bladeCount = 1)
{
    Vector3 baseDirection = Owner != null ? Owner.forward : Vector3.forward;
    baseDirection.y = 0f;
    if (baseDirection.sqrMagnitude <= 0.0001f)
        baseDirection = Vector3.forward;

    baseDirection.Normalize();
    float offset = bladeCount <= 1 ? 0f : (360f / bladeCount) * bladeIndex;
    Vector3 orbitDirection = Quaternion.AngleAxis(_spinAngle + offset, Vector3.up) * baseDirection;
    return GetOwnerOrigin() + orbitDirection * GetScaledOrbitRadius(tuning);
}
```

- [ ] **Step 3: Add Atomic Sharpness damage and knockback modifiers**

Add:

```csharp
private float GetAtomicSharpnessDamageScale() => IsAtomicSharpnessPath() ? 2f : 1f;

private float GetAtomicSharpnessKnockbackScale(float original) => IsAtomicSharpnessPath() ? 0f : original;
```

Apply these in auto, manual, and active damage calls.

- [ ] **Step 4: Add Multi-Blade manual/active repeats**

In manual slash, repeat the cone damage `GetBladeCount()` times with a short angular offset:

```csharp
int swingCount = GetBladeCount();
for (int swing = 0; swing < swingCount; swing++)
{
    Vector3 swingDirection = Quaternion.AngleAxis((swing - (swingCount - 1) * 0.5f) * 8f, Vector3.up) * slashDirection;
    int hitCount = EnemyRegistry.CollectClosestOnPlaneInCone(origin, swingDirection, range, tuning.BladeManualConeAngle, MaxManualTargets, _targets);
    for (int i = 0; i < hitCount; i++)
        ApplyBladeDamage(_targets[i], damageScale * GetAtomicSharpnessDamageScale(), origin, GetAtomicSharpnessKnockbackScale(tuning.BladeManualKnockbackScale));
}
```

For active ability, repeat the thrust line `GetBladeCount()` times with the same offset.

- [ ] **Step 5: Sandbox smoke**

Manual sandbox checks:

- Path A shows/damages with multiple automatic blade positions.
- Path A manual slash and active thrust hit more broadly.
- Path B deals higher damage and low/no knockback.
- Base rotating blade remains unchanged with no path.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs
git commit -m "feat: add rotating blade upgrade behavior"
```

### Task 10: Full Sandbox Regression Pass

**Files:**
- Verify: `Assets/Scenes/WeaponTestingSandbox.unity`
- Verify: `Assets/Scripts/Weapon/Testing/WeaponSandboxDebugUI.cs`
- Verify: all modified weapon/effect scripts

- [ ] **Step 1: Run all relevant editor tests**

Run Unity editor tests:

- `WeaponUpgradeMathTests`
- `WeaponUpgradeEffectTests`
- `SandboxWeaponUpgradeDataTests`
- `ManualWeaponFireCooldownTests`
- `AutomaticCannonFireLogicTests`
- `MortarTrajectoryTests`
- `MortarTerrainFilterTests`
- `ReticlePresentationLogicTests`

Expected: PASS.

- [ ] **Step 2: Open sandbox and verify every weapon**

For each weapon:

- Level 1, path `None`
- Level 6, `PathA`
- Level 6, `PathB`
- Level 10, `PathA`
- Level 10, `PathB`

For each state:

- Automatic mode
- Manual mode
- Active ability
- Heat 0 percent
- Heat 50 percent
- Heat 100 percent
- Normal dummy
- Elite dummy
- Boss dummy
- Group spawn
- Moving target spawn when relevant

Expected: no console errors; metrics update; visual effects are visible enough to verify behavior; no weapon blocks the sandbox loop.

- [ ] **Step 3: Record residual gaps**

For every effect that stops on a missing engine hook, record the exact effect name, the missing hook, the test or sandbox step that exposed the block, and the behavior that remains active. Do not replace the blocked effect with unrelated behavior.

- [ ] **Step 4: Commit final fixes**

```bash
git add Assets/Scripts/Weapon Assets/Tests/Editor
git commit -m "test: verify sandbox weapon upgrades"
```

### Task 11: Final Review

**Files:**
- Review: all changed scripts and tests

- [ ] **Step 1: Inspect diff**

Run:

```bash
git diff --stat HEAD~10..HEAD
git diff HEAD~10..HEAD -- Assets/Scripts/Weapon Assets/Tests/Editor
```

Expected: only weapon behavior, weapon effect helpers, sandbox upgrade data tests, and targeted tests changed.

- [ ] **Step 2: Check production integration boundaries**

Confirm no changes were made to:

- `WeaponLevelUpHandler`
- material/inventory systems
- crafting station systems
- save/load systems

Expected: no production progression integration in this pass.

- [ ] **Step 3: Summarize verification**

Prepare final summary with:

- Tests run and pass/fail result.
- Sandbox weapons verified.
- Any residual gaps caused by missing movement, invulnerability, or non-sandbox systems.
