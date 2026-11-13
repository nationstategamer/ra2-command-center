#include "../src/NativeUI.h"
#include <stdio.h>
#include <string.h>
#include <wchar.h>
namespace NativeUI {
void TestRegion(void*, DWORD);
bool TestCommit();
void TestFailWrite(int);
void TestFailAfterWrite(int);
bool TestSha(const wchar_t*, char (&)[65]);
void TestReset();
bool InstallTooltips() { return true; }
bool InstallEconomy() { return true; }
bool InstallPlacement() { return true; }
bool InstallCommands() { return true; }
bool InstallGameClock() { return true; }
bool InstallHealthBars() { return true; }
bool InstallRangeCircles() { return true; }
bool InstallSessionSettings() { return true; }
}
using namespace NativeUI;
static BYTE* code;
static int calls = 0;
static DWORD alternate = 0;
static bool stackOK = false;
static int failures = 0;
__declspec(align(16)) static DWORD xmmInput[4] = {1,2,3,4};
__declspec(align(16)) static DWORD xmmOutput[4] = {};
static void Check(bool ok, const char* name) {
    printf("%s: %s\n", ok ? "PASS" : "FAIL", name);
    if (!ok) ++failures;
}
static DWORD __cdecl Inspect(Registers* r) {
    ++calls;
    stackOK = r->Stack<int>(4) == 0x12345678;
    __asm { fninit }
    __asm { pxor xmm0, xmm0 }
    return 0;
}
static DWORD __cdecl Alternate(Registers* r) { r->eax = 99; return alternate; }
static DWORD __cdecl Fault(Registers* r) {
    ++calls;
    r->eax = 555;
    RaiseException(0xE0424242, 0, 0, NULL);
    return 0;
}
static DWORD __cdecl BadJump(Registers*) { return 0x42; }

int wmain() {
    code = static_cast<BYTE*>(VirtualAlloc(NULL, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!code) return 2;
    const BYTE original[] = {0xB8,7,0,0,0, 0x83,0xD0,0, 0xC3}; // mov eax,7; adc eax,0; ret
    memcpy(code, original, sizeof(original));
    memcpy(code + 32, original, sizeof(original));
    DWORD old;
    VirtualProtect(code, 4096, PAGE_EXECUTE_READ, &old);
    TestRegion(code, 4096);
    const DWORD site = reinterpret_cast<DWORD>(code);
    BYTE bad[5] = {0xB8,8,0,0,0};
    AddHook(site, bad, 5, Inspect, "mismatch");
    Check(!TestCommit() && !memcmp(code, original, sizeof(original)), "signature mismatch leaves code untouched");
    TestReset();
    AddHook(site, original, 5, Inspect, "first");
    AddHook(site + 32, original, 5, Inspect, "second");
    TestFailWrite(1);
    Check(!TestCommit() && !memcmp(code, original, sizeof(original)) &&
        !memcmp(code + 32, original, sizeof(original)), "second write failure rolls back the entire transaction");
    TestReset();
    AddHook(site, original, 5, Inspect, "first protection");
    AddHook(site + 32, original, 5, Inspect, "second protection");
    TestFailAfterWrite(1);
    const bool protectionResult = TestCommit();
    MEMORY_BASIC_INFORMATION protection;
    VirtualQuery(code, &protection, sizeof(protection));
    Check(!protectionResult && !memcmp(code, original, sizeof(original)) &&
        !memcmp(code + 32, original, sizeof(original)) && protection.Protect == PAGE_EXECUTE_READ,
        "post-write failure restores original bytes and read-only executable protection");
    TestReset();
    AddHook(site, original, 5, Inspect, "preservation");
    Check(TestCommit(), "guarded hook installs");
    int result;
    double fpu;
    DWORD ebxValue, esiValue, ediValue, stackBefore, stackAfter;
    __asm {
        push ebx
        push esi
        push edi
        mov ebx, 0x11223344
        mov esi, 0x55667788
        mov edi, 0x12344321
        mov stackBefore, esp
        fld1
        movdqa xmm0, xmmInput
        push 0x12345678
        stc
        call code
        mov result, eax
        lea esp, [esp + 4]
        mov stackAfter, esp
        mov ebxValue, ebx
        mov esiValue, esi
        mov ediValue, edi
        movdqa xmmOutput, xmm0
        fstp fpu
        pop edi
        pop esi
        pop ebx
    }
    Check(result == 8, "original instructions and carry flag preserved");
    Check(calls == 1 && stackOK && stackBefore == stackAfter, "handler receives original stack and returns without stack drift");
    Check(ebxValue == 0x11223344 && esiValue == 0x55667788 && ediValue == 0x12344321, "callee-saved registers preserved");
    Check(fpu == 1.0 && !memcmp(xmmInput, xmmOutput, sizeof(xmmInput)), "x87 and SSE state preserved across a destructive handler");
    TestReset();
    alternate = site + 8;
    AddHook(site, original, 5, Alternate, "alternate");
    TestCommit();
    typedef int (__cdecl *Function)(int);
    Function fn = reinterpret_cast<Function>(code);
    Check(fn(0) == 99, "verified alternate continuation returns edited register");
    TestReset();
    calls = 0;
    AddHook(site, original, 5, Fault, "exception");
    TestCommit();
    __asm { clc }
    const int first = fn(0);
    __asm { clc }
    const int second = fn(0);
    Check(first == 7 && second == 7 && calls == 1, "faulting handler restores original context and disables itself");
    TestReset();
    AddHook(site, original, 5, BadJump, "invalid continuation");
    TestCommit();
    __asm { clc }
    Check(fn(0) == 7, "out-of-range continuation falls back to original code");
    TestReset();
    Check(!Readable(NULL, 4) && !Readable(reinterpret_cast<void*>(0xFFFFFFF0), 32) &&
        Readable(code, 4096), "memory guards reject null and wrapping ranges");
    wchar_t fixture[MAX_PATH];
    GetModuleFileNameW(NULL, fixture, MAX_PATH);
    wchar_t* slash = wcsrchr(fixture, L'\\');
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - fixture), L"sha-test.tmp");
    HANDLE file = CreateFileW(fixture, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, NULL);
    DWORD written;
    WriteFile(file, "abc", 3, &written, NULL); CloseHandle(file);
    char hex[65];
    Check(TestSha(fixture, hex) && !strcmp(hex, "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"), "executable hash gate uses correct SHA256");
    DeleteFileW(fixture);
    VirtualFree(code, 0, MEM_RELEASE);
    printf("Failures: %d\n", failures);
    return failures ? 1 : 0;
}
