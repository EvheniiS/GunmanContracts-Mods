import UnityPy, glob, os, re, sys
from gamedir import game_dir
D = os.path.join(game_dir(), 'GunmanContracts_Data')
files = [os.path.join(D, f) for f in os.listdir(D) if re.match(r'(level\d+|sharedassets\d+\.assets|resources\.assets|globalgamemanagers\.assets)$', f)]
for f in files:
    env = UnityPy.load(f)
    for obj in env.objects:
        if obj.type.name == 'AnimatorController':
            try:
                tt = obj.read_typetree()
            except Exception as e:
                print('ERR', f, e); continue
            names = tt.get('m_TOS', [])
            strs = [v for k, v in names]
            leg = [s for s in strs if re.search(r'(?i)leg|knee|kneel', s)]
            print(os.path.basename(f), obj.path_id, tt.get('m_Name'), 'clips', len(tt.get('m_AnimationClips', [])), 'legstates', leg[:30])
