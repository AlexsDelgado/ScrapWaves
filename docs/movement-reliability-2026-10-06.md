# Movement reliability and response pass

Implemented on BugFrank from `f754eb5ce1a5b3a26db64072f50b99bed473fb69`. This takes inspiration from responsive steering and reliable directional evasion, without claiming Returnal's proprietary tuning. Base movement remains 6.8 m/s. Existing weapons, dash charges, slide prerequisites, pads, gravity, jump-height progression and damage rules remain.

## Actual changes and in-game checks

| Change | Old → new | Test in GameplayScene | Expected result |
|---|---|---|---|
| Normal steering | Force acceleration plus an early cap → bounded movement toward input velocity | Hold W, release; repeat W→S and W→D, including diagonals | Exact 6.8 normal cap; quicker direction changes and a short stop without rebound |
| Independent response rates | Speed-dependent 75→26.25 acceleration and 10 release braking → 75 acceleration, 90 reversal, 35 release braking, all m/s² | Repeat short positioning movements around pickups/cover; compare stopping defaults 25/35/45 in Inspector | Stopping and reversal can be tuned independently; released velocity reaches zero |
| Directional dash | Old momentum + directional boost → desired direction × (movement stat + dash stat) | While running W, dash S/A/D and diagonals. Repeat with ExtraSpeed | Requested escape direction is reliable, including backward under ×2/×4 buffs |
| Dash strength/progression | Baseline forward roughly 17.125, backward 2.875 → 16.8 in every input direction | Compare equal-duration dashes in each direction | Equal intended travel absent collisions. At movement stats 13.6/27.2, dash is 23.6/37.2. No idle-input fallback was added |
| Recovery | Expiration hard-clamped speed → bounded overspeed recovery at 35 m/s² | Dash with keys released/held; receive a charger shove, then recover | No one-tick velocity cliff when grace expires. Ground dash recovery now uses 35 rather than 3 m/s² braking |
| Ctrl cleanup | Release could be lost during locks → release/reconciliation independent of action entry | Release Ctrl while stunned, charging a weapon, on a pad, in hit-stop or menu pause | Crouch/slide ends; no need to press Ctrl again |
| Jump forgiveness | No buffer/coyote → 120 ms request buffer and 100 ms coyote | Press Space slightly before landing and just after leaving an edge | A nearby valid jump is accepted once; very early/late requests expire |
| Air/duplicate jump | Additive upward impulse and reusable stale ground → fixed stat-derived launch speed and consumed eligibility | Use an air-jump passive early/late in ascent; rapidly tap Space on takeoff | Air jump has repeatable vertical launch speed; no duplicate ground launch or refill while ascending |
| Combat hit-stop input | Early return lost action edges → unscaled 120 ms jump/dash requests, consumed in physics | Tap Space/Shift during a brief combat impact pause | Action executes on resume if still valid. Menu pause discards requests; stun/pad lock still blocks new actions |
| Support | Centre ray/Everything → lower-capsule sphere sweep, ≤50° support normals, Default+Terrain mask, explicit enemy/self rejection | Jump on 30°/45° slopes and near a ledge; stand on enemy bodies | Supported capsule remains grounded; enemy bodies do not replenish ground resources. Uphill velocity is not confused with jumping |
| Slide landing | Ctrl landing ignored entry speed → same entry predicate as normal slide | Land with Ctrl at zero/low speed, then repeat after dash | No zero-speed slide event pulse. Slide remains a momentum/dash extension: ordinary 6.8 run is below the retained 10.2 threshold |
| Suction | Force submitted per render Update → once per Destroyer FixedUpdate | Compare the same suction at 30/60/120/240 FPS | Pull strength follows authored 10 m/s² rather than increasing with FPS |
| Camera basis | Previous rendered/shaken Transform → current unshaken gameplay orientation, camera order −150 before motor −100 | Turn quickly while moving/dashing; compare with weapon feedback/shake | Movement intent and reticle use the same current orbit basis; yaw/roll cosmetic impulses do not steer the motor |
| Camera obstruction | Everything, hard distance snap, unchecked 12-hit capacity → qualified environment, immediate safe inward movement, 8 m/s outward restoration, saturation/overlap fallbacks | Strafe near walls and a crowd, move out of cover, put the shoulder anchor against a pillar | Enemies no longer zoom the camera; outward return is smooth; inward correction stays immediate. Cosmetic position shake is collision-limited |
| Rigidbody facing | Direct Transform writes → Rigidbody.MoveRotation | Strafe/fire/turn at 60/120/144 FPS | Physics owns interpolated facing; no deliberate change to weapon aim ownership |
| Body facing | 0.12 s / 25° strafe → 0.08 s / 10° | Compare side movement with/without firing | Smaller gait-direction mismatch and quicker ordinary turning; aim speed remains 720°/s |
| Animation scale/ascent | Gait reference ignored root scale; Fall after 0.2 s → root-scale stride calibration and Jump held through ascent | Observe feet at main scale 1.2; jump while moving | Playback matches scaled stride more closely; ascent does not switch prematurely to Fall |
| Moving landing | Moving land skipped all feedback → brief 4° upper-body settle over the existing 0.22 s landing window | Land while continuing W and firing | Small visible settling cue without a motor lock, displaced feet or changed root position |

## Preserved behavior and intentional changes

- Input dash still consumes one charge, lasts 0.25 s and regenerates in 2 s ground/4 s air. Raw recharge transition semantics are unchanged. Changing additive momentum is intentional and the directional-dash integration assertion is updated.
- Weapon dash retains its explicit speed/duration and does not consume input-dash charges. Setting requested planar velocity directly makes weapon/input dash launch consistent; vertical velocity is preserved.
- Knockback impulse magnitudes and the 0.3 s reduced-friction window remain. Afterward, recovery is bounded rather than hard-capped. Continuous pull remains a physical acceleration.
- Slow affects the normal movement target; dash/slide retain their previous slow-cap bypass. Movement/jump/dash stat assets and power-up formulas are unchanged.
- Pads retain ballistic velocity, flight input lock, 0.45 s ground grace and the landing dead stop. Landing no longer creates a zero-speed Ctrl slide. No cinematic work was copied or modified.
- Crouch retains its full-height collider; it now has an explicit 0.5 normal-speed multiplier (3.4 m/s instead of the old approximately 3.487 equilibrium). This is a deliberate consistency adjustment, not new traversal crouch.
- Slide still has no steering and retains its speed prerequisite. No sprint, invulnerability, wall-running or new movement ability was added.
- Non-freezing weapon charge locks retain the existing 10 m/s squared grounded coasting friction. Test by running, starting a non-freezing charge and releasing movement: velocity decays without a stun pose; a freeze-style charge still holds/restores its captured momentum.
- Dash interruption emits its end event; runtime friction material is released on destruction. No enemy-registry/performance rewrite was made without a profile.
- The testing camera's main-game preset now matches the scene's existing pivot 1.5, shoulder 1.0 and pitch limits -70/+70. Compare the same aim/strafe exercise in the weapon sandbox and GameplayScene; their orbit geometry should agree. Main-scene FOV remains 90.

## Validation and limits

Focused fixtures: `PlayerMovementReliabilityTests`, `MovementCameraReliabilityTests`, `MovementPresentationReliabilityTests`, `MovementRuntimeReliabilityTests`. The runtime fixture enters Play Mode and drives the actual Input System, Update/FixedUpdate, Rigidbody/interpolation and floor contacts; direct fixtures also query actual Unity physics geometry. Existing directional-dash, aim, pause, animation, weapon, pad and collision regressions should run against the same final snapshot.

Validation is performed in a temporary project copy, avoiding contention with user/cinematic editors and asset writes in the original checkout. Results and exact limitations are recorded after execution below.

Unity version: 6000.3.13f1. Final graphics-enabled EditMode suite, including native Play Mode transitions: **1,025 cases, 1,000 passed, 25 failed, none skipped**. All **64 new reliability cases passed**. The unchanged HEAD comparison ran 961 cases: 937 passed and 24 failed. All 24 baseline failures remain in the candidate. The additional failure is `PassiveItemTestingControllerTests.HealthAndShieldHelpers_ProvideARepeatableDefensiveScenario`, a challenge-tracker initialization null reference. Running that case alone on unchanged HEAD also failed with the identical stack at ChallengeProgressTracker.cs:69, PlayerHealth.cs:299 and PassiveItemTestingController.cs:354. Its whole-suite pass/fail depends on fixture/global initialization order; no unrelated production fix was made. The full suite is not green.

The native player-loop fixture instantiates the player prefab at the scene's 1.2 scale, uses actual Input System updates, counts native FixedUpdate callbacks and simulates floor contact with Rigidbody interpolation enabled. It measured **6.8 m/s running, 0.5940 m stopping travel with 0.02 s physics steps, and -37.2 m/s backward dash under a 27.2 movement-speed stat**. Deterministic fixtures also cover eight dash directions at three movement speeds; capped motion at multiple timesteps; no release sign reversal; stopping defaults 25/35/45; bounded recovery; Ctrl cleanup; coasting charge; hit-stop versus UI pause; buffers/coyote/air jumps; slope and off-centre support; ballistic pad semantics; suction submission cadence; camera collision saturation and overlaps; scale-adjusted playback; moving landing; and ascent state.

Final publication validation combined this pass with the approved flamethrower Q presentation and refined scrappunk geysers. The graphics-enabled aggregate ran **1,039 cases: 1,015 passed, 24 failed, none skipped**. All **64 movement reliability cases**, **10 directional-dash cases**, **20 Q cases** and **8 geyser/pad cases** passed. Every final failed case is in the baseline-failure table below; the timing-sensitive title-screen open-animation case passed this time. Windows Standalone player-script compilation passed again with **31 assemblies**. The first combined run exposed four mortar failures caused by copied map solids surviving the geyser test's Play Mode exit; an empty-scene teardown removed those failures in the final run without changing production gameplay. Evidence is under `.utmp/publication-2026-10-06/`. The repository's full suite remains red.

A first headless aggregate attempt crashed in an existing animation render test because it creates RenderTextures; it produced no completed aggregate result. Validation was rerun with D3D11 rendering. Windows Standalone player-script compilation passed: 31 assemblies, Unity exit code 0. A script compilation is not a packaged build or a human playtest.

The first full-suite copy retained the original company/product identity. Existing `UserSettingsServiceTests` delete `ScrapWaves.UserSettings.v1.Data`, so saved Unity Editor sensitivity/audio/accessibility settings may have reset. No previous-value backup exists. Subsequent runs use the separate product identity `Dumpster Fire Movement Validation`. Original project settings and the unrelated font edit were not changed by this work.

No subjective playtest is implied by automated tests. Main-scene traversal, crowd profiling, real pad routes and visual comparison still require Fran's playtest. Ground support does not add a step solver or foot IK. The camera cannot guarantee a clear viewpoint if the entire orbit is embedded in geometry. Extreme speed buffs can still exceed the authored 2.5× gait playback ceiling.

## Full-suite failure comparison

These existing regressions remain outside this movement pass. "Yes" means the identical case failed on unchanged HEAD in the comparison run; the defensive-item case additionally failed in isolation on unchanged HEAD.

| Case | Failed on baseline | Failure |
|---|---|---|
| ActiveAbilityAmmoTests.SpendAbilityAmmo_CanDriveAmmoToZero | Yes | System.NullReferenceException : Object reference not set to an instance of an object |
| AutomaticCannonPresentationTests.AutomaticBurst_DelayedRoundsTrackTargetFromCurrentMuzzlePosition | Yes | Expected: True But was: False |
| AutomaticCannonPresentationTests.CannonImpactFeedback_DetachesFromMuzzleAndUsesColliderContactPoint | Yes | System.Reflection.TargetInvocationException : Exception has been thrown by the target of an invocation. ----> System.NullReferenceException : Object reference not set to an instance of an object |
| AutomaticCannonPresentationTests.CannonProjectile_EmitsAfterSuccessfulSpawnAndConfirmedImpactThenResets | Yes | System.Reflection.TargetInvocationException : Exception has been thrown by the target of an invocation. ----> System.NullReferenceException : Object reference not set to an instance of an object |
| AutomaticWeaponMountTests.EmptyManualAmmo_UpdatesRealMountLightsAndFiresNextAutomaticRoundFromCannon | Yes | Expected: Automatic But was: Cooldown |
| EconomyBalanceTests.WeaponStatsCsv_ImportsFlamethrowerDamageBase | Yes | Expected: 25.0f +/- 0.00999999978f But was: 0.0f |
| GameplayHudAuthoringTests.AuthoringAndAwake_PreserveExistingObjectsAndAuthoredPresentation(PauseMenuUI) | Yes | Expected is <System.Int32[124]>, actual is <System.Linq.Enumerable+SelectArrayIterator`2[UnityEngine.Transform,System.Int32]> Values differ at index [24] Expected: -53524 But was: -54516 |
| GameplayHudAuthoringTests.PauseMenu_AwakeUsesSerializedReferencesAfterAuthoredChildrenAreRenamed | Yes | Expected: 124 But was: 151 |
| PassiveItemTestingControllerTests.HealthAndShieldHelpers_ProvideARepeatableDefensiveScenario | Yes, isolated baseline | System.NullReferenceException : Object reference not set to an instance of an object |
| PlayerStatConsumerTests.ApplyRegeneration_HealsAfterDamageDelayUsingHealthRegeneration | Yes | System.NullReferenceException : Object reference not set to an instance of an object |
| PlayerStatConsumerTests.TakeDamage_ReducesIncomingDamageByDamageResistance | Yes | System.NullReferenceException : Object reference not set to an instance of an object |
| PlayerStatConsumerTests.WeaponDamageApplier_HealsPlayerFromLifestealAfterSuccessfulDamage | Yes | System.NullReferenceException : Object reference not set to an instance of an object |
| RearThreatAuthoringTests.PlayerScene_HasOneAuthoredRearThreatIndicatorUnderUi("Assets/Scenes/GameplayScene.unity") | Yes | Expected: not null But was: null |
| RearThreatAuthoringTests.PlayerScene_HasOneAuthoredRearThreatIndicatorUnderUi("Assets/Scenes/Testing/WeaponTestingSandbox.unity") | Yes | Expected: not null But was: null |
| RearThreatAuthoringTests.PlayerScene_HasOneAuthoredRearThreatIndicatorUnderUi("Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity") | Yes | Expected: not null But was: null |
| RocketLauncherPresentationTests.AuthoredRocketExplosion_EmitsImpactDamageAndKineticStatusAtDetonation | Yes | System.Reflection.TargetInvocationException : Exception has been thrown by the target of an invocation. ----> System.NullReferenceException : Object reference not set to an instance of an object |
| RotatingBladePresentationTests.ScrapVfxShader_ExposesParticleMainTextureProperty | Yes | Expected: not null But was: null |
| SandboxWeaponUpgradeDataTests.DashCharges_BaseValueMatchesSpec | Yes | Expected: 0 But was: 1.0f |
| TitleScreenControllerTests.EnabledBuildSettingsOrder_MatchesProductionAndTestingDestinations | Yes | Expected is <System.String[4]>, actual is <System.String[5]> Values differ at index [4] Extra: < "Assets/Scenes/Testing/test_balance.unity" > |
| TitleScreenScreenStackTests.OpenSettings_BlocksInvisibleControlsUntilAnimationCompletes | Yes | The authored open animation did not complete in the allotted frames. Expected: False But was: True |
| WeaponUpgradeEffectTests.ExplosionRadiusVfx_CanSpawnWithCustomColor | Yes | Expected: 0.100000001f +/- 0.00100000005f But was: 0.101960786f |
| WeaponUpgradeEffectTests.ExplosiveProjectile_DetonatesWhenFastMovementSweepsThroughGround | Yes | Expected: property Length equal to 1 But was: 2 |
| WeaponUpgradeEffectTests.PlayerMovement_MomentumPreservingChargeLock_CanCoastWithoutStunFeedback | Yes | Expected: greater than 0.0f But was: 0.0f |
| WeaponUpgradeEffectTests.RotatingBladeAtomicSharpnessManual_ShowsDarkPurpleRadialSlashWithoutUpgradeStreaks | Yes | Expected: less than or equal to 0.150000006f But was: 0.156862751f |
| WeaponUpgradeEffectTests.RotatingBladeMultiBladeManual_ShowsOnePeachSwishPerSwordWithoutVerticalSwordOrUpgradeStreaks | Yes | Expected: 0.680000007f +/- 0.00200000009f But was: 0.717647076f |
