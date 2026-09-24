import sys, re
from disx import *
pat = sys.argv[1]; prefix = sys.argv[2] if len(sys.argv) > 2 else 'ANBBasicNPC'
seen=set()
for k in sorted(name2addr):
    if not (k.startswith(prefix) or (prefix=='ANBBasicNPC' and k.startswith('<'))): continue
    for a in name2addr[k]:
        if a in seen: continue
        seen.add(a)
        try: txt = dis(a, 0x6000)
        except Exception: continue
        hits=[l for l in txt.split('\n') if re.search(pat, l)]
        if hits:
            print('==', k, hex(a)); print('\n'.join(hits))
