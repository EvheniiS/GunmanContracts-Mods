import sys, capstone
from il2 import *
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = False

def func_bytes(va, maxlen=0x1200):
    o = va2off(va)
    # stop at next known function start
    i = bisect.bisect_right(sorted_addrs, va)
    end = sorted_addrs[i] if i < len(sorted_addrs) else va + maxlen
    n = min(end - va, maxlen)
    return data[o:o+n]

def dis(va, maxlen=0x1200):
    out = []
    for ins in md.disasm(func_bytes(va, maxlen), va):
        s = f"{ins.address:x}: {ins.mnemonic:6} {ins.op_str}"
        if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
            tgt = int(ins.op_str, 16)
            nm = addr2name.get(tgt)
            if nm: s += f"    ; {nm}"
            elif not (va <= tgt < va + maxlen):
                nr = nearest(tgt)
                if nr and nr[1] < 0x4000: s += f"    ; ~{nr[0]}+{nr[1]:#x}"
        out.append(s)
        if ins.mnemonic == 'int3': break
    return '\n'.join(out)

if __name__ == '__main__':
    for arg in sys.argv[1:]:
        addrs = name2addr.get(arg) or [int(arg, 16)]
        for a in addrs:
            print(f"==== {arg} @ {a:#x}")
            print(dis(a))
