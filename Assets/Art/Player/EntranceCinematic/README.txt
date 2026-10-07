Entrance cinematic integrated into BugFrank, 2026-10-07

Merged source commit f8cf0e01e076f6916fe63ac718d0abb4ca049fcd into BugFrank after c4f804dfd1a58d6752edcc9f301ad41506fb6364. The 13-second authored sequence retains the approved compact landing, visible palm push / one-knee rise, corrected forward elbow flex from 8 seconds, relaxed idle ending and behind-player camera return.

The scene resolution preserves all 1,693 existing BugFrank object documents and adds the cinematic's 98 documents and one root entry. Runtime integration retains the newer capsule support probe, movement and side-slide behavior, obstruction handling, weapon timing and startup guards. Grounded synchronization uses the current support probe without emitting a duplicate landing event.

Validation: 196/196 combined cinematic, pause, player-animation, weapon-manager, threat, movement/camera, side-slide and scene-navigation checks passed. The actual natural startup test verifies body/head/camera position continuity within 3 cm at handoff and resume, behind-player orientation, grounded placement, relaxed idle carry, threat presenter restoration and one selected weapon. Skip/retry, held selector click / fresh fire and gameplay damage/timer pause behavior remain covered.

All authored cinematic assets match source Git blobs; no motion, effects or camera choreography pass was made. The existing private Drive preview remains the motion reference; no new merged-project video was captured or uploaded. It was recorded from the earlier isolated scene, so it does not represent all newer BugFrank environment/gameplay changes.
Preview: https://drive.google.com/file/d/1V0lW0A0Bxm7_xp2Xc-dKZ-ccvBvyg2KQ/view?usp=drivesdk
Last verified preview revision: 0B3uxkkSEMQBcd2JGaFRyQi96WUJ0UzJiQWlsZXVvR3BtbU1VPQ

Unity Editor validation only; no standalone build, full repository-suite rerun, physical input/focus test or exhaustive mesh-intersection proof. Existing Library preview files remain old version 1 after prior native replacement failure; no Library write attempted for this merge.

Combined tests, protected-file snapshots, scene resolution and final integration evidence:
C:\Users\franc\Documents\Codex\2026-10-06\task\entrance-cinematic-approved-merge
