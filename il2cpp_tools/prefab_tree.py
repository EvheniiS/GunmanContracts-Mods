"""Print a prefab/scene GameObject hierarchy with component class names and local transforms.
Usage: python prefab_tree.py <asset file> <GameObject pathID | Transform pathID> [maxdepth]
MonoBehaviour class names come from monoscripts.pkl (MonoScript pathID -> class, globalgamemanagers.assets).
"""
import os
import pickle
import struct
import sys

import UnityPy

from gamedir import game_dir

D = os.path.join(game_dir(), "GunmanContracts_Data")
MS = pickle.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "monoscripts.pkl"), "rb"))


def load(fname):
    env = UnityPy.load(os.path.join(D, fname))
    return env, {o.path_id: o for o in env.objects}


def mb_class(o):
    raw = o.get_raw_data()
    return MS.get(struct.unpack_from("<q", raw, 20)[0], "MB?")


def comp_names(objs, g):
    out = []
    for c in g.m_Components:
        co = objs.get(c.path_id)
        if co is None:
            out.append("ext")
        elif co.type.name == "MonoBehaviour":
            out.append(mb_class(co))
        elif co.type.name not in ("Transform",):
            out.append(co.type.name)
    return out


def tree(objs, go_pid, depth=0, maxd=6):
    g = objs[go_pid].read()
    tr = next((objs[c.path_id].read() for c in g.m_Components
               if c.path_id in objs and objs[c.path_id].type.name in ("Transform", "RectTransform")), None)
    pos = rot = ""
    if tr:
        p, r = tr.m_LocalPosition, tr.m_LocalRotation
        pos = " p(%.3f %.3f %.3f)" % (p.x, p.y, p.z)
        rot = " r(%.2f %.2f %.2f %.2f)" % (r.x, r.y, r.z, r.w)
    print("  " * depth + "%s [%d] {%s}%s%s%s" % (g.m_Name, go_pid, ", ".join(comp_names(objs, g)), pos, rot,
                                                "" if g.m_IsActive else " (inactive)"))
    if tr and depth < maxd:
        for ch in tr.m_Children:
            if ch.path_id in objs:
                tree(objs, objs[ch.path_id].read().m_GameObject.path_id, depth + 1, maxd)


if __name__ == "__main__":
    env, objs = load(sys.argv[1])
    pid = int(sys.argv[2])
    o = objs[pid]
    if o.type.name in ("Transform", "RectTransform"):
        pid = o.read().m_GameObject.path_id
    tree(objs, pid, 0, int(sys.argv[3]) if len(sys.argv) > 3 else 6)
