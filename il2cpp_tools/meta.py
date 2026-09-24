import struct, sys, re
P = r"E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\GunmanContracts_Data\il2cpp_data\Metadata\global-metadata.dat"
d = open(P, 'rb').read()
names = ["stringLiteral","stringLiteralData","string","events","properties","methods",
 "parameterDefaultValues","fieldDefaultValues","fieldAndParameterDefaultValueData","fieldMarshaledSizes",
 "parameters","fields","genericParameters","genericParameterConstraints","genericContainers","nestedTypes",
 "interfaces","vtableMethods","interfaceOffsets","typeDefinitions","images","assemblies","fieldRefs",
 "referencedAssemblies","attributeData","attributeDataRange","unresolvedIndirectCallParameterTypes",
 "unresolvedIndirectCallParameterRanges","windowsRuntimeTypeNames","windowsRuntimeStrings","exportedTypeDefinitions"]
H = {}
for i, n in enumerate(names):
    H[n] = struct.unpack_from('<II', d, 8 + i*8)
sOff = H['string'][0]
def S(i):
    e = d.index(b'\0', sOff+i); return d[sOff+i:e].decode('utf8','replace')
TD = 88
to, ts = H['typeDefinitions']
if '--sizes' in sys.argv:
    for n in names: print(n, H[n], [H[n][1]/k for k in (12,36,84,88,92)][:5])
    sys.exit()
ntypes = ts // TD
fo = H['fields'][0]; mo = H['methods'][0]; po = H['properties'][0]
types = []
for t in range(ntypes):
    b = to + t*TD
    v = struct.unpack_from('<15i8H2I', d, b) if TD == 84 else struct.unpack_from('<16i8H2I', d, b)
    types.append(v)
def tinfo(v):
    if TD == 84:
        nameI, nsI, byval, decl, parent, gc, flags, fStart, mStart, eStart, pStart, nStart, iStart, vStart, ioStart = v[:15]
        mc, pc, fc, ec, nc, vc, ic, ioc = v[15:23]
    else:
        nameI, nsI, byval, byref, decl, parent, gc, flags, fStart, mStart, eStart, pStart, nStart, iStart, vStart, ioStart = v[:16]
        mc, pc, fc, ec, nc, vc, ic, ioc = v[16:24]
    return dict(name=S(nameI), ns=S(nsI), fStart=fStart, fc=fc, mStart=mStart, mc=mc, pStart=pStart, pc=pc, parent=parent, decl=decl)
pat = re.compile(sys.argv[1]) if len(sys.argv) > 1 else None
for t, v in enumerate(types):
    ti = tinfo(v)
    full = (ti['ns'] + '.' if ti['ns'] else '') + ti['name']
    if pat and not pat.search(full): continue
    print(f"== [{t}] {full}  fields={ti['fc']} methods={ti['mc']}")
    for f in range(ti['fc']):
        nI, tI, tok = struct.unpack_from('<3i', d, fo + (ti['fStart']+f)*12)
        print('   F', S(nI), f'(type#{tI})')
    for m in range(ti['mc']):
        nI = struct.unpack_from('<i', d, mo + (ti['mStart']+m)*36)[0]
        print('   M', S(nI))
