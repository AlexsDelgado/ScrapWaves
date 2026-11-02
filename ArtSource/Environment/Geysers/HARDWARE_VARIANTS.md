# Optional fan and vent jump pads

Approved fan and four-louver vent shapes, uniformly reduced to 75% of the model drafts. Each footprint is 3.6 × 3.6 metres. Art scale is baked into the FBXs; place prefab roots at scale (1,1,1).

Place either prefab from `Assets/Art/Environment/Geysers/Prefabs/`:

- `FanJumpPad.prefab`
- `VentJumpPad.prefab`

Assign the receiving platform's **Transform with its solid Collider** to **Map Launch Pad → Landing** on the prefab instance. This is the existing launch workflow: the ballistic solver aims at the collider's top centre. The prefab intentionally has no scene-specific destination; an unassigned destination does not launch. Keep the existing 12-metre apex clearance unless a designer deliberately changes the route. Moving the destination updates the actual launch and directional particles. Rotating the hardware does not override an assigned world destination.

The **FlowPresentation → Geyser Vfx** Inspector retains colour, density, speed, streak length, spread, particle budget, opening radius and entry feedback controls. Both variants use the established hot-air wind ribbons, launch accent and overheat suppression. Player landing feedback stays on the existing player movement/presentation path. No landing platform, existing geyser or scene placement is replaced.

`WalkableSurface` is a smooth, low-poly support envelope under the decorative guard/louvers. It prevents catching feet or falling into gaps, including while overheat disables launching. The root trigger covers the opening. Emitters start above the hardware: 0.62m for fan, 0.54m for vent. The fan's `FanRotor_PIVOT` remains separate from the stationary casing/guard; no new animation script is required.

Source: `ScrapWaves_Fan_Vent.blend`; reproducible exporter: `build_jump_pad_variants.py`. Run the exporter in a disposable Blender process. It writes only the two new model FBXs and their source/metrics. Existing geyser models remain untouched.

Unity menu: **ScrapWaves → Level → Create Missing Optional Fan and Vent Jump Pads**. This creates missing optional prefabs only and preserves any existing prefab's designer edits. The original geyser authoring commands do not address these optional prefabs. Ordinary mesh regeneration updates the FBXs without rebuilding the prefabs or scaling launch values.

Validation and preview artifacts are stored in `.utmp/geyser-hardware-2026-10-31/`. The preview is a controlled Unity render with real materials/particles; native launch and landing behavior is checked separately by `GeyserHardwareVariantTests`.
