# Native UI 0.2.0: address evidence

These are static-analysis results only. Nothing was executed in the game to derive them.

| Item | Value |
| --- | --- |
| Target `game.exe` SHA-256 | `73288c03b58d370be268ca6d156b4e33bfdb2066dc980359467d8852ff3b00df` |
| Reference `gamemd.exe` SHA-256 | `3e81a61775d2745d1dabe397325ef663cd994ffc194da4e998e3bf5d2d308600` |
| Reference sources | `../refs` (YRpp addresses, Phobos v0.4.0.2 command implementations) |
| Tools | `match.py` (instruction-shape matcher), `mixread.py` (dependency-free MIX reader with a Blowfish self-test), `hooksites.py` (hook-site byte and branch-target check) |

## Method

1. Take a Yuri's Revenge function address from YRpp or Phobos.
2. Normalise each instruction: keep registers and small offsets, and replace large constants and branch targets with wildcards.
3. Find the best-aligned window in RA2's `.text` section.
4. Confirm every match by reading the RA2 disassembly. Where possible, also confirm it against a stock RA2 routine that uses the same calls.

The matcher was first checked against a pair proven in earlier work: YR `0x4FCE30` and RA2 `0x4E8330` (house power percentage). It gave similarity 1.00.

## Keyboard commands

| Purpose | YR / Phobos | RA2 | Evidence |
| --- | --- | --- | --- |
| Command registration ("Init Commands") | `0x532150` | `0x5173C0` | similarity 1.00; called at `0x51277B`, right after the "Init Commands" log string |
| `CommandClass::Array` | `0x87F658` | `0x832358` | used by every add in `0x5173C0` |
| `DynamicVectorClass::AddItem` | — | `0x51E360` | `push &item; mov ecx, 0x832358; call` (ret 4) |
| Hotkey index | `0x87F680` | `0x832380` | rebuilt by the KEYBOARD.INI loader `0x519240`: items, count `+4`, capacity `+8`, sorted flag `+0xC` |
| `IndexClass::AddIndex(&key, &value)` | — | `0x51E3B0` | copies both values as dwords, clears the sorted flag, returns 1 |
| KEYBOARD.INI load | — | `0x51915F` → `0x519240` | inside the registration function: the commands must exist before this point |
| Command vtable layout | 9 slots | 9 slots | stock RA2 vtables `0x7A43C4` and `0x7A3DFC`, compared with YR `0x7EBE14`; default key filters `0x51AE70` (false), `0x51AE80` (true unless the release bit `0x800` is set), `0x51AE90` (false) |
| Stock categories | — | `TXT_SELECTION`, `TXT_INTERFACE` | the stock NextObject command returns `TXT_SELECTION` |

The stock default bindings come from `ra2.mix/local.mix/keyboard.ini`, extracted with `mixread.py`. Plain keys I, J, M, O, V and Y are unbound, and so are all Ctrl+letter keys except Ctrl+C. The defaults chosen are **I** for Next Idle Harvester and **Ctrl+S** for Quick Save. A default is applied only when the player has not bound the command and the key is still free.

## Next Idle Harvester

Every call is copied from the stock NextObject command's Execute (`0x51B8B0`):

- the map/display object at `0x8324E0`;
- the cursor-mode resets `0x49C980(0)`, `0x49C860(0, 0)`, `0x49CA20(0)` and `0x49C7C0(0)`;
- `UnselectAll` at `0x482BC0` (YR `0x48DC90`, similarity 1.00);
- `Select` at vtable `+0x140`;
- `CenterMap` at `0x49E3B0` (YR `0x4AE290`, similarity 1.00);
- the redraw `0x4E09A0(1)`;
- the current selection, a vector at `0xA40C60` (items `+4`, count `+0x10`).

Whether a harvester is idle uses exactly the H: counter's rules, documented in `../economy-evidence.txt`.

## Quick Save

| Purpose | YR / Phobos | RA2 | Evidence |
| --- | --- | --- | --- |
| Safe save point in the main loop | Phobos hook `0x55DBCD` | `0x540433` | same `cmp [GameMode], edi` / WasGameSaved block; the stock campaign autosave calls SaveGame from this block (`0x5404F8`) |
| `ScenarioClass::SaveGame(file, description, bargraph)` | `0x67CEF0` | `0x6554A0` | similarity 1.00; fastcall, third argument on the stack, as in the stock callers |
| Scenario / session mode | `0xA8B230` / `0xA8B238` | `0xA3D290` / `0xA3D298` | 8 bytes apart in both engines; Campaign = 0, Skirmish = 5 |
| Scenario wide name | — | `+0x940` | used by the stock autosave description `L"[%s]"` |
| Save file name | — | `SAVE%04lX.%3s` | the stock save dialog format; the quick-save slot is `SAVEFFFF.SAV` |

## Messages and text

| Purpose | YR | RA2 | Evidence |
| --- | --- | --- | --- |
| `MessageListClass::AddMessage` | `0x5D3BA0` | `0x5B4DA0` | similarity 1.00; stock call at `0x51D820`: list `0xA3DC70`, style `0x4046`, timeout `0x96` |
| String table, label only | — | `0x6FA180` | stock CSF labels used: `TXT_SAVING_GAME`, `TXT_GAME_WAS_SAVED`, `TXT_ERROR_SAVING_GAME`, `GUI:Time` |

## Game clock

The renderer is RA2 `0x6A5280`, the counterpart of YR `0x6D4B50` (`PrintTimerOnTactical`). It is a fastcall taking `(line, ColorScheme*, seconds, label, 0, 0)` and returns with `ret 0x10`. Its three stock callers draw the mission, superweapon and power-blackout timers. At `0x6A5252` the stock block has finished and `[esp+0x18]` holds the number of timer lines used, so the clock is drawn as the next line.

Other values used:

- colour schemes: vector at `0xAB6EF8` (items at `0xAB6EFC`);
- the player's colour scheme index: `HouseClass +0x15F40`;
- the frame counter at `0xA40D2C`, divided by 15 to give seconds, which is the engine's own rule for timers.

## Health bars (0.3.0)

I found RA2's `TechnoClass::DrawHealthBar` (`0x6C3F00`, `ret 0xC`) through the pip sprites rather than by instruction matching, because YR rewrote this routine. The pip-sprite globals are `PIPS.SHP` at `0xA72FE4` and `PIPS2.SHP` at `0xA72FE8` in RA2, and `0xAC147C` / `0xAC1480` in YR. The YR reads of `0xAC147C` fall inside the known `DrawHealthBar` (`0x6F64A0`), and the RA2 reads at `0x6C4109`, `0x6C41C7` and `0x6C43FA` fall inside `0x6C3F00`. Both have the same building/unit split.

The vtable slot is RA2 `+0x3E0`; YR's is `+0x44C`. The YRpp order is `vt_entry_448`, `DrawHealthBar`, `DrawPipScalePips`, and YR's building branch calls `+0x450` (pips) where RA2's calls `+0x3E4`. A first attempt that put the slot one entry later actually found `DrawVeterancyPips`, which `DrawExtras` calls for every object.

RA2 calls the slot only from `TechnoClass::DrawExtras` (`0x6C2C40`), at three places, mirroring YR `0x6F5E27`, `0x6F5E7B` and `0x6F5E87`:

- **`0x6C388D`**: the object is selected.
- **`0x6C38DA` / `0x6C38E6`**: the hover path. If `IsMouseHovering` (`+0x380`) is set and `IsSelected` (`+0x77`) is not, the bar is drawn with flag 1 and no selection bracket, still subject to the stock disguise check (vslots `0xC4` / `0xCC`).

The hook is at the hover test `0x6C389D` (`8A 86 80 03 00 00`). For an object that qualifies, it continues at `0x6C38A7`, the stock `IsSelected` check that follows the hover test. The object therefore takes the unchanged stock hover path. Objects that are hovered, selected or not qualifying replay the original instruction.

The "damaged" test uses `0x5D4540`, which computes `Health(+0x6C) / Type(vslot 0x84)->Strength(+0xA0)` and matches YRpp's `GetHealthPercentage`.

## Range circles (0.5.0)

- **Rules flags.** `HasRadialIndicator` is parsed at `0x5D7A5D` into ObjectType `+0x208`, and `RadialColor` into `+0x98`. The only reader of `+0x208` in all of `.text` is `TacticalClass::DrawRadialIndicators` (`0x6AC420`, called at `0x6A4F0D`). In stock rules only `GAGAP`, `NAPSIS` and `NAPSYB` set the flag.
- **What `0x6AC420` does.**
  - For each selected object (`CurrentObjects` at `0xA40C60`), if its type has the flag, it calls the object's `DrawRadialIndicator(1)` (vtable `+0x128`).
  - While a building is being placed (`0x833680`, the same address the placement preview uses), it draws a circle at the cursor whenever `0x451930(building) > 0`. That part is gated on `0xA40B32`, which is **UnitActionLines**: the `[Options]` loader `0x5D8CAB` stores that key at `0xA40B18 + 0x1A`, beside ShowHidden `+0x1B` and ToolTips `+0x1C`.
- **The radius function.** `0x451930` (`BuildingClass` radial radius, in cells) returns a gap or sensor radius where the type defines one, otherwise the primary weapon's `Range / 256`.
- **The drawing routine.** `BuildingClass::DrawRadialIndicator` is `0x4519D0`, at vtable slot `+0x128` (vtable `0x79CBBC`, with RTTI immediately before it). It draws only when the radius is above 0, the building's `+0x530` flag is set, and the owner passes the stock player check at `0x4F52B0`.
- **The hook.** It sits at `0x6AC45B` (`38 98 08 02 00 00`, the selected-object flag test, with EAX = type and ESI = object). For a building (vtable `+0x2C` WhatAmI returns 6, as at `0x453C40`) with radius between 1 and 63 cells, it continues at `0x6AC463`, the stock `DrawRadialIndicator(1)` call. Everything else replays the stock test.

## Match stats (0.6.0)

**Score counters.** YR `TechnoClass` death handling (`0x7031A3`, `0x7031D5`, `0x70305C`) shows what the counters mean:

- The **victim's** owner increments `TotalKilledUnits` (`+0x5434`) or `TotalKilledBuildings` (`+0x5488`). These are that house's **losses**.
- The **killer's** owner increments `KilledUnitsOfHouses[victim house]` (`+0x53E4`) or `KilledBuildingsOfHouses` (`+0x5438`). These are its **kills**, per victim house.

The RA2 equivalents sit `0xD4` lower, the same shift as the power fields:

- `TotalKilledUnits` at `+0x5360`: stored at `0x6CF0C4`, and read in the YR pattern at `0x465E00`.
- `TotalKilledBuildings` at `+0x53B4`: incremented at `0x6CEFC8`.
- The per-house arrays at `+0x5310` (`0x6CF0D9`) and `+0x5364` (`0x6CEFFD`).

**Units for army value.** RA2's `TechnoClass::Array` has its items at `0xA40C24` and count at `0xA40C30`. This was found by matching YR `0x4ADEE0` to RA2 `0x49E000` (similarity 0.97). Each object's value comes from the sidebar's own `TechnoType::GetActualCost(house)` (type vtable `+0x84`, as in the tooltips). Buildings (WhatAmI 6) and harvesters (UnitType `+0xBA6`) are left out.

**Text line.** The line is drawn with the engine timer renderer's own printer:

- `0x497B40(&size, text, surface [0x839964], surfaceRect (vtable +0x78), &at, ColorScheme*, 0, 0x4618)`
- `at` is `([0x839638] - 3, [0x83963C] - (fontHeight + 2) * (line + 1))`.
- The font comes from `0x497A50(0x4618)`, with its height at `+0x1C`. All of this is taken from `0x6A5378`–`0x6A53D1`.
- It uses the existing timer hook at `0x6A5252` and is placed one line above the game clock.

## Hook sites

`hooksites.py` checked each site. It confirmed the exact bytes and that no branch anywhere in `.text` lands inside a patched range:

| Site | Bytes | Purpose |
| --- | --- | --- |
| `0x519150` | `8D442408 B958238300` | add the commands before KEYBOARD.INI loads |
| `0x519164` | `8D4C2414 BE2E000000` | apply default keys after KEYBOARD.INI loads |
| `0x540433` | `393D98D2A300` | quick-save point |
| `0x6A5252` | `8D4D24 51 8BCD` | game clock |
| `0x6C389D` | `8A8680030000` | health bars: hover test; qualifying objects continue at `0x6C38A7` |
| `0x6AC45B` | `389808020000` | range circles: selected-object `HasRadialIndicator` test; qualifying defenses continue at `0x6AC463` |

None of these ranges contains a relative branch, so the hook framework's replay of the original bytes is exact.
