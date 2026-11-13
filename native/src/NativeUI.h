#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>

namespace NativeUI {
// pushfd/pushad context followed by the stub's callback and continuation.
struct Registers {
    DWORD edi, esi, ebp, esp, ebx, edx, ecx, eax;
    DWORD flags, callback, next;
    DWORD OriginalEsp() const { return esp + 12; }
    template<class T> T& Stack(int offset) const { return *reinterpret_cast<T*>(OriginalEsp() + offset); }
};
typedef DWORD (__cdecl *HookHandler)(Registers*);
// Queue a hook. Returning zero from its handler replays the original bytes.
// A nonzero return selects a verified alternative continuation.
bool AddHook(DWORD site, const BYTE* expected, size_t length, HookHandler handler, const char* name);
void Log(const char* format, ...);
bool Enabled(const char* key, bool defaultValue = true);
bool Readable(const void* address, size_t length);

bool InstallTooltips();
bool InstallEconomy();
bool InstallPlacement();
bool InstallSessionSettings();
bool InstallCommands();
bool InstallGameClock();
bool InstallHealthBars();
bool InstallRangeCircles();
// Range circles (RangeCircles.cpp): whether a selected object without the stock
// HasRadialIndicator flag should draw the stock radial indicator.
bool ShowSelectedRange(bool stockFlag, bool isBuilding, int radiusCells);
// Integer setting from [UI] in RA2NativeUI.ini (e.g. HealthBars=0/1/2).
int Setting(const char* key, int defaultValue);
// Health bars (HealthBars.cpp): whether an object that is neither hovered nor selected
// should take the stock hover health-bar path. mode 0 = stock, 1 = damaged, 2 = all.
bool UseHoverHealthBar(int mode, bool hovering, bool selected, int health, double ratio);

// Shared harvester classification (Economy.cpp), used by Next Idle Harvester.
int CollectHarvesters(DWORD* units, bool* idle, int capacity);
// Index of the next idle entry after `selected` (wrapping), or -1 if none is idle.
int NextIdleIndex(const DWORD* units, const bool* idle, int count, DWORD selected);
// Whole game seconds for an engine frame count (15 frames per game second).
int GameSeconds(int frame);
// Match statistics (GameClock.cpp): kills of other houses' units and buildings,
// own units and buildings lost, and the value of owned units (harvesters excluded).
struct MatchStats { int kills; int losses; int army; };
int SumKills(const int* units, const int* buildings, int houses, int self);
bool FormatMatchStats(wchar_t* text, size_t capacity, const MatchStats& stats);
// Autosave timing (Commands.cpp): real playing time between advancing frames.
struct AutosaveClock { bool started; int lastFrame; DWORD lastTick; DWORD activeMs; };
bool AdvanceAutosave(AutosaveClock& clock, int frame, DWORD now, DWORD intervalMs);
// Missing slot first, otherwise the slot with the oldest write time.
int PickAutosaveSlot(const bool* exists, const FILETIME* times, int count);
}
