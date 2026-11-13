"""Verify planned hook sites: exact expected bytes, and no branch from anywhere in .text lands
strictly inside a patched range (a branch to the first byte is fine)."""
import sys
sys.path.insert(0, '.')
import match
SITES = {
    0x519150: '8D442408B958238300',   # Commands: register (before KEYBOARD.INI load)
    0x519164: '8D4C2414BE2E000000',   # Commands: default keys (after KEYBOARD.INI load)
    0x540433: '393D98D2A300',         # Main loop: quick save point (campaign autosave check)
    0x6A5252: '8D4D24518BCD',         # Tactical timers: append game clock line
    0x6C389D: '8A8680030000',         # DrawExtras: hover health-bar test (0.3.0 health bars)
    0x6AC45B: '389808020000',         # Radial indicators: selected-object HasRadialIndicator test (0.5.0 range circles)
}
b = match.Bin('game.exe')
ok = True
for site, hexbytes in SITES.items():
    actual = b.bytes(site, len(hexbytes) // 2).hex().upper()
    good = actual == hexbytes
    ok &= good
    print(f'{site:08X} bytes {"OK" if good else "MISMATCH " + actual}')
inner = {a: s for s, h in SITES.items() for a in range(s + 1, s + len(h) // 2)}
hits = []
for addr, size, mnem, op in b.md.disasm_lite(b.text, b.text_va):
    if (mnem.startswith('j') or mnem in ('call', 'loop')) and op.startswith('0x'):
        target = int(op, 16)
        if target in inner:
            hits.append((addr, mnem, target))
for h in hits:
    print(f'BRANCH INTO PATCH: {h[0]:08X} {h[1]} -> {h[2]:08X}')
ok &= not hits
print('RESULT', 'PASS' if ok else 'FAIL')
