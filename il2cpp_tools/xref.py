"""xref.py <Class::Method>... : find direct calls / tail jumps to the method(s) and name the callers.

Game code is in the 'il2cpp' PE section, engine code in '.text': both are scanned. Only direct
E8 (call) and E9 (jmp) rel32 are found - calls through vtables, delegates, UnityEvents or
coroutine MoveNext state machines won't show (0 hits does NOT mean "never called").
Byte scanning can also find E8/E9 inside another instruction: results are candidates,
not verified call sites. Confirm each important site with disassembly.
"""
import sys, bisect, numpy as np
from il2 import *

CODE = (b'.text', b'il2cpp')
scans = []
for s in pe.sections:
    if s.Name.rstrip(b'\x00') not in CODE:
        continue
    t0 = s.PointerToRawData; tn = s.SizeOfRawData; tva = base + s.VirtualAddress
    buf = np.frombuffer(data, dtype=np.uint8, count=tn, offset=t0)
    idx = np.nonzero((buf[:-4] == 0xE8) | (buf[:-4] == 0xE9))[0]
    rel = (buf[idx+1].astype(np.int64) | (buf[idx+2].astype(np.int64) << 8)
           | (buf[idx+3].astype(np.int64) << 16) | (buf[idx+4].astype(np.int64) << 24))
    rel = np.where(rel >= 1 << 31, rel - (1 << 32), rel)
    scans.append((tva, idx, tva + idx + 5 + rel, buf))

addrs = sorted(addr2name)
for name in sys.argv[1:]:
    for a in name2addr.get(name, []):
        hits = [(tva, int(h), 'call' if buf[h] == 0xE8 else 'jmp')
                for tva, idx, tgt, buf in scans for h in idx[tgt == a]]
        print(f"== callers of {name} @ {a:#x}: {len(hits)}")
        for tva, h, kind in hits:
            va = tva + h
            i = bisect.bisect_right(addrs, va) - 1
            owner = f'{addr2name[addrs[i]]}+{va - addrs[i]:#x}' if i >= 0 else '(before first known method)'
            print(f"   {kind:4} candidate {va:#x}  near {owner}")
