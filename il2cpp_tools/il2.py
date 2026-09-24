"""Minimal IL2CPP v31 metadata + GameAssembly mapper: method addresses, field offsets, field types."""
import struct, pefile, pickle, os, bisect
G = r"E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone"
MD = G + r"\GunmanContracts_Data\il2cpp_data\Metadata\global-metadata.dat"
GA = G + r"\GameAssembly.dll"
d = open(MD, 'rb').read()
names = ["stringLiteral","stringLiteralData","string","events","properties","methods",
 "parameterDefaultValues","fieldDefaultValues","fieldAndParameterDefaultValueData","fieldMarshaledSizes",
 "parameters","fields","genericParameters","genericParameterConstraints","genericContainers","nestedTypes",
 "interfaces","vtableMethods","interfaceOffsets","typeDefinitions","images","assemblies","fieldRefs",
 "referencedAssemblies","attributeData","attributeDataRange","unresolvedIndirectCallParameterTypes",
 "unresolvedIndirectCallParameterRanges","windowsRuntimeTypeNames","windowsRuntimeStrings","exportedTypeDefinitions"]
H = {n: struct.unpack_from('<II', d, 8 + i*8) for i, n in enumerate(names)}
sOff = H['string'][0]
def S(i):
    e = d.index(b'\0', sOff+i); return d[sOff+i:e].decode('utf8', 'replace')

# ---- types
to, ts = H['typeDefinitions']; NT = ts // 88
types = []
for t in range(NT):
    v = struct.unpack_from('<16i8H2I', d, to + t*88)
    types.append(dict(name=S(v[0]), ns=S(v[1]), byval=v[2], decl=v[4], parent=v[5], fStart=v[8], mStart=v[9],
                      mc=v[16], fc=v[18]))
def tname(t):
    ty = types[t]; n = ty['name']
    if ty['decl'] >= 0 and ty['decl'] < 0x7fffffff:
        pass
    return (ty['ns'] + '.' if ty['ns'] else '') + n
# ---- images
io_, isz = H['images']
images = []
for i in range(isz // 40):
    nI, aI, tS, tC = struct.unpack_from('<4i', d, io_ + i*40)
    images.append((S(nI), tS, tC))
# ---- methods
mo = H['methods'][0]
def method(mi):
    v = struct.unpack_from('<7i4H', d, mo + mi*36)
    return dict(name=S(v[0]), decl=v[1], ret=v[2], pstart=v[4], token=v[6], pcount=v[10])
fo = H['fields'][0]
def field(fi):
    nI, tI, tok = struct.unpack_from('<3i', d, fo + fi*12)
    return S(nI), tI

# ---- PE
pe = pefile.PE(GA, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
data = pe.__data__
secs = [(s.Name.rstrip(b'\0').decode(), s.VirtualAddress, s.Misc_VirtualSize, s.PointerToRawData, s.SizeOfRawData) for s in pe.sections]
def va2off(va):
    rva = va - base
    for n, v, vs, p, ps in secs:
        if v <= rva < v + max(vs, ps): return p + rva - v
    return None
def off2va(off):
    for n, v, vs, p, ps in secs:
        if p <= off < p + ps: return base + v + off - p
def rq(va): return struct.unpack_from('<Q', data, va2off(va))[0]
def rd(va): return struct.unpack_from('<I', data, va2off(va))[0]
def ri(va): return struct.unpack_from('<i', data, va2off(va))[0]

CACHE = os.path.join(os.path.dirname(__file__), 'il2map.pkl')
if os.path.exists(CACHE):
    addr2name, name2addr, fieldoffs_ptr, types_ptr = pickle.load(open(CACHE, 'rb'))
else:
    import re
    addr2name = {}; name2addr = {}
    tn = [tname(t) for t in range(NT)]
    NI = len(images)
    mods = None
    for m in re.finditer(re.escape(struct.pack('<Q', NI)), data):
        o = m.start()
        if o % 8: continue
        arr = struct.unpack_from('<Q', data, o+8)[0]
        if va2off(arr) is None: continue
        try:
            p0 = rq(arr); s = rq(p0); so = va2off(s)
            if so is not None and data[so:so+64].split(b'\0')[0].endswith(b'.dll'):
                mods = arr; break
        except Exception: pass
    print('codegen modules at', hex(mods))
    bym = {}
    for i in range(NI):
        p = rq(mods + i*8); so = va2off(rq(p))
        bym[data[so:so+200].split(b'\0')[0].decode()] = p
    for (iname, tS, tC) in images:
        p = bym[iname]; arr = rq(p+16)
        for t in range(tS, tS+tC):
            for k in range(types[t]['mc']):
                mm = method(types[t]['mStart'] + k)
                rid = mm['token'] & 0xFFFFFF
                if not arr: continue
                fp = rq(arr + (rid-1)*8)
                full = tn[t] + '::' + mm['name']
                if fp:
                    addr2name.setdefault(fp, full)
                    name2addr.setdefault(full, []).append(fp)
    # metadata registration: fieldOffsetsCount == typeDefinitionsSizesCount == NT
    pat = struct.pack('<Q', NT)
    fieldoffs_ptr = types_ptr = None
    for m in re.finditer(re.escape(pat), data):
        o = m.start()
        if o % 8: continue
        if struct.unpack_from('<Q', data, o+16)[0] == NT:
            fieldoffs_ptr = struct.unpack_from('<Q', data, o+8)[0]
            # types are 4 pairs before: typesCount/types at o-32
            types_ptr = struct.unpack_from('<Q', data, o-32+8)[0]
            break
    pickle.dump((addr2name, name2addr, fieldoffs_ptr, types_ptr), open(CACHE, 'wb'))

sorted_addrs = sorted(addr2name)
def nearest(va):
    i = bisect.bisect_right(sorted_addrs, va) - 1
    if i >= 0: return addr2name[sorted_addrs[i]], va - sorted_addrs[i]

TYPE_NAMES = {1:'void',2:'bool',3:'char',4:'i8',5:'u8',6:'i16',7:'u16',8:'int',9:'uint',10:'long',11:'ulong',12:'float',13:'double',14:'string',
              0x11:'valuetype',0x12:'class',0x15:'generic',0x1d:'array[]',0x14:'array',0x1c:'object',0x13:'T',0x1e:'M'}
def typestr(ti):
    p = rq(types_ptr + ti*8)
    dat = rq(p); attrs = rd(p+8); tt = (attrs >> 16) & 0xff
    if tt in (0x11, 0x12): return tname(dat) if dat < NT else '?'
    return TYPE_NAMES.get(tt, hex(tt))

def fields_of(t):
    ty = types[t]
    arr = rq(fieldoffs_ptr + t*8)
    out = []
    for k in range(ty['fc']):
        n, tI = field(ty['fStart'] + k)
        off = ri(arr + k*4) if arr else None
        out.append((n, typestr(tI), off))
    return out

def find_type(full):
    return [t for t in range(NT) if tname(t) == full or types[t]['name'] == full]

def parents(t):
    # Bounded: a mis-resolved parent can point back at itself, and an unbounded loop once
    # consumed ~60 GB of RAM. Parent resolution here is also unreliable for v31; don't trust it.
    out = []
    for _ in range(16):
        if t is None or not (0 <= t < NT) or t in out: break
        out.append(t)
        p = types[t]['parent']
        if p < 0: break
        # parent is a type index into types table -> Il2CppType -> data is typedef index
        pp = rq(types_ptr + p*8)
        attrs = rd(pp+8); tt = (attrs >> 16) & 0xff
        if tt not in (0x11, 0x12): break
        t = rq(pp)
    return out

po_ = H['parameters'][0]
def sig(mi):
    m = method(mi)
    ps = []
    for k in range(m['pcount']):
        nI, tok, tI = struct.unpack_from('<3i', d, po_ + (m['pstart']+k)*12)
        ps.append(f"{typestr(tI)} {S(nI)}")
    return f"{typestr(m['ret'])} {m['name']}({', '.join(ps)})"
def methods_of(t):
    return [sig(types[t]['mStart']+k) for k in range(types[t]['mc'])]
