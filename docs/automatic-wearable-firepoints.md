# Automatic wearable fire points

The player and weapon testing sandbox use `WearableWeaponMounts` in Resources. Each weapon type has one authored model, body position, and muzzle. The existing three-slot inventory limit is unchanged.

| Weapon | Model | Body attachment | Model height before attachment tilt (Unity units) |
| --- | --- | --- | --- |
| Rotating blade / sword | BeltCharm(Sword) | Back-left belt, visible from behind | 0.16 |
| Automatic cannon | ScrapFeeder(Cannon) | Seated on top of the right shoulder | 0.21 |
| Flamethrower | ACBracelet(Flame) | Top of the left forearm, tilted to follow the arm | 0.086 |
| Rocket launcher | BackPipe(Rocket) | Right side of the back | 0.55 |
| Mortar | ShoulderChute(Mortar) | Left side of the back | 0.49 |

The actual player mesh is 1.869 units tall. All five models were checked together against it, including all ten pairs of renderer bounds. Muzzles sit outside their model outlets; the rocket origin clears the top of the tilted lip before its vertical launch.

Mounting edges sit slightly inside the actual body surface so the attachments remain seated. The forearm attachment rolls 53 degrees around the body's forward axis to follow the arm while preserving its forward-facing outlet. The waist charm uses 14 degrees of pitch and 11 degrees of yaw to follow the curved belt and keep its face visible and upper hook attached. The two back pieces are approximately 40% larger than the previous reduced-size layout.

Only owned weapons create attachments. Switching to manual keeps the attachment visible, darkens its green indicator, and moves firing to the existing Main Weapon Fire Point. Switching to automatic lights the indicator and restores that weapon's own body muzzle. Removing the last copy of a weapon removes its model. Duplicate sandbox slots share one model; its indicator is on when any copy is automatic.

Only material slots named `AutoIndicator…` receive runtime color and emission overrides. Shared materials and the rest of each model keep their authored appearance. The wearable meshes stay fixed while aiming and firing, preserving their separation. The current player model has no animated skeleton; positions are relative to the player root.

## Approved weapon adaptations

- The automatic cannon selects targets in the body's forward 180 degrees, independent of the camera. Pending burst rounds stop if the target moves behind the body or the player turns away. Manual cannon aim is unchanged.
- Automatic rockets start with one rocket per burst, with heat adding one at each 25%, 50%, and 75% threshold. They exit the back pipe upward with launches 0.11 seconds apart, clear the player's height, then curve toward the acquired target point. Switching to manual cancels any unlaunched automatic rounds. Manual and active-ability rockets retain straight flight.
- The waist charm activates the existing player-centered rotating-blade orbit. Orbit position, range, contact damage, manual slashes, and active abilities remain unchanged.
- The flamethrower emits its existing body-forward cone from the left-arm muzzle. Mortar shells follow their existing arcs from the back chute.

## Editing and checking attachments

Use **Tools → ScrapWaves → Build Wearable Weapon Mounts** after intentionally changing imported models or the builder's authored placement values. It regenerates the wearable prefabs, URP materials, and catalog, and wires the player prefab. Source FBXs are imported without modifying the originals.

In the weapon testing sandbox, equip each type, switch manual slots, use Force Automatic, remove slots, and try duplicate selections. Check that each automatic attack uses its matching model and that only the manual weapon's indicator is dark. Check the rear view with rocket, mortar, and sword equipped together, then the arm and shoulder combination.

The editor regression tests cover ownership, mode changes, indicator material isolation, duplicate selections, all-five attachment separation, cannon targeting, and automatic rocket launch and pooling behavior.
