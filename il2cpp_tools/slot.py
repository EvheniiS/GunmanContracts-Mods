"""Decode IL2CPP metadata-usage slots referenced by rip-relative loads in a disassembly (strings, types, methods)."""
import sys, re, struct
from il2 import *
slo, sls = H['stringLiteral']; sdo = H['stringLiteralData'][0]
def strlit(i):
    ln, di = struct.unpack_from('<Ii', d, slo + i*8)
    return d[sdo+di:sdo+di+ln].decode('utf8','replace')
def decode(va):
    v = rq(va)
    if not (v & 1): return None
    ty = (v >> 29) & 7; idx = (v & 0x1FFFFFFE) >> 1
    if v >> 32: return None
    if ty == 5: return 'str "%s"' % strlit(idx)
    if ty == 1 or ty == 2:
        try: return ('typeinfo ' if ty==1 else 'type ') + typestr(idx)
        except Exception: return 'type#%d' % idx
    if ty == 3:
        m = method(idx); return 'method %s::%s' % (tname(m['decl']), m['name'])
    if ty == 4: return 'field#%d' % idx
    if ty == 6: return 'methodref#%d' % idx
    return 'usage%d #%d' % (ty, idx)
import capstone
_md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
def lits(va, n=0x3000):
    o = va2off(va); out = {}
    for ins in _md.disasm(data[o:o+n], va):
        r = re.search(r'\[rip ([+-]) (0x[0-9a-f]+)\]', ins.op_str)
        if r:
            tgt = ins.address + ins.size + int(r.group(2), 16) * (1 if r.group(1) == '+' else -1)
            try: dd = decode(tgt)
            except Exception as e: dd = None
            if dd: out[ins.address] = dd
        if ins.mnemonic == 'int3': break
    return out
if __name__ == '__main__':
    for a in sys.argv[1:]:
        va = name2addr[a][0] if a in name2addr else int(a, 16)
        seen = {}
        for k, v in lits(va).items(): print(hex(k), v)

# ---- method specs (generic method instances, e.g. GetComponent<T>)
_o = None
for _m in re.finditer(re.escape(struct.pack('<QQ', NT, fieldoffs_ptr)), data):
    if _m.start() % 8 == 0: _o = _m.start(); break
_specs = struct.unpack_from('<Q', data, _o - 8)[0]
_ginsts = struct.unpack_from('<Q', data, _o - 56)[0]
def _tptr(p):
    dat = rq(p); attrs = rd(p+8); tt = (attrs >> 16) & 0xff
    if tt in (0x11, 0x12): return tname(dat) if dat < NT else '?'
    return TYPE_NAMES.get(tt, hex(tt))
def methodspec(i):
    mdi, ci, mi = struct.unpack_from('<3i', data, va2off(_specs) + i*12)
    m = method(mdi); s = tname(m['decl']) + '::' + m['name']
    if mi >= 0:
        gi = rq(_ginsts + mi*8); argc = rq(gi); argv = rq(gi+8)
        s += '<' + ','.join(_tptr(rq(argv + k*8)) for k in range(argc)) + '>'
    return s
_old = decode
def decode(va):
    r = _old(va)
    if r and r.startswith('methodref#'):
        try: return 'method ' + methodspec(int(r[10:]))
        except Exception as e: return r
    return r
