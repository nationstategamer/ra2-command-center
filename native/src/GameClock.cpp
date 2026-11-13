#include "NativeUI.h"
#include <stdio.h>
#include <limits.h>

// Elapsed game time and match statistics, drawn on the battlefield in the style of the
// engine's own timers (same printer, font, right alignment and player color), stacked
// above any mission or superweapon timers.
// game.exe SHA256 73288C03...FF3B00DF only; see NativeRA2/analysis/features/evidence.md.
namespace NativeUI {
namespace {
// End of the stock timer block: every mission/superweapon/blackout timer has been
// drawn and [esp+0x18] holds the number of timer lines used.
const DWORD kTimerSite = 0x6A5252;
const BYTE  kTimerBytes[] = { 0x8D,0x4D,0x24, 0x51, 0x8B,0xCD };
const DWORD kPrintTimer = 0x6A5280;     // __fastcall(line, ColorScheme*, seconds, label, 0, 0), ret 0x10
const DWORD kLoadString = 0x6FA180;
const DWORD kCurrentFrame = 0xA40D2C;
const DWORD kCurrentPlayer = 0xA35DB4, kHouseColorScheme = 0x15F40;
const DWORD kColorSchemes = 0xAB6EF8;   // DynamicVectorClass<ColorScheme*>: items +4, count +0x10
const int kFramesPerSecond = 15;        // the engine's own frames-to-seconds rule for timers
// The timer renderer's own text drawing (0x6A5378..0x6A53D1).
const DWORD kFontForStyle = 0x497A50;   // __fastcall(style) -> font; height at +0x1C
const DWORD kPrintText = 0x497B40;      // cdecl(Point* size, text, surface, Rect* bounds, Point* at, ColorScheme*, 0, style)
const DWORD kTimerSurface = 0x839964, kTacticalRight = 0x839638, kTacticalBottom = 0x83963C;
const int kTimerStyle = 0x4618;
// Score counters (HouseClass; RA2 = YR - 0xD4): see evidence.md, "Match stats".
const DWORD kHouses = 0xA32298;         // DynamicVectorClass<HouseClass*>: items +4, count +0x10
const DWORD kKilledUnitsOf = 0x5310, kLostUnits = 0x5360, kKilledBuildingsOf = 0x5364, kLostBuildings = 0x53B4;
const DWORD kTechnos = 0xA40C20;        // DynamicVectorClass<TechnoClass*>: items +4, count +0x10
const DWORD kOwner = 0x1B4, kHealth = 0x6C, kAlive = 0x7D;
const int kKindUnit = 1, kKindBuilding = 6;

bool gClock = true, gStats = true;
int gStatsFrame = -1;
MatchStats gStatsValue = {};

template<class T> T Read(DWORD address) { return *reinterpret_cast<const T*>(address); }
bool Memory(DWORD address, size_t length) { return address && Readable(reinterpret_cast<const void*>(address), length); }

bool Vector(DWORD vector, DWORD& items, int& count) {
    if (!Memory(vector, 0x14)) return false;
    items = Read<DWORD>(vector + 4);
    count = Read<int>(vector + 0x10);
    return count >= 0 && count < 65536 && (!count || Memory(items, static_cast<size_t>(count) * 4));
}

DWORD Virtual(DWORD object, DWORD slot) {
    if (!Memory(object, 4)) return 0;
    const DWORD vtable = Read<DWORD>(object);
    if (!Memory(vtable + slot, 4)) return 0;
    const DWORD fn = Read<DWORD>(vtable + slot);
    return fn >= 0x401000 && fn < 0x780000 ? fn : 0;
}

bool Sample(DWORD house, MatchStats& stats) {
    stats = MatchStats();
    if (!Memory(house + kKilledUnitsOf, kLostBuildings + 4 - kKilledUnitsOf)) return false;
    DWORD items; int count;
    if (!Vector(kHouses, items, count)) return false;
    int self = -1;
    for (int i = 0; i < count; ++i) if (Read<DWORD>(items + i * 4) == house) { self = i; break; }
    int units[20], buildings[20];
    for (int i = 0; i < 20; ++i) {
        units[i] = Read<int>(house + kKilledUnitsOf + i * 4);
        buildings[i] = Read<int>(house + kKilledBuildingsOf + i * 4);
    }
    stats.kills = SumKills(units, buildings, 20, self);
    stats.losses = Read<int>(house + kLostUnits) + Read<int>(house + kLostBuildings);
    if (!Vector(kTechnos, items, count)) return true;
    for (int i = 0; i < count; ++i) {
        const DWORD object = Read<DWORD>(items + i * 4);
        if (!Memory(object, kOwner + 4) || Read<DWORD>(object + kOwner) != house ||
            !Read<BYTE>(object + kAlive) || Read<int>(object + kHealth) <= 0) continue;
        const DWORD whatAmI = Virtual(object, 0x2C), getType = Virtual(object, 0x84);
        if (!whatAmI || !getType) continue;
        const int kind = reinterpret_cast<int (__thiscall *)(void*)>(whatAmI)(reinterpret_cast<void*>(object));
        if (kind == kKindBuilding) continue;
        const DWORD type = reinterpret_cast<DWORD (__thiscall *)(void*)>(getType)(reinterpret_cast<void*>(object));
        if (kind == kKindUnit && Memory(type + 0xBA6, 1) && Read<BYTE>(type + 0xBA6)) continue;   // harvesters are economy
        const DWORD cost = Virtual(type, 0x84);                   // TechnoType::GetActualCost(house), as the sidebar
        if (!cost) continue;
        const int value = reinterpret_cast<int (__thiscall *)(void*, void*)>(cost)(reinterpret_cast<void*>(type), reinterpret_cast<void*>(house));
        if (value > 0 && value < 100000 && stats.army < INT_MAX - value) stats.army += value;
    }
    return true;
}

void PrintLine(int line, const wchar_t* text, DWORD color) {
    const DWORD surface = Memory(kTimerSurface, 4) ? Read<DWORD>(kTimerSurface) : 0;
    const DWORD getRect = Virtual(surface, 0x78);
    if (!getRect || !Memory(kTacticalRight, 8)) return;
    const DWORD font = reinterpret_cast<DWORD (__fastcall *)(int)>(kFontForStyle)(kTimerStyle);
    if (!Memory(font + 0x1C, 4)) return;
    const int height = Read<int>(font + 0x1C);
    if (height <= 0 || height > 64) return;
    struct Point { int x, y; } size = {}, at = { Read<int>(kTacticalRight) - 3, Read<int>(kTacticalBottom) - (height + 2) * (line + 1) };
    struct Rect { int x, y, w, h; } bounds = {};
    reinterpret_cast<Rect* (__thiscall *)(void*, Rect*)>(getRect)(reinterpret_cast<void*>(surface), &bounds);
    typedef void (__cdecl *PrintFn)(Point*, const wchar_t*, void*, Rect*, Point*, void*, int, int);
    reinterpret_cast<PrintFn>(kPrintText)(&size, text, reinterpret_cast<void*>(surface), &bounds, &at, reinterpret_cast<void*>(color), 0, kTimerStyle);
}

DWORD __cdecl DrawTimerLines(Registers* regs) {
    int line = regs->Stack<int>(0x18);
    if (line < 0 || line > 32 || !Memory(kCurrentFrame, 4) || !Memory(kCurrentPlayer, 4)) return 0;
    const int frame = Read<int>(kCurrentFrame);
    const DWORD house = Read<DWORD>(kCurrentPlayer);
    if (frame < 0 || !Memory(house + kHouseColorScheme, 4) || !Memory(kColorSchemes, 0x14)) return 0;
    const int scheme = Read<int>(house + kHouseColorScheme);
    const int schemes = Read<int>(kColorSchemes + 0x10);
    const DWORD items = Read<DWORD>(kColorSchemes + 4);
    if (scheme < 0 || scheme >= schemes || !Memory(items + scheme * 4, 4)) return 0;
    const DWORD color = Read<DWORD>(items + scheme * 4);
    if (!Memory(color, 4)) return 0;
    if (gClock) {
        typedef const wchar_t* (__fastcall *LoadFn)(const char*, void*);
        const wchar_t* label = reinterpret_cast<LoadFn>(kLoadString)("GUI:Time", NULL);
        if (!label || !Memory(reinterpret_cast<DWORD>(label), 2)) label = L"Time";
        typedef void (__fastcall *PrintFn)(int, void*, int, const wchar_t*, int, int);
        reinterpret_cast<PrintFn>(kPrintTimer)(line++, reinterpret_cast<void*>(color), GameSeconds(frame), label, 0, 0);
    }
    if (gStats) {
        // Recount once per game second; the renderer may run several times per frame.
        if (gStatsFrame < 0 || frame < gStatsFrame || frame - gStatsFrame >= kFramesPerSecond) {
            MatchStats next;
            if (Sample(house, next)) gStatsValue = next;
            gStatsFrame = frame;
        }
        wchar_t text[64];
        if (FormatMatchStats(text, 64, gStatsValue)) PrintLine(line, text, color);
    }
    return 0;
}
} // namespace

int GameSeconds(int frame) { return frame <= 0 ? 0 : frame / kFramesPerSecond; }

int SumKills(const int* units, const int* buildings, int houses, int self) {
    long long total = 0;
    for (int i = 0; i < houses; ++i) {
        if (i == self) continue;   // own units destroyed by own fire are not kills
        if (units[i] > 0) total += units[i];
        if (buildings[i] > 0) total += buildings[i];
    }
    return total > INT_MAX ? INT_MAX : static_cast<int>(total);
}

bool FormatMatchStats(wchar_t* text, size_t capacity, const MatchStats& stats) {
    if (!text || capacity < 48) return false;
    // Thousands separators by hand: the game's printer is not locale-aware.
    wchar_t army[24];
    int value = stats.army < 0 ? 0 : stats.army;
    wchar_t digits[16];
    swprintf_s(digits, L"%d", value);
    const int length = static_cast<int>(wcslen(digits));
    int at = 0;
    for (int i = 0; i < length; ++i) {
        if (i && (length - i) % 3 == 0) army[at++] = L',';
        army[at++] = digits[i];
    }
    army[at] = 0;
    return swprintf_s(text, capacity, L"Kills %d   Lost %d   Army $%ls",
        stats.kills < 0 ? 0 : stats.kills, stats.losses < 0 ? 0 : stats.losses, army) > 0;
}

bool InstallGameClock() {
    gClock = Enabled("GameClock");
    gStats = Enabled("MatchStats");
    if (!gClock && !gStats) return true;
    return AddHook(kTimerSite, kTimerBytes, sizeof kTimerBytes, DrawTimerLines, "Tactical: game clock and match stats");
}
} // namespace NativeUI
