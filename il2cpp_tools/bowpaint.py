import UnityPy,struct,sys
G=r"E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\GunmanContracts_Data"
env=UnityPy.load(G+r"\sharedassets2.assets")
sf=list(env.files.values())[0]
objs=sf.objects
gos={}
for pid,o in objs.items():
    if o.type.name=='GameObject':
        try:
            n=o.read().m_Name
        except Exception: continue
        if n.startswith('Bow - '): gos[pid]=n
print(gos)
# find MonoBehaviours referencing these pathIDs
for pid,o in objs.items():
    if o.type.name!='MonoBehaviour': continue
    raw=o.get_raw_data()
    for gp,gn in gos.items():
        k=struct.pack('<iq',0,gp)
        i=raw.find(k)
        if i>=0: print('MB',pid,'size',len(raw),'refs',gn,'at',i)
raw=objs[89005].get_raw_data()
o=0
def rs(o):
    n=struct.unpack_from('<I',raw,o)[0]; s=raw[o+4:o+4+n].decode('latin1'); o=(o+4+n+3)&~3; return s,o
# header: GameObject pptr, enabled, script pptr, name
go=struct.unpack_from('<iq',raw,0); o=12; o+=4; sc=struct.unpack_from('<iq',raw,o); o+=12
name,o=rs(o)
print('go',go,'script',sc,'name',repr(name),'fields start',o)
print(raw[o:o+140].hex(' '))
# locate designColor block: walk back from first ref at 200
p=raw.find(bytes.fromhex('0000803f00004040'))
print('design1,max1,design2,max2',struct.unpack_from('<4f',raw,p)); o=p+16
for part in (1,2):
  for k in range(1,6):
    n,o=rs(o); col=struct.unpack_from('<4f',raw,o); o+=16
    c=struct.unpack_from('<I',raw,o)[0]; o+=4
    refs=[gos.get(struct.unpack_from('<iq',raw,o+12*j)[1], struct.unpack_from('<iq',raw,o+12*j)) for j in range(c)]; o+=12*c
    i,o=rs(o)
    print(f'{part}_{k}', repr(n), tuple(round(x,3) for x in col), refs, 'ID=',repr(i))
