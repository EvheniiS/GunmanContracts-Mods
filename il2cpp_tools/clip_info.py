import UnityPy, os, re
from gamedir import game_dir
D = os.path.join(game_dir(), 'GunmanContracts_Data')
for f in ['sharedassets1.assets'] + [x for x in os.listdir(D) if re.match(r'sharedassets\d+\.assets$|resources\.assets$', x) and x!='sharedassets1.assets']:
    env = UnityPy.load(os.path.join(D, f))
    for o in env.objects:
        if o.type.name != 'AnimationClip': continue
        try: name = o.peek_name()
        except Exception: continue
        if not name or not re.search(r'(?i)hit_legs|^hit_', name): continue
        tt = o.read_typetree()
        mc = tt['m_MuscleClip']
        evs = [(e['time'], e['functionName']) for e in tt.get('m_Events', [])]
        print(f, o.path_id, name, 'start', round(mc['m_StartTime'],3), 'stop', round(mc['m_StopTime'],3), 'rate', tt['m_SampleRate'], 'events', evs)
