"""Read-only, dependency-free MIX reader (XCC MIX format), used to inspect stock game data.

Blowfish's P-array and S-boxes are the fractional hexadecimal digits of pi, so they are
computed here instead of being copied from a library. The implementation is checked
against the standard all-zero-key test vector before any archive is read.

Usage: python -I mixread.py <name> [archive ...]   -> writes <name> from the first archive
       containing it (nested local/cache/conquer mixes included) to ./stock/<name>
"""
from pathlib import Path
import base64, struct, sys, zlib

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[3]


def pi_hex_words(count):
    """First `count` 32-bit words of pi's fractional part (Machin formula, exact integers)."""
    digits = count * 8 + 16
    scale = 16 ** digits
    def arctan_inv(x):
        total, term, n, sign = 0, scale // x, 1, 1
        while term:
            total += sign * (term // n)
            term //= x * x; n += 2; sign = -sign
        return total
    pi = 4 * (4 * arctan_inv(5) - arctan_inv(239))
    frac = pi - 3 * scale
    text = format(frac, 'x').rjust(digits, '0')
    return [int(text[i * 8:i * 8 + 8], 16) for i in range(count)]


_WORDS = pi_hex_words(18 + 1024)


class Blowfish:
    def __init__(self, key):
        self.p = _WORDS[:18]
        self.s = [_WORDS[18 + 256 * i:18 + 256 * (i + 1)] for i in range(4)]
        j = 0
        for i in range(18):
            k = 0
            for _ in range(4):
                k = (k << 8) | key[j % len(key)]; j += 1
            self.p[i] ^= k
        l = r = 0
        for i in range(0, 18, 2):
            l, r = self._enc(l, r); self.p[i], self.p[i + 1] = l, r
        for box in self.s:
            for i in range(0, 256, 2):
                l, r = self._enc(l, r); box[i], box[i + 1] = l, r

    def _f(self, x):
        s = self.s
        return ((((s[0][x >> 24] + s[1][(x >> 16) & 255]) & 0xFFFFFFFF) ^ s[2][(x >> 8) & 255]) + s[3][x & 255]) & 0xFFFFFFFF

    def _enc(self, l, r):
        for i in range(16):
            l ^= self.p[i]; r ^= self._f(l); l, r = r, l
        l, r = r, l
        return l ^ self.p[17], r ^ self.p[16]

    def _dec(self, l, r):
        for i in range(17, 1, -1):
            l ^= self.p[i]; r ^= self._f(l); l, r = r, l
        l, r = r, l
        return l ^ self.p[0], r ^ self.p[1]

    def decrypt_ecb(self, data):
        out = bytearray()
        for i in range(0, len(data) - len(data) % 8, 8):   # standard big-endian ECB, as the earlier extractor
            out += struct.pack('>II', *self._dec(*struct.unpack_from('>II', data, i)))
        return bytes(out)


def _self_test():
    bf = Blowfish(bytes(8))
    l, r = bf._enc(0, 0)
    assert (l, r) == (0x4EF99745, 0x6198DD78), 'Blowfish self-test failed'


def mix_id(name):
    b = name.upper().encode('ascii')
    rem = len(b) % 4
    if rem:
        b += bytes([rem]) + bytes([b[len(b) - rem]]) * (3 - rem)
    return zlib.crc32(b)


PUBLIC = base64.b64decode('AihRvNoIbTn85FZRYNZRcT+i6KpU+maCsEqr3Q5q+LDB5tH7Tz2qQ38V')


def index(data):
    flags = struct.unpack_from('<I', data)[0] if data[:2] == b'\0\0' else None
    start = 4 if flags is not None else 0
    if flags is not None and flags & 0x20000:
        modulus = int.from_bytes(PUBLIC[2:], 'big')
        outlen = (modulus.bit_length() - 1) // 8
        inlen = outlen + 1
        block = data[4:84]
        key = b''.join(pow(int.from_bytes(block[i:i + inlen], 'little'), 65537, modulus)
                       .to_bytes(inlen, 'little')[:outlen] for i in range(0, 80, inlen))[:56]
        bf = Blowfish(key)
        count = struct.unpack_from('<H', bf.decrypt_ecb(data[84:92]))[0]
        size = ((6 + 12 * count + 7) // 8) * 8
        header = bf.decrypt_ecb(data[84:84 + size])
        start = 84 + size
    else:
        count = struct.unpack_from('<H', data, start)[0]
        header = data[start:start + 6 + 12 * count]
        start += 6 + 12 * count
    count, body = struct.unpack_from('<HI', header)
    result = {}
    for i in range(count):
        ident, offset, length = struct.unpack_from('<III', header, 6 + 12 * i)
        result[ident] = (start + offset, length)
    return result


NESTED = ['local.mix', 'cache.mix', 'conquer.mix', 'generic.mix', 'localmd.mix', 'cachemd.mix']


def find(data, name, origin, depth=0):
    entries = index(data)
    hit = entries.get(mix_id(name))
    if hit:
        return origin, data[hit[0]:hit[0] + hit[1]]
    if depth < 3:
        for nested in NESTED:
            sub = entries.get(mix_id(nested))
            if sub:
                found = find(data[sub[0]:sub[0] + sub[1]], name, origin + '/' + nested, depth + 1)
                if found:
                    return found
    return None


if __name__ == '__main__':
    _self_test()
    name = sys.argv[1]
    archives = sys.argv[2:] or ['ra2.mix', 'language.mix', 'multi.mix']
    for archive in archives:
        result = find((GAME / archive).read_bytes(), name, archive)
        if result:
            out = HERE / 'stock' / name
            out.parent.mkdir(exist_ok=True)
            out.write_bytes(result[1])
            print(f'{name}: found in {result[0]} ({len(result[1])} bytes) -> {out}')
            break
    else:
        print(f'{name}: not found in {archives}')
