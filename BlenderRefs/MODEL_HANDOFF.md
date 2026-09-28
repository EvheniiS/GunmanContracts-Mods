# Billy club model handoff

Built through the connected Blender MCP in Blender 5.2.2 LTS on 2026-09-27. Grip revised for mod 0.5.1 after the user's successful in-game test of 0.5.0.

## Grip revision 0.5.1
- User feedback: silver looks good, but fine grip detail disappears in VR; darken the red and enlarge the pattern.
- Circumferential cycles reduced from 80 to 32: phase pitch 1.335 mm to 3.338 mm, 2.5 times larger along both surface axes.
- Bump distance 0.07 to 0.35 mm; strength 0.55 to 0.8. This is shading relief, with no change to the mesh or colliders.
- Linear burgundy peak RGB now (0.047, 0.0025, 0.005), modulated by 0.68 + 0.32 * procedural height for subtle darker groove pigmentation. No scene lighting is painted into the map.
- Grip roughness now 0.50 - 0.10 * height (0.40-0.50). Metallic remains 0.12. Silver shader and geometry are unchanged.
- Base color, tangent normal and roughness rebaked at 2048 px on a temporary copy of the exact triangulated export mesh. Metallic map retained.
- Old .blend and all export textures preserved in `checkpoints/grip-v1-0.5.0/`; existing earlier previews retained.
- Comparison previews: `13_v1_top_v2_bottom.png` and `14_v1_top_v2_bottom_dim.png` (old above, revised below). Lighting, exposure and camera are shared within each comparison. Revised detail is clearly more visible in both still renders.
- Integrated and installed as mod 0.5.1. Build and embedded-asset tests pass. The revised texture still needs a headset test for appearance and shimmer in motion.

## Deliverables
- `billy_club.blend`: editable source, final export mesh, linked pair, references, lighting and camera.
- `out/billy_club.obj` and `out/billy_club.mtl`: one club for runtime loading.
- `out/textures/billy_club_{basecolor,normal,metallic,roughness}.png`: four 2048 x 2048 maps.
- `previews/05_side.png`, `06_end.png`, `06b_axial_end.png`, `07_three_quarter.png`, `08_baked_uv_seam.png`, `09_pair.png`, `10_obj_roundtrip_grip.png`: final 64-segment model.
- Earlier numbered previews and checkpoints document the initial 48-segment material/blockout stages.

## Dimensions and mesh
- Metres; +X follows club toward silver tip, Z up. Origin (0, 0, 0).
- X bounds -0.300 to +0.300 m, total 600 mm.
- Grip at game hand x = -157 mm: diameter 34.0 mm.
- Largest collar diameter: 34.8 mm.
- Silver main section: x = +150 to +300 mm (25% of length); 6 mm silver heel cap.
- Narrow grip seam centered at approximately x = -117 mm.
- 64 radial segments; 5,876 triangles per club. Scale (1,1,1).
- Export: one mesh `BC_Export`, one material `BC_GameAtlas`, one UV map `BC_Atlas`; triangles already applied.
- No button or button socket. No mechanical internals, cable or staff mechanism.

## Scene organization
- `Club`: final triangulated export mesh at the origin.
- `BillyClubs`: hidden editable parts `BC_Grip`, `BC_MetalTip`, `BC_GripCap`, with procedural materials. Enable this collection and hide Club/BC_Presentation to edit without overlapping meshes.
- `BC_Presentation`: second club sharing the final export mesh and material. It is offset for presentation and excluded from export.
- `BC_MaterialTests`: hidden short cylinder using the same grip mapping.
- `BC_Studio`: camera and three broad neutral area lights.
- `GameRefs` and `DesignRefs`: preserved from the starter, hidden for presentation; never exported.
- The starter file is untouched. The saved final scene opens with the pair visible.
- `build_club_source.py` records only the initial blockout stage and is not a rebuild of the finished asset. The final .blend is authoritative.

## Original 0.5.0 materials and bake (superseded grip settings)
The burgundy grip is a painted/coated appearance with a restrained metallic response (0.12), linear base RGB (0.075, 0.003, 0.007) and roughness 0.34-0.375. Silver uses metallic 1.0 and roughness approximately 0.29 with subtle procedural machining variation.

Crossed groove families have 80 complete repetitions around the 106.8 mm grip circumference; axial phase pitch approximately 1.335 mm. Mapping is continuous across the longitudinal UV seam and between grip sections. Source bump distance is 0.07 mm at strength 0.55. Detail is shading only; no diamond geometry.

Cycles baked the maps with 12-pixel extended margins:
- Base color: sRGB, free of lighting/highlights.
- Normal: tangent space, +X/+Y/+Z (OpenGL), Non-Color; normal strength 1.
- Metallic and roughness: grayscale values in RGB PNGs, Non-Color.
- 2048 px intentionally softens microscopic detail compared with the procedural macro render. Evaluate mipmaps and motion in VR before increasing resolution or bump strength.
- Normal map rebaked after the final 64-segment triangulation.
- All texture paths in the final .blend are relative to `out/textures/`.
- No channel packing. Integration should pack metallic and 1 - roughness as required by the runtime URP material.

OBJ uses the supplied export settings: forward NEGATIVE_Z, up Y, global scale 1, selected geometry only, normals and UVs enabled. The generated MTL references relative texture paths. Its map_Ns/map_refl/map_Bump conventions are exporter metadata; the runtime loader should use the explicitly named maps above.

## Validation
The MCP safe mode disallows `os` imports and filesystem helpers in the original `export_club.py`. To respect that restriction, `export_club_mcp.py` preserves its geometry checks and OBJ export options, substitutes literal project paths, and verifies each texture by loading it in Blender and checking its 2048 x 2048 dimensions. Output folders were created and files checked with the workspace file tools. The original exporter was not modified or executed verbatim.

The compatible checker printed **RESULT: ALL PASS**. Full output: `out/export_validation.txt`.

OBJ roundtrip in Blender:
- 5,876 triangles, dimensions (0.6000, 0.0348, 0.0348) m, one UV map.
- MTL imports four image nodes and a Normal Map node.
- Reimported geometry rendered with the final atlas material after renaming its UV layer to BC_Atlas; grip detail and seam visually match the pre-export render.
- Side, oblique, axial end, close grip and opposite-side seam views inspected.
- Initial metal shading dents were corrected with explicit surface normals; radial resolution increased after the end-view inspection.
- Pair shares one mesh datablock.

## Integration status
0.5.0 was integrated and tested in the game by the user, who described it as a huge success and supplied screenshots. The game log confirms the custom URP Lit mesh/material loaded; the screenshots motivated the grip revision above. This 0.5.1 update changes textures and the mod version only; loader and gameplay logic are unchanged. It has not yet been evaluated in headset motion.

The .blend retains extracted game reference meshes for local work; distribute only the intended authored export assets when appropriate.
