#include "NativeUI.h"
#include <stdio.h>
#include <wchar.h>
#include <float.h>
#include <limits.h>

namespace NativeUI {
namespace {
// All addresses/offsets are for the hash-gated Steam RA2 1.006 game.exe.
// Evidence: analysis/tooltip-mapping.json and tooltip-disassembly.txt.
const DWORD kHelpTextHook = 0x0067F5B4;
const DWORD kHelpTextReturn = 0x0067F675;
const DWORD kCurrentPlayer = 0x00A35DB4;
const DWORD kRules = 0x00839848;
wchar_t gTooltip[256];
bool gReportedFailure = false;

template<class T> bool Read(const void* base, size_t offset, T& value) {
    const BYTE* p = static_cast<const BYTE*>(base) + offset;
    if (!Readable(p, sizeof(T))) return false;
    value = *reinterpret_cast<const T*>(p);
    return true;
}

int ObjectKind(int cameoKind) {
    switch (cameoKind) {
    case 1: case 40: return 1; // vehicle / vehicle type
    case 2: case 3: return 2;  // aircraft / aircraft type
    case 6: case 7: return 6;  // building / building type
    case 15: case 16: return 15; // infantry / infantry type
    default: return 0;
    }
}

bool TruncateTicks(double value, int& ticks) {
    if (!_finite(value) || value < 0.0 || value > 2147483646.0) return false;
    ticks = static_cast<int>(value);
    return true;
}

// Mirrors the complete original RA2 TechnoClass::TimeToBuild (0x6C22A0).
// Uses actual House/type objects and their read-only engine helpers. No object
// is created, injected into the simulation, or represented by a fake vtable.
// RA2 has no YR BuildTimeMultiplier step. Each integer conversion is retained.
bool BuildTicks(void* type, void* house, int objectKind, void** vtable, int& ticks) {
    void* rules = *reinterpret_cast<void**>(kRules);
    void* houseType = 0;
    if (!rules || !Read(house, 0x34, houseType) || !houseType ||
        !Readable(houseType, 0x148) || !Readable(house, 0x52D8)) return false;

    typedef int (__thiscall *GetBuildSpeed)(void*);
    typedef float (__thiscall *HouseBias)(void*, void*);
    typedef float (__thiscall *PowerRatio)(void*);
    typedef int (__thiscall *FactoryCount)(void*, int, bool);
    const int base = reinterpret_cast<GetBuildSpeed>(vtable[0x88 / 4])(type);
    const float bias = reinterpret_cast<HouseBias>(0x004F5A70)(house, type);
    if (!TruncateTicks(static_cast<double>(base) * bias, ticks)) return false;

    const float power = reinterpret_cast<PowerRatio>(0x004E8330)(house);
    float minLow, maxLow, penalty, multiple;
    if (!Read(rules, 0x490, minLow) || !Read(rules, 0x494, maxLow) ||
        !Read(rules, 0x498, penalty) || !Read(rules, 0x49C, multiple) ||
        !_finite(power) || !_finite(minLow) || !_finite(maxLow) ||
        !_finite(penalty) || !_finite(multiple)) return false;
    double production = 1.0 - (1.0 - static_cast<double>(power)) * penalty;
    if (production <= minLow) production = minLow;
    if (power < 1.0f && production > maxLow) production = maxLow;
    if (production == 0.0) production = static_cast<double>(0.01f);
    if (production < 0.0 || !TruncateTicks(ticks / production, ticks)) return false;

    BYTE naval = 0;
    if (objectKind == 1 && !Read(type, 0xADA, naval)) return false;
    const int factories = reinterpret_cast<FactoryCount>(0x004EB980)(house, objectKind, naval != 0);
    if (factories < 0 || factories > 4096) return false;
    if (multiple > 0.0f) {
        for (int i = 1; i < factories; ++i)
            if (!TruncateTicks(static_cast<double>(ticks) * multiple, ticks)) return false;
    }
    if (objectKind == 6) {
        BYTE wall = 0;
        if (!Read(type, 0x1215, wall)) return false;
        if (wall) {
            double wallCoefficient;
            if (!Read(rules, 0x678, wallCoefficient) ||
                !TruncateTicks(ticks * wallCoefficient, ticks)) return false;
        }
    }
    if (ticks < 54) ticks = 54; // Same minimum as the enhanced YR tooltip.
    return true;
}

bool MakeTooltip(DWORD stripEntry) {
    int kind = 0, index = 0;
    const void* entry = reinterpret_cast<const void*>(stripEntry);
    if (!Read(entry, 0x58, kind) || !Read(entry, 0x54, index)) return false;
    const int objectKind = ObjectKind(kind);
    if (!objectKind || index < 0) return false; // Keep native superweapon tips.
    typedef void* (__fastcall *GetType)(int, int);
    void* type = reinterpret_cast<GetType>(0x00482C00)(kind, index);
    void* house = *reinterpret_cast<void**>(kCurrentPlayer);
    void** vtable = 0;
    const wchar_t* name = 0;
    if (!type || !house || !Read(type, 0, vtable) ||
        !Readable(vtable, 0x8C) || !Read(type, 0x60, name) || !name) return false;
    if (!Readable(vtable[0x84 / 4], 1) || !Readable(vtable[0x88 / 4], 1)) return false;
    size_t nameLength = 0;
    while (nameLength < 128) {
        if (!Readable(name + nameLength, sizeof(wchar_t))) return false;
        if (!name[nameLength]) break;
        ++nameLength;
    }
    if (!nameLength || nameLength == 128) return false;

    typedef int (__thiscall *ActualCost)(void*, void*);
    const int cost = reinterpret_cast<ActualCost>(vtable[0x84 / 4])(type, house);
    if (cost == INT_MIN) return false;
    int ticks = 0;
    if (!BuildTicks(type, house, objectKind, vtable, ticks)) return false;
    int power = 0;
    if (objectKind == 6) {
        int output, drain;
        if (!Read(type, 0xC68, output) || !Read(type, 0xC6C, drain)) return false;
        const __int64 net = static_cast<__int64>(output) - drain;
        if (net > INT_MAX || net < INT_MIN) return false;
        power = static_cast<int>(net);
    }
    const int seconds = ticks / 15; // Nominal game time, matching default Phobos.
    const int absoluteCost = cost < 0 ? -cost : cost;
    if (power) {
        _snwprintf_s(gTooltip, _countof(gTooltip), _TRUNCATE,
            L"%ls\n%ls$%d  T:%02d:%02d\nP:%+d", name, cost < 0 ? L"+" : L"",
            absoluteCost, seconds / 60, seconds % 60, power);
    } else {
        _snwprintf_s(gTooltip, _countof(gTooltip), _TRUNCATE,
            L"%ls\n%ls$%d  T:%02d:%02d", name, cost < 0 ? L"+" : L"",
            absoluteCost, seconds / 60, seconds % 60);
    }
    return true;
}

DWORD __cdecl TooltipHook(Registers* regs) {
    bool made = false;
    __try { made = MakeTooltip(regs->eax); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        if (!gReportedFailure) {
            Log("Tooltip calculation unavailable; preserving the original tooltip.");
            gReportedFailure = true;
        }
    }
    if (!made) return 0;
    regs->eax = reinterpret_cast<DWORD>(gTooltip);
    // No callee-saved registers were pushed before this hook. This verified
    // original epilogue returns the text pointer and pops the one argument.
    return kHelpTextReturn;
}
} // namespace

bool InstallTooltips() {
    if (!Enabled("ExtendedTooltips")) return true;
    const BYTE expected[] = { 0x8B, 0x48, 0x58, 0x83, 0xF9, 0x1F };
    return AddHook(kHelpTextHook, expected, sizeof(expected), TooltipHook, "Extended tooltips");
}
} // namespace NativeUI
