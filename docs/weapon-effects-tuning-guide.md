# ScrapWaves weapon-effects tuning guide

This guide explains how to fine-tune the production weapon presentation without
changing damage, targeting, cadence, collision, status logic, or other gameplay.
It covers the Automatic Cannon, Rocket Launcher, Flamethrower, Mortar, and
Rotating Blade as they are currently implemented.

The most important rule is simple:

> Tune presentation against the visible gameplay area, timing, and number of
> damaging events. An effect is successful only when it looks good and tells the
> truth about what the weapon did.

## 1. Fast tuning workflow

For most changes, use this loop:

1. Open `Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity`.
2. Equip only the weapon being tuned, so other automatic weapons do not add noise.
3. Select the desired base/path level and firing mode.
4. Disable Audio, Camera Feedback, Hit-stop, and Enemy Reactions in the sandbox.
   Leave Production Presentation and VFX enabled.
5. Tune one cue or one visual layer at a time.
6. Test at 0%, 25%, 50%, 75%, 80%, and 100% heat.
7. Re-enable the other feedback channels one at a time.
8. Test Low, Medium, and High quality and both Reduced Flash and Reduced Shake.
9. Test while standing still, moving toward the effect, moving sideways, and
   looking from a low camera angle.
10. Turn on Debug Geometry and verify that visual range, radius, cone, or path
    does not contradict the gameplay visualization.
11. Check the sandbox metrics for active FX, pool capacity, suppression, frame
    time, and managed memory.
12. Exit Play Mode and save intentional asset changes.

Do not tune a runtime clone named `(Weapon VFX Pool)`, `[Flamethrower Stream]`,
`[Rotating Blade VFX]`, or `(Pooled)`. Changes to those scene instances are not
the authored source and will be lost.

## 2. Where each kind of tuning lives

| Desired change | Authoring location |
| --- | --- |
| Which effect is selected for an event | Profile `Feedback Bindings` |
| Prefab, duration, pool, camera, hit-stop, and audio | Profile `Cues` |
| Projectile mesh, scale, trail, smoke, and light | Profile `Projectile Archetypes` |
| Heat color and response | Profile `Heat` plus per-cue `Heat Multiplier` |
| Secondary-effect limits | Profile `Density` |
| Weapon-specific presentation caps/prefabs | Profile weapon-specific settings |
| Mesh animation, colors, and effect lifetime | Component on the VFX prefab root |
| Particle count, shape, velocity, lifetime, and noise | Particle System children |
| Shared surface appearance | Materials in `Assets/GameFeel/Materials` |
| Low/Medium/High optional-layer policy | `Assets/GameFeel/Profiles/GameFeelQuality_PC.asset` |

The five profiles are in `Assets/ScriptableObjects/WeaponPresentation`:

- `AutomaticCannonPresentation.asset`
- `RocketLauncherPresentation.asset`
- `FlamethrowerPresentation.asset`
- `MortarPresentation.asset`
- `RotatingBladePresentation.asset`

Production VFX prefabs are grouped under
`Assets/GameFeel/Prefabs/Weapons/<WeaponName>`.

## 3. How the presentation pipeline works

The runtime flow is:

```text
Weapon gameplay
  -> semantic feedback event and context
  -> profile Feedback Binding
  -> profile Cue
  -> pooled VFX/audio plus camera/hit-stop
  -> prefab receives position, direction, heat, radius, quality, and intensity
```

The gameplay context can include:

- Firing mode and upgrade path.
- Normalized heat.
- Origin, direction, impact point, and impact normal.
- Explosion radius.
- Damage amount and event intensity.
- Critical, weak-point, kill, and ability flags.
- Target and surface type.
- Accessibility and quality settings.

This separation is why most effect tuning should not touch the weapon behavior
classes. The behavior reports the truth; the profile and prefab decide how that
truth is shown.

### 3.1 Binding resolution

Each `Feedback Binding` maps an event to a cue and may filter by mode, upgrade
path, critical hit, weak point, kill, or surface. The most specific matching
binding wins.

When an effect does not play, check the binding before editing the prefab. A
perfect prefab cannot appear if no binding selects its cue.

When adding an override:

1. Keep a general fallback binding.
2. Add the more specific path/mode/flag binding.
3. Confirm the specific binding selects a cue present in the same profile.
4. Avoid duplicate bindings with identical filters because their intent is
   ambiguous even if list order appears to make one work.

### 3.2 Cue data reference

Every entry under `Cues` contains the shared tuning contract.

#### Identity and VFX

- `Cue`: semantic presentation identifier.
- `Vfx Prefab`: pooled production prefab. A null prefab is valid for an
  audio/camera-only cue.

#### Lifetime and concurrency

- `Duration`: how long a non-looping pooled instance remains active.
- `Min Replay Interval`: rate limit for repeated requests of this cue.
- `Prewarm Count`: instances created before the cue is needed.
- `Max Simultaneous`: hard pool capacity for this cue.
- `Loop`: keeps the instance alive until the weapon sends a stop event.

For non-looping effects, there are usually two lifetimes:

1. The cue `Duration`, which controls pool release.
2. The prefab component `_lifetime` and child particle/trail lifetimes, which
   control visible animation.

Use this relationship:

```text
Cue Duration >= longest meaningful visual lifetime + 0.03 to 0.10 seconds
```

If cue duration is shorter, particles or curves are cut off. If it is much
longer, invisible instances occupy the pool and may suppress later effects.

A practical starting capacity for a frequent one-shot is:

```text
Max Simultaneous >= ceil(max events per second * Cue Duration) + 1 or 2
```

Then validate against the real burst, barrage, or swarm scenario. Prewarm the
normal steady-state count, not necessarily the absolute maximum.

#### Camera

- `Camera Position Impulse`: short positional kick.
- `Camera Rotation Impulse`: short rotational kick.
- `Camera Fov Kick`: temporary field-of-view kick.
- `Camera Min Replay Interval`: camera-specific rate limit.

Camera response is multiplied by event intensity, the profile heat camera curve,
distance from an impact, the controller master scale, and Reduced Shake. Tune it
after the VFX timing is correct. High-frequency weapons usually need smaller,
rate-limited impulses; delayed heavy impacts can support one stronger impulse.

#### Hit-stop

- `Hit Stop Duration`: unscaled freeze request.
- `Hit Stop Priority`: determines which overlapping request wins.

Use hit-stop on confirmed damage, not merely on firing. Repeated weapons should
use very small values or none. Large values can make a mechanically correct
cadence feel broken.

#### Quality and density

- `Essential Gameplay Cue`: never suppressed by quality or density.
- `Secondary Effect`: may be suppressed when active FX exceed the profile limit
  or when the effect is beyond `Distant Secondary Cutoff`.
- `Minimum Quality`: hides a non-essential cue below the selected tier.

Keep projectile readability, damaging impacts, landing warnings, and other
gameplay-critical information essential. Smoke wisps, extra debris, heat vents,
and decorative status accents are good secondary candidates.

#### Heat multiplier

`Heat Multiplier` is evaluated after the profile's shared heat curves. It lets a
cue react more or less strongly than the rest of its weapon.

The effective response is approximately:

```text
shared heat curve(normalized heat) * cue heat multiplier(normalized heat)
```

Keep a curve flat at 1 when the cue should use only the shared response. Do not
use a zero value unless the layer is intentionally invisible at that heat.

#### Audio fields

Audio and status feedback are not the current polish focus, but the profile
already supports clip variants or simultaneous layers, volume, mechanical-layer
volume, heat strain, progress-driven pitch, pitch range, spatial blend,
attenuation distance, priority, and looping. Leave these stable while isolating
visual tuning, then polish them in a separate pass.

## 4. Heat tuning

The profile `Heat` section contains these curves:

- `Color`: projectile/weapon color progression.
- `Emission`: brightness multiplier.
- `Smoke Rate`: smoke-particle budget multiplier.
- `Spark Rate`: spark-particle budget multiplier.
- `Audio Pitch`: heat pitch response.
- `Mechanical Strain Volume`: heat layer volume.
- `Trail Width`: projectile trail width multiplier.
- `Camera Vibration`: camera impulse multiplier.

All curve X axes use normalized heat from 0 to 1. The sandbox's 80% point is
especially important because the underlying heat mapping has a segment boundary
there.

Recommended curve workflow:

1. Start every visual curve flat at 1.
2. Establish a readable but calm 0% presentation.
3. Set the 100% endpoint without clipping the screen or saturating colors.
4. Add a subtle shoulder around 75-80% so high heat feels intentional.
5. Test intermediate presets and drag the heat slider slowly to detect sudden
   jumps.
6. Enable Reduced Flash and confirm high heat remains readable without relying
   only on brightness.

Prefer changes in color, smoke cadence, motion, and trail width over extreme
emission. Additive white saturation destroys shape information.

## 5. Projectile archetype tuning

Projectile archetypes control regular pooled projectiles used by the Cannon and
Rocket Launcher.

- `Mesh`, `Material`: projectile silhouette and surface.
- `Local Scale`, `Local Euler Angles`: visual-only alignment and size.
- `Trail Material`, `Trail Lifetime`, start/end width, and gradient.
- `Flight Smoke Material`, rate, size, and lifetime.
- `Light Intensity` and `Light Range`.
- `Base Emission`.

The projectile collider, speed, and damage do not change when these values are
edited. This is useful, but it also means an oversized mesh can visibly hit
before or after the authoritative collider.

### 5.1 Trail readability formula

For a sequence of identical bullets:

```text
visual trail length = projectile speed * trail lifetime
bullet spacing      = projectile speed * interval between bullets
overlap ratio       = trail lifetime / bullet interval
```

Speed cancels out. To make every projectile readable, begin with a trail lifetime
around 25-60% of the shot interval. Above roughly 75%, consecutive trails tend to
merge from the gameplay camera. Use a brighter tracer every few shots rather than
making every trail longer.

Always retest while moving. A trail that looks separated when stationary can
collapse into one ribbon when player velocity changes the apparent spacing.

### 5.2 Projectile shader

`Scrap Projectile` exposes base color, emission color/intensity, heat, metallic,
and smoothness. The runtime controller overrides the colors, emission, and heat
with a `MaterialPropertyBlock`. Metallic and smoothness remain material-owned.

If a material color edit appears to do nothing in Play Mode, check whether the
runtime property block is overriding it. Tune the profile heat gradient or base
emission instead of creating a unique runtime material.

## 6. Prefab component and curve tuning

The root components for transient effects follow a similar pattern:

- Cannon: `AutomaticCannonCueVfx`
- Rocket: `RocketLauncherCueVfx`
- Flamethrower cue: `FlamethrowerCueVfx`
- Mortar cue: `MortarCueVfx`
- Blade impact: `RotatingBladeCueVfx`

Common fields include:

- `_primaryColor` and `_coreColor`.
- `_meshLayers` and `_particleLayers`.
- `_animatedRoots`.
- `_lifetime`.
- `_size` where applicable.
- `_scaleOverLife`.
- `_emissionOverLife` and `_dissolveOverLife` where applicable.
- `_rotationDegreesPerSecond`.
- `_baseEmission`.

Curve X is normalized life: 0 is spawn and 1 is the end of `_lifetime`.

### 6.1 Scale curve

A readable impact often uses three phases:

- Fast attack: rise from 20-40% size to full size in the first 10-20% of life.
- Brief readable body: remain near full size long enough to identify the shape.
- Release: expand slightly or contract while dissolving.

Avoid a zero-size first frame for a critical damaging event; it can disappear at
low frame rates. Avoid holding maximum scale for most of the lifetime unless it
is a warning or persistent area.

### 6.2 Emission and dissolve curves

Emission communicates the initial energy. Dissolve communicates material breakup.
Usually emission falls before dissolve reaches 1. If both remain high until the
last frame, the effect pops off instead of fading.

Reduced Flash multiplies and recolors runtime emission, so test the authored
curve in both modes.

### 6.3 Animated roots

Only transforms listed in `_animatedRoots` receive component-driven scale and
rotation. Use a dedicated animation root rather than scaling the complete prefab
when particle shapes or world-space children must remain stable.

Set `_rotationDegreesPerSecond` to zero for ground targeters, directional cones,
and any effect whose orientation communicates gameplay. Rotation is suitable for
scrap debris, energy coils, or shape-symmetric decorative layers.

## 7. Particle System tuning

Tune particles in Prefab Mode, with the root component disabled temporarily if
its runtime animation makes inspection difficult.

### 7.1 Main module

- `Start Lifetime`: how long an emitted particle survives.
- `Start Speed`: initial travel speed.
- `Start Size`: silhouette size.
- `Simulation Space`: Local follows the emitter; World remains where emitted.
- `Max Particles`: authored upper bound before runtime quality/heat scaling.
- `Stop Action`: keep `None` for pooled effects; never use `Destroy`.

The root pool duration must outlive meaningful particles. A long particle life on
a short pooled cue will still be cut when the pooled object is disabled.

### 7.2 Emission

Use bursts for impacts and short releases. Use rate-over-time for streams, smoke,
and sustained loops. Do not compensate for poor shape or timing by multiplying
particle count; establish a clear primary mesh/core first.

Runtime quality and heat modify `maxParticles`. Therefore, author High-quality
maximums in the prefab and verify that Low still preserves the primary read.

### 7.3 Shape and space

- Cone: muzzle bursts, forward sparks, exhaust.
- Sphere/hemisphere: radial explosion debris.
- Circle: ground puddle bubbles or perimeter residue; rotate the authored XY
  circle into the ground XZ plane.
- Mesh: distinctive scrap fragments.

Use World simulation for debris, smoke, scorch particles, and residue that must
stay at the collision point. Use Local for nozzle flames and particles that must
continue following an anchored weapon loop.

### 7.4 Motion modules

`Velocity over Lifetime`, `Limit Velocity`, `Noise`, `Size over Lifetime`, and
`Color over Lifetime` should reinforce the temporal signature:

- Sparks: fast start, strong drag, short life.
- Smoke: slower start, rising velocity, expanding size, soft alpha fade.
- Flame: directional speed plus noise and erosion, not random radial drift.
- Dirt/debris: outward/upward burst, gravity, visible deceleration.
- Frost: crisp outward shard, slower vapor follow-up.

### 7.5 Avoiding particle afterimages

When a parent visual fades before its particles:

1. Stop emission before the parent reaches zero alpha.
2. Allow existing particles to complete a short fade.
3. Ensure the pool releases only after that padding.
4. Verify no particle is still visible one frame after pool release.

This is especially important for puddle bubbles and smoke.

## 8. Material and shader tuning

Shared materials are in `Assets/GameFeel/Materials`.

`Scrap VFX` is additive, transparent, double-sided, depth-tested, and does not
write depth. It exposes:

- `Base Color`.
- `Emission Color` and `Emission Intensity`.
- `Heat`.
- `Pulse`.
- `Dissolve`.
- `Noise Scale` and `Noise Speed`.
- `Vertex Jitter`.

Most cue components override base/emission colors, emission intensity, heat,
pulse, and dissolve at runtime. Noise and vertex jitter normally remain authored
on the shared material; the Flamethrower stream explicitly overrides noise scale
and speed from its component.

Practical rules:

- Tune emission in Game view with the production post-processing and camera.
- Keep enough base color to preserve shape when Reduced Flash lowers emission.
- Increase noise scale for smaller breakup cells; decrease it for larger chunks.
- Increase noise speed for agitation, not for perceived projectile speed.
- Use vertex jitter sparingly on targeters and radius markers because it can make
  their boundary look inaccurate.
- Do not edit a shared material to solve one prefab until checking every prefab
  that references it.
- Do not use `renderer.material` for normal variation; it creates material
  instances. Existing runtime variation uses property blocks.

## 9. Automatic Cannon tuning

### Main authored areas

- Profile: `AutomaticCannonPresentation.asset`.
- Cue prefabs: `Assets/GameFeel/Prefabs/Weapons/AutomaticCannon`.
- Projectile archetypes: Cannon Round, Cannon Tracer, and Head Hunter Bolt.
- Cue component: `AutomaticCannonCueVfx`.

### Profile-specific controls

- `Base Tracer Frequency`: bright tracer cadence for base active scatter. Line
  bursts intentionally emphasize their final round.
- `Continuous Tracer Frequency`: tracer cadence during Continuous Fire.
- `Maximum Piercing Accents`: full secondary impact accents permitted for one
  Head Hunter shot.
- `Sustained Feedback Grace`: aggregation grace around continuous fire.

### Recommended tuning order

1. Make the projectile body readable with trails disabled.
2. Establish distinct Cannon Round, Tracer, and Head Hunter silhouettes/colors.
3. Add the shortest trail that still communicates direction.
4. Test automatic and manual bursts from the side and behind the player.
5. Confirm each projectile can be counted and each damaging collision produces
   its own impact confirmation.
6. Tune muzzle flash below the projectile silhouette so it does not hide later
   rounds.
7. Tune Continuous Fire as a sustained cadence without making it visually weaker
   than a base burst.
8. Tune Head Hunter charge/release separately from its piercing impacts.

If a burst looks like one bullet, first reduce trail lifetime and width, then
reduce muzzle/impact persistence. Do not change gameplay burst interval merely to
repair an effect overlap problem.

## 10. Rocket Launcher tuning

### Main authored areas

- Profile: `RocketLauncherPresentation.asset`.
- Cue prefabs: `Assets/GameFeel/Prefabs/Weapons/RocketLauncher`.
- Projectile archetypes: Rocket, Fragment Rocket, and Cluster Rocket.
- Cue component: `RocketLauncherCueVfx`.

### Radius-scaled effects

Rocket cue prefabs may enable `_scaleFromExplosionRadius`. Their scale becomes:

```text
effect scale = explosion radius * explosion-radius multiplier * authored curve
```

Tune the multiplier while Debug Geometry shows the damage radius. The bright
primary boundary should not imply damage outside the actual area. Smoke may drift
beyond it after the damage moment, but the initial damage read should match.

### Fragmentation forward mini explosions

Fragment cues expose:

- `_forwardConeRangeMultiplier`.
- `_miniExplosionStart`.
- `_miniExplosionStagger`.
- `_miniExplosionDuration`.
- Authored mini-explosion transforms under `_forwardMiniExplosionRoot`.

The root is scaled from explosion radius and the range multiplier. Treat these as
gameplay-coupled controls: every mini explosion that visually confirms damage
must remain inside the authoritative forward fragment cone. Test the smallest
and largest possible explosion radius and rotate around the target to inspect the
cone edge.

Tune the regular radial explosion first. Then tune the forward sequence as a
secondary directional grammar. The radial layer says “main impact”; the staggered
mini explosions say “fragment damage continues forward.”

### Recommended tuning order

1. Launch/backblast and rocket silhouette.
2. Travel trail and smoke.
3. Main radial impact matched to radius.
4. Kinetic inward/outward motion and displacement emphasis.
5. Fragment forward cone and mini-explosion timing.
6. Cluster child impact consistency.
7. Targeting/lock readability with Reduced Flash.

## 11. Flamethrower tuning

### Main authored areas

- Profile: `FlamethrowerPresentation.asset`.
- Stream prefab: `GF_Flamethrower_Stream.prefab`.
- Puddle prefab: `GF_JellifiedFuel_Puddle.prefab`.
- Cue/status prefabs: `Assets/GameFeel/Prefabs/Weapons/Flamethrower`.
- Runtime components: `FlamethrowerStreamVfx`, `FlamethrowerFuelPuddle`, and
  `FlamethrowerCueVfx`.

### Stream controls

- `_maximumSegments`: presentation cap for hose/cone geometry.
- `_ribbonWidth`: width from nozzle to stream end.
- `_coreWidthMultiplier`: bright inner core relative to body.
- `_automaticWidthMultiplier`: automatic cone visual width.
- `_automaticHeightMultiplier`: automatic cone vertical thickness.
- `_noiseScale`, `_noiseSpeed`, `_erosionSpeed`: surface motion/breakup.
- `_baseEmission`, `_heatEmissionMultiplier`.
- `_emberRate`, `_smokeRate`.

The procedural stream is generated from authoritative range/cone/hose data. Tune
its multipliers, not the gameplay point list. For third-person visibility, test
automatic fire while aiming away from and across the camera. Reduce automatic
width and height before lowering the whole effect's opacity if it obscures the
screen.

Use the ribbon-width curve to establish a narrower nozzle, a readable body, and a
tapered end. A large bright core across the whole range tends to read as a solid
beam rather than fire.

### Jellified Fuel puddle controls

- `_edgeThickness`: outline thickness relative to the generated footprint.
- `_viscousPulseSpeed`: slow material pulse.
- Bubble and dark-smoke Particle Systems.

Puddle mesh radius is driven by gameplay radius. Keep bubbles distributed across
the circle in XZ, not along one axis or only at the center. For ability-sized
puddles, verify the full area at both a high and low camera angle.

The puddle owns a short visual fade after gameplay duration. Bubble/smoke emission
stops first, existing particles fade, and the pooled object releases last. Do not
increase particle lifetime without rechecking that sequence.

### Recommended tuning order

1. Base automatic cone dimensions and camera occlusion.
2. Manual hose continuity and endpoint readability.
3. Flame/fuel/nitrogen palette separation.
4. Surface noise and erosion.
5. Embers and smoke.
6. Active burst orientation and radius.
7. Puddle fill, edge, bubbles, smoke, and disappearance.

## 12. Mortar tuning

### Main authored areas

- Profile: `MortarPresentation.asset`.
- Shell: `GF_Mortar_AuthoredShell.prefab`.
- Manual marker: `GF_Mortar_ManualLandingIndicator.prefab`.
- Cue prefabs: `Assets/GameFeel/Prefabs/Weapons/Mortar`.
- Components: `MortarShellVfx`, `MortarLandingIndicatorVfx`, and `MortarCueVfx`.

### Profile-specific controls

- `Shell Prewarm Count` and `Shell Pool Capacity`.
- `Maximum Detailed Rain Shells`: active-rain shells allowed full trail, smoke,
  and landing-warning detail.
- Shell and landing-indicator prefab references.

### Landing indicator

The indicator's blast ring diameter is derived directly from gameplay radius.
The time-to-impact ring contracts using travel time, and path color identifies
base, Grapeshot, or Multi-Charged behavior.

Tune `_minimumPulsePeriod` for legibility, not perceived projectile speed. Do not
scale the indicator root to make it prettier; that breaks the radius relationship.
Tune child mesh thickness/material instead.

The shell marker stays locked to the resolved surface. The presentation uses a
terrain-coverage rule: a slope should orient the effect only when it supports the
majority of the AoE; small obstacles fall back to supporting ground. This rule is
code-tested and is not intended as routine artistic tuning.

### Impact cues

`MortarCueVfx` scales impact grammar from explosion radius. Launch and Grapeshot
airburst use specialized rules. Tune prefab base transforms and curves while
checking Debug Geometry because additional code-side radius scaling is applied.

Each damaging Grapeshot child impact and each Multi-Charged repeat must receive a
visible confirmation. Density tuning may simplify smoke/debris, but it must not
merge several damage pulses into one misleading effect.

### Recommended tuning order

1. Shell silhouette and arc readability.
2. Landing radius and time-to-impact marker.
3. Base impact timing and radius.
4. Grapeshot airburst followed by individually readable child impacts.
5. Multi-Charged initial impact and every repeated pulse.
6. Barrage rain detail and pool pressure.
7. Flat ground, majority slope, small obstacle, and terrain-edge cases.

## 13. Rotating Blade tuning

### Main authored areas

- Profile: `RotatingBladePresentation.asset`.
- Runtime prefab: `GF_RotatingBlade_Runtime.prefab`.
- Impact prefabs: `Assets/GameFeel/Prefabs/Weapons/RotatingBlade`.
- Runtime component: `RotatingBladeVfx`.
- Impact component: `RotatingBladeCueVfx`.

### Profile/runtime controls

- `Maximum Orbiting Blades`.
- `Maximum Concurrent Slashes`.
- `Maximum Concurrent Thrusts`.
- `_orbitGuideAlpha`.
- `_minimumBladeLength`.
- `_baseTrailWidth` and `_baseTrailTime`.
- Blade prototype plus trail, slash, thrust, and atomic materials.

The physical blade and hit geometry are separate. Keep the visual blade centered
on the authoritative orbit/contact area and avoid extending a bright edge far
beyond the damaging surface.

The orbit guide is support information, not the primary weapon body. Keep it
subordinate to the blade and trail. Manual slash and active thrust need distinct
directional silhouettes. Multi-Blade's final hit and Atomic dash/slice should be
stronger without obscuring their exact direction.

### Recommended tuning order

1. Physical blade size, orientation, and orbit position.
2. Persistent trail width/time.
3. Orbit guide alpha.
4. Base slash surface.
5. Thrust ribbon.
6. Multi-Blade staging and final impact.
7. Atomic palette, dash afterimages, and slice impact.
8. Contact-spark density under continuous enemy contact.

## 14. Generated-asset warning

The following Unity menu commands rebuild generated meshes, materials, prefabs,
and profiles:

- `Tools > ScrapWaves > Game Feel > Rebuild Cannon Production Assets`
- `Tools > ScrapWaves > Game Feel > Rebuild Rocket Launcher Production Assets`
- `Tools > ScrapWaves > Game Feel > Rebuild Flamethrower Production Assets`
- `Tools > ScrapWaves > Game Feel > Rebuild Mortar Production Assets`
- `Tools > ScrapWaves > Game Feel > Rebuild Rotating Blade Production Assets`

Running a rebuild can overwrite direct Inspector tuning with constants from its
editor builder script in `Assets/Scripts/Weapon/Editor`.

Before rebuilding:

1. Commit or diff the current assets.
2. Decide whether the Inspector edit is a one-off experiment or the new default.
3. For a permanent generated default, update the matching builder recipe too.
4. Run the rebuild.
5. Review the resulting asset diff; do not assume only one prefab changed.
6. Rerun the relevant presentation tests and sandbox matrix.

For ordinary fine-tuning, avoid running a builder after direct prefab/profile
changes unless its recipe has also been updated.

## 15. Safe tuning passes

Do not tune everything simultaneously. Use these passes in order.

### Pass A: gameplay truth

- Count visible projectiles and damage confirmations.
- Match radius, cone, and directional coverage.
- Match warnings to actual collision points.
- Ensure world impacts stay on the collision object/ground.

### Pass B: silhouette and identity

- Tune mesh size, primary/core colors, and layer hierarchy.
- Establish clear path palettes.
- View against bright, dark, enemy-filled, and empty backgrounds.

### Pass C: temporal signature

- Tune attack, readable body, and release.
- Match cue duration to component/particle life.
- Fix trail overlap and repeated-pulse merging.

### Pass D: heat

- Tune 0% first and 100% second.
- Add intentional intermediate response.
- Confirm 80% has no accidental jump.

### Pass E: camera and hit-stop

- Add one channel at a time.
- Test repeated fire for discomfort and cadence distortion.
- Verify Reduced Shake.

### Pass F: density, quality, and accessibility

- Spawn groups and repeat the most expensive ability.
- Compare Low/Medium/High.
- Confirm essential cues are never suppressed.
- Verify Reduced Flash preserves shape and causality.

### Pass G: audio and enemy-status polish

Handle this as a later dedicated pass so audio/status changes do not mask weapon
VFX problems.

## 16. Acceptance matrix

For every weapon, test:

| Dimension | Cases |
| --- | --- |
| Mode | Automatic, Manual, Active |
| Upgrade | Base, Path A, Path B |
| Heat | 0, 25, 50, 75, 80, 100% |
| Motion | Still, forward, backward, sideways |
| Target | No collision, ground, normal enemy, elite/boss where relevant |
| Camera | Normal, close, low angle, looking across the effect |
| Quality | Low, Medium, High |
| Accessibility | Default, Reduced Flash, Reduced Shake, both |
| Density | Single target, group, repeated ability/barrage |

Acceptance questions:

- Can the player identify the weapon and path without reading UI?
- Can each projectile or damaging pulse be counted when counting matters?
- Does the first visible contact occur on the collided object?
- Does the primary radius/cone stay inside the damaging area?
- Does residue stay in world space instead of following the player?
- Does anything important disappear at Low quality or under suppression?
- Does high heat add intensity without covering the player, target, or reticle?
- Does the effect end smoothly with no particles surviving after pool release?
- Are active FX and audio voices below capacity in the worst expected case?
- Does managed memory remain stable after warm-up?

## 17. Troubleshooting

### Effect is cut off

- Increase cue `Duration`, or shorten the component/particle lifetime.
- Check whether the pool is releasing the root before child particles finish.
- Check `Max Simultaneous` and the suppression metric.

### Effect lingers or particles remain after the parent disappears

- Shorten particle lifetime.
- Stop emission earlier.
- Add a short fade instead of an abrupt stop.
- Ensure pooled reset clears every Particle System and Trail Renderer.

### Impact follows the player or weapon

- The cue may be using an `Anchor` when it should use a world position.
- A Particle System may be using Local simulation instead of World.
- A child may be parented under the moving presentation controller.

### Ground effect tilts, spins, or intersects terrain

- Check whether the root uses impact normal or firing direction.
- Set decorative root rotation to zero if orientation communicates gameplay.
- Check for accumulated rotation on pooled child transforms.
- Verify the collision surface actually covers most of the effect footprint.
- Adjust only the small surface offset needed to prevent z-fighting; large offsets
  make the marker float.

### Material edit has no visible effect

- Runtime `MaterialPropertyBlock` values may override that shader property.
- Confirm the prefab references the material you edited.
- Confirm a builder did not regenerate the material after the edit.

### Effect works in High but vanishes in Low

- Check `Essential Gameplay Cue`, `Secondary Effect`, and `Minimum Quality`.
- Check whether the component reduces `maxParticles` to an unreadable result.
- Check whether the quality asset disables its only light-based read.

### Effect plays in one path/mode but not another

- Inspect `Feedback Bindings` and their filters.
- Confirm the selected cue exists and has the expected prefab.
- Check `Min Replay Interval` and whether a loop was properly stopped.

### Repeated shots look like one event

- Reduce trail lifetime/width.
- Shorten muzzle flash and impact persistence.
- Reduce additive emission that visually merges neighboring rounds.
- Ensure each damaging event actually emits its own presentation cue.

## 18. Verification after tuning

Run the relevant EditMode presentation tests in Unity Test Runner:

- `AutomaticCannonPresentationTests`
- `RocketLauncherPresentationTests`
- `FlamethrowerPresentationTests`
- `MortarPresentationTests`
- `RotatingBladePresentationTests`

Also run mechanic-specific tests when changing radius/path-coupled presentation,
such as Mortar terrain/trajectory tests or Cannon fire-logic tests.

Automated tests protect references, pooling contracts, routing, and important
presentation/gameplay relationships. They do not replace the manual sandbox
matrix; readability, occlusion, perceived cadence, and comfort still require a
human visual pass.

## 19. Definition of done for a tuned cue

A cue is ready when:

1. Its binding selects it in every intended mode/path case.
2. Its primary visual accurately represents collision, direction, radius, and
   number of damaging events.
3. Internal, particle, trail, and pool lifetimes end cleanly.
4. It remains readable at every heat preset.
5. It behaves correctly while the player and target move.
6. It has a deliberate Low/Medium/High degradation path.
7. Reduced Flash and Reduced Shake preserve the gameplay read.
8. Pool and suppression metrics remain healthy after warm-up.
9. The relevant automated tests pass.
10. The generated-asset builder cannot unexpectedly erase the accepted tuning,
    or its recipe has been updated to reproduce it.
