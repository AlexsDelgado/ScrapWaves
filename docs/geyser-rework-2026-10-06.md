# Approved geyser rework — #175

Prepared on `BugFrank` from `f754eb5ce1a5b3a26db64072f50b99bed473fb69`. The approved two-variant mound design replaces only the two launch-pad visuals and activation volumes in GameplayScene. The existing movement pass, font changes, and concurrent flamethrower work were preserved during implementation. Git publication was authorized separately after visual review; no HNP mutation was requested or performed.

The subsequent visual refinement responds to “too natural looking” with restrained compacted ordinary scrap rather than rock/soil volcanoes. It changes only editable model source, two visual FBXs, visual material mappings/materials, the visual refresh helper and documentation. The scene, both collision FBXs, runtime movement/launch/VFX scripts and all particle/collider/transform records in the two prefabs remain unchanged. Only one visual FBX material-override record changed per prefab. The new 8/8 affected integration/native-route tests passed; the earlier 110/110 regression and Windows compilation results below are from the original implementation pass.

## Result

- **Trash geyser, floor:** compacted ordinary junk, overlapping bent panels, corrugated roofing offcuts, discarded pipe and a crushed household pail, with a rough contrasting metal lip and dark opening. Continuously rising pale dust/air carries small tumbling scraps. Entry produces a stronger short plume burst and six extra lightweight debris particles.
- **Hot-air geyser, upper islands:** gray/heat-darkened sheet-metal scrap, muted rust, roof offcuts and short discarded pipe/duct, with the same clear rough metal opening. Continuous pale updraft uses animated screen-space heat distortion. Entry produces a stronger plume burst and temporarily increases distortion.
- Both openings accept the player immediately. There is no readiness cycle, eruption wait, flame presentation, damage, or added movement ability. The solid mound matches its geometry rather than retaining an invisible rectangular deck.
- The receiving platforms, their colliders, original launch destinations, 12 m apex clearance, ballistic solver, and movement launch/landing behavior are retained. The existing landing dead stop remains.

## Editable source and assets

`ArtSource/Environment/Geysers/ScrapWaves_Geysers.blend` contains two separate editable collections at the origin. Hide the other collection when editing. `build_geysers.py` deterministically regenerates the file and both visual FBX exports; it preserves the approved collision exports by default. `collision-profiles.json` retains the exact approved source geometry, with collision regeneration requiring explicit `--export-collision`. Units are meters; exports use Unity Y-up. Bent sheets, roofing offcuts, opening lip and everyday scrap objects are separately editable. No textures or third-party assets are required.

| Asset | Visual triangles | Material slots | Simplified collision triangles |
| --- | ---: | ---: | ---: |
| TrashGeyser | 1,284 | 6 | 310 |
| HotAirGeyser | 1,294 | 5 | 310 |

Both imported mounds are approximately 9.9 m wide and 2.05–2.07 m tall. The hot-air distortion volume adds 160 triangles. Native mesh-particle debris uses a four-triangle mesh. No per-debris Rigidbody or GameObject is created.

Unity prefabs, materials, meshes, shaders and model importer metadata are in `Assets/Art/Environment/Geysers`. `GeyserModelAuthoring.RefreshVisuals` updates only imported visual material mappings, retaining the scene, colliders and VFX. `Run` remains the full initial-authoring operation; it is repeatable and removes an existing owned visual before replacement. `PadModelAuthoring` skips geyser-owned roots so the old pad tool does not overwrite them. Full authoring is a deliberate regeneration operation; change prefab/scene Inspector values directly for ordinary tuning.

## Inspector tuning

`GeyserVfx` keeps presentation separate from the launch solver. Its fields expose flow color, emission, speed, lifetime, size, opening radius, particle budgets, entry count/duration/brightness, and heat distortion. Native particle modules remain editable for cone shape, debris rotation and burst spread.

| Default | Trash | Hot air |
| --- | ---: | ---: |
| Continuous plume emission | 12/s | 14/s |
| Upward start speed | 2.2 m/s | 2.2 m/s |
| Plume lifetime | 2.4–3.0 s | 2.4–3.0 s |
| Plume start size | 0.675–1.08 m | 0.675–1.08 m |
| Opening particle radius | 1.25 m | 1.25 m |
| Plume capacity | 48 | 48 |
| Debris emission / capacity | 4/s / 20 | None |
| Entry count / capacity | 28 / 40 | 28 / 40 |
| Entry accent | 0.45 s, 1.45× brightness | Same |
| Heat distortion | None | 0.0035 normalized-screen offset; 1.5× during accent |
| Maximum particle capacity | 108 | 88 |

The plume and distortion pause with scaled game time. On disable, subscriptions are removed and native particles are stopped and cleared. Re-enabling restores the continuous flow. Fixed seeds make authoring previews repeatable.

The hot-air shader samples the existing PC URP opaque scene texture; that pipeline already enables opaque/depth textures, so no graphics or quality setting was changed. It distorts the opaque background before drawing the pale plume. Readability comes from a dark opening, broken contrasting metal lip, persistent upward motion and distinct trash debris/heat presentation. Models use rough URP Lit metal/paint materials in restrained gray, muted rust and aged off-white. Particle shaders use alpha blending rather than fire-colored additive emission.

## Scene preservation

| Root | Original position (m) | Destination |
| --- | --- | --- |
| Jump floor | (-45.7, 0.16, 184.57) | Land floor (-12.36, 68.98, 191.75) |
| Jump sky | (22.74, 67.9, 191.8) | Land sky (159.72, 97.9, 154.97) |

The four pad-root transforms retain their original `(10, 2, 10)` scale and rotation. The visual children counter-scale to preserve real meter dimensions. Each launch root now has a 2.9 × 2.6 × 2.9 m opening trigger, centered 0.7 m above the root, plus one static non-convex mound MeshCollider. The plume leans toward the existing horizontal destination. Receiving pads retain their original 10 × 2 × 10 m box collider and separate visual.

The delivered scene was assembled from six changed launch-owned component records, four removed old launch-prefab records and four new geyser-prefab records. Every unrelated record is byte-for-byte identical to the pre-geyser scene, including the earlier movement camera settings. Both versions have 1,694 Unity YAML records. Unity's unrelated scene-save refreshes were excluded. Whitespace was normalized only inside the four new records.

SHA256 checks also verify that the player prefab, PlayerMovement, ThirdPersonCamera, DestroyerBehavior, PlayerAnimationDriver and the existing modified font were unchanged during integration. Evidence is under `.utmp/geyser-validation-2026-10-06/`.

## Validation

All execution used the disposable `%TEMP%/ScrapWavesMovementValidation-20261006` project, with a distinct validation product identity. The cinematic checkout was not opened or touched. The isolated geyser/scene files match the delivered files. Concurrent flamethrower edits were not copied into this validation snapshot and were left untouched in the working checkout.

**Final focused run: 110/110 passed.** This includes both new geyser fixtures, existing pad integration, movement reliability, camera collision, native movement loop, animation presentation, directional dash and reticle tests. Assertions cover model scale/budgets, solid slope support, visible/collision alignment, continuous flow, native entry particles, destination references, direction, URP shader compilation and existing graphics dependencies. Runtime tests cover first entry and exit/re-entry for both routes.

The native flight fixture uses the production player and geyser prefabs at the original pad coordinates. It copies 73 active solid shapes from GameplayScene into a disposable physics scene without starting unrelated gameplay systems. A scene CharacterController is represented by a static capsule; this is a geometry/route regression check, not a full horde gameplay session. Physics advances at the native 0.02 s step; time capture accelerates deterministic test execution.

| Measured native result | Floor | Upper |
| --- | --- | --- |
| Entry latency | 1 physics tick (20 ms) | 1 physics tick (20 ms) |
| Initial velocity (m/s) | (8.03, 53.96, 1.73) | (41.30, 38.91, -11.11) |
| Solver flight time | 4.152 s | 3.316 s |
| Actual landing body position (m) | (-12.62, 71.17, 191.69) | (159.04, 100.06, 155.15) |
| Residual planar speed | 0.0000 m/s | 0.0000 m/s |
| Re-entry | Launched again | Launched again |

Analytical endpoints are asserted within 0.015 m; native landing XZ is within 1.1 m of the receiver center and vertical position within 0.2 m of the solver target. Native physics integration/collision explains the small position difference from the analytical center. Direction dot products exceed 0.9999. The movement regression still measures 6.8 m/s running and a 0.594 m stopping distance at 50 Hz.

**Windows Standalone player script compilation passed: 31 assemblies.** Rendering also completed with the actual PC URP shaders and scene materials. The final exact scene was reopened for focused geyser/pad verification after the owned whitespace cleanup: **8/8 passed**. Its result is included with the local evidence.

The whole repository suite was not rerun during the initial geyser implementation pass. Final publication validation subsequently combined the exact refined geysers, Q presentation and movement files: **1,039 cases, 1,015 passed, 24 failed, none skipped**. All **8 geyser/pad cases**, **20 Q cases**, **64 movement reliability cases** and **10 directional-dash cases** passed. Windows Standalone player-script compilation also passed with **31 assemblies**. The remaining failed cases were reproduced on baseline and are listed in the movement report; its timing-sensitive title-screen animation case passed in the final run.

The first combined run found a test-isolation issue: exiting Play Mode restored the geyser fixture's copied map colliders in the Editor scene, causing four subsequent mortar trajectory tests to fail. The fixture now replaces that disposable world with an empty scene in teardown. All four mortar failures disappeared in the final run; launch, collision and mortar production behavior was unchanged. Final XML, logs and assembly evidence are under `.utmp/publication-2026-10-06/`.

This report does not claim a globally green repository. No human playtest or target-hardware 60 FPS benchmark was performed. Triangle, particle and draw budgets are intentionally small, but translucent overdraw/opaque-copy cost still needs a representative hardware profile.

## Preview and next comparisons

`Dumpster-Fire-Geysers-Scrappunk-Refinement.mp4` is the current preview; `Dumpster-Fire-Geysers-In-Context.mp4` retains the earlier natural-mound version. Each shows six seconds of each geyser in the real GameplayScene with its authored lighting and production player visual for scale. The player is posed in place. Each segment shows continuous flow, one actual native-particle entry burst at three seconds, then recovery. Rendering does not save the scene. It is a controlled VFX preview; the separate native test validates player flight.

The 1280 × 720 H.264 video contains 288 frames at 24 FPS, totaling 12 seconds. Captured peaks were 76 particles for trash and 67 for hot air, below their hard caps; these counts are not a frame-rate measurement. The refined video replaced the previous native Library preview as version 1; no Drive fallback was needed. Local preview, decoded frames, metrics and verification are retained under `.utmp/geyser-previews/2026-10-06/scrappunk-refinement/`. Focused tests and preservation evidence are in `.utmp/geyser-scrappunk-refinement-2026-10-06/`.

For a human comparison, approach each opening while running and strafing; check that the mound is climbable without a snag and the trigger reads as the opening. Enter at different points, confirm the immediate burst and recognizable receiving area, return and enter again. Check pale plume/heat against bright sky, dark walls and overlapping combat VFX. Profile both simultaneous bursts with a representative horde at 1080p/60 FPS, then tune emission, wisp size and distortion in the Inspector if overdraw or visual competition is excessive.
