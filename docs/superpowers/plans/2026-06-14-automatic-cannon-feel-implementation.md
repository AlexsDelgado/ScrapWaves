# Automatic Cannon Feel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add subtle per-projectile cannon scatter and reduce held manual fire to two attack-speed-scaled volleys per second.

**Architecture:** Put deterministic cadence and direction math in a pure `AutomaticCannonFireLogic` helper. Keep orchestration and random sampling in `AutomaticCannonWeapon`, and store all new tuning exclusively in `AutomaticCannonTuning`.

**Tech Stack:** Unity 6, C#, NUnit EditMode tests, ScriptableObject YAML assets

---

### Task 1: Define Cannon Fire Math with Tests

**Files:**
- Create: `Assets/Tests/Editor/AutomaticCannonFireLogicTests.cs`
- Create: `Assets/Scripts/Weapon/Types/AutomaticCannonFireLogic.cs`

- [ ] **Step 1: Write failing cadence tests**

Add tests asserting that `GetManualBurstInterval(2f, 1f, 1f)` returns `0.5f`, and that attack-speed and weapon-rate multipliers reduce the interval.

- [ ] **Step 2: Write failing scatter tests**

Add tests asserting that zero scatter preserves the normalized aim direction and that horizontal and vertical unit-circle samples alter their corresponding axes without exceeding the configured scatter.

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'C:\Users\franc\OneDrive\Documentos\GitHub\ScrapWaves' `
  -runTests -testPlatform EditMode `
  -testFilter AutomaticCannonFireLogicTests `
  -testResults 'Temp\automatic-cannon-red.xml'
```

Expected: compilation fails because `AutomaticCannonFireLogic` does not exist.

- [ ] **Step 4: Implement the pure helper**

Implement:

```csharp
public static float GetManualBurstInterval(
    float baseBurstsPerSecond,
    float attackSpeedMultiplier,
    float weaponRateMultiplier)
```

and:

```csharp
public static Vector3 ApplyProjectileScatter(
    Vector3 direction,
    float spreadDegrees,
    Vector2 unitCircleSample)
```

- [ ] **Step 5: Run focused tests and verify GREEN**

Repeat the focused Unity test command with `Temp\automatic-cannon-green.xml`.

### Task 2: Add Cannon-Only Tuning

**Files:**
- Modify: `Assets/Scripts/Weapon/Base/WeaponData.cs`
- Modify: `Assets/Scripts/Weapon/Editor/WeaponDataEditor.cs`

- [ ] **Step 1: Add tuning fields**

Add `CannonBurstProjectileScatterDegrees = 1.5f` and `CannonManualBurstsPerSecond = 2f` to `AutomaticCannonTuning`, both with non-negative inspector constraints.

- [ ] **Step 2: Expose fields in the cannon inspector**

Draw both properties only inside `DrawAutomaticCannon`.

### Task 3: Apply Cadence and Scatter

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`

- [ ] **Step 1: Use manual volley cadence**

Replace the manual `GetFireInterval()` assignment with `AutomaticCannonFireLogic.GetManualBurstInterval`, passing the cannon base volleys per second, player attack speed, and `WeaponMath.GetAttackRateMultiplier(Runtime)`.

- [ ] **Step 2: Add per-projectile scatter**

Pass `CannonBurstProjectileScatterDegrees` to both automatic and manual line bursts. Keep projectile positions on the burst centerline and calculate each projectile's direction from a fresh `Random.insideUnitCircle` sample.

- [ ] **Step 3: Preserve active behavior**

Leave `FireScatterBurst` unchanged so the active ability remains the only shotgun-like cannon attack.

### Task 4: Persist Defaults and Verify

**Files:**
- Modify: `Assets/Scripts/Weapon/SO/AutomaticCannon.asset`
- Modify: `Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset`
- Modify: `Assets/Scripts/Weapon/Testing/Editor/WeaponTestingSandboxSceneBuilder.cs`

- [ ] **Step 1: Persist tuning values**

Set the new scatter to `1.5` degrees and manual cadence to `2` volleys per second in the production and sandbox cannon assets.

- [ ] **Step 2: Update generated sandbox defaults**

Assign the same values in `WeaponTestingSandboxSceneBuilder`.

- [ ] **Step 3: Run all EditMode tests**

Run the Unity EditMode suite and require zero failed tests.

- [ ] **Step 4: Compile the project**

Run Unity in batch mode without tests and require an exit code of zero with no C# compiler errors.

