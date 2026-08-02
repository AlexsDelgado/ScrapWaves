# ScrapWaves Game Feel Foundation

This folder contains the shared feedback foundation plus complete Automatic Cannon and Rocket Launcher production presentations. Flamethrower, Mortar, and Rotating Blade remain future slices.

## Tuning workflow

1. Open the relevant profile in `Assets/ScriptableObjects/WeaponPresentation`.
2. Tune semantic bindings for fire mode, ability path, weak-point, critical, kill, heat, and ammo events.
3. Tune camera, audio, VFX, hit-stop, pool, distance, density, and quality values on each referenced cue.
4. Tune projectile archetypes and heat curves on the same profile.
5. Validate in `Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity` using its channel, quality, accessibility, and metrics controls.

The weapon reports semantic combat events. `CombatFeedbackDirector` resolves those events through the profile and routes them to the pooled VFX, audio, additive camera, centralized hit-stop, recoil, projectile, and enemy feedback systems. Presentation data does not alter damage, cadence, movement, collision, or other gameplay state.

## Authored assets

- `Prefabs/Weapons/AutomaticCannon`: pooled muzzle, sustained-fire, impact, kill, Head Hunter, and heat effects.
- `Prefabs/Weapons/RocketLauncher`: pooled launch/backblast, targeting, layered explosion, Kinetic implosion, Fragmentation casing, status, and kill effects.
- `Materials`: shared shader materials. Runtime variation uses material property blocks rather than material instances.
- `Meshes`: low-cost authored procedural meshes generated in the editor, including UV data for shader dissolve/noise.
- `Profiles/GameFeelQuality_PC.asset`: Low, Medium, and High particle/decal/light budgets.
- `Shaders/ScrapVfx.shader`: emission, fresnel, pulse, heat, vertex jitter, and dissolve for transient effects.
- `Shaders/ScrapProjectile.shader`: stepped lighting and emission for readable projectile silhouettes.

The player prefab owns a cosmetic recoil rig. The shared projectile prefab owns swappable Cannon, Head Hunter, Rocket, cluster-casing, and lightweight fragment archetypes, including profile-driven Rocket exhaust smoke. Enemy prefabs own non-gameplay `EnemyHitFeedback` and `EnemyDeathFeedback` components; kill effects are detached pooled effects so enemy despawn cannot cut them short.

## Performance contract

- Transient VFX and audio voices are prewarmed and pooled.
- After pool warm-up, repeated firing does not create meshes, materials, textures, or effect hierarchies.
- Cue-level distance, density, priority, and quality rules suppress secondary layers before core readability.
- Camera feedback is bounded, additive, rate-limited, and never changes gameplay aim.
- Hit-stop is centralized, unscaled, prioritized, overlap-safe, and restores the previous time scale.
- The sandbox HUD reports active/capacity counts and suppression totals for budget checks.

To rebuild generated assets after changing an editor recipe, use the matching command under `Tools > ScrapWaves > Game Feel`, or invoke `GameFeelFoundationAssetBuilder.BuildBatch` / `RocketLauncherAssetBuilder.BuildBatch` in Unity batch mode.
