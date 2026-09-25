import sys
from il2 import *
for nm in sys.argv[1:]:
    for t in find_type(nm):
        print('==', tname(t), '[%d]'%t)
        for f in fields_of(t): print('   F %-40s %-30s %s' % (f[0], f[1], hex(f[2]) if f[2] is not None else None))
        for k in range(types[t]['mc']):
            mm = method(types[t]['mStart']+k)
            full = tname(t)+'::'+mm['name']
            a = name2addr.get(full, [None])[0]
            print('   M', sig(types[t]['mStart']+k), hex(a) if a else '')
