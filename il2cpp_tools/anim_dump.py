import UnityPy, os, json
from gamedir import game_dir
D = os.path.join(game_dir(), 'GunmanContracts_Data')
env = UnityPy.load(os.path.join(D, 'sharedassets1.assets'))
obj = [o for o in env.objects if o.path_id == 1777][0]
tt = obj.read_typetree()
tos = {k: v for k, v in tt['m_TOS']}
c = tt['m_Controller']
print('layers:', [(i, tos.get(l['data']['m_Binding'], l['data']['m_Binding'])) for i, l in enumerate(c['m_LayerArray'])])
params = c['m_Values']['data']['m_ValueArray']
print('params:', [(tos.get(p['m_ID']), p['m_Type']) for p in params])
for smi, sm in enumerate(c['m_StateMachineArray']):
    sm = sm['data']
    for st in sm['m_StateConstantArray']:
        st = st['data']
        nm = tos.get(st['m_FullPathID'], st['m_FullPathID'])
        if 'leg' not in str(nm).lower() and 'No hit' not in str(nm): continue
        print(f"\nSM{smi} STATE {nm} speed={st['m_Speed']} speedParam={tos.get(st.get('m_SpeedParamID'))} loop={st.get('m_Loop')}")
        for tr in st['m_TransitionConstantArray']:
            tr = tr['data']
            conds = [(tos.get(cc['data']['m_ConditionEvent'], cc['data']['m_ConditionEvent']), cc['data']['m_ConditionMode'], cc['data']['m_EventTreshold']) for cc in tr['m_ConditionConstantArray']]
            print('   ->', tos.get(tr['m_FullPathID'], tr['m_FullPathID']), 'hasExit', tr['m_HasExitTime'], 'exit', round(tr['m_ExitTime'],3), 'dur', round(tr['m_TransitionDuration'],3), 'fixed', tr.get('m_HasFixedDuration'), 'conds', conds)
    # any-state transitions
    for tr in sm['m_AnyStateTransitionConstantArray']:
        tr = tr['data']
        n = tos.get(tr['m_FullPathID'], tr['m_FullPathID'])
        if 'leg' in str(n).lower():
            print('ANY ->', n, tr['m_HasExitTime'])
