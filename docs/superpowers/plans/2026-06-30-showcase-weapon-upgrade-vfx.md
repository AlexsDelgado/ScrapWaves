# Showcase Weapon Upgrade VFX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add readable, punchier code-generated sandbox/runtime VFX that make each upgrade path visibly different from the base weapon behavior.

**Architecture:** Add one shared `WeaponUpgradeVfx` helper for short-lived rings, beams, cone rays, target pulses, and path text labels. Keep behavior logic in the existing weapon scripts and call the helper at already-established effect points. Avoid prefab or particle asset dependencies for this showcase pass.

**Tech Stack:** Unity C#, `LineRenderer`, primitive-free runtime `GameObject` effects, existing weapon scripts and editmode tests.

---

### Task 1: Shared Upgrade VFX Helper

**Files:**
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponUpgradeVfx.cs`
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponUpgradeVfx.cs.meta`
- Modify: `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`

- [x] Add failing editmode tests proving `WeaponUpgradeVfx` exists and can spawn a ring/beam without exceptions.
- [x] Implement `WeaponUpgradeVfx` with static methods:
  - `SpawnRing(Vector3 center, float radius, Color color, float duration, float widthMultiplier = 1f, string label = null)`
  - `SpawnBeam(Vector3 start, Vector3 end, Color color, float duration, float width = 0.08f, string label = null)`
  - `SpawnCone(Vector3 origin, Vector3 direction, float range, float angle, Color color, float duration, int rays = 7, string label = null)`
  - `SpawnTargetPulse(Transform target, Color color, float duration, string label = null)`
- [ ] Run `WeaponUpgradeEffectTests`. Blocked: Unity batch mode refused to open while another Unity instance has this project open.

### Task 2: Rocket, Flamethrower, and Mortar Path VFX

**Files:**
- Modify: `Assets/Scripts/Weapon/Projectiles/Projectile.cs`
- Modify: `Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/FlamethrowerFuelPuddle.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs`

- [x] Rocket PathA explosion adds an orange kinetic shock ring and vulnerable target pulse.
- [x] Rocket PathB explosion adds cyan fragmentation cone rays.
- [x] Flamethrower PathA puddles show sticky fuel rings and tick pulses.
- [x] Flamethrower PathB active freeze shows icy target pulses.
- [x] Mortar PathA grapeshot shows shard beams/rings.
- [x] Mortar PathB repeat explosions show purple delayed/repeat rings.
- [ ] Run `WeaponUpgradeEffectTests`, `MortarTrajectoryTests`, and `MortarTerrainFilterTests`. Blocked by open Unity project instance.

### Task 3: Automatic Cannon and Rotating Blade Path VFX

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs`

- [x] Automatic Cannon PathA adds warm sustained-fire streaks.
- [x] Automatic Cannon PathB active adds a cyan precision beam and weak-point target pulses.
- [x] Rotating Blade PathA colors multi-blade orbit/slash/thrust effects green-gold.
- [x] Rotating Blade PathB colors atomic slash/thrust effects cyan-white and sharper.
- [ ] Run `WeaponUpgradeMathTests` and `WeaponUpgradeEffectTests`. Blocked by open Unity project instance.

### Task 4: Full Verification

**Files:**
- Verify all changed scripts and tests.

- [ ] Run the full Unity editmode suite. Blocked by open Unity project instance.
- [x] Confirm `ProjectSettings/ProjectSettings.asset` is not staged or modified by this work.
- [x] Summarize manual sandbox checks still required for visual feel.
