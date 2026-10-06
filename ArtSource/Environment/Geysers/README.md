# Geyser mound models

Run `build_geysers.py` in a disposable Blender background process. It builds the editable `ScrapWaves_Geysers.blend` and exports both visual models to `Assets/Art/Environment/Geysers/Models`. Normal regeneration preserves both approved collision FBXs byte-for-byte. `collision-profiles.json` retains their exact original source geometry; `--export-collision` is a deliberate opt-in regeneration operation.

Both variants are simple, irregular mounds of compacted ordinary junk. Overlapping bent sheet metal, two corrugated roofing offcuts and a rough metal lip replace the soil/stone volcano appearance. Trash has muted rust, off-white salvaged panels, a crushed household pail and discarded pipe. Hot air uses restrained gray/heat-darkened metal, rust and short duct/pipe offcuts. There are no machinery assemblies, sci-fi details, flames or eruption schedules. Continuous updrafts and entry bursts remain authored in Unity.

Meter units, flat low-poly shading, centered origin. Mound bottom is -1 m and rim approximately +1 m, matching the approved launch-root height. The two editable collections overlap at the origin intentionally: hide the other collection while editing. Exports use Unity Y-up; inspect final geometry counts in `geometry.json` (1,284 trash / 1,294 hot-air visual triangles, unchanged 310-triangle collision meshes).

Unity retains the existing launch-root transforms and destination references. The root collider becomes the always-active opening trigger; the separate mound collision mesh provides the solid walkable slope. Receiving platforms remain distinct from geysers.

Use `ScrapWaves/Level/Refresh Geyser Visuals Only` to update imported model material mappings without regenerating the scene, colliders or particle presentation. `Author Approved Geysers` remains the full initial-authoring operation.
