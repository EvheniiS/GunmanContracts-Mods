import UnityPy, os, struct, sys
from gamedir import game_dir
env = UnityPy.load(os.path.join(game_dir(),'GunmanContracts_Data','sharedassets1.assets'))
pid = int(sys.argv[1]) if len(sys.argv) > 1 else 1027
o=[o for o in env.objects if o.path_id==pid][0]
tt=o.read_typetree(); mc=tt['m_MuscleClip']; c=mc['m_Clip']['data']
idx = mc['m_IndexArray']
ci = idx[8]  # RootT.y (humanoid: 0-6 motion, 7-9 RootT)
s=c['m_StreamedClip']; S=s['curveCount']; d=c['m_DenseClip']; Dn=d['m_CurveCount']
print('RootT.y curve index', ci, 'streamed', S, 'dense', Dn, 'stop', mc['m_StopTime'])
def sample(t):
    if ci < S:
        raw = struct.pack(f'<{len(s["data"])}I', *s['data']); p=0; last={}
        while p < len(raw):
            ft, n = struct.unpack_from('<fI', raw, p); p+=8
            for _ in range(n):
                k, a,b,cc,dd = struct.unpack_from('<I4f', raw, p); p+=20
                if ft <= t: last[k]=(ft,a,b,cc,dd)
            if ft > t: break
        ft,a,b,cc,dd = last[ci]; x=t-ft; return ((a*x+b)*x+cc)*x+dd
    elif ci < S+Dn:
        j = ci - S; f = min(int(round((t-d['m_BeginTime'])*d['m_SampleRate'])), d['m_FrameCount']-1)
        return d['m_SampleArray'][f*Dn + j]
    else:
        return c['m_ConstantClip']['data'][ci-S-Dn]
T = mc['m_StopTime']
for i in range(0, 57):
    t = T*i/56
    print(f"{t:5.2f}s  {t/T*100:5.1f}%  hipY={sample(t):.3f}")
