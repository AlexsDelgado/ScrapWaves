# Automatic Cannon Presentation Polish Implementation Plan

## Goal

Deliver a rendering-agnostic Automatic Cannon vertical slice with configurable, pooled fire/impact VFX, weapon audio, camera feedback, and clear presentation differences between base, Continuous Fire, and Head Hunter behavior.

## Working Rules

- Preserve gameplay behavior and balance.
- Keep presentation optional and null-safe.
- Emit feedback only after a shot or hit succeeds.
- Prefer dependency injection through the weapon factory over static global presentation events.
- Use standard URP assets and avoid experimental PSX dependencies.
- Add new focused tests instead of treating the unrelated red full-suite baseline as green.

## First-Batch Verification Record

- Existing `AutomaticCannon` tests before implementation: 32/32 passing.
- New presentation fixture: 5/5 passing.
- Combined `AutomaticCannon` tests after implementation: 37/37 passing.
- Full EditMode suite before implementation: 186/209 passing, with 23 pre-existing failures.
- Full EditMode suite after implementation: 191/214 passing, with the same 23 failures.
- Focused `WeaponUpgradeEffectTests` baseline: 60/78 passing, with 18 pre-existing failures.

## Runtime-Services Verification Record

- Presentation fixture after Tasks 4–5: 9/9 passing.
- Combined `AutomaticCannon` tests after Tasks 4–5: 41/41 passing.
- Full EditMode suite after Tasks 4–5: 195/218 passing, with the same 23 pre-existing failures.
- The full-suite asset and project-setting mutations were reverted after the run.

## Runtime-Integration Verification Record

- Combined `AutomaticCannon` tests after Tasks 6–7: 45/45 passing.
- Full EditMode suite after Tasks 6–7: 199/222 passing, with the same 23 pre-existing failures.
- Production player-prefab wiring, sandbox profile wiring, successful-spawn cues, confirmed impacts, and projectile-pool reset behavior are covered by the focused fixture.
- Full-suite test-generated balance assets and project-setting mutations were reverted after the run.

## Base-VFX Verification Record

- Combined `AutomaticCannon` tests after the base VFX slice: 52/52 passing.
- Full EditMode suite after the base VFX slice: 206/229 passing, with the same 23 pre-existing failures.
- Six inactive authored prefabs resolve through the production profile and build their renderer and particle layers during pool prewarm.
- Full-suite test-generated balance assets and project-setting mutations were reverted after the run.

## Phase 0: Establish the Test Boundary

### Task 1: Record and isolate the cannon baseline

**Files:**

- Create: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`
- Reference: `Assets/Tests/Editor/AutomaticCannonFireLogicTests.cs`
- Reference: `Assets/Tests/Editor/WeaponUpgradeEffectTests.cs`
- Reference: `Assets/Tests/Editor/WeaponUpgradeMathTests.cs`

- [x] Run the existing tests whose names contain `AutomaticCannon` and record the result.
- [x] Add a new empty presentation fixture and prove it can run independently.
- [x] Record the focused `WeaponUpgradeEffectTests` baseline of 60 passing and 18 failing.
- [x] Classify touched baseline failures as runtime defects, test defects, or unrelated failures.
- [x] Do not require the full 209-test suite to be green before beginning presentation work.

**Gate passed:** Existing Automatic Cannon mechanics are green in isolation, and the new presentation fixture is green.

## Phase 1: Presentation Contracts and Data

### Task 2: Add cue and context models

**Files:**

- Create: `Assets/Scripts/Weapon/Presentation/WeaponPresentationCue.cs`
- Create: `Assets/Scripts/Weapon/Presentation/WeaponPresentationContext.cs`
- Create: `Assets/Scripts/Weapon/Presentation/IWeaponPresentationSink.cs`
- Create: `Assets/Scripts/Weapon/Presentation/NullWeaponPresentationSink.cs`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

Define cannon cues for:

- base automatic projectile;
- base manual projectile;
- base scatter ability;
- Continuous Fire projectile;
- Continuous Fire active loop;
- Head Hunter automatic release;
- Head Hunter manual release;
- Head Hunter charge;
- Head Hunter active release;
- normal impact;
- critical impact;
- weak-point impact.

Context carries only presentation facts:

- weapon instance;
- world position and direction;
- intensity;
- ability/critical/weak-point flags;
- optional target;
- stable loop handle where required.

- [x] Prove all context models are value types or allocation-free runtime objects.
- [x] Prove the null sink accepts every cue without throwing.
- [x] Keep damage and targeting data out of presentation configuration.

### Task 3: Add data-driven presentation profiles

**Files:**

- Create: `Assets/Scripts/Weapon/Presentation/WeaponPresentationProfile.cs`
- Create: `Assets/Scripts/Weapon/Presentation/WeaponPresentationCueData.cs`
- Modify: `Assets/Scripts/Weapon/Base/WeaponData.cs`
- Modify: `Assets/Scripts/Weapon/Editor/WeaponDataEditor.cs`
- Create: `Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset`
- Modify: `Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

Each cue may configure:

- pooled VFX prefab;
- fire and impact clip variants;
- volume and bounded pitch variation;
- camera position/rotation impulse;
- duration and intensity;
- minimum replay interval;
- maximum simultaneous instances;
- loop behavior.

- [x] Resolve cue data without per-shot list scanning or allocations.
- [x] Validate duplicate cues and invalid limits in the editor.
- [x] Missing profile or cue falls back to no presentation.
- [x] The production cannon asset references its presentation profile.

**Gate passed:** Cue selection and missing-data behavior are covered by green EditMode tests.

## Phase 2: Runtime Presentation Services

### Task 4: Add pooled VFX and audio playback

**Files:**

- Create: `Assets/Scripts/Weapon/Presentation/WeaponPresentationController.cs`
- Create: `Assets/Scripts/Weapon/Presentation/WeaponVfxPool.cs`
- Create: `Assets/Scripts/Weapon/Presentation/WeaponAudioVoicePool.cs`
- Create: `Assets/Scripts/Weapon/Presentation/PooledWeaponVfx.cs`
- Modify: `Assets/Scripts/Audio/AudioManager.cs`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

- [x] Prewarm cue pools from the configured profile.
- [x] Reuse VFX objects after their lifetime expires.
- [x] Reuse a bounded set of AudioSources for overlapping weapon transients.
- [x] Apply the existing global SFX volume to weapon voices.
- [x] Rate-limit repeated fire and impact cues without affecting gameplay.
- [x] Stop and release all loops on disable and scene unload.
- [x] Fail quietly when a pool is exhausted.

### Task 5: Add additive camera impulse support

**Files:**

- Modify: `Assets/Scripts/ThirdPersonCamera.cs`
- Modify: `Assets/Scripts/Weapon/Presentation/WeaponPresentationController.cs`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

- [x] Add a presentation-only impulse API that never changes gameplay aim.
- [x] Blend position and rotation impulses after normal camera placement.
- [x] Support a master `0..1` camera-feedback scale.
- [x] Rate-limit normal sustained-fire impulses.
- [x] Clear impulses when the camera or presentation controller disables.

**Gate passed:** Pools reuse instances, audio loops clean up, and camera feedback can be disabled without changing shots.

## Phase 3: Safe Runtime Integration

### Task 6: Inject presentation into weapon behavior

**Files:**

- Modify: `Assets/Scripts/Weapon/Managers/WeaponManager.cs`
- Modify: `Assets/Scripts/Weapon/Managers/WeaponBehaviourFactory.cs`
- Modify: `Assets/Scripts/Weapon/Base/WeaponBehaviourBase.cs`
- Modify: `Assets/Prefabs/player.prefab`
- Modify: `Assets/Scenes/WeaponTestingSandbox.unity`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

- [x] `WeaponManager` supplies a presentation sink to created behaviors.
- [x] Tests and scenes without a controller receive the null sink.
- [x] Base projectile fire helpers report spawn success.
- [x] Existing weapon call sites may ignore the new success result without behavior changes.
- [x] No presentation call occurs for invalid aim, missing pool, empty ammo, or failed spawn.

### Task 7: Carry presentation context to real impacts

**Files:**

- Modify: `Assets/Scripts/Weapon/Projectiles/ProjectilePool.cs`
- Modify: `Assets/Scripts/Weapon/Projectiles/Projectile.cs`
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

- [x] Add a projectile spawn overload that returns the configured projectile.
- [x] Attach a compact presentation context to cannon projectiles.
- [x] Emit impact feedback only after confirmed damage or world impact.
- [x] Return presentation state to defaults when pooled projectiles release.
- [x] Direct Head Hunter impacts use the same sink and cue model.
- [x] Critical and weak-point flags select their intended impact cues.

**Gate passed:** Fire and impact feedback corresponds to successful runtime events, including delayed piercing impacts.

## Phase 4: Automatic Cannon Vertical Slice

### Task 8: Author base cannon VFX

**Files:**

- Create: `Assets/Prefabs/Vfx/Weapons/AutomaticCannon/`
- Create: `Assets/Materials/Vfx/Weapons/AutomaticCannon/`
- Modify: `Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset`
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`

Author pooled standard-URP effects for:

- compact automatic muzzle flash;
- heavier manual muzzle flash;
- narrow tracer;
- normal impact spark;
- critical impact spark;
- base active start flash.

- [x] Automatic fire reads as a three-shot burst.
- [x] Manual fire reads as a heavier five-shot volley.
- [x] Base active uses one ability-level start cue and bounded per-projectile feedback.
- [ ] Effects remain legible without bloom and against bright or dark backgrounds.

### Task 9: Integrate temporary audio and recoil tuning

**Files:**

- Modify: `Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset`
- Modify: `Assets/Scenes/GameplayScene.unity`
- Modify: `Assets/Scenes/WeaponTestingSandbox.unity`

- [x] Use `shoot.wav` only as a temporary integration source.
- [x] Configure separate relative weight for automatic, manual, and active cues.
- [x] Rate-limit burst and sustained-fire audio.
- [x] Tune camera impulses according to the design feedback hierarchy.
- [x] Verify global SFX volume affects all weapon cues.

### Task 10: Polish Continuous Fire

**Files:**

- Create: Continuous Fire muzzle/tracer/sustained VFX under the cannon VFX folder
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Modify: `Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

- [ ] Start one loop when sustained fire begins.
- [ ] Update heat-dependent presentation without restarting the loop.
- [ ] Stop the loop on release, ability end, weapon cycle, cancellation, disable, and scene unload.
- [ ] Cap normal camera impulses during high-rate fire.
- [ ] Distinguish the path through cadence, shape, and audio as well as color.

### Task 11: Polish Head Hunter

**Files:**

- Modify or replace: `Assets/Scripts/Weapon/Projectiles/HeadHunterChargeVfx.cs`
- Create: Head Hunter tracer, puncture, weak-point, and release VFX under the cannon VFX folder
- Modify: `Assets/Scripts/Weapon/Types/AutomaticCannonWeapon.cs`
- Modify: `Assets/ScriptableObjects/WeaponPresentation/AutomaticCannonPresentation.asset`
- Modify: `Assets/Tests/Editor/AutomaticCannonPresentationTests.cs`

- [ ] Charge presentation follows the current aim direction.
- [ ] Charge cleanup is safe in play mode and EditMode tests.
- [ ] Piercing impacts play in projectile travel order.
- [ ] Weak-point feedback occurs at confirmed impact time.
- [ ] Active release is the strongest single cannon cue.
- [ ] Distinguish the path through precision and impact shape as well as color.

**Gate:** Every mode and path meets the presentation matrix without changing mechanic test expectations.

## Phase 5: Verification and Approval

### Task 12: Automated verification

- [x] Run `AutomaticCannonPresentationTests`.
- [x] Run `AutomaticCannonFireLogicTests`.
- [x] Run Automatic Cannon cases from `WeaponUpgradeMathTests`.
- [x] Run Automatic Cannon cases from `WeaponUpgradeEffectTests`.
- [x] Run the full EditMode suite and compare against the recorded 186/209 baseline.
- [x] Confirm test-generated tracked changes can be isolated and reverted after the run.

### Task 13: Sandbox review

Use:

- base cannon at low and full heat;
- level-six Continuous Fire at low and full heat;
- Continuous Fire active at full heat;
- level-six Head Hunter automatic and manual;
- Head Hunter weak-point shots;
- Head Hunter active through a target group;
- rapid weapon cycling during loops and charge;
- normal, elite, and boss dummies.

- [ ] Complete every item in the design acceptance checklist.
- [ ] Capture before/after footage with the standard URP renderer.
- [ ] Record profiler object-allocation and pool-exhaustion observations.
- [ ] Collect tuning notes separately from mechanic or balance requests.

## Manual Review Checkpoint

Open `Assets/Scenes/WeaponTestingSandbox.unity`, enter Play mode, and use the right-side debug panel:

1. Keep Slot 1 on Automatic Cannon and hold left mouse in manual mode. The five-shot volley should feel heavier than the automatic burst.
2. Choose **Force Automatic Mode**. The recurring three-shot burst should be readable without camera judder or clipped audio.
3. Set Slot 1 to Level 6, Path A. Compare normal fire at 0% and 100% heat, then return to manual mode and press Q. Continuous Fire should be the lightest per-shot feedback and remain comfortable at its highest cadence.
4. Set Slot 1 to Level 6, Path B. Compare automatic and manual releases, then hold and release Q through a target group. Head Hunter active should be the strongest single recoil cue.
5. Use the SFX slider in the normal gameplay scene to confirm the weapon audio follows the existing global setting.

The current audio deliberately reuses `shoot.wav`. Base-cannon VFX are now assigned; Path A and Path B authored VFX remain for the next slice.

### Base-burst readability adjustment

The first manual review identified that one report per three- or five-projectile event made the base cannon read like a shotgun. Temporary audio now plays on every successful base projectile cue, while the event-level cue retains the heavier recoil layer without doubling the first report. Shot count, damage, spread, intra-burst timing, and overall cadence remain unchanged.

- Focused `AutomaticCannon` verification after the adjustment: 46/46 passing.

### Base-burst geometry adjustment

The second manual review showed that the remaining issue was spatial rather than temporal. Production and sandbox base-cannon tuning now use zero whole-burst accuracy spread and zero per-projectile scatter. Automatic and manual projectiles therefore share one direction and use line spacing to form a collinear sequence instead of a shotgun fan. The sandbox also disables `RunStartWeaponChoice`, preventing the production first-weapon selection from opening over the testing controls.

- Focused `AutomaticCannon` verification after the geometry and sandbox UI fixes: 49/49 passing.

### Moving-burst aim correction

Delayed rounds no longer reuse the first frame's cached direction. Manual bursts refresh from the current aim direction before each shot, and automatic bursts recompute the direction from the current muzzle position to the live tracked target. A stable whole-burst accuracy sample is retained, so any future configured accuracy offset moves with the aim axis without reintroducing per-projectile shotgun scatter.

- Focused `AutomaticCannon` verification including moving-manual and moving-automatic burst coverage: 52/52 passing.

### Base-VFX review checkpoint

The first renderer-independent visual set was assigned to the base cannon, then replaced after manual review found the line-only geometry too prototype-like. The current second pass uses:

1. An authored irregular hot-core muzzle bloom layered over the narrow tracer.
2. Directional tapered spark particles with distinct automatic/manual counts and speed.
3. A low-opacity residual smoke/heat layer that outlives the sharp flash.
4. Reduced line geometry as a supporting accent instead of the dominant visual language.
5. Normal, critical, weak-point, and active cues use separate scale, debris count, and timing.
6. Every effect remains a pooled inactive prefab; all line and particle children are constructed during prewarm.

- Focused presentation verification after the layered pass: 20/20 passing.
- Complete focused `AutomaticCannon` verification after the layered pass: 52/52 passing.

The next manual pass found that delayed line-burst projectiles were also supplying their progressively offset spawn positions to the muzzle VFX. Shot cues now anchor to the live muzzle position while projectile spacing remains unchanged. Primary flash scale and rotation are deterministic; only secondary spark and residue motion varies.

- Muzzle-anchor presentation verification: 20/20 passing.
- Complete focused `AutomaticCannon` verification after the anchor correction: 52/52 passing.

### Deterministic muzzle reset checkpoint

The layered per-round effect was still structurally noisy: every round created a flash, sparks, residue, and a static muzzle line, and several long-lived instances overlapped during each burst. Stage 1 now provides a deliberately minimal baseline:

1. Each successful round creates exactly one flash and no shot-time line, spark, or residue layers.
2. The flash emission shape is disabled, so the particle begins at local origin without positional variance.
3. The pooled effect is parented to the live muzzle transform and follows player movement until release.
4. Automatic and manual flash lifetimes are 0.03 and 0.04 seconds, both below the 0.05-second intra-burst interval.
5. Shot cue pool duration is 0.05 seconds; impact and active effects remain unchanged for this checkpoint.

- Deterministic muzzle presentation verification: 20/20 passing.
- Complete focused `AutomaticCannon` verification after the reset: 52/52 passing.

Manual review should now ignore missing projectile trails and judge only the deterministic muzzle baseline: three flashes for automatic, five for manual, all from the same live barrel point while stationary, moving, and turning. Projectile-attached tracers remain deliberately blocked on this approval.
