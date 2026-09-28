"""Render the arsenal panel picture for Billy Clubs (Weapon Framework entry), in the style of the game's own
gunicon_* sprites: a light grey shaded render of the model with a dark outline on a transparent background, 2.15:1.

Run headless (never touches an open Blender scene):
  blender --background --factory-startup --python render_arsenal_icon.py
Two passes, combined into out/billy_clubs_icon.png with Pillow (any Python that has it):
  clay    white clay, studio light, cavity + dark object outline  -> the shading and the lines
  texture the model's own colours, flat                           -> which parts are dark red and which are metal
Final = clay x (0.80 + 0.20 x texture brightness), so the grip reads a little darker than the metal tip, like the
game's icons, and the outline stays dark.
"""
import math
import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OBJ = os.path.join(HERE, "out", "billy_club.obj")
OUT = os.path.join(HERE, "out", "billy_clubs_icon.png")
TMP = os.path.join(HERE, "out", "icon_pass_%s.png")
W, H = 1364, 635          # the bow's sprite size
CROSS_DEG = 15.0          # each club tilted this much from horizontal: a shallow X

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

clubs = []
for i, depth in enumerate((0.0, 0.06)):
    bpy.ops.wm.obj_import(filepath=OBJ)      # OBJ is Y-up: the club lies along Blender +X (tip at +X)
    ob = bpy.context.selected_objects[0]
    ob.name = f"club{i}"
    # Second club turned half round the vertical: the opposite slope, tip at the other end (a proper X).
    ob.rotation_euler = (0.0, math.radians(CROSS_DEG), math.pi if i == 1 else 0.0)
    ob.location = (0.0, depth, 0.0)
    clubs.append(ob)

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 0.66
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
cam.location = (0.0, -2.0, 0.0)
cam.rotation_euler = (math.radians(90.0), 0.0, 0.0)   # looking along +Y at the XZ plane
scene.camera = cam

scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x, scene.render.resolution_y = W, H
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.display.render_aa = "32"
scene.view_settings.view_transform = "Standard"
sh = scene.display.shading
sh.light = "STUDIO"
sh.show_specular_highlight = True


def render(name, clay):
    sh.color_type = "SINGLE" if clay else "TEXTURE"
    sh.single_color = (1.0, 1.0, 1.0)
    sh.light = "STUDIO" if clay else "FLAT"
    sh.show_cavity = clay
    sh.cavity_type = "BOTH"
    sh.cavity_ridge_factor = 1.2
    sh.cavity_valley_factor = 1.8
    sh.show_object_outline = clay
    sh.object_outline_color = (0.06, 0.06, 0.06)
    scene.render.filepath = TMP % name
    bpy.ops.render.render(write_still=True)


render("clay", True)
render("texture", False)

print("passes written - now run: python make_arsenal_icon.py")
