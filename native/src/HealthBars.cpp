#include "NativeUI.h"

// Health bars without selecting. Stock RA2 draws a unit's or building's health bar
// only when it is selected or under the mouse (TechnoClass::DrawExtras, 0x6C2C40).
// For objects that qualify under the chosen mode, this takes the stock mouse-hover
// path, so the bar is the game's own (no selection bracket), and the stock rules
// for disguised units and selected objects still apply.
// game.exe SHA256 73288C03...FF3B00DF only; see NativeRA2/analysis/features/evidence.md.
namespace NativeUI {
namespace {
const DWORD kHoverTestSite = 0x6C389D;   // mov al, [esi+0x380]  (IsMouseHovering)
const BYTE  kHoverTestBytes[] = { 0x8A,0x86,0x80,0x03,0x00,0x00 };
const DWORD kAfterHoverTest = 0x6C38A7;  // mov al, [esi+0x77]: the stock hover path continues here
const DWORD kHealthRatio = 0x5D4540;     // ObjectClass health / type strength (double)
const DWORD kHovering = 0x380, kSelected = 0x77, kHealth = 0x6C;
int gMode = 1;

template<class T> T Read(DWORD address) { return *reinterpret_cast<const T*>(address); }

DWORD __cdecl ShowHealthBar(Registers* regs) {
    const DWORD object = regs->esi;
    if (!gMode || !Readable(reinterpret_cast<const void*>(object), kHovering + 1)) return 0;
    const bool hovering = Read<BYTE>(object + kHovering) != 0;
    const bool selected = Read<BYTE>(object + kSelected) != 0;
    const int health = Read<int>(object + kHealth);
    double ratio = 1.0;
    if (gMode == 1 && !hovering && !selected && health > 0) {
        typedef double (__thiscall *RatioFn)(void*);
        ratio = reinterpret_cast<RatioFn>(kHealthRatio)(reinterpret_cast<void*>(object));
    }
    return UseHoverHealthBar(gMode, hovering, selected, health, ratio) ? kAfterHoverTest : 0;
}
} // namespace

bool UseHoverHealthBar(int mode, bool hovering, bool selected, int health, double ratio) {
    // Hovered and selected objects keep the stock behavior (return false = replay it).
    if (hovering || selected || health <= 0) return false;
    if (mode == 2) return true;
    if (mode == 1) return ratio < 1.0;
    return false;
}

bool InstallHealthBars() {
    gMode = Setting("HealthBars", 1);
    if (gMode < 0 || gMode > 2) gMode = 1;
    Log("Health bars mode %d (0 = stock, 1 = damaged, 2 = all).", gMode);
    if (!gMode) return true;
    return AddHook(kHoverTestSite, kHoverTestBytes, sizeof kHoverTestBytes, ShowHealthBar, "DrawExtras: health bars");
}
} // namespace NativeUI
