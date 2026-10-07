Entrance cinematic elbow correction, 2026-10-06

Authored sequence: 13 seconds. Fresh actual runtime preview: 359 frames encoded at nominal 24 fps, 14.9583 seconds including weapon selection and resumed gameplay.

Corrected the backward-looking elbow bend from 8 seconds through the relaxed ending pose by moving the authored idle elbow pole behind the shoulder-to-wrist line. Both forearms now flex forward while the hands remain lowered. The accepted landing, right-palm recovery, timing, effects and camera return are preserved. Earlier animation through 7.45 seconds has zero position difference and 0.0000094 degree maximum rotation difference; non-arm curves have zero difference from the saved accepted idle clip. Runtime animation driver, weapon manager, movement, camera and approved landing reference bytes are unchanged in this revision.

Validation: 25/25 focused cinematic checks passed, including both-arm signed elbow geometry from 8-13 seconds at 60 Hz (602 checks), recovery continuity, support/clearance, skip/retry, startup pause guards, release-before-selection and selector-click/ammo/fresh-fire behavior. Actual startup recording passed with zero body/head/camera position jumps at natural handoff. The unchanged idle carry remains through weapon selection and resume.

Visual review: actual runtime full sequence, get-up, camera return and selector/resume frames; CPU-skinned front and both side angles; before/after elbow views; fifteen frames decoded from the final MP4. Unity Editor only; no standalone build, current full repository-suite rerun, physical input/focus test or exhaustive self-intersection proof. Capture forces focus, uses synthetic lifecycle input, has variable rendering cadence and composites authored SFX.

Updated private Drive preview: https://drive.google.com/file/d/1V0lW0A0Bxm7_xp2Xc-dKZ-ccvBvyg2KQ/view?usp=drivesdk
Drive file ID: 1V0lW0A0Bxm7_xp2Xc-dKZ-ccvBvyg2KQ
Current revision: 0B3uxkkSEMQBcd2JGaFRyQi96WUJ0UzJiQWlsZXVvR3BtbU1VPQ
Byte count: 2863190
SHA256 (local): 98e7fefacca612e800b9621d63167d76ffae05e7964471be2e6e7b2de21c862b
Fresh metadata/revisions confirmed same ID, matching byte count, new revision, unchanged parent and private owner-only access. No remote checksum is exposed.

Native Library replacement failed before preparation: Library prepare_uploads is not available. Video libfile_e0806be2d564819192db6deec85afac7 and get-up image libfile_079b5d92ba248191a9a4cd3b87eb2a8c remain confirmed version 1 and contain the old preview. The existing private Drive replacement is the authorized fallback.

Current video, get-up/elbow frames, final tests and exact evidence:
C:\Users\franc\Documents\Codex\2026-10-06\task\entrance-cinematic-elbow
