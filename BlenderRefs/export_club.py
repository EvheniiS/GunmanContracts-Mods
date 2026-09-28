"""Checks the club in the "Club" collection against the mod's rules and exports it for the game.

Run inside Blender (Scripting tab -> open -> Run), or headless on a saved file:
    blender --background billy_club.blend --python export_club.py
Writes out/billy_club.obj (+ .mtl). Textures go in out/textures/ (see BILLY_CLUBS_MODELING.md). Prints PASS/FAIL per rule.
"""
import os
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath or __file__))
OUT = os.path.join(HERE, "out")

LENGTH = (0.55, 0.65)        # m, along X
MAX_DIAMETER = 0.040         # m, anywhere (collars included)
GRIP_DIAMETER = (0.030, 0.036)  # m, at the grip point x = -0.157 (the game hand)
MAX_TRIS = 6000

col = bpy.data.collections.get("Club")
objs = [o for o in (col.all_objects if col else []) if o.type == "MESH" and o.visible_get()]
problems = []


def check(ok, msg):
    print(("PASS  " if ok else "FAIL  ") + msg)
    if not ok:
        problems.append(msg)


check(len(objs) >= 1, f"mesh objects in 'Club': {len(objs)}")
if objs:
    dg = bpy.context.evaluated_depsgraph_get()
    pts, tris, uv_ok, mats, section = [], 0, True, set(), []
    for o in objs:
        e = o.evaluated_get(dg)
        m = e.to_mesh()
        wv = [o.matrix_world @ v.co for v in m.vertices]
        pts += wv
        # cross-section at the hand: where edges cross the plane x = -0.157
        for ed in m.edges:
            a, b = wv[ed.vertices[0]], wv[ed.vertices[1]]
            if (a.x + 0.157) * (b.x + 0.157) <= 0 and abs(a.x - b.x) > 1e-9:
                t = (-0.157 - a.x) / (b.x - a.x)
                q = a.lerp(b, t)
                section.append(Vector((0, q.y, q.z)).length)
        m.calc_loop_triangles()
        tris += len(m.loop_triangles)
        uv_ok &= len(m.uv_layers) == 1
        mats |= {s.material.name for s in o.material_slots if s.material}
        e.to_mesh_clear()
        sc = o.matrix_world.to_scale()
        check(all(abs(s - 1) < 1e-4 for s in sc), f"{o.name}: scale applied (is {tuple(round(s, 3) for s in sc)})")
    xs = [p.x for p in pts]
    L = max(xs) - min(xs)
    check(LENGTH[0] <= L <= LENGTH[1], f"length along X {L * 100:.1f} cm (want {LENGTH[0] * 100:.0f}-{LENGTH[1] * 100:.0f})")
    check(abs((max(xs) + min(xs)) / 2) < 0.01, f"centred on the origin along X (centre {((max(xs) + min(xs)) / 2) * 100:+.1f} cm)")
    r = max((Vector((0, p.y, p.z)).length for p in pts), default=0)
    check(2 * r <= MAX_DIAMETER, f"max diameter {2 * r * 1000:.1f} mm (max {MAX_DIAMETER * 1000:.0f})")
    check(bool(section), "club reaches the hand position x = -15.7 cm")
    if section:
        d = 2 * max(section)
        check(GRIP_DIAMETER[0] <= d <= GRIP_DIAMETER[1], f"grip diameter at the hand {d * 1000:.1f} mm (want {GRIP_DIAMETER[0] * 1000:.0f}-{GRIP_DIAMETER[1] * 1000:.0f})")
    check(tris <= MAX_TRIS, f"triangles {tris} (max {MAX_TRIS})")
    check(uv_ok, "exactly one UV map per object")
    check(len(mats) == 1, f"one material (found {sorted(mats)})")

    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.hide_select = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, "billy_club.obj")
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, forward_axis="NEGATIVE_Z", up_axis="Y",
                          apply_modifiers=True, export_uv=True, export_normals=True, export_materials=True,
                          export_triangulated_mesh=True, global_scale=1.0)
    print("exported", path)
    tex = os.path.join(OUT, "textures")
    for t in ("billy_club_basecolor.png", "billy_club_normal.png", "billy_club_metallic.png", "billy_club_roughness.png"):
        check(os.path.exists(os.path.join(tex, t)), f"texture out/textures/{t}")

print("RESULT:", "ALL PASS" if not problems else f"{len(problems)} problem(s)")
