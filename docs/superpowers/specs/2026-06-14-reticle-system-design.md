# Weapon Reticle System Design

## Goal

Replace the single generic crosshair with weapon-specific reticles that communicate how each manual weapon aims. Reticle visuals must follow the current manual weapon, remain readable at different resolutions, and accurately reflect mortar landing and rocket lock progress.

## Weapon Reticles

### Rotating Blade and Flamethrower

Display two opposing rectangular brackets:

- Left bracket opens toward the center.
- Right bracket opens toward the center.
- The shape is wide and horizontal, matching the supplied reference.
- It remains fixed at screen center.

### Automatic Cannon and Rocket Launcher

Display a compact circular outline with a center dot.

- The circle and dot remain fixed at screen center.
- This is also the rocket launcher's idle reticle before and after its active ability.

### Mortar

Display two coordinated indicators:

- A small downward-facing `V` fixed at screen center.
- A world-space ground marker at the predicted terrain landing point.

The ground marker contains:

- A bright inner landing ring and center point.
- A softer outer ring scaled to the mortar's current manual explosion radius.
- The outer ring includes `ProjectileAreaSize` scaling.

The terrain marker ignores enemies when predicting the landing point. It remains on terrain beneath or behind moving enemies even when the actual shell would collide with an enemy first.

### Rocket Launcher Active Ability

While Q is held:

- Replace the normal circle-and-dot reticle with four corner brackets.
- The initial five requested locks use the smallest bracket frame.
- Each additional actual rocket lock advances the frame toward its next size.
- Visual size smoothly eases between lock steps.
- Full lock uses a cinematic frame equal to 70 percent of the reference canvas width.
- Releasing or cancelling Q returns immediately to the normal circle-and-dot reticle.

Expansion is based on actual assigned rocket locks, not elapsed hold time. Repeated rockets assigned to elites or bosses count toward progress because they are real rockets in the prepared volley.

## Architecture

### ReticleHud

`ReticleHud` remains the owner of all reticle presentation. It will:

- Resolve the active `WeaponManager`.
- Read the current manual `WeaponInstance`.
- Switch visual groups based on `WeaponType`.
- Read rocket charging and lock progress through a small read-only ability status interface.
- Read mortar terrain prediction from `ReticleAimProvider`.
- Create the screen-space and world-space primitives at runtime.
- Expose colors, thicknesses, dimensions, easing speed, and marker materials as inspector fields.

Weapons will not instantiate or position HUD elements.

### Rocket Lock Status

Add a dedicated read-only rocket targeting status interface that exposes:

- Current assigned rocket count.
- Current maximum rocket count.

`RocketLauncherWeapon` will update those values whenever its target plan changes. `ReticleHud` converts the normalized progress from the initial five locks to the current maximum into a target frame size.

### Mortar Trajectory Prediction

Extract the mortar parabola equation into a shared utility used by both:

- `MortarShellImpact` for shell movement.
- `ReticleAimProvider` or a dedicated mortar predictor for the terrain marker.

Prediction will sample and sweep the same quadratic trajectory used by the shell. It will:

- Start at the projectile spawn.
- Use the current aim direction, base range, travel target, arc height, and shell collision radius.
- Ignore the player and enemy/damageable colliders.
- Stop at the first valid map or terrain collision.
- Hide the marker when no terrain collision can be predicted within the shell failsafe interval.

The actual shell collision remains unchanged: it still explodes on the first valid physical collision, including enemies.

## Data Flow

1. `WeaponManager` updates the manual weapon and exposes its behavior/runtime to the HUD.
2. `ReticleHud` reads the manual weapon type every frame.
3. For ordinary weapons, it activates the matching screen-space reticle group.
4. For rocket active charging, it reads assigned and maximum locks, computes normalized progress, and eases the bracket frame toward the corresponding size.
5. For mortar, it requests a terrain-only predicted impact point and places the world marker there.

## Visual Construction

The screen-space reticles continue using a `ScreenSpaceOverlay` canvas and simple runtime-generated `Image` elements.

The mortar marker uses world-space ring renderers aligned to the terrain hit normal. It must:

- Follow uneven terrain without clipping excessively.
- Sit slightly above the surface to avoid z-fighting.
- Disable itself for non-mortar weapons.
- Avoid colliders and raycast interaction.

All reticle elements use a bright foreground color with a dark offset shadow or outline for contrast.

## Configuration

Reticle tuning remains on `ReticleHud`, not `WeaponData`, because these values define shared HUD presentation rather than weapon balance.

Inspector fields include:

- Line color, shadow color, thickness, and sorting order.
- Bracket size and arm length for blade/flamethrower.
- Circle diameter and center-dot diameter for cannon/launcher.
- Mortar `V` size.
- Mortar landing ring size, ring thickness, colors, and surface offset.
- Rocket active minimum and maximum frame dimensions.
- Rocket frame easing speed.

Weapon balance values continue to determine:

- Mortar explosion radius and `ProjectileAreaSize` scaling.
- Rocket initial lock count and maximum lock count.

## Failure Handling

- If no manual weapon exists, hide all reticle groups.
- If the camera, manager, or aim provider is unavailable, retain the appropriate centered reticle but hide the mortar ground marker.
- If rocket charging ends because of weapon cycling, insufficient ammo, or cancellation, restore the normal launcher reticle.
- Destroy runtime-generated materials and objects with the owning HUD to avoid editor play-mode leaks.

## Verification

Automated tests should cover:

- Weapon type to reticle-mode selection.
- Rocket normalized lock progress, including a maximum equal to the initial lock count.
- Mortar trajectory sampling using the same equation as shell movement.
- Terrain prediction filtering enemies while accepting map colliders.

Play-mode verification should confirm:

- Each weapon switches to the correct reticle.
- Rocket Q begins at the compact frame, eases outward only as locks increase, reaches the cinematic cap, and resets on release.
- Mortar rings remain attached to terrain and the outer ring matches explosion radius.
- Enemies crossing the mortar path do not move the ground marker, but shells still collide with them.
- Reticles remain centered and proportionate at common 16:9 and ultrawide resolutions.
