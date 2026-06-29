# Sandbox Weapon Upgrades Design

## Goal

Implement the upgraded versions of the existing weapons and validate them in the weapon testing sandbox before connecting them to crafting, materials, inventory, or the production level-up offer flow.

This pass should make level/path weapon behavior testable and playable in `WeaponTestingSandbox`, while leaving the main run progression architecture stable.

## Scope

Included:

- Use the existing `WeaponInstance.Level` and `WeaponInstance.SelectedPath` fields as the runtime source of truth.
- Use the existing sandbox controls that apply weapon level and path.
- Populate or verify sandbox weapon upgrade data so level and path math has real values.
- Implement missing path-specific behavior inside the weapon behavior classes.
- Add focused editor tests for deterministic math and helper behavior.
- Verify visual and combat behavior manually in `Assets/Scenes/WeaponTestingSandbox.unity`.

Deferred:

- Enemy material drops.
- Material inventory.
- Crafting stations.
- Advanced tinkering costs.
- Refusing a path and increasing future cost.
- Main-run weapon upgrade offer flow.
- `WeaponLevelUpHandler` path selection.
- Save/load of upgraded weapons.

## Current Architecture Fit

The current architecture can support this pass with small, local changes.

`WeaponData` already stores:

- Base weapon tuning.
- `LevelData`.
- `PathA`.
- `PathB`.
- Weapon-specific tuning payloads.

`WeaponInstance` already stores:

- Equipped weapon data.
- Current level.
- Selected path.
- Current state and ammo.

`WeaponMath` and `WeaponDamageResolver` already read weapon level and path data for:

- Damage multiplier.
- Attack-rate multiplier.
- Manual ammo.

`WeaponTestingSandboxManager` already has `ApplyWeaponLevelAndPath`, which makes it the right first integration point. The sandbox can select level 1-10 and `None`, `PathA`, or `PathB` without changing production progression.

## Architecture

### Runtime Data

No new top-level progression model is needed for this pass.

Weapon level and path stay on `WeaponInstance`. Each weapon behavior reads `Runtime.Level`, `Runtime.HasAdvancedPath`, and `Runtime.SelectedPath`.

Sandbox weapon assets remain the first source of upgrade tuning. Production weapon assets may keep neutral or empty upgrade data until the real crafting/material system needs them.

### Weapon Behavior Boundary

Weapon-specific path logic belongs in the matching weapon class:

- `AutomaticCannonWeapon`
- `RocketLauncherWeapon`
- `FlamethrowerWeapon`
- `MortarWeapon`
- `RotatingBladeWeapon`

Shared multiplier logic stays in:

- `WeaponMath`
- `WeaponDamageResolver`

If a path needs a reusable status effect or projectile effect, create a small focused component under `Assets/Scripts/Weapon/Projectiles` or a nearby weapon folder instead of expanding the manager classes.

### Sandbox Boundary

The sandbox remains the only player-facing integration for this pass.

Existing sandbox controls should continue to:

- Set loadout slots.
- Set weapon level.
- Set selected path.
- Adjust heat.
- Spawn normal, elite, boss, group, moving, and knockback test targets.
- Show metrics.

Only add sandbox UI if an upgraded behavior cannot be tested with current controls.

## Weapon Upgrade Targets

### Flamethrower

Path A: `Jellified Fuel`

- Extend the current burn-focused path behavior.
- Add persistent fuel puddle behavior as a focused component that damages enemies through the existing `IDamageable` contract.
- Scale puddle duration and size by weapon level where the spec calls for level scaling.
- Keep automatic, manual, and active ability behavior testable from the sandbox.

Path B: `Liquid Nitrogen`

- Extend the current status-label behavior into a real slow/freeze status component.
- The status component should apply movement changes only through existing enemy movement components that can be safely restored; unsupported targets still receive the status timer and damage behavior without movement mutation.
- Active ability should apply the stronger freeze/slow behavior in a radius.

### Rocket Launcher

Path A: `Kinetic Explosion`

- Preserve the larger blast and lower falloff direction already present.
- Add or isolate an enemy vulnerability status for extra damage from all sources if there is a clean damage hook.
- Adjust active ability knockback behavior without changing the basic rocket targeting lifecycle.

Path B: `Fragmentation Cap`

- Preserve the smaller blast and extra rocket direction already present.
- Add cone or fragment damage behavior as an explicit projectile/impact effect, not as manager logic.
- Keep rocket lock reticle and targeting behavior compatible with the existing hold-to-lock ability.

### Mortar

Path A: `Grapeshot`

- Add airburst or post-impact projectile spread behavior in the mortar path.
- Keep `MortarShellImpact` responsible for shell travel and impact timing.
- Add any spread payload as a separate effect called by the mortar impact flow.

Path B: `Multi-Charged Shells`

- Add delayed repeated explosions from one mortar shell.
- Keep delays and repeated explosions local to shell impact behavior or a small spawned effect component.
- Avoid changing global projectile pool behavior.

### Automatic Cannon

Path A: `Continuous Fire`

- Extend the current extra-burst direction toward continuous-fire behavior.
- Keep manual fire cadence compatible with existing click/hold fixes.
- Support the level 10 capstone as a branch inside the cannon behavior.

Path B: `Head Hunter`

- Extend the current elite/boss damage and crit multiplier behavior.
- Add piercing or line-shot behavior for the path-specific active ability through a focused cannon helper, leaving ordinary projectile spawning unchanged.
- Preserve ordinary projectile spawning for base cannon behavior.

### Rotating Blade

Path A: `Multi-Blade`

- Add multiple orbiting blades based on level/path.
- Keep blade hit detection bounded by existing target collection limits.
- Manual and active multi-swing behavior should reuse the current cone/line collection helpers.

Path B: `Atomic Sharpness`

- Add faster, higher-damage, lower-knockback behavior.
- Treat the dash-style active ability as a separate implementation task because it touches player movement and invulnerability.
- If that task cannot use existing movement and invulnerability hooks safely, it should stop at a documented test failure rather than substituting unrelated behavior.

## Data Flow

1. The sandbox applies level/path through `WeaponTestingSandboxManager.ApplyWeaponLevelAndPath`.
2. The `WeaponInstance` stores the chosen level and path.
3. Each weapon behavior reads its runtime level and path every tick or ability use.
4. Shared damage, attack-rate, and ammo formulas apply `LevelData`, `PathA`, and `PathB`.
5. Weapon-specific classes apply their unique path behavior.
6. Sandbox metrics observe damage, ammo, crits, kills, knockback, status effects, and active ability use.

## Failure Handling

- If a weapon has no selected path, use base behavior.
- If a weapon is below level 6, ignore selected path and use base behavior.
- If path data is missing or neutral, shared math falls back to safe multipliers.
- If a status effect target lacks the needed movement or damage hook, skip that effect without breaking damage application.
- If a projectile pool or spawn reference is missing, preserve existing no-op behavior rather than throwing.

## Testing Strategy

Editor tests should cover deterministic pieces:

- `WeaponMath` level and path multipliers.
- `WeaponDamageResolver` level/path damage effects.
- Level below 6 ignores selected path.
- Path-specific helper calculations where they can be made pure.
- New status/effect components with direct method tests where practical.

Sandbox manual verification should cover each weapon at:

- Level 1 with no path.
- Level 6 Path A.
- Level 6 Path B.
- Level 10 Path A.
- Level 10 Path B.

For each scenario, verify:

- Automatic mode.
- Manual mode.
- Active ability.
- Heat at 0 percent, 50 percent, and 100 percent where the spec defines heat behavior.
- Normal, elite, boss, and grouped targets where relevant.
- Metrics are plausible and no console errors occur.

## Implementation Order

1. Lock down tests for shared level/path math.
2. Verify sandbox upgrade data exists and is non-neutral.
3. Implement the smallest missing behavior slice for one weapon.
4. Run editor tests and sandbox checks for that weapon.
5. Repeat per weapon.
6. Do a full sandbox regression pass across all five weapons.

The recommended weapon order is:

1. Automatic Cannon, because it already has the most path branching.
2. Rocket Launcher, because it has clear path hooks and existing lock behavior.
3. Flamethrower, because status effects need careful isolation.
4. Mortar, because repeated/airburst effects touch shell impact flow.
5. Rotating Blade, because one path may touch player movement and invulnerability.

## Risks

- Some spec effects require systems that are not fully present yet, such as enemy slow/freeze, global damage vulnerability, or player invulnerability frames.
- VFX-heavy behavior may need sandbox verification more than editor-test coverage.
- Adding path-specific behavior directly to existing weapon classes can grow those files. If a behavior needs substantial state, split it into a focused helper component.
- Production assets currently have empty upgrade data. This is acceptable for this pass as long as sandbox assets drive verification.

## Completion Criteria

This pass is complete when:

- Each existing weapon has meaningful Path A and Path B behavior in the sandbox.
- Level 1 and no-path behavior remains unchanged.
- Level/path shared math is covered by editor tests.
- Each weapon has been manually verified in the sandbox at level 6 and level 10 for both paths.
- No production crafting, material, inventory, or offer-flow changes are required to test the upgrades.
