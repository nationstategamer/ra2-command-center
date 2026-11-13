// Tests for native UI 0.2.0 features that can run without the game:
// idle-harvester selection order, game-clock seconds, and the keyboard command
// objects' vtable layout as the engine calls it (thiscall, by slot number).
#include "../src/NativeUI.h"
#include <stdio.h>
#include <string.h>
#include <wchar.h>

namespace NativeUI {
void* TestCommand(unsigned index);
bool InstallTooltips() { return true; }
bool InstallPlacement() { return true; }
bool InstallSessionSettings() { return true; }
}
using namespace NativeUI;
static int failures = 0;
static void Check(bool ok, const char* name) {
    printf("%s: %s\n", ok ? "PASS" : "FAIL", name);
    if (!ok) ++failures;
}

typedef const char* (__thiscall *NameFn)(void*);
typedef const wchar_t* (__thiscall *WideFn)(void*);
typedef bool (__thiscall *KeyFn)(void*, int);

static void* Slot(void* object, int index) { return (*reinterpret_cast<void***>(object))[index]; }

int main() {
    const DWORD units[] = { 0x100, 0x200, 0x300, 0x400 };
    const bool none[] = { false, false, false, false };
    const bool some[] = { false, true, false, true };
    const bool onlyThird[] = { false, false, true, false };
    Check(NextIdleIndex(units, none, 4, 0) == -1, "No idle harvester gives no selection");
    Check(NextIdleIndex(units, some, 4, 0) == 1, "Without a selection the first idle harvester is chosen");
    Check(NextIdleIndex(units, some, 4, 0x200) == 3, "The next idle harvester after the selected one is chosen");
    Check(NextIdleIndex(units, some, 4, 0x400) == 1, "Selection wraps around to the first idle harvester");
    Check(NextIdleIndex(units, some, 4, 0x300) == 3, "A selected working harvester moves to the following idle one");
    Check(NextIdleIndex(units, onlyThird, 4, 0x300) == 2, "A lone idle harvester that is selected stays selected");
    Check(NextIdleIndex(units, some, 4, 0x999) == 1, "A non-harvester selection starts from the first idle harvester");
    Check(NextIdleIndex(units, some, 0, 0) == -1 && NextIdleIndex(NULL, some, 4, 0) == -1, "Empty or missing lists are refused");

    Check(GameSeconds(0) == 0 && GameSeconds(14) == 0 && GameSeconds(15) == 1, "Clock: 15 frames make one game second");
    Check(GameSeconds(900) == 60 && GameSeconds(54000) == 3600, "Clock: 900 frames are one minute, 54000 one hour");
    Check(GameSeconds(-5) == 0, "Clock: negative frame counts show zero");

    Check(!UseHoverHealthBar(0, false, false, 50, 0.5), "Health bars mode 0 keeps the stock selected/hover-only rule");
    Check(UseHoverHealthBar(1, false, false, 50, 0.5) && !UseHoverHealthBar(1, false, false, 100, 1.0),
        "Health bars mode 1 shows damaged objects and hides undamaged ones");
    Check(UseHoverHealthBar(2, false, false, 100, 1.0), "Health bars mode 2 shows every object");
    Check(!UseHoverHealthBar(2, true, false, 50, 0.5) && !UseHoverHealthBar(2, false, true, 50, 0.5),
        "Hovered and selected objects stay on the stock path");
    Check(!UseHoverHealthBar(2, false, false, 0, 0.0) && !UseHoverHealthBar(1, false, false, -5, 0.0),
        "Objects without health are left to the stock code");
    {
        wchar_t ini[MAX_PATH];
        const DWORD n = GetModuleFileNameW(NULL, ini, MAX_PATH);
        wchar_t* slash = n ? wcsrchr(ini, L'\\') : NULL;
        bool ok = false;
        if (slash) {
            wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - ini), L"RA2NativeUI.ini");
            const bool existed = GetFileAttributesW(ini) != INVALID_FILE_ATTRIBUTES;
            if (!existed && WritePrivateProfileStringW(L"UI", L"HealthBars", L"2", ini)) {
                ok = Setting("HealthBars", 1) == 2 && Setting("MissingSwitch", 7) == 7;
                DeleteFileW(ini);
            }
        }
        Check(ok, "Integer settings are read from [UI] with defaults for missing keys");
    }

    {
        AutosaveClock clock = {};
        const DWORD minute = 60000;
        bool due = AdvanceAutosave(clock, 0, 1000, minute);
        int frame = 0; DWORD now = 1000;
        for (int i = 0; i < 59 && !due; ++i) { frame += 15; now += 1000; due = AdvanceAutosave(clock, frame, now, minute); }
        Check(!due, "Autosave: not due before the interval of play");
        frame += 15; now += 1000;
        Check(AdvanceAutosave(clock, frame, now, minute), "Autosave: due after one minute of steady play");
        Check(clock.activeMs == 0, "Autosave: the count restarts after a save");
        // Paused for ten minutes: frames do not advance, and the long gap is not counted.
        for (int i = 0; i < 600; ++i) { now += 1000; due = AdvanceAutosave(clock, frame, now, minute); if (due) break; }
        frame += 15; now += 1000;
        Check(!due && !AdvanceAutosave(clock, frame, now, minute) && clock.activeMs < 2000, "Autosave: pauses and menus do not count");
        // Several main-loop calls per frame still count the whole frame time.
        AutosaveClock fast = {};
        AdvanceAutosave(fast, 0, 0, minute);
        DWORD t = 0; int f = 0;
        for (int i = 0; i < 400; ++i) { t += 25; if (i % 4 == 3) ++f; AdvanceAutosave(fast, f, t, 0); }
        Check(fast.activeMs >= 9000 && fast.activeMs <= 10000, "Autosave: time between advancing frames is fully counted");
        AdvanceAutosave(fast, 2, t + 50, 0);
        Check(fast.activeMs == 0, "Autosave: a new game or loaded save restarts the count");
        bool noneExist[3] = { false, false, false }, allExist[3] = { true, true, true }, firstExists[3] = { true, false, false };
        FILETIME times[3] = { { 30, 0 }, { 10, 0 }, { 20, 0 } };
        Check(PickAutosaveSlot(noneExist, times, 3) == 0 && PickAutosaveSlot(firstExists, times, 3) == 1,
            "Autosave: empty slots are used first");
        Check(PickAutosaveSlot(allExist, times, 3) == 1, "Autosave: the oldest slot is overwritten");
    }

    {
        int units[20] = {}, buildings[20] = {};
        units[0] = 4; units[1] = 7; units[3] = 2; buildings[1] = 1; buildings[3] = 3;
        Check(SumKills(units, buildings, 20, 0) == 13, "Stats: kills add units and buildings of other houses, not your own");
        Check(SumKills(units, buildings, 20, -1) == 17, "Stats: with no known own house every house counts");
        units[5] = -9;
        Check(SumKills(units, buildings, 20, 0) == 13, "Stats: corrupt negative counters are ignored");
        wchar_t text[64];
        MatchStats stats = { 12, 3, 1234567 };
        Check(FormatMatchStats(text, 64, stats) && !wcscmp(text, L"Kills 12   Lost 3   Army $1,234,567"), "Stats: line format with thousands separators");
        MatchStats small = { 0, 0, 900 };
        Check(FormatMatchStats(text, 64, small) && !wcscmp(text, L"Kills 0   Lost 0   Army $900"), "Stats: small values have no separator");
        MatchStats bad = { -1, -2, -3 };
        Check(FormatMatchStats(text, 64, bad) && !wcscmp(text, L"Kills 0   Lost 0   Army $0"), "Stats: negative values show as zero");
        Check(!FormatMatchStats(text, 8, stats), "Stats: too-small buffers are refused");
    }
    Check(ShowSelectedRange(false, true, 9), "Range circles: a selected defense with a weapon shows its range");
    Check(!ShowSelectedRange(true, true, 9), "Range circles: stock-flagged buildings keep the stock path");
    Check(!ShowSelectedRange(false, true, 0) && !ShowSelectedRange(false, true, -1), "Range circles: buildings without a weapon show nothing");
    Check(!ShowSelectedRange(false, false, 9), "Range circles: units are left to the stock code");
    Check(!ShowSelectedRange(false, true, 500), "Range circles: implausible radii are ignored");

    const char* names[] = { "NextIdleHarvester", "QuickSave" };
    const wchar_t* uiNames[] = { L"Next Idle Harvester", L"Quick Save" };
    for (unsigned i = 0; i < 2; ++i) {
        void* command = TestCommand(i);
        char label[96];
        sprintf_s(label, "%s: engine slot 1 GetName", names[i]);
        Check(command && !strcmp(reinterpret_cast<NameFn>(Slot(command, 1))(command), names[i]), label);
        sprintf_s(label, "%s: engine slot 2 GetUIName", names[i]);
        Check(!wcscmp(reinterpret_cast<WideFn>(Slot(command, 2))(command), uiNames[i]), label);
        sprintf_s(label, "%s: engine slot 4 GetUIDescription", names[i]);
        const wchar_t* description = reinterpret_cast<WideFn>(Slot(command, 4))(command);
        Check(description && wcslen(description) > 20, label);
        sprintf_s(label, "%s: engine slots 5-7 match the stock key filters", names[i]);
        Check(!reinterpret_cast<KeyFn>(Slot(command, 5))(command, 'I') &&
              reinterpret_cast<KeyFn>(Slot(command, 6))(command, 'I') &&
              reinterpret_cast<KeyFn>(Slot(command, 6))(command, 0x200 | 'S') &&
              !reinterpret_cast<KeyFn>(Slot(command, 6))(command, 0x800 | 'I') &&
              !reinterpret_cast<KeyFn>(Slot(command, 7))(command, 'I'), label);
    }
    printf("RESULT %d failed\n", failures);
    return failures ? 1 : 0;
}
