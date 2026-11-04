# Broken crafting workbench

`WorkbenchBroken.blend` is the editable source for the inactive/broken state. Individual bench parts remain separate. `build_workbench_broken.py` reproducibly derives the variant from the existing `Assets/Art/Level/Workbench.fbx`, which it reads without changing.

The variant keeps the same origin, source units, feet and practical footprint. Its height is deliberately lower: the split worktop sags, the lamp folds inward and is unpowered, a crate plank loosens, and severed power leads hang below the lamp. The split is actual geometry, so the state remains readable without color. There are no effects, particles, physics bodies or generated runtime objects in this asset.

## Deliverables

- `Assets/Art/Level/WorkbenchBroken.fbx`: combined game mesh, 17,831 vertices / 34,454 triangles / 12 material slots.
- `WorkbenchBroken.blend`: editable model at the original origin.
- `workbench-comparison.png`: rendered comparison of the actual original and exported-source geometry, with state labels.
- `geometry.json`: exact bounds, mesh counts, material names and import scale.

## Rebuild

Run from the repository root in PowerShell:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python ArtSource/Level/WorkbenchBroken/build_workbench_broken.py
```

Blender 5.1.2 was used. The script exports only the broken model; the comparison's original model, floor, lights, camera and labels are not included in the FBX. The saved source `.blend` precedes creation of that temporary review stage.

## Unity authoring

Use ModelImporter **Scale Factor 0.5**, matching the existing workbench importer. Export uses forward `-Z`, up `Y`. Place the imported model beneath a wrapper carrying the intact model's local transform, preserving the imported child's rotation and scale: the broken FBX has a single mesh root, while the intact FBX imports beneath a container. Keep the station's existing collision and interaction point independent of the visual swap. Do not use the broken silhouette as a replacement collision mesh.

The FBX's embedded material names begin with `Broken`. They are texture-free worn steel, charcoal, oxide, muted ochre, wood and rubber. The lamp glass is dark and has zero emission. Materials remain editable through Unity's normal extract/remap workflow.

Blender-space bounds: minimum `(-2.425, -1.11503, 0.04158)`; maximum `(2.37816, 1.075, 3.61258)`. The original footprint was `(-2.425, -1.11550)` to `(2.46650, 1.075)`; the damaged right edge moves inward rather than expanding collision footprint.
