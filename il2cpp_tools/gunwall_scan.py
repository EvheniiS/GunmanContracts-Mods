"""Dump the gun wall (ANBGunwall arsenal panel) from the scene files: its Items / ItemsID lists and,
for each entry, the GameObject hierarchy with component class names and the ANBGunwallSpot fields.
Usage: python gunwall_scan.py [levelN ...]   (default: every level)
"""
import os
import struct
import sys

import UnityPy

from gamedir import game_dir

D = os.path.join(game_dir(), "GunmanContracts_Data")
import pickle
MONOSCRIPTS = pickle.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "monoscripts.pkl"), "rb"))


class R:
    def __init__(self, b):
        self.b, self.o = b, 0

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.o)[0]; self.o += 4; return v

    def f32(self):
        v = struct.unpack_from("<f", self.b, self.o)[0]; self.o += 4; return v

    def u8(self):
        v = self.b[self.o]; self.o += 1; return v

    def align(self):
        self.o = (self.o + 3) & ~3

    def pptr(self):
        f = self.i32(); p = struct.unpack_from("<q", self.b, self.o)[0]; self.o += 8; return (f, p)

    def s(self):
        n = self.i32(); v = self.b[self.o:self.o + n].decode("utf8", "replace"); self.o += n; self.align(); return v

    def boolean(self):
        v = self.u8(); self.align(); return bool(v)


def mb_head(raw):
    r = R(raw)
    go = r.pptr(); r.boolean(); script = r.pptr(); name = r.s()
    return r, go, script, name


def main(levels):
    for lv in levels:
        path = os.path.join(D, lv)
        if not os.path.exists(path):
            continue
        env = UnityPy.load(path)
        objs = {o.path_id: o for o in env.objects}
        ms = MONOSCRIPTS

        def cls(o):
            try:
                return ms.get(mb_head(o.get_raw_data())[2][1], "?")
            except Exception:
                return "?"

        walls = [o for o in objs.values() if o.type.name == "MonoBehaviour" and cls(o) == "ANBGunwall"]
        spots = [o for o in objs.values() if o.type.name == "MonoBehaviour" and cls(o) == "ANBGunwallSpot"]
        print("==", lv, "ANBGunwall:", len(walls), "ANBGunwallSpot:", len(spots))

        def goname(pid):
            o = objs.get(pid)
            return o.read().m_Name if o else "<ext %d>" % pid

        def comps(go_pid):
            g = objs[go_pid].read()
            out = []
            for c in g.m_Components:
                co = objs.get(c.path_id)
                if not co:
                    continue
                out.append(cls(co) if co.type.name == "MonoBehaviour" else co.type.name)
            return g, out

        def tree(go_pid, depth=0, maxd=4):
            g, cs = comps(go_pid)
            print("   " + "  " * depth + "%s  [%s]%s" % (g.m_Name, ", ".join(cs), "" if g.m_IsActive else "  (inactive)"))
            if depth >= maxd:
                return
            tr = None
            for c in g.m_Components:
                co = objs.get(c.path_id)
                if co and co.type.name in ("Transform", "RectTransform"):
                    tr = co.read(); break
            if tr:
                for ch in tr.m_Children:
                    cto = objs.get(ch.path_id)
                    if cto:
                        tree(cto.read().m_GameObject.path_id, depth + 1, maxd)

        for w in walls:
            raw = w.get_raw_data()
            r, go, _, _ = mb_head(raw)
            main_spot = r.pptr(); sec = r.pptr(); r.pptr(); r.pptr()
            n = r.i32(); items = [r.pptr() for _ in range(n)]
            n2 = r.i32(); ids = [r.s() for _ in range(n2)]
            print(" wall on GO '%s'  items %d  ids %s" % (goname(go[1]), n, ids))
            for (f, p), iid in zip(items, ids):
                print(" -- item '%s' id '%s'" % (goname(p), iid))
                tree(p, 1, 3)

        for s in spots:
            raw = s.get_raw_data()
            r, go, _, _ = mb_head(raw)
            prefab = r.pptr()
            is_pistol, is_knife, only_gun, start_sock, slot_grab = (r.boolean() for _ in range(5))  # each bool is 4-byte aligned
            lock_t = r.f32(); gun_parent = r.pptr(); travel = r.f32(); travel_t = r.f32(); demo_t = r.f32()
            keep = r.boolean(); demo = r.pptr(); slot_id = r.s()
            print(" spot on '%s': prefab %s pistol %d knife %d onlyGun %d startSock %d slotGrab %d parent %s demo %s slotID '%s'"
                  % (goname(go[1]), prefab, is_pistol, is_knife, only_gun, start_sock, slot_grab,
                     goname(gun_parent[1]) if gun_parent[0] == 0 and gun_parent[1] else gun_parent,
                     goname(demo[1]) if demo[0] == 0 and demo[1] else demo, slot_id))


if __name__ == "__main__":
    main(sys.argv[1:] or ["level%d" % i for i in range(0, 6)])
