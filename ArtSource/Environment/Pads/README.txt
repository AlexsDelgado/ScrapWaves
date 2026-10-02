ScrapWaves #175 — first Blender review pair

JumpPlatform: pale safety-yellow launch deck, three broad +Y chevrons,
four exposed compression springs, salvaged hydraulic guides and small service lights.
LandingPlatform: matching industrial frame, concentric receiving target,
stacked rubber cushions and impact bumpers. No launch chevrons.

Made with Blender 5.2.1 LTS. This is a static review pair, not a Unity scene change.
References inspected: ArtSource/Legs/scripts/piston_assisted_jumping.py,
Piston-assisted Jumping_render.png and Material_MetalPipe.png.

CONTENTS
ScrapWaves_PadPair_Review.blend: editable named parts in two model collections.
The REVIEW_STAGE_NOT_FOR_EXPORT collection contains only review camera, lights and floor.
exports/: one FBX and one self-contained GLB per model.
renders/: oblique, low approach, side silhouette and detail views.
build_pads.py: reproducible model authoring script.
geometry.json: Blender mesh measurements before export.

DIMENSIONS / ORIGIN
Both footprints are exactly 10 x 10m; height 1.998m (nominal original 2m).
Meter units, no unapplied source object scaling. Center origin, local bottom -1m,
top approximately +1m. In Blender Z is up and launcher chevrons point +Y.
FBX uses forward -Z/up Y; GLB converts to Y-up. Exported meshes are at origin,
with no stage offset. In the source .blend the roots have clearly visible review
offsets of X=-6.3m and +6.3m; reset these root locations to zero when working on
an individual model. Mesh parts are named and editable; no hidden external textures.

GEOMETRY
Jump: 2,906 mesh vertices / 5,404 triangles / 91 editable mesh parts.
Landing: 1,544 mesh vertices / 2,612 triangles / 89 editable mesh parts.
Each static export is a single mesh with seven material slots, not seven objects.
Exported vertex counts may increase at flat-shading, material and UV boundaries.
Exports have smart UVs, outward normals and flat faceted shading.

UNITY REVIEW
FBX exports are provided for Unity import; GLB is an interchange preview alternative.
Keep existing 10 x 2 x 10 box collider and gameplay script separate from these visuals.
Full-surface automatic activation is represented by the broad launch deck; no button
or small activation hotspot is modeled. Existing launch/destination logic is unchanged.
No collision meshes, animation, LODs, launch VFX or sounds are supplied yet.
Seven simple color materials per mesh keep the style editable. Unity URP may require
material remapping; small edge-light emission must be checked in the target renderer.
FBX material conversion, actual game lighting and camera readability have not been
verified in Unity. Renders use a controlled brown review floor, not the live game map.

No production code, scene, prefab, balance, HacknPlan, commit or push was changed.
