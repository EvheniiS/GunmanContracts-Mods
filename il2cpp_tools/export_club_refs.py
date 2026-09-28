"""Export Blender scale references for the Billy Clubs model, in "club space".

Club space = the space the mod builds the club in:
  origin = centre of the crowbar's shaft collider (the club's centre), metres,
  Blender +X = along the club towards the metal tip, Blender +Z = up.
Unity (left-handed, Y up) -> Blender (right-handed, Z up): (x, y, z) -> (x, z, y), triangle winding flipped.
The .obj files are Y-up (OBJ convention): Blender's DEFAULT OBJ import (Forward -Z, Up Y) shows them in these axes.

Writes to ../BlenderRefs/game/:
  crowbar.obj        the game's crowbar mesh (the club is built on it; its hooks stick out at both ends)
  shaft_collider.obj the crowbar's shaft collider box = what hits things; the hand grips this
  grip_point.obj     small arrow at GrabPoint_Base (where the game's hand pose sits when you hold it)
  glove_right.obj    the player's right glove (bind pose, open hand) placed on the grip point - approximate
  club_current.obj   the club the mod draws now (0.6 m, radius 1.8 cm, 25% silver tip)
Usage: python export_club_refs.py
"""
import os
import struct

import numpy as np
import UnityPy

G = r"E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\GunmanContracts_Data"
OUT = os.path.join(os.path.dirname(__file__), "..", "BlenderRefs", "game")


def trs(t):
    p, r, s = t.m_LocalPosition, t.m_LocalRotation, t.m_LocalScale
    x, y, z, w = r.x, r.y, r.z, r.w
    R = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    M = np.eye(4)
    M[:3, :3] = R @ np.diag([s.x, s.y, s.z])
    M[:3, 3] = [p.x, p.y, p.z]
    return M


def to_root(t, root_pid):
    """Matrix from transform t's local space to the root's local space."""
    M = np.eye(4)
    while t.object_reader.path_id != root_pid:
        M = trs(t) @ M
        t = t.m_Father.read()
    return M


def mesh_arrays(mesh):
    """Positions (Unity space) and triangles of a Unity mesh, via UnityPy's OBJ export (which negates x)."""
    v, f = [], []
    for line in mesh.export().splitlines():
        if line.startswith("v "):
            x, y, z = map(float, line.split()[1:4])
            v.append((-x, y, z))
        elif line.startswith("f "):
            f.append([int(p.split("/")[0]) - 1 for p in line.split()[1:]])
    return np.array(v), f


def write_obj(name, verts_unity, faces, club):
    """verts in the crowbar root's space -> club space -> Blender axes."""
    C, X = club
    Y = np.array([0.0, 1.0, 0.0]) - X * X[1]
    Y /= np.linalg.norm(Y)
    Z = np.cross(X, Y)
    rel = verts_unity - C
    u = np.stack([rel @ X, rel @ Y, rel @ Z], 1)          # club space, Unity handedness (X along, Y up)
    # Unity is left-handed, Blender right-handed: (x, y, z) -> (x, z, y) is a mirror, so faces are reversed.
    b = u[:, [0, 2, 1]]                                    # Blender axes: X along the club, Z up
    o = np.stack([b[:, 0], b[:, 2], -b[:, 1]], 1)          # .obj is Y-up; Blender's default import turns it back into b
    with open(os.path.join(OUT, name + ".obj"), "w") as fh:
        fh.write(f"# {name} - club space, metres. Blender default OBJ import: +X = club tip, +Z = up\no {name}\n")
        for p in o:
            fh.write(f"v {p[0]:.6f} {p[1]:.6f} {p[2]:.6f}\n")
        for tri in faces:
            fh.write("f " + " ".join(str(i + 1) for i in reversed(tri)) + "\n")
    lo, hi = b.min(0), b.max(0)
    print(f"{name}: {len(b)} verts, size {hi - lo} m")


def box(center, size):
    c = np.array(center); h = np.array(size) / 2
    v = np.array([c + h * np.array([sx, sy, sz]) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)])
    f = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]]
    return v, f


def cylinder(x0, x1, r, n=32):
    v, f = [], []
    for x in (x0, x1):
        for i in range(n):
            a = 2 * np.pi * i / n
            v.append((x, r * np.cos(a), r * np.sin(a)))
    for i in range(n):
        j = (i + 1) % n
        f.append([i, j, n + j, n + i])
    f.append(list(range(n))[::-1]); f.append(list(range(n, 2 * n)))
    return np.array(v), f


def main():
    os.makedirs(OUT, exist_ok=True)
    sf = list(UnityPy.load(os.path.join(G, "level2")).files.values())[0]
    root_go = next(o.read() for o in sf.objects.values()
                   if o.type.name == "GameObject" and o.read().m_Name == "Prop-Bluntweapon-Crowbar")
    root_t = root_go.m_Transform.read()
    root_pid = root_t.object_reader.path_id

    def walk(t):
        yield t
        for ch in t.m_Children:
            yield from walk(ch.read())

    mesh_v = None; shaft = None; grip = None
    for t in walk(root_t):
        go = t.m_GameObject.read()
        for c in go.m_Components:
            pptr = c if hasattr(c, "path_id") else c.component
            tn = sf.objects[pptr.path_id].type.name
            if tn not in ("MeshFilter", "BoxCollider"):
                continue
            co = pptr.read()
            if tn == "MeshFilter" and go.m_IsActive and mesh_v is None:
                v, f = mesh_arrays(co.m_Mesh.read())
                M = to_root(t, root_pid)
                mesh_v = ((M[:3, :3] @ v.T).T + M[:3, 3], f)
            if tn == "BoxCollider":
                M = to_root(t, root_pid)
                size = np.array([co.m_Size.x, co.m_Size.y, co.m_Size.z])
                axes = [M[:3, i] * size[i] for i in range(3)]
                i = int(np.argmax([np.linalg.norm(a) for a in axes]))
                ln = np.linalg.norm(axes[i])
                if shaft is None or ln > shaft[2]:
                    cen = M[:3, :3] @ np.array([co.m_Center.x, co.m_Center.y, co.m_Center.z]) + M[:3, 3]
                    shaft = (cen, axes[i] / ln, ln, M, size, np.array([co.m_Center.x, co.m_Center.y, co.m_Center.z]))
        if go.m_Name == "GrabPoint_Base":
            grip = to_root(t, root_pid)

    C, X, L = shaft[0], shaft[1], shaft[2]
    # tip = away from the grip point (same rule as the mod)
    if grip is not None and np.dot(grip[:3, 3] - C, X) > 0:
        X = -X
    club = (C, X)
    print(f"shaft: {L:.3f} m, grip point {np.dot(grip[:3, 3] - C, X):+.3f} m along it")

    write_obj("crowbar", mesh_v[0], mesh_v[1], club)
    bv, bf = box(shaft[5], shaft[4])
    M = shaft[3]
    write_obj("shaft_collider", (M[:3, :3] @ bv.T).T + M[:3, 3], bf, club)

    # grip point: a 4 cm arrow along the grab point's own forward (+Z), with a 1 cm cube at its base
    gv = np.array([[0, 0, 0], [0, 0, 0.04], [0.004, 0, 0], [0, 0.004, 0]])
    gp = (grip[:3, :3] @ gv.T).T + grip[:3, 3]
    cv, cf = box((0, 0, 0), (0.01, 0.01, 0.01))
    cv = (grip[:3, :3] @ cv.T).T + grip[:3, 3]
    write_obj("grip_point", np.vstack([cv, gp]), cf + [[8, 9, 10], [8, 9, 11]], club)

    # glove: the player's right glove in its bind pose, placed on the grab point (approximate: the pose closes it)
    rs = list(UnityPy.load(os.path.join(G, "resources.assets")).files.values())[0]
    glove = next(o.read() for o in rs.objects.values()
                 if o.type.name == "Mesh" and o.read().m_Name == "righthand_fpsgloveorig_HD")
    v, f = mesh_arrays(glove)
    ext = v.max(0) - v.min(0)
    if ext.max() > 5:          # centimetres -> metres
        v = v / 100.0
    write_obj("glove_right", (grip[:3, :3] @ v.T).T + grip[:3, 3], f, club)

    # the club the mod draws now: body 75% red, 25% silver tip towards +X
    Lc, r = 0.6, 0.018
    cv, cf = cylinder(-Lc / 2, Lc / 2, r)
    Y = np.array([0.0, 1.0, 0.0]) - X * X[1]; Y /= np.linalg.norm(Y); Z = np.cross(X, Y)
    world = C + np.outer(cv[:, 0], X) + np.outer(cv[:, 1], Y) + np.outer(cv[:, 2], Z)
    write_obj("club_current", world, cf, club)


if __name__ == "__main__":
    main()
