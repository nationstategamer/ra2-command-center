"""Find original-RA2 (game.exe) equivalents of Yuri's Revenge (gamemd.exe) functions.

Static analysis only: nothing is executed. Yuri's Revenge was built from the Red Alert 2
code base, so most engine functions keep the same instruction shape while absolute
addresses and some structure offsets move. Each instruction is normalised to a token
(mnemonic + registers + small offsets/immediates; large addresses become wildcards) and
RA2's code is searched for the best-aligned window.

Usage (from this folder):
  python -I match.py find <yr_va> [length]      best RA2 candidates for a YR function
  python -I match.py dump <exe> <va> [bytes]     disassemble game.exe / gamemd.exe
  python -I match.py refs <exe> <value>          addresses whose bytes contain a 32-bit value
  python -I match.py callers <exe> <va>          direct call/jmp sites that target <va>
"""
from pathlib import Path
import sys, struct, pickle, hashlib

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / 'deps'))
import pefile, capstone  # noqa: E402

GAME = HERE.parents[3]
CACHE = HERE / 'cache'
EXPECTED = {
    'game.exe': '73288c03b58d370be268ca6d156b4e33bfdb2066dc980359467d8852ff3b00df',
    'gamemd.exe': '3e81a61775d2745d1dabe397325ef663cd994ffc194da4e998e3bf5d2d308600',
}


class Bin:
    def __init__(self, name):
        self.name = name
        self.data = (GAME / name).read_bytes()
        digest = hashlib.sha256(self.data).hexdigest()
        if digest != EXPECTED[name]:
            raise SystemExit(f'{name} hash {digest} is not the analysed version')
        self.pe = pefile.PE(data=self.data, fast_load=True)
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        text = next(s for s in self.pe.sections if s.Name.rstrip(b'\0') == b'.text')
        self.text_va = self.base + text.VirtualAddress
        self.text = text.get_data()[:text.Misc_VirtualSize]
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.skipdata = True

    def bytes(self, va, n):
        off = self.pe.get_offset_from_rva(va - self.base)
        return self.data[off:off + n]

    def dis(self, va, n):
        return list(self.md.disasm(self.bytes(va, n), va))

    def dump(self, va, n=160):
        for i in self.dis(va, n):
            print(f'{i.address:08X} {i.bytes.hex(" "):<30} {i.mnemonic} {i.op_str}')

    def stream(self):
        """Linear sweep of .text -> (addresses, tokens), cached per executable."""
        CACHE.mkdir(exist_ok=True)
        path = CACHE / (self.name + '.tokens.pickle')
        if path.exists():
            return pickle.loads(path.read_bytes())
        addrs, toks = [], []
        for i in self.md.disasm_lite(self.text, self.text_va):
            addrs.append(i[0])
            toks.append(token(i[2], i[3]))
        result = (addrs, toks)
        path.write_bytes(pickle.dumps(result))
        return result


def norm_operand(op):
    op = op.strip()
    out = []
    for part in op.replace('[', ' [ ').replace(']', ' ] ').replace('+', ' + ').replace('-', ' - ').replace('*', ' * ').split():
        if part.startswith('0x') or part.isdigit():
            try:
                value = int(part, 0)
            except ValueError:  # far pointers in data swept as code
                out.append('#'); continue
            out.append(part if value < 0x80 else '#')
        else:
            out.append(part)
    return ''.join(out)


def token(mnemonic, op_str):
    if mnemonic in ('call', 'jmp') or mnemonic.startswith('j') or mnemonic == 'loop':
        return mnemonic + ('' if op_str.startswith('0x') else ' ' + norm_operand(op_str))
    return mnemonic + ' ' + ','.join(norm_operand(o) for o in op_str.split(',')) if op_str else mnemonic


def function_tokens(binary, va, limit):
    toks, addrs = [], []
    for i in binary.md.disasm(binary.bytes(va, limit * 8), va):
        toks.append(token(i.mnemonic, i.op_str)); addrs.append(i.address)
        if len(toks) >= limit or (i.mnemonic == 'ret' and len(toks) > 4):
            break
    return toks


def find(yr_va, length=60, top=5):
    yr, ra2 = Bin('gamemd.exe'), Bin('game.exe')
    probe = function_tokens(yr, yr_va, length)
    addrs, toks = ra2.stream()
    gram = 5
    index = {}
    for p in range(len(toks) - gram):
        index.setdefault(hash(tuple(toks[p:p + gram])), []).append(p)
    votes = {}
    for k in range(0, len(probe) - gram):
        for p in index.get(hash(tuple(probe[k:k + gram])), ()):
            start = p - k
            if start >= 0:
                votes[start] = votes.get(start, 0) + 1
    scored = []
    for start, _ in sorted(votes.items(), key=lambda kv: -kv[1])[:200]:
        window = toks[start:start + len(probe)]
        same = sum(a == b for a, b in zip(probe, window))
        scored.append((same / len(probe), addrs[start]))
    scored.sort(reverse=True)
    print(f'YR {yr_va:08X}: {len(probe)} instructions compared')
    for score, va in scored[:top]:
        print(f'  RA2 {va:08X}  similarity {score:.2f}')
    return scored[:top]


def refs(binary, value):
    pat = struct.pack('<I', value)
    out, start = [], 0
    while (off := binary.data.find(pat, start)) >= 0:
        start = off + 1
        try:
            out.append(binary.base + binary.pe.get_rva_from_offset(off))
        except Exception:
            pass
    return out


def callers(binary, target):
    addrs, toks = binary.stream()
    found = []
    text = binary.text
    for idx in range(len(text) - 5):
        if text[idx] in (0xE8, 0xE9):
            va = binary.text_va + idx
            if (va + 5 + struct.unpack_from('<i', text, idx + 1)[0]) & 0xFFFFFFFF == target:
                found.append(va)
    return found


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'find':
        find(int(sys.argv[2], 0), int(sys.argv[3], 0) if len(sys.argv) > 3 else 60)
    elif cmd == 'dump':
        Bin(sys.argv[2]).dump(int(sys.argv[3], 0), int(sys.argv[4], 0) if len(sys.argv) > 4 else 160)
    elif cmd == 'refs':
        print(' '.join(f'{x:08X}' for x in refs(Bin(sys.argv[2]), int(sys.argv[3], 0))))
    elif cmd == 'callers':
        print(' '.join(f'{x:08X}' for x in callers(Bin(sys.argv[2]), int(sys.argv[3], 0))))
