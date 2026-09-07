# Compactor escape test

Open **Tools > ScrapWaves > Open Compactor Escape Test**, or open
`Assets/Scenes/Testing/WeaponTestingSandbox.unity`. The GameFeel variant also contains the compactor.

1. Enter Play Mode. The compactor is to the right of the spawn area at `(7, 0.06, 7)`.
2. In the debug panel, click **Start Escape Test**. This grants the sandbox's required keys and starts the normal exit charge.
3. The door and hydraulic pistons open over the exit's five-second charge. At **100%**, the door holds fully open and its doorway blocker switches off. The test does not automatically end the run.
4. Click **Reset Door** to close and replay. During normal gameplay, **E** near the door starts charging after collecting the keys; a second **E** after it is ready completes the escape.

The imported FBX has one non-looping Generic clip, **Open**. `CompactorDoorPresentation` samples it from `ExitDoor.ChargeNormalized`, so changing the charge duration keeps the animation synchronized. The full-open pose also holds in the `Used` state. No Animator Controller is needed.

Reusable prefab: `Assets/Prefabs/Level/Compactor/CompactorExitDoor.prefab`.
Its local **+Z** points out of the doorway. Assign a scene `LevelExitObjective` to its `ExitDoor` when placing it in another level. This change places the new art in the two test arenas; existing production door placements are preserved.

The model uses plain URP color materials. **Black Suction Opening** is a separate opaque, double-sided quad behind the door. Its procedural shader uses no textures; **Air Flow Color** and **Flow Speed** can be adjusted on `CompactorSuction.mat`. Runtime activity increases with escape progress and remains active while ready.

**Tools > ScrapWaves > Setup Compactor Escape Test** regenerates the material mappings, prefab, and scene references. Unity Edit Mode tests filtered by `Compactor` validate the imported mechanical poses, end hold, reset, collision, key gating, and references in both arenas.
