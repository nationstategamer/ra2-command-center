#include "NativeUI.h"
#include <string.h>

namespace NativeUI {
namespace {
// Original RA2 1.006 only, additionally gated by the framework's full SHA256.
// No save format, simulation data or INI is changed. See native-evidence.json.
const DWORD kCommitSite = 0x004CFADA;
const BYTE kCommitBytes[] = {0x89, 0x0D, 0x18, 0x0B, 0xA4, 0x00};
// Immediately after the normal in-game Load Game call returns. AL is its result.
const DWORD kLoadReturnSite = 0x0053C6EA;
const BYTE kLoadReturnBytes[] = {0x8B, 0x0D, 0x68, 0x9C, 0x83, 0x00};
const DWORD kMode = 0x00A3D298;
const DWORD kGameActive = 0x00A40958;
const DWORD kSessionLifecycleActive = 0x00A40318;
const DWORD kSavedSessionSpeed = 0x007F3230;
const DWORD kActiveSpeed = 0x00A40B18;
unsigned gChangeLogs = 0, gReloadLogs = 0, gLoadDiagnostics = 0;
int gExplicitSpeed = -1, gExplicitMode = -1;

#ifdef NATIVE_UI_TEST
DWORD gSite = kCommitSite, gLoadReturnSite = kLoadReturnSite;
DWORD gMode = kMode, gGameActive = kGameActive;
DWORD gSessionLifecycleActive = kSessionLifecycleActive, gSavedSessionSpeed = kSavedSessionSpeed;
DWORD gActiveSpeed = kActiveSpeed, gLoadOperand = 0x00839C68;
#else
const DWORD gSite = kCommitSite, gLoadReturnSite = kLoadReturnSite;
const DWORD gMode = kMode, gGameActive = kGameActive;
const DWORD gSessionLifecycleActive = kSessionLifecycleActive, gSavedSessionSpeed = kSavedSessionSpeed;
const DWORD gActiveSpeed = kActiveSpeed;
#endif

bool SettingsReadable() {
    return Readable(reinterpret_cast<void*>(gMode), sizeof(int)) &&
        Readable(reinterpret_cast<void*>(gGameActive), sizeof(BYTE)) &&
        Readable(reinterpret_cast<void*>(gSessionLifecycleActive), sizeof(BYTE)) &&
        Readable(reinterpret_cast<void*>(gSavedSessionSpeed), sizeof(int));
}

DWORD __cdecl CommitGameSpeed(Registers* regs) {
    // ECX is the explicit slider selection. The trampoline replays the original
    // active-speed MOV; retain the choice even if the session cache already matches.
    if (!Enabled("KeepGameSpeed") || regs->ecx > 6 || !SettingsReadable()) return 0;
    const int mode = *reinterpret_cast<const int*>(gMode);
    if ((mode != 0 && mode != 5) || *reinterpret_cast<const BYTE*>(gGameActive) != 1 ||
        *reinterpret_cast<const BYTE*>(gSessionLifecycleActive) != 1) return 0;
    int* const saved = reinterpret_cast<int*>(gSavedSessionSpeed);
    const int previous = *saved;
    if (previous < 0 || previous > 6) return 0;
    gExplicitSpeed = static_cast<int>(regs->ecx);
    gExplicitMode = mode;
    if (previous == gExplicitSpeed) return 0;
    *saved = gExplicitSpeed;
    if (gChangeLogs < 8) {
        ++gChangeLogs;
        Log("KeepGameSpeed: explicit selection %lu retained (previous session value %d).",
            regs->ecx, previous);
    }
    return 0;
}

DWORD __cdecl RestoreAfterSkirmishLoad(Registers* regs) {
    // Restrict this correction to the existing in-game skirmish Load Game path,
    // a true return value, and a choice explicitly made in this same process.
    // Campaign resume forces speed 2 separately and is outside this correction.
    if (!Enabled("KeepGameSpeed") || !SettingsReadable() ||
        !Readable(reinterpret_cast<void*>(gActiveSpeed), sizeof(int))) return 0;
    if (gLoadDiagnostics < 8) {
        ++gLoadDiagnostics;
        Log("KeepGameSpeed: load return result=%lu mode=%d explicit=%d/%d active=%d game=%u lifecycle=%u cache=%d.",
            regs->eax & 0xFF, *reinterpret_cast<const int*>(gMode), gExplicitMode, gExplicitSpeed,
            *reinterpret_cast<const int*>(gActiveSpeed), *reinterpret_cast<const BYTE*>(gGameActive),
            *reinterpret_cast<const BYTE*>(gSessionLifecycleActive), *reinterpret_cast<const int*>(gSavedSessionSpeed));
    }
    if ((regs->eax & 0xFF) != 1 || gExplicitMode != 5 || gExplicitSpeed < 0 || gExplicitSpeed > 6) return 0;
    if (*reinterpret_cast<const int*>(gMode) != gExplicitMode ||
        *reinterpret_cast<const BYTE*>(gGameActive) != 1 ||
        *reinterpret_cast<const BYTE*>(gSessionLifecycleActive) != 0 ||
        *reinterpret_cast<const int*>(gSavedSessionSpeed) != -1) return 0;
    int* const active = reinterpret_cast<int*>(gActiveSpeed);
    const int loaded = *active;
    if (loaded < 0 || loaded > 6 || loaded == gExplicitSpeed) return 0;
    *active = gExplicitSpeed;
    // Keep the cache's inactive sentinel. Normal session resumption captures
    // the corrected active speed; all other loaded options remain untouched.
    if (gReloadLogs < 8) {
        ++gReloadLogs;
        Log("KeepGameSpeed: selection %d restored after skirmish load (loaded speed %d).",
            gExplicitSpeed, loaded);
    }
    return 0;
}
} // namespace

bool InstallSessionSettings() {
    if (!Enabled("KeepGameSpeed")) return true;
    BYTE expected[sizeof(kCommitBytes)], loadExpected[sizeof(kLoadReturnBytes)];
    memcpy(expected, kCommitBytes, sizeof(expected));
    memcpy(loadExpected, kLoadReturnBytes, sizeof(loadExpected));
#ifdef NATIVE_UI_TEST
    // Relocate only absolute data operands in the synthetic executable fixtures.
    memcpy(expected + 2, &gActiveSpeed, sizeof(gActiveSpeed));
    memcpy(loadExpected + 2, &gLoadOperand, sizeof(gLoadOperand));
#endif
    return AddHook(gSite, expected, sizeof(expected), CommitGameSpeed, "KeepGameSpeed explicit settings commit") &&
        AddHook(gLoadReturnSite, loadExpected, sizeof(loadExpected), RestoreAfterSkirmishLoad,
            "KeepGameSpeed in-game skirmish load return");
}

#ifdef NATIVE_UI_TEST
void TestSettingsTargets(void* site, int* active, int* mode, BYTE* gameActive,
    BYTE* lifecycleActive, int* saved, void* loadSite, DWORD* loadOperand) {
    gSite = reinterpret_cast<DWORD>(site); gActiveSpeed = reinterpret_cast<DWORD>(active);
    gMode = reinterpret_cast<DWORD>(mode); gGameActive = reinterpret_cast<DWORD>(gameActive);
    gSessionLifecycleActive = reinterpret_cast<DWORD>(lifecycleActive);
    gSavedSessionSpeed = reinterpret_cast<DWORD>(saved);
    gLoadReturnSite = reinterpret_cast<DWORD>(loadSite); gLoadOperand = reinterpret_cast<DWORD>(loadOperand);
    gChangeLogs = gReloadLogs = gLoadDiagnostics = 0; gExplicitSpeed = gExplicitMode = -1;
}
HookHandler TestSettingsHandler() { return CommitGameSpeed; }
HookHandler TestLoadSettingsHandler() { return RestoreAfterSkirmishLoad; }
unsigned TestSettingsLogCount() { return gChangeLogs; }
unsigned TestReloadLogCount() { return gReloadLogs; }
#endif
} // namespace NativeUI
