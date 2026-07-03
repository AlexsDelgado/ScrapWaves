# Final Weapon Upgrade Mechanics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the five weapon upgrade paths closer to the Google spec before manual mechanics and VFX tuning.

**Architecture:** Keep the existing weapon behavior classes as the integration points. Add only small support components where the spec requires persistent enemy state or delayed/repeated ability behavior. Preserve the sandbox VFX helper as presentation only; gameplay rules live in weapon/projectile/status scripts.

**Tech Stack:** Unity C#, existing weapon runtime, edit-mode NUnit tests, code-generated VFX.

---

### Task 1: Flamethrower Final Path Mechanics

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/FlamethrowerWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/FlamethrowerFuelPuddle.cs`
- Create: `Assets/Scripts/Weapon/Projectiles/WeaponMovementSlowStatus.cs`
- Test: `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`

- [ ] Add tests for Jellified Fuel double burn duration, active player puddle spawn data, Liquid Nitrogen slow, and active freeze followed by slow.
- [ ] Implement `WeaponMovementSlowStatus` for real enemy movement scripts and sandbox dummy status labels.
- [ ] Change Jellified Fuel burn duration from `1.5x` to `2x`.
- [ ] Spawn the spec active puddle under the player at half active radius and double base puddle duration.
- [ ] Apply 50% Liquid Nitrogen slow on normal hits, ramp manual slow toward 90% across six ticks, and apply post-freeze 90% slow after active freeze.

### Task 2: Rocket Final Path Numbers And Fragmentation Active

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/RocketLauncherWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/Projectile.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/ProjectilePool.cs`
- Test: `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`

- [ ] Add tests for Kinetic `2x` radius, Kinetic active half knockback, Fragmentation `0.5x` radius, `0.25x` knockback, full-damage fragment cone, and active cluster payload data.
- [ ] Keep Kinetic vulnerability duration at 5 seconds and route follow-up damage through `WeaponDamageAmplifierStatus`.
- [ ] Implement Fragmentation active as one massive double-damage rocket with no cone that spawns 20 radial child rockets at half damage, each with the normal fragmentation cone.

### Task 3: Mortar Grapeshot And Multi-Charged Shells

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/MortarWeapon.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/MortarShellImpact.cs`
- Test: `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`

- [ ] Add tests for Grapeshot airborne detonation payload, 15 half-damage spread hits, active rain settings, and Multi-Charged repeat payload.
- [ ] Force Grapeshot shells to detonate before ground impact.
- [ ] Use half-damage grapeshot hits instead of full-damage mini blasts.
- [ ] Implement Grapeshot active as a short projectile rain payload, scaled by heat.
- [ ] Keep Multi-Charged triple explosions at 2 second intervals and make active payload use synchronized triple-repeat ground blasts.

### Task 4: Automatic Cannon Path Replacements

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Test: `Assets/Tests/Editor/WeaponUpgradeMathTests.cs`

- [ ] Add tests for Continuous Fire cadence/ammo/active duration and Head Hunter piercing/falloff/active charge data.
- [ ] Replace Continuous Fire burst feel with one-shot-per-tick continuous cadence, 400 manual ammo, and a 40 bullets-per-second active barrage for 2 seconds plus heat duration.
- [ ] Replace Head Hunter automatic burst with fast piercing shots, double elite damage, triple boss damage, up to 10 enemies, and 10% damage falloff per pierced target.
- [ ] Preserve manual weak-point multiplier scaling from `5x` to `10x` with heat.
- [ ] Add the 1-second active charge gate before firing the infinite piercing shot.

### Task 5: Rotating Blade Multi-Blade Timing And Atomic Dash

**Files:**
- Modify: `Assets/Scripts/Weapon/Types/RotatingBladeWeapon.cs`
- Test: `Assets/Tests/Editor/WeaponUpgradeMathTests.cs`

- [ ] Add tests for Multi-Blade staged swing/thrust timing metadata and Atomic Sharpness active dash parameters.
- [ ] Space Multi-Blade manual swings and active thrusts by `0.1s` instead of applying all hits instantly.
- [ ] Only apply knockback on the final Multi-Blade manual swing.
- [ ] Replace Atomic Sharpness active thrust with a short dash-style damage line using 150% swing damage, no knockback, and reset-on-hit-ready behavior for sandbox verification.

### Task 6: Asset Data And Verification

**Files:**
- Modify: `Assets/ScriptableObjects/WeaponSO/*.asset`
- Modify: `Assets/Scripts/Weapon/Testing/SO/*.asset` only when sandbox tuning needs to match production.
- Test: `Assets/Tests/Editor/SandboxWeaponUpgradeDataTests.cs`

- [ ] Configure production `WeaponSO` path names and manual ammo overrides to match the spec.
- [ ] Keep sandbox path data aligned with production names and ammo overrides.
- [ ] Run runtime/editor builds and available Unity edit-mode tests.
- [ ] Update manual test plan with any mechanics that remain intentionally simplified.
