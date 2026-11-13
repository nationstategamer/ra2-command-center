#include "NativeUI.h"

// Range circles for selected defenses. Stock RA2 draws a selected object's radial
// indicator only when its type sets HasRadialIndicator (Gap Generator, Psychic Sensor,
// Psychic Beacon). TacticalClass::DrawRadialIndicators (0x6AC420) tests that flag for
// each selected object; this lets buildings whose radius is positive through as well.
// The stock BuildingClass::DrawRadialIndicator (vtable +0x128, 0x4519D0) then draws the
// circle from the stock radius function (0x451930: gap/sensor radius, otherwise the
// primary weapon's range), only for the player's own, active buildings.
// game.exe SHA256 73288C03...FF3B00DF only; see NativeRA2/analysis/features/evidence.md.
namespace NativeUI {
namespace {
const DWORD kFlagTestSite = 0x6AC45B;    // cmp byte ptr [eax+0x208], bl   (EAX = type, ESI = object)
const BYTE  kFlagTestBytes[] = { 0x38,0x98,0x08,0x02,0x00,0x00 };
const DWORD kDrawIndicator = 0x6AC463;   // stock call of the object's DrawRadialIndicator(1)
const DWORD kRadius = 0x451930;          // BuildingClass radial radius in cells
const DWORD kHasRadialIndicator = 0x208;
const DWORD kWhatAmISlot = 0x2C;
const int kBuilding = 6;

template<class T> T Read(DWORD address) { return *reinterpret_cast<const T*>(address); }

DWORD __cdecl SelectedRangeCircle(Registers* regs) {
    const DWORD type = regs->eax, object = regs->esi;
    if (!Readable(reinterpret_cast<const void*>(type), kHasRadialIndicator + 1) ||
        !Readable(reinterpret_cast<const void*>(object), 4)) return 0;
    const bool stockFlag = Read<BYTE>(type + kHasRadialIndicator) != 0;
    if (stockFlag) return 0;                      // replay: the stock indicator is drawn anyway
    const DWORD vtable = Read<DWORD>(object);
    if (!Readable(reinterpret_cast<const void*>(vtable + kWhatAmISlot), 4)) return 0;
    const DWORD whatAmI = Read<DWORD>(vtable + kWhatAmISlot);
    if (whatAmI < 0x401000 || whatAmI >= 0x780000) return 0;
    const int kind = reinterpret_cast<int (__thiscall *)(void*)>(whatAmI)(reinterpret_cast<void*>(object));
    int radius = 0;
    if (kind == kBuilding) radius = reinterpret_cast<int (__thiscall *)(void*)>(kRadius)(reinterpret_cast<void*>(object));
    return ShowSelectedRange(stockFlag, kind == kBuilding, radius) ? kDrawIndicator : 0;
}
} // namespace

bool ShowSelectedRange(bool stockFlag, bool isBuilding, int radiusCells) {
    // Stock-flagged types already draw; otherwise only buildings with a real radius.
    return !stockFlag && isBuilding && radiusCells > 0 && radiusCells < 64;
}

bool InstallRangeCircles() {
    if (!Enabled("RangeCircles")) return true;
    return AddHook(kFlagTestSite, kFlagTestBytes, sizeof kFlagTestBytes, SelectedRangeCircle, "Radial indicators: range circles for selected defenses");
}
} // namespace NativeUI
