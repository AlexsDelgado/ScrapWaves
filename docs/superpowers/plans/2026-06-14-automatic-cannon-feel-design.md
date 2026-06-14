# Automatic Cannon Feel Design

## Goal

Keep normal cannon bursts visually recognizable as lines while removing perfect projectile alignment, and prevent held manual fire from producing an unintended 25 projectiles per second at base attack speed.

## Approved Behavior

- Automatic and manual line bursts use a small `1.5` degree per-projectile scatter on both local axes.
- Projectile spawn positions remain evenly spaced along the burst centerline.
- Manual fire produces `2` complete five-round volleys per second at base attack speed.
- Attack-speed and weapon-rate multipliers continue to scale the time between manual volleys.
- The active ability keeps its separate shotgun-style scatter behavior.

## Data Design

Add the following fields only to `AutomaticCannonTuning`, so they appear only when `WeaponType.AutomaticCannon` is selected:

- `CannonBurstProjectileScatterDegrees`
- `CannonManualBurstsPerSecond`

The custom `WeaponDataEditor` exposes both controls in the Automatic Cannon section.

## Runtime Design

Use a small pure `AutomaticCannonFireLogic` helper for deterministic cadence and scatter calculations. `AutomaticCannonWeapon` samples a random point inside a unit circle for every projectile and passes it to the helper, allowing EditMode tests to verify the geometry without relying on random state.

The automatic burst keeps its existing burst-wide accuracy offset, then adds the subtle per-projectile deviation. Manual fire uses the same subtle deviation without the automatic mode's larger burst-wide offset.

## Verification

EditMode tests cover:

- Two base manual volleys per second produce a `0.5` second interval.
- Attack-speed and weapon-rate multipliers shorten that interval.
- Zero scatter preserves direction.
- Horizontal and vertical samples both affect projectile direction within the configured angular limit.

