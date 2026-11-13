#include "NativeUI.h"
#include <wchar.h>
#include <string.h>

namespace NativeUI {
namespace {
// These addresses/offsets are validated against the original Steam game.exe,
// SHA256 73288C03B58D370BE268CA6D156B4E33BFDB2066DC980359467D8852FF3B00DF.
// See analysis/economy-evidence.txt. No simulation or save-game fields are added.
const DWORD CurrentPlayer = 0xA35DB4;
const DWORD UnitArray = 0x866198;
const DWORD SidebarSurface = 0x839948;
const DWORD CurrentFrame = 0xA40D2C;
struct Point { int x, y; };
struct Rect { int x, y, width, height; };
struct VectorView { DWORD items; int count; };
struct Snapshot {
    DWORD house;
    int active, total, output, drain;
    bool valid, hasBuildings, blackout;
};
bool showHarvesters = false, showPower = false;
Snapshot current = {}, drawn = {};
int sampledFrame = -1;

// All pointer-chasing happens on the game's drawing thread. Bounds and mapped
// pages are checked before reading the native arrays.
template<class T> T Read(DWORD base, size_t offset = 0) {
    return *reinterpret_cast<const T*>(base + offset);
}
bool Memory(DWORD address, size_t length) {
    return address && Readable(reinterpret_cast<const void*>(address), length);
}
bool Vector(DWORD address, VectorView& result, int limit = 32768, bool dynamic = true) {
    result = {};
    if (!Memory(address, dynamic ? 0x18 : 0x10)) return false;
    const int count = Read<int>(address, dynamic ? 0x10 : 0x8);
    const int capacity = Read<int>(address, 0x8);
    const DWORD items = Read<DWORD>(address, 4);
    if (count < 0 || count > limit || capacity < count || capacity > 1048576) return false;
    if (count && !Memory(items, static_cast<size_t>(count) * sizeof(DWORD))) return false;
    result = { items, count };
    return true;
}
bool Living(DWORD unit) {
    return Memory(unit, 0x80) && Read<BYTE>(unit, 0x7D) && Read<int>(unit, 0x6C) > 0;
}
bool IsDockType(DWORD buildingType, const VectorView& docks) {
    for (int i = 0; i < docks.count; ++i)
        if (Read<DWORD>(docks.items, i * 4) == buildingType) return true;
    return false;
}
bool HasOwnedDock(const VectorView& buildings, const VectorView& docks) {
    for (int i = 0; i < buildings.count; ++i) {
        DWORD building = Read<DWORD>(buildings.items, i * 4);
        if (!Memory(building, 0x41C) || !Living(building) || Read<BYTE>(building, 0x76)) continue;
        if (IsDockType(Read<DWORD>(building, 0x418), docks)) return true;
    }
    return false;
}
bool LinkIsBuilding(DWORD link, const VectorView& buildings) {
    for (int i = 0; i < buildings.count; ++i)
        if (Read<DWORD>(buildings.items, i * 4) == link) return true;
    return false;
}
bool IsWorking(DWORD unit, DWORD type, const VectorView& buildings) {
    // Native RA2 has no YR slave miner or powered robot tank. The checks below
    // cover ore/chrono miners, including docking, idle ore fields and CLEG freeze.
    if (Read<BYTE>(unit, 0x76) || Read<BYTE>(unit, 0x204) || Read<DWORD>(unit, 0x20C)) return false;
    VectorView docks;
    if (!Vector(type + 0x3AC, docks, 1024) || !HasOwnedDock(buildings, docks)) return false;
    VectorView links;
    if (!Vector(unit + 0xC8, links, 1024, false)) return false;
    DWORD firstLink = links.count ? Read<DWORD>(links.items) : 0;
    bool anyLink = false, dockLink = false;
    for (int i = 0; i < links.count; ++i) {
        DWORD link = Read<DWORD>(links.items, i * 4);
        if (!link) continue;
        anyLink = true;
        if (LinkIsBuilding(link, buildings) && Memory(link, 0x41C)
            && IsDockType(Read<DWORD>(link, 0x418), docks)) dockLink = true;
    }
    switch (Read<int>(unit, 0x98)) {
    case 10: // Harvest: factory radio links are not mining activity.
        return (!anyLink || dockLink) && !Read<BYTE>(unit, 0x328);
    case 16: // Unload
        return true;
    case 7: // Enter: a linked unit means transport boarding, not unloading.
        return !anyLink || !firstLink || LinkIsBuilding(firstLink, buildings);
    case 5: { // Guard: full miner moving while waiting to unload.
        if (!Read<DWORD>(unit, 0x1B0) || Read<int>(type, 0x6FC) <= 0) return false;
        DWORD loco = Read<DWORD>(unit, 0x564);
        if (!Memory(loco, 4)) return false;
        DWORD vtable = Read<DWORD>(loco);
        if (!Memory(vtable, 0x14)) return false;
        DWORD movingAddress = Read<DWORD>(vtable, 0x10);
        if (movingAddress < 0x401000 || movingAddress >= 0x780000 || !Memory(movingAddress, 1)) return false;
        typedef double (__thiscall *StorageFn)(void*);
        typedef bool (__stdcall *MovingFn)(void*);
        const double stored = reinterpret_cast<StorageFn>(0x705030)(reinterpret_cast<void*>(unit));
        return stored > 0.0 && reinterpret_cast<MovingFn>(movingAddress)(reinterpret_cast<void*>(loco));
    }
    default:
        return false;
    }
}
Snapshot Sample() {
    Snapshot s = {};
    DWORD house = Read<DWORD>(CurrentPlayer);
    if (!Memory(house, 0x52D8) || Read<BYTE>(house, 0x13D)) return s;
    s.house = house;
    VectorView buildings;
    if (!Vector(house + 0x68, buildings)) return s;
    s.hasBuildings = buildings.count > 0;
    s.output = Read<int>(house, 0x52D0);
    s.drain = Read<int>(house, 0x52D4);
    int start = Read<int>(house, 0x1EC);
    int duration = Read<int>(house, 0x1F4);
    int frame = Read<int>(CurrentFrame);
    s.blackout = start != -1 && duration > 0 && static_cast<__int64>(frame) - start < duration;
    if (showHarvesters) {
        VectorView units;
        if (!Vector(UnitArray, units)) return s;
        for (int i = 0; i < units.count; ++i) {
            DWORD unit = Read<DWORD>(units.items, i * 4);
            if (!Memory(unit, 0x5B0) || Read<DWORD>(unit, 0x1B4) != house || !Living(unit)) continue;
            DWORD type = Read<DWORD>(unit, 0x5AC);
            if (!Memory(type, 0xBA8) || !Read<BYTE>(type, 0xBA6)) continue;
            // Count registered living units even inside transports or during a
            // chrono transit. No extension state has to be serialized in saves.
            if (!Read<BYTE>(unit, 0x324)) continue;
            ++s.total;
            if (IsWorking(unit, type, buildings)) ++s.active;
        }
    }
    s.valid = true;
    return s;
}
bool Equal(const Snapshot& a, const Snapshot& b) {
    return a.house == b.house && a.valid == b.valid && a.hasBuildings == b.hasBuildings
        && a.blackout == b.blackout && a.active == b.active && a.total == b.total
        && a.output == b.output && a.drain == b.drain;
}
DWORD __cdecl PrepareCounters(Registers* regs) {
    const int frame = Read<int>(CurrentFrame);
    const DWORD house = Read<DWORD>(CurrentPlayer);
    // Counter data changes at most once per simulation frame. The engine can
    // invoke its renderer several times for a paused frame or an exposed window.
    if (frame != sampledFrame || house != current.house) {
        current = Sample();
        sampledFrame = frame;
    }
    if (!Equal(current, drawn) && Memory(regs->ecx, 0x10))
        *reinterpret_cast<BYTE*>(regs->ecx + 8) = 1; // CreditsClass redraw flag only.
    return 0;
}
int Pixel(int red, int green, int blue) {
    // Exact RGB shift globals used by the native credits drawing code.
    int rl = Read<int>(0x852FB8), rr = Read<int>(0x852FBC);
    int bl = Read<int>(0x852FC0), br = Read<int>(0x852FC4);
    int gl = Read<int>(0x852FC8), gr = Read<int>(0x852FCC);
    if (rl < 0 || rl > 24 || rr < 0 || rr > 8 || bl < 0 || bl > 24
        || br < 0 || br > 8 || gl < 0 || gl > 24 || gr < 0 || gr > 8) return 0xFFFF;
    return (red >> rr << rl) | (green >> gr << gl) | (blue >> br << bl);
}
void Print(DWORD surface, const Rect& bounds, int x, const wchar_t* text, int color, int flags) {
    Point result = { 0, 0 }, location = { x, 2 };
    typedef Point* (__cdecl *TextFn)(Point*, const wchar_t*, void*, const Rect*, const Point*, int, int, int, ...);
    reinterpret_cast<TextFn>(0x497A60)(&result, text, reinterpret_cast<void*>(surface), &bounds, &location, color, 0, flags);
}
DWORD __cdecl DrawCounters(Registers*) {
    drawn = current;
    if (!current.valid) return 0;
    DWORD surface = Read<DWORD>(SidebarSurface);
    if (!Memory(surface, 4)) return 0;
    DWORD vtable = Read<DWORD>(surface);
    if (!Memory(vtable, 0x80)) return 0;
    DWORD getRect = Read<DWORD>(vtable, 0x78);
    if (getRect < 0x401000 || getRect >= 0x780000 || !Memory(getRect, 1)) return 0;
    typedef Rect* (__thiscall *RectFn)(void*, Rect*);
    Rect bounds = {};
    reinterpret_cast<RectFn>(getRect)(reinterpret_cast<void*>(surface), &bounds);
    if (bounds.width < 140 || bounds.width > 4096 || bounds.height < 16 || bounds.height > 8192) return 0;
    wchar_t text[32];
    if (showHarvesters) {
        swprintf_s(text, L"H:%d/%d", current.active, current.total);
        int color = current.active == current.total ? Pixel(Read<BYTE>(0xAC1028), Read<BYTE>(0xAC1029), Read<BYTE>(0xAC102A))
            : current.active * 2 >= current.total ? Pixel(255, 255, 0) : Pixel(255, 0, 0);
        Print(surface, bounds, bounds.width / 2 + 50, text, color, 0x4108);
    }
    if (showPower && current.hasBuildings) {
        int color;
        if (current.blackout) {
            wcscpy_s(text, L"P:X");
            color = Pixel(128, 128, 128);
        } else {
            const __int64 delta = static_cast<__int64>(current.output) - current.drain;
            swprintf_s(text, L"P:%+lld", delta);
            color = current.drain > current.output ? Pixel(255, 0, 0)
                : static_cast<__int64>(current.drain) * 4 >= static_cast<__int64>(current.output) * 3
                    ? Pixel(255, 255, 0) : Pixel(0, 255, 0);
        }
        Print(surface, bounds, bounds.width / 2 - 70, text, color, 0x4008);
    }
    return 0;
}
}

// The current player's selectable harvesters in UnitArray order, classified with
// the same working/idle rules as the H: counter. Units in limbo (inside a
// transport) or being chrono-warped out cannot be selected and are omitted.
int CollectHarvesters(DWORD* units, bool* idle, int capacity) {
    if (!units || !idle || capacity <= 0 || !Memory(CurrentPlayer, 4)) return 0;
    const DWORD house = Read<DWORD>(CurrentPlayer);
    if (!Memory(house, 0x52D8) || Read<BYTE>(house, 0x13D)) return 0;
    VectorView buildings, all;
    if (!Vector(house + 0x68, buildings) || !Vector(UnitArray, all)) return 0;
    int found = 0;
    for (int i = 0; i < all.count && found < capacity; ++i) {
        const DWORD unit = Read<DWORD>(all.items, i * 4);
        if (!Memory(unit, 0x5B0) || Read<DWORD>(unit, 0x1B4) != house || !Living(unit)) continue;
        const DWORD type = Read<DWORD>(unit, 0x5AC);
        if (!Memory(type, 0xBA8) || !Read<BYTE>(type, 0xBA6) || !Read<BYTE>(unit, 0x324)) continue;
        if (Read<BYTE>(unit, 0x76) || Read<BYTE>(unit, 0x204)) continue;
        units[found] = unit;
        idle[found] = !IsWorking(unit, type, buildings);
        ++found;
    }
    return found;
}

int NextIdleIndex(const DWORD* units, const bool* idle, int count, DWORD selected) {
    if (!units || !idle || count <= 0) return -1;
    int start = -1;
    for (int i = 0; i < count; ++i)
        if (units[i] == selected) { start = i; break; }
    // Cycle forward from the current selection; the current unit is offered last.
    for (int step = 1; step <= count; ++step) {
        const int index = (start + step) % count;
        if (idle[index]) return index;
    }
    return -1;
}

bool InstallEconomy() {
    showHarvesters = Enabled("HarvesterCounter");
    showPower = Enabled("PowerCounter");
    if (!showHarvesters && !showPower) return true;
    const BYTE prepare[] = { 0x8A,0x44,0x24,0x04,0x83,0xEC,0x20 };
    const BYTE draw[] = { 0x83,0xC4,0x24,0xC6,0x46,0x08,0x00 };
    return AddHook(0x494530, prepare, sizeof prepare, PrepareCounters, "Credits: refresh economy counters")
        && AddHook(0x494638, draw, sizeof draw, DrawCounters, "Credits: draw economy counters");
}
}

