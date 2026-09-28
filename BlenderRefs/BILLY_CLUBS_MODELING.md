# Billy clubs: Blender MCP modeling brief

## Purpose and scope

Create a reusable Daredevil-style billy club visual asset for the Billy Clubs mod in **Gunman Contracts - Stand Alone**. Use Blender MCP when connected. This document is the brief for a future modeling session; no Blender model, material test, or export has been produced yet.

Build one master club and instance it for the pair. Prioritize a clean silhouette, good appearance in VR, and a convincing fine red grip texture. The first version has **no round button** on the silver section. Keep the narrow ring grooves and collars. Staff conversion, cable mechanisms, internal parts, and animation are outside this modeling pass.

## References and their priority

All links are relative to this folder (`BlenderRefs/`), which is the modeling project root:

1. [Original design](billiyClubs.jpg): primary source for silhouette, proportions, burgundy color, grip segmentation, and metal ring placement. The filename really is `billiyClubs.jpg`.
2. [Generated metal close-ups](billy-clubs-metal-reference.png): supplementary guidance for silver finish, bevels, and grooves. Ignore the button.
3. [Generated grip close-ups](billy-clubs-grip-reference.png): supplementary guidance for the diamond pattern, seam, and cap transition.
4. [Generation prompts](billy-clubs-reference-prompts.md): provenance of the generated references.
5. **Game references in [game/](game/)**, exported from the game files at true scale, already in the club's coordinate system (see "Starter project" below).

The generated images are visual interpretations, not measured orthographic drawings or seamless texture maps. They can disagree with each other. Follow the original for shape; use the close-ups for material appearance. Make the grip pattern **finer and shallower than the generated macro views suggest**. Do not project their lighting, shadows, or highlights into the material. Text in the reference artwork is not an instruction to build its mechanisms.

## Starter project (built from the game files, Sep 27 2026)

- **`BillyClubs_start.blend`** (Blender 5.2): metric scene with three collections. **Open it and save your work
  under a new name** (e.g. `billy_club.blend`); rebuild it any time with `blender --background --python setup_scene.py`.
  - `GameRefs` (locked, never export): `REF_crowbar` (the game crowbar the club is built on; its hooks stick out at
    both ends), `REF_shaft_collider` (blue wire box, 596 × 22 × 26 mm = the part that actually hits things; the game's
    hand pose closes around this), `REF_grip_point` (green marker at **x = −157 mm** = where the game puts the hand),
    `REF_glove_right` (the player's glove, open hand in bind pose; for size only, its placement is approximate),
    `REF_club_now` (the placeholder the mod draws today: 600 mm, 36 mm diameter), and `MARK_*` empties at −300 / 0 / +300 mm.
  - `DesignRefs`: the reference pictures as image empties above the club (front view).
  - `Club`: **empty; model here.** Only this collection is exported.
- **`export_club.py`**: checks the `Club` collection against the rules below, then writes `out/billy_club.obj`.
  Run it in Blender's Scripting tab, or `blender --background billy_club.blend --python export_club.py`. It prints
  PASS/FAIL per rule and ends with `RESULT: ALL PASS`.
- `game/*.obj` came from `../il2cpp_tools/export_club_refs.py`. They are extracted game assets: **don't publish them**
  (they're git-ignored).

## Existing mod constraints

The mod (`../Daredevil/BillyClubs/`) today draws the club from Unity cylinders (`BuildVisual`, `Part`, `MakeMaterial`), on top of
the game crowbar's grip points and colliders. The new model will replace only the visual, through an **OBJ loader the
mod doesn't have yet** (a separate integration task). These rules are what that loader will assume:

| Property | Rule | Why |
|---|---|---|
| Axis | **Blender +X = along the club, metal tip at +X, grip end at −X.** Z up. | Same axes as the `game/` references |
| Origin | At the club's centre, **x ∈ [−300, +300] mm** | The mod places the club by its centre on the crowbar's shaft |
| Overall length | **600 mm** (checker allows 550-650), caps included | The hit shape stays the crowbar's 596 mm collider |
| Grip (red) diameter at the hand (x = −157 mm) | **30-36 mm** | The 36 mm placeholder looked right in the hand in game; the game's hand pose was made for the 22-26 mm crowbar shaft, so don't go thicker |
| Largest diameter (collars included) | **≤ 40 mm** | |
| Red / silver split | about 75% / 25% (silver ≈ 150 mm at +X) | Matches the original and today's placeholder |
| Scale | Applied (object scale 1.0), metres | |
| Triangles | **≤ 6000 per club** | Two clubs in VR on a CPU-bound game |
| Export copy | **One mesh object, one material, one UV map**, triangulated | The loader creates one URP Lit material |

These are compatibility values, not measurements of an official prop.

The mod currently builds primitive cylinders under `BillyClubVisual` and retains the crowbar's grip and hit shape. A new mesh does not automatically replace those primitives or change gameplay. Asset loading and replacement of `BuildVisual` are a separate integration task. The current `MakeMaterial` clears texture slots and disables normal-map keywords, so it must not be reused unchanged for textured imported materials.

## Blender MCP session workflow

1. Verify that Blender MCP tools are actually available. Inspect the active scene, Blender version, current file path, and existing objects before editing. Do not claim to have modeled anything if the connection is absent.
2. Preserve unrelated scene content. Create a dedicated `BillyClubs` collection and save to a new project file. On later runs, update named objects in this collection rather than accumulating duplicates.
3. Use small, repeatable operations or scripts. Group the work into blockout, grip test, geometry refinement, material work, and export. After each stage, inspect a viewport capture or render and correct visible problems.
4. Record actual dimensions, axis conventions, texture settings, polygon counts, and export settings in a short handoff note. Save checkpoints before destructive modifier application or baking.

## Stage 1: blockout and proportions

- Use metric units with 1 Blender unit representing 1 meter (the start file is set up this way). Model the shaft along **Blender +X**, silver tip at +X, origin at the midpoint, lined up with `REF_club_now`. `export_club.py` handles the conversion to the game; don't rotate the model to suit Unity.
- Create named parts such as `BC_Grip`, `BC_MetalTip`, `BC_TransitionBand`, and `BC_GripCap`. Keep them editable initially. The source file may use two procedural materials, `BC_RedGrip` and `BC_Silver`; the **export copy** is one joined mesh with one material and the baked texture set (Stage 4).
- Start with 48 radial segments, then inspect the silhouette at hand distance. Increase only if faceting is visible. Use small bevels on exposed edges so they catch light; avoid a soft, inflated appearance.
- Put a narrow circumferential seam in the red body where the original reference changes grip sections. Keep both sections coaxial and at the same nominal diameter. No finger grooves, leather wrapping, or padded profile.
- Check the side silhouette, end view, and three-quarter view. Keep total length at 600 mm and avoid floating caps, accidental gaps, and coincident visible surfaces.

## Stage 2: test the grip before detailing the whole asset

Build a short test cylinder with the same diameter and material mapping as the master grip. Use it to tune the pattern at close range and at normal hand distance before applying it to the full club.

The target is deep burgundy with a dense, regular **diamond knurl**: small raised diamonds separated by shallow crossing grooves. The geometry should remain a simple cylinder. Use a procedural height pattern driving a bump node for look development, then bake a tangent-space normal map for export. Do not model every diamond into the final mesh.

Suggested construction:

1. Cylindrically unwrap the grip side wall with a straight longitudinal UV seam; unwrap end faces separately. Maintain consistent texel density across both grip sections.
2. Build two narrow diagonal groove families crossing in opposite directions. Calibrate their spacing in physical surface units so diamonds look balanced on the cylinder rather than stretched by the long UV island.
3. Combine the groove masks into a shallow height pattern. Soften the groove edges slightly. Keep bump strength low enough that the surface reads as fine knurling rather than large spikes or a woven basket.
4. Adjust the circumferential repetition to close cleanly at the UV seam. Inspect the seam directly; do not hide a discontinuity only by turning it away from the camera.
5. Start with roughly 1 mm pattern spacing as an artistic test, not a measured requirement. Adjust density and depth against the original image and VR-distance previews. Match the pattern between grip sections.
6. Add restrained roughness variation. Keep the base color mostly uniform burgundy; use lighting and surface normals to reveal the diamonds. Avoid painted-on bright highlights and black crevices.

The reference does not establish the grip's real material composition. Start with a coated-metal appearance and tune it visually. For a painted coating, use a nonmetallic top surface; for an anodized-metal interpretation, test a metallic response. Choose one coherent appearance and document it rather than assuming that red automatically means rubber or metal.

Render the test under broad neutral lighting and a second light angle. Check for excessive sparkle, moire, shimmering, visible tiling, and diamonds disappearing at hand distance. In-game movement is the final check for temporal aliasing; a still render cannot confirm that.

## Stage 3: finish the metal and geometry

- Use satin silver with a metallic response, controlled roughness, and subtle machining marks. Broad highlights should describe the cylinder without obscuring its shape.
- Keep only the ring grooves and collars needed to match the original. Model details that affect silhouette; use texture detail for microscopic scratches and brushing.
- Omit the round button and its recess in version 1. Do not leave a blank socket where the button would have been.
- Inspect smooth shading and bevels at the caps and groove transitions. Remove shading dents, flipped normals, and visible intersections. Maintain an editable source before creating an export copy.
- Prefer a few thousand triangles per club as an initial working budget, not a verified game limit. Record the actual count and reduce invisible detail first. Share the mesh and materials between the two clubs where integration allows it.

## Stage 4: bake and prepare for Unity

Keep the procedural material in the source `.blend`. Exported game assets need ordinary textures; Blender shader nodes will not recreate themselves in Unity.

- Start with a 2048 px texture set and evaluate whether it preserves the fine pattern. Decide the final resolution using visual quality and memory cost, not the macro image alone. **VR note:** the headset resolves roughly 22 pixels per degree, so a 1 mm knurl is far below what it can show at arm's length. It will read as texture, and mipmaps will average it out. Judge it at hand distance, and prefer slightly coarser/softer over sparkling.
- Bake from the final UV layout (one UV map, both materials packed into one atlas, with island padding) into **four PNGs in `out/textures/`**: `billy_club_basecolor.png` (sRGB), `billy_club_normal.png` (tangent space, **OpenGL / +Y up, which is Blender's and Unity's convention, so no green flip**), `billy_club_metallic.png` and `billy_club_roughness.png` (grayscale, Non-Color). **Don't channel-pack:** the game uses URP Lit, and the mod will pack metallic + (1 − roughness) into URP's metallic/smoothness map itself.
- Confirm normal detail remains correct after triangulation and export. Inspect the UV seam, end-cap shading, and the direction of raised versus recessed features.
- Export with **`export_club.py`** (OBJ + MTL, triangulated, axes handled). It exports only the `Club` collection and checks length, centring, diameters, triangle count, UV maps, material count and the four textures. Fix every FAIL before handing over.
- The game can't load FBX without a Unity Editor build, and the mod will load the OBJ at runtime. **Don't deliver FBX.**
- Preserve the existing grip, collision, holster, and throw behavior during the first visual replacement. Adjust those separately only if a real mismatch is found.

## Suggested deliverables

Create these during the future modeling session; the paths below are planned outputs:

```text
BlenderRefs/billy_club.blend                     editable source (saved from BillyClubs_start.blend)
BlenderRefs/out/billy_club.obj  (+ .mtl)         written by export_club.py
BlenderRefs/out/textures/billy_club_{basecolor,normal,metallic,roughness}.png
BlenderRefs/previews/...
BlenderRefs/MODEL_HANDOFF.md                     dimensions, triangle count, texture settings, the export_club.py output
```

Keep the master editable and use versioned saves when an existing file would otherwise be overwritten. Include a straight side preview, an end view, a three-quarter preview, and a grip close-up. Keep preview lights and cameras out of the mesh export.

## Completion checks

- [ ] Original proportions are recognizable; metal buttons are omitted.
- [ ] Total length, diameter, origin, and imported axis match the agreed convention.
- [ ] Grip diamonds are fine, shallow, evenly spaced, and continuous at the UV seam.
- [ ] Grip and silver look convincing under more than one lighting setup.
- [ ] No visible faceting, shading dents, accidental gaps, or cap intersections.
- [ ] Exported normal detail matches Blender; material maps and packing are documented.
- [ ] Source file, textures, export, previews, and handoff note are saved with recorded triangle counts.
- [ ] Unity import checks are recorded separately from Blender-only checks.
- [ ] In-game visual fit, grip alignment, holstering, and moving-texture stability are verified during integration, or explicitly marked untested.

## Starter instruction for the next Blender MCP session

> Read `BlenderRefs/BILLY_CLUBS_MODELING.md` and inspect its linked references. Verify the Blender MCP connection, open `BlenderRefs/BillyClubs_start.blend`, save it as `billy_club.blend`, and model in the `Club` collection against the `GameRefs` (along +X, metal tip at +X, centred on the origin). Create the master club blockout plus a small grip material test. Use the original reference for shape and the generated images for material guidance. Omit the metal button, use fine shallow diamond knurling through a bump/normal workflow, and inspect the result at hand distance. Continue through refinement, baking and `export_club.py` until it prints `RESULT: ALL PASS`, recording any integration checks that cannot yet be performed. Do not edit the mod's C# code or anything outside `BlenderRefs/`.
