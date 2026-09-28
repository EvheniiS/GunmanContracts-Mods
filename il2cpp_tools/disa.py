"""Annotated disassembly of a whole method: call targets named, rip-relative metadata slots decoded
(string literals, TypeInfo, generic methods such as GetComponent<T>). Runs to the next known method start.
Usage: python disa.py <Class::Method | 0xADDR> [> out.txt]
"""
import bisect
import re
import sys

import capstone

import slot
from il2 import name2addr, nearest, va2off, data

_starts = sorted({a for v in name2addr.values() for a in v})


def method_end(va):
    i = bisect.bisect_right(_starts, va)
    return _starts[i] if i < len(_starts) else va + 0x4000


def dis(va):
    end = method_end(va)
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    o = va2off(va)
    for ins in md.disasm(data[o:o + (end - va)], va):
        note = ''
        r = re.search(r'\[rip ([+-]) (0x[0-9a-f]+)\]', ins.op_str)
        if r:
            tgt = ins.address + ins.size + int(r.group(2), 16) * (1 if r.group(1) == '+' else -1)
            try:
                d = slot.decode(tgt)
            except Exception:
                d = None
            if d:
                note = '  ; ' + d
        elif ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
            t = int(ins.op_str, 16)
            if not (va <= t < end):
                n = nearest(t)
                if n and n[1] < 0x40:
                    note = '  ; %s%s' % (n[0], '+0x%x' % n[1] if n[1] else '')
        print('%x: %-7s %s%s' % (ins.address, ins.mnemonic, ins.op_str, note))


if __name__ == '__main__':
    a = sys.argv[1]
    dis(name2addr[a][0] if a in name2addr else int(a, 16))
