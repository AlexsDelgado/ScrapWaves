# Automatic Cannon Presentation Polish Design

## Status

Approved first weapon-polish vertical slice.

This document defines the intended presentation and acceptance criteria. It does not change weapon balance or decide whether the experimental PSX renderer will ship.

## Goal

Make every Automatic Cannon mode immediately readable and satisfying through coordinated muzzle, projectile, impact, audio, recoil, and camera feedback while preserving the current mechanics.

The result should establish a rendering-agnostic presentation pattern that can later be reused by the Rocket Launcher, Mortar, Flamethrower, and Rotating Blade.

## Constraints

- `main` and the standard URP renderer are the implementation baseline.
- No runtime or asset dependency on the experimental PSX renderer.
- Effects must remain legible if the PSX renderer is evaluated again later.
- Do not change damage, cadence, ammo cost, range, heat scaling, targeting, or upgrade mechanics during this slice.
- Do not require a visible weapon model. All launch feedback must work from the existing player fire point.
- High-rate fire must use pooling and rate-limited feedback.
- Color cannot be the only difference between upgrade paths.

## Current Mechanical Baseline

| Mode | Current behavior |
|---|---|
| Base automatic | Three-projectile line burst, `0.05s` between shots, whole-burst accuracy spread plus subtle per-projectile scatter |
| Base manual | Five-projectile line burst, `0.05s` between shots, two volleys per second before stat scaling |
| Base active | Twenty-projectile minimum scatter burst, with additional projectiles from heat |
| Continuous Fire automatic/manual | Single-projectile continuous cadence with heat-scaled speed |
| Continuous Fire active | Forty projectiles per second for two to three seconds depending on heat |
| Head Hunter automatic | Slower piercing shot with elite/boss scaling |
| Head Hunter manual | One shot per click, piercing falloff, weak-point multiplier |
| Head Hunter active | One-second tracked charge followed by an effectively unlimited piercing shot |

## Feel Direction

The cannon should feel like a heavy, improvised industrial weapon:

- mechanically forceful rather than sleek;
- short, hard transients instead of soft energy effects;
- readable shot rhythm even when several weapons are active;
- heavier manual and ability feedback without making automatic fire exhausting;
- precision and restraint for Head Hunter;
- escalating mechanical urgency for Continuous Fire.

## Feedback Hierarchy

Feedback intensity must follow this order:

1. Head Hunter active release
2. Base or Continuous Fire active
3. Head Hunter manual weak-point hit
4. Base manual volley
5. Head Hunter automatic shot
6. Base automatic burst
7. Continuous Fire normal shot

Routine high-rate fire must not produce stronger camera or audio feedback than abilities.

## Presentation Matrix

### Base Automatic

- A compact warm muzzle flash for every projectile.
- A narrow, short-lived tracer that preserves the current three-shot line rhythm.
- Small directional recoil impulse per projectile.
- One mechanical burst body sound plus quieter per-projectile transients.
- Small impact spark and brief hit flash on a successful enemy impact.
- Critical impacts add a distinct bright core and sharper audio transient.

The three shots must read as one burst rather than three unrelated weapons firing.

### Base Manual

- Uses the same visual family as automatic fire.
- Muzzle flash and tracer are slightly larger and brighter.
- Stronger first-shot recoil followed by smaller impulses for the remaining volley.
- A heavier volley-start sound and a short mechanical tail.
- Impacts are more visible than automatic impacts, but remain below ability intensity.

The five-shot volley must feel deliberate and heavier without changing its cadence.

### Base Active

- One clear ability-start flash at the fire point.
- A wide but readable fan of tracers.
- One strong camera impulse at activation; do not shake once per projectile.
- A layered blast/mechanical sound that communicates the full scatter event.
- Standard impacts may be visually reduced when many arrive in the same frame.

The active must read as one power event, not twenty independent maximum-intensity shots.

### Continuous Fire

- A tighter, smaller muzzle flash than the base cannon.
- Thin hot tracers with a stable visual direction.
- A continuous mechanical loop with a clean start and stop.
- Normal shots use low camera intensity with a fixed maximum impulse rate.
- Heat increases pitch, emissive intensity, or cadence detail gradually.
- The active adds a distinct start accent and sustained barrel/muzzle effect.

The path must read through cadence, shape, and audio—not only through orange coloration.

### Head Hunter

- A thin, high-contrast precision tracer rather than a broad beam.
- A restrained charge visual that grows toward the fire point and aim direction.
- A dry, high-energy release sound with a short tail.
- Normal impacts create a compact puncture flash.
- Each pierced target receives delayed impact feedback when damage actually arrives.
- Weak-point hits add a unique impact shape, reticle confirmation, and audio accent.
- The active release receives the strongest single camera impulse in the cannon kit.

The path must read through precision, timing, and impact shape—not only through blue or white coloration.

## Shared Presentation Responsibilities

The cannon behavior remains responsible for deciding when gameplay events occur. A presentation layer is responsible for rendering and playing them.

Required cue boundaries:

- projectile successfully launched;
- burst or volley started;
- ability started and completed;
- sustained loop started, updated, and stopped;
- charge started, updated, cancelled, and released;
- projectile or piercing shot successfully hit;
- critical or weak-point hit confirmed.

Presentation calls must not decide damage, ammo, cooldowns, targeting, or cadence.

## Rendering Requirements

- Use standard URP-compatible unlit or particle materials.
- Do not sample PSX renderer globals or require VNTG shaders.
- Avoid effects whose readability depends only on bloom.
- Prefer compact silhouettes, value contrast, and motion.
- Tracers and flashes must remain readable against both bright sky and dark ground.
- Avoid large opaque quads that hide enemies.
- All frequently spawned effects must be pooled.
- Materials must be pre-authored or cached; no per-shot `new Material`.

## Audio Requirements

- Weapon audio uses dedicated pooled voices rather than changing pitch on the global shared SFX source.
- Avoid one full-volume sound per projectile during high-rate fire.
- Support small clip variation and bounded pitch/volume variation.
- Continuous loops must stop on weapon state changes, ability completion, cancellation, scene unload, and owner disable.
- Impact audio must be rate-limited during the scatter and Continuous Fire abilities.
- Existing global SFX and music volume controls remain authoritative.

Final audio assets are not required to build the runtime hooks. The current `shoot.wav` may be used only as a temporary integration clip.

## Camera and Recoil Requirements

- Camera feedback is additive to the existing third-person camera movement.
- Recoil must never change the gameplay aim direction.
- Sustained fire uses an impulse rate limit so high attack speed does not continuously displace the camera.
- Ability impulses are authored separately from normal shot impulses.
- A master camera-feedback scale must allow the effect to be reduced to zero.
- No hit stop is included in the first slice; it can be evaluated after camera and audio feedback are working.

## Performance Budgets

- Zero new `GameObject` or `Material` allocations per steady-state shot after pools warm.
- No per-shot scene searches.
- No per-shot delegate or closure allocation.
- Normal cannon feedback must remain stable at the Continuous Fire active rate of forty projectiles per second.
- Repeated impacts in one frame may be coalesced or visually capped.
- Presentation pool exhaustion must fail quietly without affecting damage or firing.

## Sandbox Acceptance Checklist

Test in `Assets/Scenes/WeaponTestingSandbox.unity`.

### General

- [ ] Every cue originates from the current fire point or confirmed impact point.
- [ ] Presentation never fires when projectile spawning fails.
- [ ] A missing profile, clip, prefab, camera receiver, or audio source does not break firing.
- [ ] Disabling presentation leaves mechanics unchanged.
- [ ] Effects remain readable over the sandbox floor and bright sky.
- [ ] No presentation objects remain after leaving play mode.

### Base Cannon

- [ ] The automatic three-shot rhythm is visually and audibly obvious.
- [ ] The manual five-shot volley feels heavier than automatic fire.
- [ ] The base active reads as one ability event.
- [ ] Critical impacts are distinguishable without relying only on color.

### Continuous Fire

- [ ] Normal fire remains readable at minimum and maximum heat.
- [ ] Audio and camera feedback do not become harsher solely because the fire rate increases.
- [ ] The active loop starts and stops exactly with the active barrage.
- [ ] No loop or sustained VFX survives weapon cycling or cancellation.

### Head Hunter

- [ ] Automatic, manual, weak-point, and active releases have distinct weight.
- [ ] The charge follows aim direction for the full one-second gate.
- [ ] Pierced targets receive feedback in projectile travel order.
- [ ] Weak-point feedback occurs only when weak-point damage is confirmed.
- [ ] The active is the strongest cannon event without obscuring targets.

### Stress

- [ ] Spawn a group and run Continuous Fire active at full heat without visible pool churn.
- [ ] Run the base scatter active into a clustered group without excessive audio stacking.
- [ ] Cycle weapons during a loop and during Head Hunter charge without orphaned feedback.
- [ ] Repeat abilities after cooldown without accumulating presentation objects.

## Automated Acceptance

- New presentation-specific tests must run in a separate green fixture.
- Existing Automatic Cannon mechanic tests must continue to pass unchanged.
- Tests must cover missing-profile safety, cue selection, loop lifecycle, pool reuse, and feedback rate limiting.
- The existing `WeaponUpgradeEffectTests` baseline is currently `60/78` passing when run alone. This slice must not increase its eighteen known failures.
- Any existing failure in a touched behavior must either be fixed or explicitly documented before merge.

## Out of Scope

- Final weapon mesh, player firing animation, or muzzle socket art.
- Balance changes.
- Final PSX renderer integration.
- Global combat hit stop.
- Polish for the other four weapons.
- Final production audio sourcing and mastering.
