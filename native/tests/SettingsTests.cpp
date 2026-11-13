#include "../src/NativeUI.h"
#include <stdio.h>
#include <string.h>
#include <wchar.h>

namespace NativeUI {
void TestRegion(void*, DWORD); bool TestCommit(); void TestReset();
bool TestOption(const char*, bool); unsigned TestHookCount();
bool TestReadKeepGameSpeed(const wchar_t*);
void TestSettingsTargets(void*, int*, int*, BYTE*, BYTE*, int*, void*, DWORD*);
HookHandler TestSettingsHandler(); unsigned TestSettingsLogCount();
HookHandler TestLoadSettingsHandler(); unsigned TestReloadLogCount();
bool InstallTooltips() { return true; }
bool InstallEconomy() { return true; }
bool InstallPlacement() { return true; }
bool InstallCommands() { return true; }
bool InstallGameClock() { return true; }
bool InstallHealthBars() { return true; }
bool InstallRangeCircles() { return true; }
}
using namespace NativeUI;
static BYTE* code;
static DWORD eaxInput = 0x12345678;
static DWORD selected, beforeFlags, afterFlags, beforeStack, afterStack;
static DWORD actualEax, actualEbx, actualEcx, actualEdx, actualEsi, actualEdi, actualEbp;
__declspec(align(16)) static DWORD xmmInput[4] = { 7, 11, 13, 17 };
__declspec(align(16)) static DWORD xmmOutput[4];
static double fpuOutput;
static int failures = 0;
struct Fixture {
    DWORD before;
    int active, mode;
    BYTE gameActive, lifecycleActive;
    int saved;
    DWORD loadOperand;
    DWORD after;
};
static Fixture* data;
static void Check(bool ok, const char* name) {
    printf("%s: %s\n", ok ? "PASS" : "FAIL", name);
    if (!ok) ++failures;
}

// Run exactly the installed bridge plus trampoline. ECX is the real register
// input, and the displaced instruction really writes the fixture's active speed.
__declspec(naked) static void RunBridge() {
    __asm {
        pushfd
        pushad
        mov beforeStack, esp
        mov eax, eaxInput
        mov ebx, 0x23456789
        mov ecx, selected
        mov edx, 0x3456789A
        mov esi, 0x456789AB
        mov edi, 0x56789ABC
        mov ebp, 0x6789ABCD
        fld1
        movdqa xmm0, xmmInput
        std
        stc
        pushfd
        pop beforeFlags
        call code
        pushfd
        pop afterFlags
        mov actualEax, eax
        mov actualEbx, ebx
        mov actualEcx, ecx
        mov actualEdx, edx
        mov actualEsi, esi
        mov actualEdi, edi
        mov actualEbp, ebp
        mov afterStack, esp
        movdqa xmmOutput, xmm0
        fstp fpuOutput
        cld
        popad
        popfd
        ret
    }
}

static void SetData(int mode = 5, BYTE game = 1, BYTE lifecycle = 1, int saved = 0) {
    data->before = 0xA5B6C7D8; data->after = 0xB6C7D8E9;
    data->active = 0; data->mode = mode; data->gameActive = game;
    data->lifecycleActive = lifecycle; data->saved = saved; data->loadOperand = 0x6758493A;
}
static bool StateIntact() {
    return data->before == 0xA5B6C7D8 && data->after == 0xB6C7D8E9 &&
        beforeStack == afterStack && beforeFlags == afterFlags &&
        actualEax == eaxInput && actualEbx == 0x23456789 && actualEcx == selected &&
        actualEdx == 0x3456789A && actualEsi == 0x456789AB && actualEdi == 0x56789ABC &&
        actualEbp == 0x6789ABCD && fpuOutput == 1.0 &&
        !memcmp(xmmInput, xmmOutput, sizeof(xmmInput));
}
static void ResetTargets() {
    TestSettingsTargets(code, &data->active, &data->mode, &data->gameActive, &data->lifecycleActive, &data->saved, code + 64, &data->loadOperand);
}
static bool Run(int newSpeed, int mode, BYTE game, BYTE lifecycle, int oldSpeed, int expected) {
    eaxInput = 0x12345678;
    SetData(mode, game, lifecycle, oldSpeed); selected = static_cast<DWORD>(newSpeed);
    RunBridge();
    return data->saved == expected && data->active == newSpeed && StateIntact();
}

static bool RunLoad(int loadedSpeed, int expectedSpeed, int mode = 5, BYTE game = 1,
    BYTE lifecycle = 0, int saved = -1, BYTE result = 1) {
    SetData(mode, game, lifecycle, saved); data->active = loadedSpeed;
    selected = data->loadOperand; eaxInput = 0x12345600 | result;
    code += 64; RunBridge(); code -= 64;
    return data->active == expectedSpeed && data->saved == saved && StateIntact();
}

int wmain() {
    code = static_cast<BYTE*>(VirtualAlloc(NULL, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    data = static_cast<Fixture*>(VirtualAlloc(NULL, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!code || !data) return 2;
    BYTE original[] = {0x89, 0x0D, 0, 0, 0, 0, 0xC3};
    const DWORD active = reinterpret_cast<DWORD>(&data->active);
    memcpy(original + 2, &active, sizeof(active)); memcpy(code, original, sizeof(original));
    BYTE loadOriginal[] = {0x8B, 0x0D, 0, 0, 0, 0, 0xC3};
    const DWORD loadOperand = reinterpret_cast<DWORD>(&data->loadOperand);
    memcpy(loadOriginal + 2, &loadOperand, sizeof(loadOperand));
    memcpy(code + 64, loadOriginal, sizeof(loadOriginal));
    DWORD protection; VirtualProtect(code, 4096, PAGE_EXECUTE_READ, &protection);
    TestRegion(code, 4096); ResetTargets();
    Check(Enabled("KeepGameSpeed"), "settings fix defaults to enabled");
    Check(InstallSessionSettings() && TestHookCount() == 2 && TestCommit(), "production installer installs both signature-checked settings hooks");
    Check(Run(3, 5, 1, 1, 0, 3), "skirmish explicit Medium commit updates cache and replays active-speed MOV");
    Check(Run(4, 0, 1, 1, 2, 4), "campaign explicit Slow commit updates cache and replays active-speed MOV");
    Check(StateIntact(), "actual bridge preserves all general registers flags stack x87 and SSE");
    Check(Run(0, 5, 1, 1, 6, 0) && Run(6, 5, 1, 1, 0, 6), "both legal slider endpoints are retained");
    bool network = true;
    for (int mode = 1; mode <= 4; ++mode) network = Run(3, mode, 1, 1, 0, 0) && network;
    Check(network, "all four non-singleplayer modes leave cache unchanged");
    Check(Run(3, -1, 1, 1, 0, 0) && Run(3, 6, 1, 1, 0, 0), "invalid session modes leave cache unchanged");
    Check(Run(3, 5, 0, 1, 0, 0) && Run(3, 5, 2, 1, 0, 0), "game-active guard rejects false and malformed flags");
    Check(Run(3, 5, 1, 0, 0, 0) && Run(3, 5, 1, 2, 0, 0), "session-lifecycle guard rejects false and malformed flags");
    Check(Run(3, 5, 1, 1, -1, -1), "inactive cache sentinel is preserved");
    Check(Run(3, 5, 1, 1, -2, -2) && Run(3, 5, 1, 1, 7, 7), "invalid cached speeds are not overwritten");
    Check(Run(-1, 5, 1, 1, 0, 0) && Run(7, 5, 1, 1, 0, 0), "invalid ECX inputs do not update cache while original MOV still replays");
    const unsigned logs = TestSettingsLogCount();
    Check(Run(3, 5, 1, 1, 3, 3) && TestSettingsLogCount() == logs, "unchanged selection produces no extra log");
    ResetTargets();
    for (int i = 0; i < 20; ++i) Run(i % 2 ? 3 : 4, 5, 1, 1, 0, i % 2 ? 3 : 4);
    Check(TestSettingsLogCount() == 8, "selection diagnostics stop after eight changed commits");
    TestSettingsTargets(code, &data->active, reinterpret_cast<int*>(1), &data->gameActive, &data->lifecycleActive, &data->saved, code + 64, &data->loadOperand);
    Check(Run(3, 5, 1, 1, 0, 0), "unreadable state is skipped without disabling or breaking original instruction");
    ResetTargets();
    Registers regs = {}; regs.ecx = 3; regs.eax = 0xABCDEF01; regs.flags = 0x447;
    const Registers originalRegs = regs; SetData();
    Check(TestSettingsHandler()(&regs) == 0 && !memcmp(&regs, &originalRegs, sizeof(regs)) && data->saved == 3,
        "real callback leaves register snapshot byte-identical and requests replay");
    ResetTargets();
    Check(RunLoad(0, 0), "loading without an explicit in-process choice retains the loaded speed");
    Check(Run(3, 5, 1, 1, 0, 3) && RunLoad(0, 3),
        "regression: explicit choice survives cache suspension and later load overwrite");
    Check(StateIntact(), "load-return bridge preserves registers flags stack x87 SSE and displaced ECX read");
    Check(RunLoad(0, 3) && RunLoad(0, 3), "repeated loads retain the explicit choice without rewriting the inactive cache");
    Check(RunLoad(0, 0, 5, 1, 0, -1, 0) && RunLoad(0, 0, 5, 1, 0, -1, 2),
        "failed or malformed load results retain original options");
    bool rejectedModes = true;
    for (int mode = 0; mode <= 6; ++mode)
        if (mode != 5) rejectedModes = RunLoad(0, 0, mode) && rejectedModes;
    Check(rejectedModes && RunLoad(0, 0, -1), "load correction excludes campaign network invalid and changed session modes");
    Check(RunLoad(0, 0, 5, 0) && RunLoad(0, 0, 5, 2), "load correction requires a valid game-active flag");
    Check(RunLoad(0, 0, 5, 1, 1) && RunLoad(0, 0, 5, 1, 2), "load correction requires the suspended lifecycle");
    Check(RunLoad(0, 0, 5, 1, 0, 0) && RunLoad(0, 0, 5, 1, 0, -2),
        "load correction preserves non-sentinel and malformed caches");
    Check(RunLoad(-1, -1) && RunLoad(7, 7), "load correction rejects malformed loaded speeds");
    ResetTargets();
    Check(Run(3, 5, 1, 1, 3, 3) && RunLoad(0, 3),
        "explicit choice matching the current cache is still remembered for loading");
    Check(Run(0, 5, 1, 1, 3, 0) && RunLoad(3, 0) && Run(6, 5, 1, 1, 0, 6) && RunLoad(0, 6),
        "load correction retains both legal explicit slider endpoints");
    ResetTargets();
    Check(Run(4, 0, 1, 1, 0, 4) && RunLoad(0, 0), "a campaign choice cannot override skirmish load settings");
    ResetTargets(); Run(3, 5, 1, 1, 0, 3);
    for (int i = 0; i < 20; ++i) RunLoad(0, 3);
    Check(TestReloadLogCount() == 8, "reload diagnostics stop after eight changed loads");
    TestOption("KeepGameSpeed", false);
    Check(RunLoad(0, 0), "runtime opt-out also bypasses the load correction");

    Check(Run(3, 5, 1, 1, 0, 0), "runtime opt-out bypasses cache update");
    TestReset(); ResetTargets();
    Check(InstallSessionSettings() && TestHookCount() == 0 && !memcmp(code, original, sizeof(original)) && !memcmp(code + 64, loadOriginal, sizeof(loadOriginal)),
        "disabled installer queues neither settings hook and retains both stock instructions");
    Check(Run(3, 5, 1, 1, 0, 0), "opted-out fixture executes original active-speed write");
    TestOption("KeepGameSpeed", true);

    wchar_t fixture[MAX_PATH]; GetModuleFileNameW(NULL, fixture, MAX_PATH);
    wchar_t* slash = wcsrchr(fixture, L'\\');
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - fixture), L"settings-switch-test.ini");
    DeleteFileW(fixture);
    Check(TestReadKeepGameSpeed(fixture), "absent configuration retains enabled default");
    const wchar_t* off[] = {L"0", L"no", L"NO", L"false", L"off"}; bool offOK = true;
    for (unsigned i = 0; i < _countof(off); ++i) {
        WritePrivateProfileStringW(L"UI", L"KeepGameSpeed", off[i], fixture);
        offOK = !TestReadKeepGameSpeed(fixture) && offOK;
    }
    Check(offOK, "real INI parser accepts numeric and textual opt-out values");
    const wchar_t* on[] = {L"1", L"yes", L"YES", L"true", L"on"}; bool onOK = true;
    for (unsigned i = 0; i < _countof(on); ++i) {
        WritePrivateProfileStringW(L"UI", L"KeepGameSpeed", on[i], fixture);
        onOK = TestReadKeepGameSpeed(fixture) && onOK;
    }
    Check(onOK, "real INI parser accepts documented yes and numeric one");
    DeleteFileW(fixture);
    VirtualFree(code, 0, MEM_RELEASE); VirtualFree(data, 0, MEM_RELEASE);
    printf("Failures: %d\n", failures);
    return failures ? 1 : 0;
}
