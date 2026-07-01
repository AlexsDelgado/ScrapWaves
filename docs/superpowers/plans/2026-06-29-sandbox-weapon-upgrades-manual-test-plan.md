# Sandbox Weapon Upgrades Manual Test Plan

**Goal:** Verify every sandbox weapon upgrade path, mode, heat state, side effect, and target type without missing a branch.

**Scope:** This is a manual Unity sandbox QA checklist for the `WeaponSystem` branch. It verifies the sandbox progression data and runtime upgrade behaviors only. It does not verify production material costs, crafting, save/load, or permanent weapon progression.

---

## Preflight

- [ ] Open Unity on branch `WeaponSystem`.
- [ ] Open `Assets/Scenes/WeaponTestingSandbox.unity`.
- [ ] Clear the Unity Console.
- [ ] Enter Play Mode.
- [ ] Confirm the sandbox debug UI is visible.
- [ ] Confirm damage/status/kill metrics update when a dummy is hit.
- [ ] Confirm no Console errors appear during scene startup.
- [ ] Optional automated safety net before manual QA:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\franc\OneDrive\Documentos\GitHub\ScrapWaves' -runTests -testPlatform editmode -testResults 'C:\Users\franc\OneDrive\Documentos\GitHub\ScrapWaves\TestResults\manual-preflight-editmode.xml' -logFile 'C:\Users\franc\OneDrive\Documentos\GitHub\ScrapWaves\TestResults\manual-preflight-editmode.log'
```

Expected automated result: all editmode tests pass.

---

## Universal Matrix

Run this matrix for every weapon:

| Done | Weapon | Level | Path | Heat | Targets | Modes |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Automatic Cannon | 1 | None | 0% | Normal single, group | Automatic, Manual, Active |
| [ ] | Automatic Cannon | 6 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Automatic Cannon | 6 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Automatic Cannon | 10 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Automatic Cannon | 10 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Rocket Launcher | 1 | None | 0% | Normal single, group | Automatic, Manual, Active hold/release |
| [ ] | Rocket Launcher | 6 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active hold/release |
| [ ] | Rocket Launcher | 6 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active hold/release |
| [ ] | Rocket Launcher | 10 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active hold/release |
| [ ] | Rocket Launcher | 10 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active hold/release |
| [ ] | Flamethrower | 1 | None | 0% | Normal single, group, moving | Automatic, Manual, Active |
| [ ] | Flamethrower | 6 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group, moving | Automatic, Manual, Active |
| [ ] | Flamethrower | 6 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group, moving | Automatic, Manual, Active |
| [ ] | Flamethrower | 10 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group, moving | Automatic, Manual, Active |
| [ ] | Flamethrower | 10 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group, moving | Automatic, Manual, Active |
| [ ] | Mortar | 1 | None | 0% | Normal single, group | Automatic, Manual, Active |
| [ ] | Mortar | 6 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Mortar | 6 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Mortar | 10 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Mortar | 10 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Rotating Blade | 1 | None | 0% | Normal single, group | Automatic, Manual, Active |
| [ ] | Rotating Blade | 6 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Rotating Blade | 6 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Rotating Blade | 10 | PathA | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |
| [ ] | Rotating Blade | 10 | PathB | 0%, 50%, 100% | Normal, Elite, Boss, group | Automatic, Manual, Active |

For every row above, also check:

- [ ] No Console errors or warnings caused by firing.
- [ ] Damage metrics increase when expected.
- [ ] Ammo decreases in manual and active use.
- [ ] Active cooldown starts after active ability.
- [ ] Heat changes the expected values without breaking firing.
- [ ] Dummies can still respawn/reset after testing.
- [ ] Weapon can be switched away from and back to without stale VFX or targeting markers.

---

## Automatic Cannon

### Baseline

- [ ] Level 1 Path None automatic fires normal bursts.
- [ ] Manual fires a line burst and consumes ammo.
- [ ] Active ability spends active ammo and fires the expected burst.
- [ ] Heat increases threshold damage as before.

### Path A: Continuous Fire

- [ ] Level 6 PathA automatic burst count is higher than baseline.
- [ ] Level 10 PathA automatic burst count is higher than Level 6 PathA.
- [ ] Manual firing cadence feels faster than baseline.
- [ ] At 100% heat, manual cadence is faster than at 0% heat.
- [ ] Active ability still fires normally and does not get stuck.

### Path B: Head Hunter

- [ ] Normal dummy receives increased PathB damage.
- [ ] Elite dummy receives more damage than normal dummy.
- [ ] Boss dummy receives more damage than elite dummy.
- [ ] Active ability fires the piercing Head Hunter shot.
- [ ] At 0% heat, active weak-point scale is visibly high.
- [ ] At 100% heat, active weak-point scale is higher than 0% heat.
- [ ] WeakPoint child exists on sandbox dummies.
- [ ] Hitting or targeting weak-point style behavior does not throw Console errors.

Note: current Head Hunter active damage applies a weak-point multiplier to line-hit targets. It does not require physically hitting the dummy's `WeakPoint` collider.

---

## Rocket Launcher

### Baseline

- [ ] Level 1 Path None automatic fires rocket volleys.
- [ ] Manual fires one aimed rocket and consumes ammo.
- [ ] Active hold starts target lock.
- [ ] Active hold adds target locks over time.
- [ ] Active release fires locked rockets.
- [ ] Active cancel or empty target state clears markers.

### Path A: Kinetic Explosion

- [ ] Explosion radius looks larger than baseline.
- [ ] Explosion falloff feels less severe than baseline.
- [ ] Hit dummies show or behave as `Vulnerable`.
- [ ] Follow-up damage on a vulnerable dummy is higher than before vulnerability.
- [ ] Knockback is stronger than baseline.
- [ ] Active lock/release still works and clears markers.

### Path B: Fragmentation Cap

- [ ] Automatic mode fires one extra rocket versus non-PathB at the same level/heat.
- [ ] Explosion radius is smaller than PathA.
- [ ] Group targets in front of the rocket impact take extra fragmentation damage.
- [ ] Targets to the side of the rocket impact take less or no fragmentation damage.
- [ ] Manual rocket still detonates at max range if it misses.
- [ ] Active lock/release still works and clears markers.

---

## Flamethrower

### Baseline

- [ ] Automatic cone damages nearby enemies in movement/facing direction.
- [ ] Manual hose follows aim and consumes ammo over time.
- [ ] Burn ticks continue after direct flame contact.
- [ ] Active ring burst damages enemies around the player.

### Path A: Jellified Fuel

- [ ] Burn damage is higher than baseline.
- [ ] Burn duration is longer than baseline.
- [ ] Fuel puddles spawn on burned targets.
- [ ] Fuel puddles damage grouped dummies standing inside the radius.
- [ ] Level 10 puddle radius feels larger than Level 6.
- [ ] Puddles expire and do not leave permanent objects/VFX.
- [ ] Active ability creates puddles on affected targets.

### Path B: Liquid Nitrogen

- [ ] Burned moving dummy slows while `Liquid Nitrogen` is active.
- [ ] Active ability applies `Freeze` status.
- [ ] Frozen moving dummy stops moving during freeze.
- [ ] Frozen dummy resumes movement after freeze expires.
- [ ] Base burn damage still ticks while the slow/freeze status is active.
- [ ] No freeze component remains permanently after status expiry.

---

## Mortar

### Baseline

- [ ] Automatic selects random target area and launches shell.
- [ ] Manual fires toward reticle/aim point with expected travel time.
- [ ] Active barrage drops multiple shells.
- [ ] Shell collision ignores player and detonates on terrain/enemies.
- [ ] Explosion VFX appears at detonation point.

### Path A: Grapeshot

- [ ] Detonation produces visible secondary hits around the impact direction.
- [ ] Grouped dummies near grapeshot zones take extra damage.
- [ ] Active ability uses grapeshot payload.
- [ ] At higher heat, active grapeshot count feels higher.
- [ ] Base explosion still deals damage at the impact point.

### Path B: Repeat Explosions

- [ ] First explosion happens at impact.
- [ ] Second explosion happens about 2 seconds later.
- [ ] Third explosion happens about 2 seconds after the second.
- [ ] Dummies inside the repeated radius take damage each time.
- [ ] Shell object cleans itself up after final repeat.
- [ ] Active barrage shells also repeat.

---

## Rotating Blade

### Baseline

- [ ] Automatic shows one orbiting blade contact point.
- [ ] Automatic only damages when the blade contact point reaches targets.
- [ ] Manual slash hits a cone in front of player.
- [ ] Active thrust hits a thick line forward.

### Path A: Multi-Blade

- [ ] Level 6 shows/damages with 2 blade positions.
- [ ] Level 10 shows/damages with 3 blade positions.
- [ ] Manual slash covers a wider area through repeated offset swings.
- [ ] Active thrust covers a wider area through repeated offset thrusts.
- [ ] Damage still occurs only in the intended blade/slash/thrust areas.

### Path B: Atomic Sharpness

- [ ] Damage is visibly higher than baseline.
- [ ] Knockback is reduced to none or nearly none.
- [ ] Automatic damage uses Atomic Sharpness.
- [ ] Manual slash uses Atomic Sharpness.
- [ ] Active thrust uses Atomic Sharpness.
- [ ] Targets are not pushed out of range by PathB hits.

---

## Side System Checklist

- [ ] `Vulnerable` status appears or follow-up damage proves the rocket PathA damage amplifier.
- [ ] `Liquid Nitrogen` status slows moving dummies.
- [ ] `Freeze` status stops moving dummies during flamethrower active PathB.
- [ ] `Jellified Fuel` status or puddle behavior appears for flamethrower PathA.
- [ ] Dummy `WeakPoint` child exists.
- [ ] Head Hunter active damage scales with heat.
- [ ] Elite and Boss classification changes damage where expected.
- [ ] Status effects expire and do not permanently alter dummy movement or damage.

---

## Failure Log

Use this table while testing.

| Weapon | Level | Path | Heat | Mode | Target Setup | Problem | Console Error | Repro Steps |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |  |  |
|  |  |  |  |  |  |  |  |  |
|  |  |  |  |  |  |  |  |  |

---

## Pass Criteria

Manual QA is complete when:

- [ ] Every row in the Universal Matrix has been tested.
- [ ] Every weapon-specific path checklist is complete.
- [ ] Side system checklist is complete.
- [ ] No new Console errors appear.
- [ ] No weapon blocks switching, respawning dummies, or continuing the sandbox loop.
- [ ] Any failures are recorded in the Failure Log with exact reproduction steps.
