"""Export the game's compound bow (HuntingBow, LOD0) for remodelling in Blender.

Source: sharedassets2.assets, prefab CompoundBow -> "Bow - basecolor" -> HuntingBow (SkinnedMeshRenderer).
The mesh is exported in its own bind-pose space, metres, no 0.92 prefab scale applied (the CompoundBow root
is scaled 0.92 in the game; the mesh itself is 1:1).

Axes: Unity (x right, y up, z forward) -> Blender default OBJ import gives (x, z, y) = Blender X right,
Y forward, Z up, the usual Unity -> Blender convention. Triangle winding is flipped for the handedness change.

Writes to ../BlenderRefs/game/bow/ (gitignored: extracted game assets, never publish):
  bow_lod0.obj/.mtl   the mesh (39k verts), one material, UVs + normals
  bow_lod1..3.obj     the game's lower LODs (reference only)
  HuntingBow_D.png    base colour (grey; the game tints it: grey 0.37, black 0.085, brown = 0.66/0.49/0.25)
  HuntingBow_N.png    tangent normal map converted to plain RGB (OpenGL / Y+, what Blender expects)
  HuntingBow_M.png    metallic (R) + smoothness (A) as the game packs it
  HuntingBow_AO.png   ambient occlusion
  bones.json          the 28 skeleton joints with bind-pose positions in the same Blender axes, plus parents
Usage: python export_bow.py
"""
import json
import os

import numpy as np
import UnityPy

G = r"E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\GunmanContracts_Data"
OUT = os.path.join(os.path.dirname(__file__), "..", "BlenderRefs", "game", "bow")
LODS = {"bow_lod0": 18245, "bow_lod1": 15191, "bow_lod2": 18937, "bow_lod3": 17874}   # HuntingBow GameObjects
TEXT = {"_BaseMap": "HuntingBow_D", "_BumpMap": "HuntingBow_N", "_MetallicGlossMap": "HuntingBow_M",
        "_OcclusionMap": "HuntingBow_AO"}


def skinned(sf, go_pid):
    go = sf.objects[go_pid].read()
    for c in go.m_Components:
        p = c if hasattr(c, "path_id") else c.component
        if sf.objects[p.path_id].type.name == "SkinnedMeshRenderer":
            return p.read()


def write_obj(path, mtl, mesh):
    """UnityPy's OBJ export negates x. Undo it, then apply the Unity -> OBJ(Y-up) mapping (x, y, -z)."""
    v, vt, vn, faces = [], [], [], []
    for line in mesh.export().splitlines():
        k = line.split()
        if not k:
            continue
        if k[0] == "v":
            x, y, z = map(float, k[1:4]); v.append((-x, y, -z))
        elif k[0] == "vn":
            x, y, z = map(float, k[1:4]); vn.append((-x, y, -z))
        elif k[0] == "vt":
            vt.append(tuple(map(float, k[1:3])))
        elif k[0] == "f":
            faces.append([tuple(int(i) if i else 0 for i in t.split("/")) for t in k[1:]])
    with open(path, "w") as fh:
        fh.write(f"# HuntingBow from Gunman Contracts, metres. Blender default OBJ import: +Y = forward, +Z = up\n")
        if mtl:
            fh.write(f"mtllib {mtl}\n")
        fh.write("o HuntingBow\n")
        for p in v:
            fh.write(f"v {p[0]:.6f} {p[1]:.6f} {p[2]:.6f}\n")
        for p in vt:
            fh.write(f"vt {p[0]:.6f} {p[1]:.6f}\n")
        for p in vn:
            fh.write(f"vn {p[0]:.6f} {p[1]:.6f} {p[2]:.6f}\n")
        if mtl:
            fh.write("usemtl HuntingBow\ns 1\n")
        for f in faces:
            fh.write("f " + " ".join("/".join(str(i) if i else "" for i in t) for t in reversed(f)) + "\n")
    a = np.array(v)
    print(f"{os.path.basename(path)}: {len(v)} verts, {len(faces)} faces, "
          f"size {np.round(a.max(0) - a.min(0), 3)} m (x, y, -z)")


def save_textures(r):
    mat = r.m_Materials[0].read()
    out = {}
    for key, pt in mat.m_SavedProperties.m_TexEnvs:
        if key in TEXT and pt.m_Texture.path_id:
            out[TEXT[key]] = pt.m_Texture.read()
    for name, tx in out.items():
        img = tx.image.convert("RGBA")
        if name == "HuntingBow_N":
            # Unity packs a normal map as DXT5nm: x in alpha, y in green. Rebuild a plain RGB normal map.
            a = np.asarray(img, dtype=np.float32) / 255.0
            nx, ny = a[..., 3] * 2 - 1, a[..., 1] * 2 - 1
            nz = np.sqrt(np.clip(1 - nx * nx - ny * ny, 0, 1))
            rgb = np.stack([nx, ny, nz], -1) * 0.5 + 0.5
            from PIL import Image
            img = Image.fromarray((rgb * 255).astype(np.uint8), "RGB")
        img.save(os.path.join(OUT, name + ".png"))
        print(f"{name}.png {tx.m_Width}x{tx.m_Height}")


def bones(sf, r):
    """Bind-pose joint positions (Blender axes) + parent names from the mesh's inverse bind matrices."""
    m = r.m_Mesh.read()
    names = [b.read().m_GameObject.read().m_Name for b in r.m_Bones]
    tf = [b.read() for b in r.m_Bones]
    pid = {t.object_reader.path_id: n for t, n in zip(tf, names)}
    res = {}
    for i, (n, t) in enumerate(zip(names, tf)):
        bp = m.m_BindPose[i] if len(m.m_BindPose) > i else None
        pos = None
        if bp is not None:
            M = np.linalg.inv(np.array([[getattr(bp, f"e{r}{c}") for c in range(4)] for r in range(4)]))
            x, y, z = M[:3, 3]
            pos = [float(x), float(z), float(y)]             # Unity (x, y, z) -> Blender (x, z, y)
        res[n] = {"parent": pid.get(t.m_Father.path_id), "bind_position_blender": pos}
    with open(os.path.join(OUT, "bones.json"), "w") as fh:
        json.dump(res, fh, indent=1)
    print(f"bones.json: {len(res)} joints")


def main():
    os.makedirs(OUT, exist_ok=True)
    sf = list(UnityPy.load(os.path.join(G, "sharedassets2.assets")).files.values())[0]
    base = skinned(sf, LODS["bow_lod0"])
    save_textures(base)
    with open(os.path.join(OUT, "bow_lod0.mtl"), "w") as fh:
        fh.write("newmtl HuntingBow\nKa 1 1 1\nKd 1 1 1\nKs 0.2 0.2 0.2\nNs 50\nd 1\nillum 2\n"
                 "map_Kd HuntingBow_D.png\nmap_Bump HuntingBow_N.png\nmap_Ka HuntingBow_AO.png\n")
    for name, gp in LODS.items():
        r = skinned(sf, gp)
        write_obj(os.path.join(OUT, name + ".obj"), name + ".mtl" if name == "bow_lod0" else None, r.m_Mesh.read())
    bones(sf, base)


if __name__ == "__main__":
    main()
