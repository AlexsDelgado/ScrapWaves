# Weapon Reticle System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build adaptive weapon reticles, a lock-progress-driven rocket active frame, and a terrain-only mortar landing marker that uses the shell's real parabola.

**Architecture:** `ReticleHud` owns all rendering and selects a mode from the current manual weapon. Focused read-only interfaces expose rocket and mortar presentation state, while `MortarTrajectory` contains the shared parabola and flight-limit math used by both the shell and terrain predictor.

**Tech Stack:** Unity 6000.3, C#, uGUI, LineRenderer, Unity Physics, NUnit/Unity Test Framework

---

### Task 1: Pure Reticle and Mortar Math

**Files:**
- Create: `Assets/Scripts/Weapon/UI/ReticlePresentationLogic.cs`
- Create: `Assets/Scripts/Weapon/Projectiles/MortarTrajectory.cs`
- Create: `Assets/Tests/Editor/ReticlePresentationLogicTests.cs`
- Create: `Assets/Tests/Editor/MortarTrajectoryTests.cs`

- [ ] **Step 1: Write failing reticle logic tests**

```csharp
[TestCase(WeaponType.AutomaticCannon, false, ReticleMode.CircleDot)]
[TestCase(WeaponType.RocketLauncher, false, ReticleMode.CircleDot)]
[TestCase(WeaponType.RocketLauncher, true, ReticleMode.RocketLock)]
[TestCase(WeaponType.Mortar, false, ReticleMode.Mortar)]
[TestCase(WeaponType.Flamethrower, false, ReticleMode.WideBrackets)]
[TestCase(WeaponType.RotatingBlade, false, ReticleMode.WideBrackets)]
public void ResolveMode_ReturnsExpectedMode(
    WeaponType weaponType,
    bool rocketCharging,
    ReticleMode expected)
{
    Assert.That(ReticlePresentationLogic.ResolveMode(weaponType, rocketCharging), Is.EqualTo(expected));
}

[TestCase(5, 5, 10, 0f)]
[TestCase(7, 5, 10, 0.4f)]
[TestCase(10, 5, 10, 1f)]
[TestCase(5, 5, 5, 0f)]
public void GetRocketLockProgress_NormalizesActualLocks(
    int current,
    int initial,
    int maximum,
    float expected)
{
    Assert.That(
        ReticlePresentationLogic.GetRocketLockProgress(current, initial, maximum),
        Is.EqualTo(expected).Within(0.0001f));
}
```

- [ ] **Step 2: Write failing mortar trajectory tests**

```csharp
[Test]
public void Evaluate_ReachesConfiguredTargetAtOne()
{
    Vector3 start = new(0f, 1f, 0f);
    Vector3 target = new(0f, 1f, 10f);

    Assert.That(MortarTrajectory.Evaluate(start, target, 7f, 1f), Is.EqualTo(target));
}

[Test]
public void Evaluate_ContinuesPastTargetAfterOne()
{
    Vector3 point = MortarTrajectory.Evaluate(Vector3.zero, Vector3.forward * 10f, 7f, 2f);

    Assert.That(point.z, Is.EqualTo(20f).Within(0.0001f));
    Assert.That(point.y, Is.LessThan(0f));
}

[Test]
public void GetMaximumNormalizedTime_MatchesShellFailsafe()
{
    Assert.That(MortarTrajectory.GetMaximumNormalizedTime(0.5f), Is.EqualTo(10f).Within(0.0001f));
}
```

- [ ] **Step 3: Run the editor tests and verify RED**

Run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'C:\Users\franc\OneDrive\Documentos\GitHub\ScrapWaves' `
  -runTests -testPlatform EditMode `
  -testResults 'Temp\reticle-tests-red.xml'
```

Expected: compilation fails because `ReticleMode`, `ReticlePresentationLogic`, and `MortarTrajectory` do not exist.

- [ ] **Step 4: Implement the pure helpers**

```csharp
public enum ReticleMode
{
    Hidden,
    WideBrackets,
    CircleDot,
    Mortar,
    RocketLock
}

public static class ReticlePresentationLogic
{
    public static ReticleMode ResolveMode(WeaponType weaponType, bool rocketCharging)
    {
        return weaponType switch
        {
            WeaponType.Flamethrower => ReticleMode.WideBrackets,
            WeaponType.RotatingBlade => ReticleMode.WideBrackets,
            WeaponType.Mortar => ReticleMode.Mortar,
            WeaponType.RocketLauncher when rocketCharging => ReticleMode.RocketLock,
            WeaponType.RocketLauncher => ReticleMode.CircleDot,
            WeaponType.AutomaticCannon => ReticleMode.CircleDot,
            _ => ReticleMode.Hidden
        };
    }

    public static float GetRocketLockProgress(int current, int initial, int maximum)
    {
        int safeInitial = Mathf.Max(0, initial);
        int safeMaximum = Mathf.Max(safeInitial, maximum);
        if (safeMaximum == safeInitial)
            return 0f;
        return Mathf.InverseLerp(safeInitial, safeMaximum, Mathf.Clamp(current, safeInitial, safeMaximum));
    }
}
```

```csharp
public static class MortarTrajectory
{
    public const float MinimumFlightFailsafe = 5f;
    public const float FlightFailsafeMultiplier = 5f;

    public static Vector3 Evaluate(Vector3 start, Vector3 target, float arcHeight, float normalizedTime)
    {
        Vector3 point = Vector3.LerpUnclamped(start, target, normalizedTime);
        point.y += 4f * Mathf.Max(0f, arcHeight) * normalizedTime * (1f - normalizedTime);
        return point;
    }

    public static float GetMaximumNormalizedTime(float travelTime)
    {
        float safeTravelTime = Mathf.Max(0.05f, travelTime);
        float failsafeSeconds = Mathf.Max(MinimumFlightFailsafe, safeTravelTime * FlightFailsafeMultiplier);
        return failsafeSeconds / safeTravelTime;
    }
}
```

- [ ] **Step 5: Run the editor tests and verify GREEN**

Run the Task 1 Step 3 command again.

Expected: all reticle and mortar math tests pass.

### Task 2: Weapon Presentation Status

**Files:**
- Modify: `Assets/Scripts/Weapon/Base/WeaponBehaviourBase.cs`
- Modify: `Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Types/MortarWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Managers/WeaponManager.cs`
- Modify: `Assets/Scripts/Weapon/Testing/WeaponTestingSandboxManager.cs`

- [ ] **Step 1: Add read-only status interfaces**

```csharp
public interface IRocketReticleStatus
{
    bool IsTargetingActive { get; }
    int CurrentRocketLocks { get; }
    int InitialRocketLocks { get; }
    int MaximumRocketLocks { get; }
}

public interface IMortarReticleStatus
{
    float ManualExplosionRadius { get; }
    float ShellCollisionRadius { get; }
    float ManualTravelTime { get; }
    float ArcHeight { get; }
}
```

- [ ] **Step 2: Expose actual rocket assignments**

Make `RocketLauncherWeapon` implement `IRocketReticleStatus`. Return `_abilityTargets.Count`, the configured initial count clamped to the current maximum, and `GetMaximumActiveRocketCount`.

- [ ] **Step 3: Expose mortar preview tuning**

Make `MortarWeapon` implement `IMortarReticleStatus`. Return area-scaled explosion/collision radii and the same heat-adjusted manual travel time already used to fire shells.

- [ ] **Step 4: Expose current behavior and spawn**

Add these accessors to `WeaponManager`:

```csharp
public IWeaponBehaviour GetCurrentManualBehaviour()
public Transform GetProjectileSpawn()
```

Add this accessor to `WeaponTestingSandboxManager`:

```csharp
public IWeaponBehaviour CurrentManualBehaviour
```

- [ ] **Step 5: Build both assemblies**

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

Expected: both builds succeed with zero errors.

### Task 3: Shared Mortar Trajectory and Terrain Prediction

**Files:**
- Modify: `Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs`
- Modify: `Assets/Scripts/Player/ReticleAimProvider.cs`
- Create: `Assets/Tests/Editor/MortarTerrainFilterTests.cs`

- [ ] **Step 1: Write failing terrain-filter tests**

```csharp
[Test]
public void IsValidMortarTerrainHit_RejectsDamageableTargets()
{
    GameObject enemy = new("Enemy");
    enemy.AddComponent<TestDamageable>();
    try
    {
        Assert.That(ReticleAimProvider.IsValidMortarTerrainTransform(enemy.transform, null), Is.False);
    }
    finally
    {
        Object.DestroyImmediate(enemy);
    }
}

[Test]
public void IsValidMortarTerrainHit_AcceptsMapCollider()
{
    GameObject map = new("Map");
    try
    {
        Assert.That(ReticleAimProvider.IsValidMortarTerrainTransform(map.transform, null), Is.True);
    }
    finally
    {
        Object.DestroyImmediate(map);
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run the Task 1 Unity command.

Expected: tests fail because `IsValidMortarTerrainTransform` does not exist.

- [ ] **Step 3: Share shell parabola and failsafe**

Replace the local mortar equation and constants in `MortarShellImpact` with:

```csharp
Vector3 nextPosition = MortarTrajectory.Evaluate(_start, _target, _arcHeight, t);
if (t >= MortarTrajectory.GetMaximumNormalizedTime(_travelTime))
    Destroy(gameObject);
```

- [ ] **Step 4: Add terrain-only trajectory prediction**

Add:

```csharp
public bool TryGetMortarTerrainImpact(
    Vector3 origin,
    Vector3 aimDirection,
    float range,
    float arcHeight,
    float collisionRadius,
    float travelTime,
    out RaycastHit terrainHit)
```

The method samples the shared trajectory in 0.05 normalized-time increments and sphere-casts each segment. It ignores `_ignoredRoot`, children of `_ignoredRoot`, and any transform with an `IDamageable` in its parent chain. It returns the closest valid map hit per segment.

- [ ] **Step 5: Run tests and verify GREEN**

Run the Task 1 Unity command.

Expected: all editor tests pass.

### Task 4: Adaptive Reticle HUD

**Files:**
- Replace: `Assets/Scripts/Weapon/UI/ReticleHud.cs`
- Modify: `Assets/Prefabs/player.prefab`

- [ ] **Step 1: Build screen-space visual groups**

Create separate roots for:

- `WideBrackets`: two inward-facing three-line brackets.
- `CircleDot`: generated circle ring sprite and centered dot.
- `Mortar`: two rotated lines forming a downward `V`.
- `RocketLock`: four two-line corner brackets within a resizable frame.

Only one centered root is active at a time.

- [ ] **Step 2: Select mode from the current manual weapon**

Resolve `WeaponManager` on the player and fall back to `WeaponTestingSandboxManager` for the sandbox. Read the current runtime and behavior, then call:

```csharp
ReticlePresentationLogic.ResolveMode(
    runtime.Data.WeaponType,
    behavior is IRocketReticleStatus rocket && rocket.IsTargetingActive);
```

- [ ] **Step 3: Animate rocket lock frame**

Use actual status values:

```csharp
float progress = ReticlePresentationLogic.GetRocketLockProgress(
    rocket.CurrentRocketLocks,
    rocket.InitialRocketLocks,
    rocket.MaximumRocketLocks);
Vector2 targetSize = Vector2.Lerp(_rocketMinimumFrameSize, _rocketMaximumFrameSize, progress);
_rocketFrame.sizeDelta = Vector2.Lerp(
    _rocketFrame.sizeDelta,
    targetSize,
    1f - Mathf.Exp(-_rocketFrameEaseSpeed * Time.unscaledDeltaTime));
```

Configure the maximum width to `1344` on the 1920-wide reference canvas.

- [ ] **Step 4: Build and update the mortar ground marker**

Create two looped world-space `LineRenderer` rings and a small unlit center sphere. For mortar mode:

- Query `IMortarReticleStatus`.
- Call `TryGetMortarTerrainImpact`.
- Place the marker at `hit.point + hit.normal * _mortarSurfaceOffset`.
- Align ring points to the hit normal.
- Use `_mortarLandingRingRadius` for the inner ring.
- Use `ManualExplosionRadius` for the outer ring.

Disable the world marker for all other modes or failed predictions.

- [ ] **Step 5: Update prefab defaults**

Serialize the new visual dimensions, including:

- Wide bracket frame around `250 x 70`.
- Circle diameter around `34`.
- Mortar V around `30 x 18`.
- Rocket minimum frame around `180 x 100`.
- Rocket maximum frame `1344 x 756`.
- Rocket ease speed around `9`.

- [ ] **Step 6: Build both assemblies**

Run both Task 2 build commands.

Expected: both builds succeed with zero errors.

### Task 5: End-to-End Verification

**Files:**
- Verify: all changed runtime, editor, prefab, and test files

- [ ] **Step 1: Run the complete editor test suite**

Run the Task 1 Unity command with results written to `Temp/reticle-tests-final.xml`.

Expected: all tests pass.

- [ ] **Step 2: Build runtime and editor assemblies**

Run both Task 2 build commands.

Expected: zero errors.

- [ ] **Step 3: Check the final diff**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intentional project changes remain.

- [ ] **Step 4: Play-mode visual checklist**

Verify in the weapon testing scene:

- Blade/flamethrower show wide rectangular brackets.
- Cannon/launcher show circle with center dot.
- Mortar shows center V and terrain landing/blast rings.
- Enemies do not pull the mortar marker off terrain.
- Rocket Q shows four corners, expands smoothly with actual locks, reaches the cinematic cap, and resets on release.
