# Flamethrower Q radial ignition

The flamethrower active ability now presents an immediate horizontal ignition footprint at the actual damage radius, with sharp flame tongues and short embers. Three combined mesh layers replace the previous overlapping sphere/billow presentation. The footprint is full-sized on the damage frame; the decorative motion does not exceed the damage disc or move its origin when the player walks away.

This is a presentation change for the base, jellified-fuel and nitrogen active cues. Damage, path-specific radius, area-stat scaling, ammo cost, cooldown, persistent fuel puddles, ordinary nozzle fire and status presentation retain their gameplay owners. Tests verify base/path radii of 6/7.2/5.4 m before area scaling, 40 ammo spent and a 14 s cooldown.

`FlamethrowerRadialBurstSettings` exposes the 0.42 s visual lifetime, opacity/travel curves, center clearance, flame height/width, colors and decorative budgets. Defaults use a 64-segment footprint, 20 flame tongues and 24 embers. Low/medium quality reduces decoration while preserving the full boundary. Reduced-flash presentation retains the readable footprint. Active cues use no sphere particles or pulse light; dynamic meshes are reused across pooled plays and released when destroyed.

Runtime source is in `Assets/Scripts/Weapon/Presentation/FlamethrowerRadialBurst.cs` and `FlamethrowerCueVfx.cs`. The dedicated shader is `Assets/GameFeel/Shaders/FlamethrowerRadialIgnition.shader`. The three existing active prefabs carry its serialized reference so player builds retain the shader. `FlamethrowerRadialBurstAuthoring.RefreshActivePrefabs` updates those active prefabs; `FlamethrowerAssetBuilder` produces the same active presentation when regenerating assets.

## Validation

The Q delivery snapshot passed **20/20 focused presentation and radial-burst tests**. Its broader weapon regression passed **149/154**; all five failures were reproduced in the baseline comparison:

- `ExplosionRadiusVfx_CanSpawnWithCustomColor`
- `ExplosiveProjectile_DetonatesWhenFastMovementSweepsThroughGround`
- `PlayerMovement_MomentumPreservingChargeLock_CanCoastWithoutStunFeedback`
- `RotatingBladeAtomicSharpnessManual_ShowsDarkPurpleRadialSlashWithoutUpgradeStreaks`
- `RotatingBladeMultiBladeManual_ShowsOnePeachSwishPerSwordWithoutVerticalSwordOrUpgradeStreaks`

These are cases in `WeaponUpgradeEffectTests`. The full suite is not claimed green. The delivery integrity record confirms source, shader, tests and all three active prefabs match the captured Q snapshot. The test fixture drives actual active-ability feedback for every path and area-stat case, checks center/radius/aim-pitch independence, bounds all geometry at sampled times, and tests pooling, final cleanup, low quality, reduced flash and Inspector curves.

Final publication validation combines this Q delivery with the approved scrappunk geysers and movement reliability pass. The graphics-enabled Unity 6000.3.13f1 EditMode suite ran **1,039 cases: 1,015 passed, 24 failed, none skipped**. All **20 Q cases**, **8 geyser/pad cases**, **64 movement reliability cases** and **10 directional-dash cases** passed. The remaining failures are cases already reproduced on baseline; the earlier movement report lists them. Its timing-sensitive title-screen animation case passed in this run. Windows Standalone player-script compilation also passed with **31 assemblies**. These runs used a disposable project with a distinct validation product identity.

The first combined run exposed copied map colliders surviving the geyser fixture's Play Mode exit and affecting four mortar tests. Clearing that disposable Editor scene in the geyser fixture's teardown removed all four failures in the final run. No mortar production code changed. Local final XML, logs and assembly list are under `.utmp/publication-2026-10-06/`. The full suite remains red from existing failures. Earlier per-task runs used separate snapshots; their pass counts do not represent the final combined result. No human gameplay or target-hardware frame-rate benchmark is implied.
