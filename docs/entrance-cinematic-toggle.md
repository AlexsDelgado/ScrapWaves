# Entrance cinematic launch switch

Open Assets/Scenes/TitleScreen.unity and select the root GameObject **Developer - Entrance Cinematic**.
In **Entrance Cinematic Launch Settings**, change **Play Entrance Cinematic**, then save the scene.
The configured value is **OFF**. This is authoring data used in Editor playtests and builds; it creates no player-facing setting and writes no player preference.

- **OFF:** menu Play and tutorial replay immediately finalize the existing grounded player/camera pose, then enter the existing tutorial or first-weapon selection.
- **ON:** those same menu routes retain the current entrance cinematic, including Space / gamepad Start skipping.
- **Direct GameplayScene entry:** retains its existing cinematic-enabled default for animation/gameplay authoring. The menu setting is a consumed launch request, not a persistent global preference.
- **Retry:** retains the existing cinematic skip and tutorial replay rules, regardless of the menu switch.
- **Fresh saves and tutorial replay:** eligibility, tutorial progression, starting weapon and save data are unchanged.
- Returning to menu, another navigation request, and subsystem registration clear any pending choice. Each menu launch samples the serialized switch again, including during Play Mode. A Play Mode edit is temporary until made/saved outside Play Mode.
- If the setting component is accidentally removed from TitleScreen, menu launches default to OFF. Keep the GameObject/component authored and use its checkbox; it has no UI or renderer.

The launch policy lives in runtime code. SceneNavigation.Load(Play) captures the menu value after validating the destination, covering the normal ScrapSceneTransition route and its direct fallback. EntranceCinematic.PlayEntrance consumes the request and only gates timed playback. It still captures/finalizes the presentation, restores camera/rigidbody/behaviours, clears effects, retains the input-release guard and transfers pause ownership through RunStartWeaponChoice. No cinematic pose or weapon stat is changed.

## Validation steps

Use an isolated save for automated runs via SCRAPWAVES_TEST_SAVE_DIRECTORY.

1. With a completed tutorial save, launch from TitleScreen with OFF. The first-weapon selector should appear without the 13-second entrance, remain paused until selection, equip once, and restore camera/movement/aim. Holding the selection click must not fire; release and click again to fire.
2. Return to TitleScreen, enable the switch in Play Mode, and launch again. The cinematic should play; natural completion and Space/Start skip must reach the same selection and grounded camera endpoint.
3. Return to menu again; the scene-authored OFF value should return. Launch once more and verify immediate selection.
4. Enter GameplayScene directly after a consumed menu request, and from a fresh Play Mode session. Its cinematic should play. Retry should immediately finalize instead.
5. With a fresh isolated save, test OFF and ON menu launches. Welcome starts after finalization, with the tutorial weapon and no normal first-weapon selector. Test explicit replay after completion, and retry an incomplete replay.
6. Repeat Play Mode with domain reload disabled. Confirm OFF is re-read, no stale pending request survives, and pause/time scale/input/camera control are restored.
7. Inspect the build's TitleScreen launch with OFF, then ON in a separate test build. Only the entrance presentation changes; gameplay remains reachable.
8. Restore and save OFF. The root authoring object must have only Transform and launch settings, with no player-facing UI.

Automated coverage: EntranceCinematicLaunchSettingsTests, EntranceCinematicLaunchRuntimeTests, existing EntranceCinematicTests, EntranceCinematicRuntimeTests, navigation tests and tutorial save tests.
Native Inspector preview: **Tools > ScrapWaves > Entrance Cinematic > Capture Launch Inspector** in a graphical Editor. The capture uses the real Unity Inspector; headless execution reports unavailable instead of generating a mock.

## Recorded validation

Unity 6000.3.13f1: 52/52 focused tests passed against the BugFrank baseline plus only the twelve toggle files in an isolated project. The menu route, natural ON playback, OFF finalization, skip/release guard, retry, direct gameplay entry, fresh-save tutorial start, explicit replay, and repeated Play Mode with domain reload disabled were exercised.

Runtime input checks use virtual Input System devices and real button callbacks. They cover camera/body/control handoff and held-selection-click suppression; they do not cover OS pointer hit-testing. Tutorial tests cover startup/replay eligibility, not completing the full lesson. A standalone player build and physical gamepad were not tested; build applicability is provided by the runtime component and serialized scene value. The current shared weapon-stat work was excluded from this validation and commit.

The additional tutorial placement comparison measured the solid capsule, actual baked skinned-mesh vertices and visible threat-indicator mesh after OFF and natural ON launches. Both matched within 0.04 m: terrain Y -1.0, capsule clearance 0.0 m, visible feet clearance about 0.02 m, and no baked vertices below the terrain. The threat anchor was about 0.308 m above the hips and 0.544 m below the head in both modes. The reported buried-player/high-indicator issue did not reproduce at this initial Welcome checkpoint. Geyser interaction and later tutorial recovery remain for the separate tutorial investigation; no related production changes are included.
