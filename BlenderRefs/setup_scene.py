"""Builds BillyClubs_start.blend: metric scene with the game references at the club's real scale.

Run once (headless is fine):
    blender --background --python setup_scene.py
Re-run any time: it rebuilds the start file from scratch (it never touches your own work files).

Collections in the result:
  GameRefs   - exported from the game (do not edit, do not export): crowbar, shaft collider, grip point,
               right glove (open hand, approximate placement), the club the mod draws now
  DesignRefs - the reference pictures as image planes, above the club
  Club       - EMPTY: model the club here. export_club.py exports only this collection.
"""
import os
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "game")

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1.0
scene.unit_settings.length_unit = "CENTIMETERS"


def collection(name, color=None):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    if color:
        c.color_tag = color
    return c


refs = collection("GameRefs", "COLOR_04")
design = collection("DesignRefs", "COLOR_05")
club = collection("Club", "COLOR_01")


def mat(name, rgba, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgba, alpha)
    return m


colors = {
    "crowbar": mat("ref_crowbar", (0.55, 0.55, 0.55)),
    "shaft_collider": mat("ref_shaft", (0.2, 0.4, 1.0), 0.35),
    "grip_point": mat("ref_grip", (0.0, 0.9, 0.2)),
    "glove_right": mat("ref_glove", (0.15, 0.15, 0.15), 0.6),
    "club_current": mat("ref_club_now", (0.6, 0.05, 0.05), 0.35),
}

for name, m in colors.items():
    path = os.path.join(GAME, name + ".obj")
    if not os.path.exists(path):
        print("missing", path)
        continue
    bpy.ops.wm.obj_import(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")
    for ob in bpy.context.selected_objects:
        for c in ob.users_collection:
            c.objects.unlink(ob)
        refs.objects.link(ob)
        ob.name = "REF_" + name
        ob.data.materials.clear()
        ob.data.materials.append(m)
        ob.hide_select = name != "grip_point"
        if name in ("shaft_collider", "club_current", "glove_right"):
            ob.show_transparent = True
        if name == "shaft_collider":
            ob.display_type = "WIRE"

# Markers: club ends and centre (the mod's club space).
for label, x in (("MARK_grip_end_-30cm", -0.30), ("MARK_centre_origin", 0.0), ("MARK_metal_tip_+30cm", 0.30)):
    e = bpy.data.objects.new(label, None)
    e.empty_display_type = "SINGLE_ARROW" if x else "PLAIN_AXES"
    e.empty_display_size = 0.05
    e.location = (x, 0, 0)
    e.rotation_euler = (0, 1.5708 if x else 0, 0)
    refs.objects.link(e)

# Design pictures as image empties above the club (side view, front orthographic = numpad 1).
pics = [f for f in sorted(os.listdir(HERE)) if f.lower().endswith((".png", ".jpg", ".jpeg"))]
for i, f in enumerate(pics):
    img = bpy.data.images.load(os.path.join(HERE, f))
    e = bpy.data.objects.new("PIC_" + os.path.splitext(f)[0], None)
    e.empty_display_type = "IMAGE"
    e.data = img
    e.empty_display_size = 0.6
    e.location = (0, 0.02 * i, 0.25 + 0.45 * i)
    e.rotation_euler = (1.5708, 0, 0)  # face the front view
    e.use_empty_image_alpha = True
    e.color[3] = 0.9
    design.objects.link(e)

out = os.path.join(HERE, "BillyClubs_start.blend")
bpy.ops.wm.save_as_mainfile(filepath=out)
print("saved", out, "-", len(refs.objects), "refs,", len(design.objects), "pictures")
