# Larguirucho animation sources and integration

The October 16, 2026 exports from `Dumpster Fire / Art / FBX` are retained here unchanged. [source-manifest.json](source-manifest.json) records their Drive URLs, IDs, timestamps, byte counts, and SHA-256 hashes. Unity imports copies from `Assets/Art/Enemies/Chaser/Animation`.

`Larguirucho_Attack.fbx` is the canonical Generic rig: 115 bones and two skinned meshes, `Circle.001` (body) and `Cintas_Animadas` (treads). Walk has the same common hierarchy but lacks the attack-only, weighted `Drill_Spin` bone. In Unity, the imported takes span frames 1–76 for Attack (2.5 seconds) and 1–49 for Walk (1.6 seconds), at 30 fps.

The builder generates these clips without modifying either source FBX:

| State | Source and clip-relative time | Playback |
|---|---|---|
| Idle | Walk at 0 seconds | Held neutral pose |
| Walk | Entire Walk take | Loop Time on; Loop Pose off |
| Charge | Attack, 0 to 10/30 seconds | Anticipation, then held through the existing windup |
| Dash | Attack, 10/30 to 44/30 seconds | Progress follows the gameplay dash |
| Recover | Attack, 44/30 to 2.5 seconds | Progress follows the existing recovery timer |

Generated Transform curves explicitly remove planar Root travel in rig-local X/Z; disabling automatic root motion alone would leave child-bone travel unaddressed. Vertical articulation remains. Idle and Walk explicitly restore `Drill_Spin` to its canonical rest pose. Walk's tread bones cycle around the belt, so their identities do not return to their initial positions even though the visible geometry loops. Automatic Loop Pose correction is intentionally disabled.

`SimpleFollow` and `ChargerEnemyBehavior` retain ownership of movement, combat timers, collision damage, push, and stun. The root-level `ChaserAnimationDriver` follows their presentation state using separate `ChargePhase`, `DashPhase`, and `RecoverPhase` parameters plus `WalkSpeed`. Early contact enters recovery immediately. No generated clip contains gameplay animation events. Existing emergence, hit response, immediate death/despawn, pooling, and dormancy remain authoritative; dormant animation evaluation is suspended.

The integration covers `Chaser.prefab`, `Chaser (variant).prefab`, `Chaser_Elite.prefab`, and the two unpacked `TutorialWorld/1 - Charger` and `TutorialWorld/2 - Charger` actors in `GameplayScene`. Tutorial AI stays disabled and the actors remain stationary. The original `EmergenceVisual/Larguirucho` outer pose, gameplay settings, colliders, and lesson/drop references are preserved. An inner `Rig` matches the old visible height, ground contact, and center. Material mappings pair imported slot semantics with each actor's actual overrides, including custom variant materials; the new treads initially use that actor's wheel material. Hit response targets the complete cosmetic wrapper.

Skinned bounds are fitted across all derived clips in root-bone space, with compensated `BakeMesh(..., true)` vertices checked against actual world-space renderer bounds. Production skins disable offscreen updates, and the Animator uses `CullUpdateTransforms`; validation covers both body and tread bindings and bounds.

Rebuild in idle Edit Mode with saved scenes through `Tools > ScrapWaves > Enemies > Build Larguirucho Animations`, or the `Tools > ScrapWaves > Build Chaser Animations` alias. `LarguiruchoAnimationAuthoring.ApplyBatch` performs the build and validation; `Validate` checks the saved bindings. Rebuilds update existing generated assets in place, retain their GUIDs, and preserve existing walk-speed calibration and material overrides. The legacy Chaser builder routes through this implementation; Drone and Slime authoring are unchanged.

Validation on October 31, 2026 passed **74/74** focused tests: `ChaserAnimationDriverTests` (12), `LarguiruchoRigIntegrationTests` (14), `EnemyDormancyTests` (10), `EnemyPoolLifecycleTests` (2), and `EnemyReactionTests` (36). Local evidence is in `.utmp/larguirucho-2026-10-31/focused-tests.xml`; authoring and scene-preservation reports are alongside it.

Controlled editor CPU measurements used Unity 6000.3.13f1 on an Intel Core i9-13980HX, with 20 warmup and 90 measured batches. Each batch sets presentation phases and calls `Animator.Update(1/60)` for every rig, forcing Always Animate on the disposable instances:

| Active rigs | Mean batch time | 95th percentile |
|---|---:|---:|
| 100 | 1.407 ms | 1.655 ms |
| 300 | 4.928 ms | 5.415 ms |

These measurements exclude rendering, physics, AI, gameplay, and full-frame scheduling. They are not FPS measurements or a complete swarm frame budget.

The local preview is `.utmp/larguirucho-2026-10-31/preview.mp4`: 15 seconds of staged Unity playback using the three production rigs and controllers. It shows walking, full and early-contact attack sequences, slow overheat movement poses, and idle. The actors remain in place and the capture supplies animation phases directly; it is not full gameplay or a collision/damage simulation. `.utmp` evidence is local and ignored by Git.
