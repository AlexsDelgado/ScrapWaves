# Crafting station rotation

## Designer workflow

1. Outside Play Mode, drag `Assets/Prefabs/Props/CraftingStation.prefab` into the gameplay scene and position it. Keep its root enabled. It automatically joins that scene's rotation; no station list or manager reference needs updating.
2. On **Crafting Station > Station rotation**, edit **Initial Break Chance Percent** (default **0**) and **Break Chance Increment Percent** (default **5**). The increment is in percentage points: 0%, 5%, 10%, and so on. Edit the prefab for shared defaults. Existing prefab instances inherit those values unless an intentional instance override is present; inspect overrides before changing individual tables.
3. Keep **Participates In Rotation** enabled for normal tables. Tutorial authoring opts its own table out and keeps it usable without break rolls.
4. Keep the authored **Active Visual**, **Broken Visual**, interaction point, collider, and E prompt references intact. Inactive tables remain visible with the broken model and a grey E key with a cross. Runtime switches the saved visual objects; it does not generate UI or meshes.

Broken and inactive are the same recyclable state. Do not disable a station's root to mark it broken: disabling removes that placement from the available pool.

## Confirmed behavior

- A scene starts with one randomly selected normal table active. Other normal tables start broken/inactive. A newly added table enters inactive while an active table already exists.
- Each completed transaction rolls the table's current break chance once. If it survives, add the configured increment and clamp at 100%. A 0% roll cannot break; a 100% roll always breaks.
- On break, finish the transaction and close its crafting session, then guide the player toward the replacement when guidance is eligible. Replacement selection gives every other available inactive placement equal probability, anywhere in the scene. Previously broken tables can return. With only one table, it reactivates itself and the session still closes.
- Every activation resets that table's chance to its own initial value. Chance and station state reset with the scene/new run; they are not written to the meta save.

Completed uses include a successful ordinary upgrade, the final basic-tinkering weapon selection, and an advanced offer's acceptance or rejection. A configured zero-cost transaction still counts. Opening, browsing, closing, failed purchases, and revealing a paid but unresolved offer do not roll or increase chance.

Paid choices retain their originating table through presentation interruption. Resolve and grant the paid outcome before applying wear. If the source table is destroyed or unavailable, preserve the outcome and skip its wear; do not redirect wear to another active table.

The spatial arrow retargets the current active table while retaining crafting affordability, pause, tutorial, duration, and exit-priority conditions. It points directly through world space; it does not calculate a room, doorway, or traversal route.

## Editable art

The intact model is `Assets/Art/Level/Workbench.fbx`. The broken model is `Assets/Art/Level/WorkbenchBroken.fbx`, with editable source at `ArtSource/Level/WorkbenchBroken/WorkbenchBroken.blend` and its build script alongside it. Keep the imported broken model's transform beneath its placement wrapper: its single-mesh FBX root differs from the intact model's multi-object container. The E interaction presentation is authored in `Assets/Prefabs/Props/InteractPrompt_E.prefab`. Review both model states and the crossed-out prompt from the gameplay camera after art or placement changes.

**ScrapWaves > Crafting > Capture Station States** renders the saved prefab in an isolated editor preview scene without saving gameplay. The reviewed captures are `ArtSource/Level/WorkbenchBroken/unity-station-states.png` and `unity-broken-prompt.png`; these are controlled Unity renders, not a recording of player movement.
