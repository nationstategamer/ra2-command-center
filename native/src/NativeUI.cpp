#include "NativeUI.h"
#include <wincrypt.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <wchar.h>
#pragma comment(lib, "advapi32.lib")

namespace NativeUI {
namespace {
struct Hook {
    DWORD site;
    BYTE expected[32];
    size_t length;
    HookHandler handler;
    const char* name;
    BYTE* allocation;
    DWORD trampoline;
    volatile LONG disabled;
    bool written;
    DWORD originalProtection;
};
Hook gHooks[16] = {};
unsigned gCount = 0;
DWORD gCodeBegin = 0, gCodeEnd = 0;
wchar_t gDirectory[MAX_PATH] = {};
bool gInitialized = false;
struct Option { const char* key; bool value; };
Option gOptions[] = {{"Enabled", true}, {"ExtendedTooltips", true},
    {"PlacementPreview", true}, {"HarvesterCounter", true}, {"PowerCounter", true},
    {"KeepGameSpeed", true}, {"NextIdleHarvester", true}, {"QuickSave", true},
    {"GameClock", true}, {"Autosave", true}, {"RangeCircles", true}, {"MatchStats", true}};
#ifdef NATIVE_UI_TEST
int gFailWrite = -1;
int gWriteCount = 0;
int gFailAfterWrite = -1;
#endif

bool FilePath(const wchar_t* name, wchar_t (&path)[MAX_PATH]) {
    if (!gDirectory[0]) {
        const DWORD n = GetModuleFileNameW(NULL, gDirectory, MAX_PATH);
        if (!n || n >= MAX_PATH) return false;
        wchar_t* slash = wcsrchr(gDirectory, L'\\');
        if (!slash) return false;
        slash[1] = 0;
    }
    return swprintf_s(path, L"%ls%ls", gDirectory, name) > 0;
}

// The new switch accepts the documented yes/no spelling as well as 1/0.
// Existing UI switches retain their original numeric parsing behavior.
bool ReadKeepGameSpeed(const wchar_t* path) {
    wchar_t value[32];
    GetPrivateProfileStringW(L"UI", L"KeepGameSpeed", L"1", value, _countof(value), path);
    return _wcsicmp(value, L"0") && _wcsicmp(value, L"no") &&
        _wcsicmp(value, L"false") && _wcsicmp(value, L"off");
}

bool Sha256(const wchar_t* path, char (&hex)[65]) {
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return false;
    HCRYPTPROV provider = 0;
    HCRYPTHASH hash = 0;
    bool ok = CryptAcquireContextW(&provider, NULL, NULL, PROV_RSA_AES, CRYPT_VERIFYCONTEXT) != FALSE;
    if (ok) ok = CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash) != FALSE;
    BYTE buffer[16384];
    DWORD count = 0;
    while (ok) {
        ok = ReadFile(file, buffer, sizeof(buffer), &count, NULL) != FALSE;
        if (!ok || !count) break;
        ok = CryptHashData(hash, buffer, count, 0) != FALSE;
    }
    BYTE digest[32];
    DWORD size = sizeof(digest);
    if (ok) ok = CryptGetHashParam(hash, HP_HASHVAL, digest, &size, 0) != FALSE && size == 32;
    if (ok) for (unsigned i = 0; i < 32; ++i) sprintf_s(hex + 2*i, 65 - 2*i, "%02X", digest[i]);
    if (hash) CryptDestroyHash(hash);
    if (provider) CryptReleaseContext(provider, 0);
    CloseHandle(file);
    return ok;
}

bool CodeAddress(DWORD address, size_t length = 1) {
    return address >= gCodeBegin && address < gCodeEnd && length <= gCodeEnd - address;
}

// The bytes are changed only while initialization runs on the main thread,
// before any gameplay UI call. No executable file on disk is modified.
bool WriteCode(DWORD address, const BYTE* data, size_t length, bool rollback, DWORD originalProtection) {
#ifdef NATIVE_UI_TEST
    if (!rollback && gWriteCount++ == gFailWrite) return false;
#else
    (void)rollback;
#endif
    DWORD old = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(address), length, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(reinterpret_cast<void*>(address), data, length);
#ifdef NATIVE_UI_TEST
    if (!rollback && gWriteCount - 1 == gFailAfterWrite) return false;
#endif
    DWORD ignored = 0;
    const BOOL restored = VirtualProtect(reinterpret_cast<void*>(address), length, originalProtection, &ignored);
    const BOOL flushed = FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(address), length);
    return restored && flushed;
}

void ReleaseQueued() {
    for (unsigned i = 0; i < gCount; ++i) {
        if (gHooks[i].allocation && !gHooks[i].written) {
            VirtualFree(gHooks[i].allocation, 0, MEM_RELEASE);
            gHooks[i].allocation = NULL;
        }
    }
}

void __cdecl SafeDispatch(Registers* regs) {
    const Registers original = *regs;
    Hook* hook = NULL;
    for (unsigned i = 0; i < gCount; ++i)
        if (gHooks[i].trampoline == regs->next && reinterpret_cast<DWORD>(gHooks[i].handler) == regs->callback) {
            hook = &gHooks[i]; break;
        }
    if (!hook || hook->disabled) return;
    DWORD result = 0, exception = 0;
    __try { result = hook->handler(regs); }
    __except (exception = GetExceptionCode(), EXCEPTION_EXECUTE_HANDLER) { }
    if (exception || (result && !CodeAddress(result))) {
        *regs = original;
        if (InterlockedExchange(&hook->disabled, 1) == 0)
            Log("Disabled %s for this session (exception=%08lX, continuation=%08lX).",
                hook->name, exception, result);
        return;
    }
    regs->esp = original.esp;
    regs->callback = original.callback;
    regs->next = result ? result : original.next;
}

__declspec(naked) void CommonDispatch() {
    __asm {
        pushfd
        pushad
        cld
        mov ebp, esp
        sub esp, 528
        and esp, 0xfffffff0
        fxsave [esp]
        push esp
        push ebp
        call SafeDispatch
        add esp, 4
        pop eax
        fxrstor [eax]
        mov esp, ebp
        popad
        popfd
        lea esp, [esp + 4]
        ret
    }
}

void Jump(BYTE* destination, DWORD target) {
    destination[0] = 0xE9;
    *reinterpret_cast<DWORD*>(destination + 1) = target - (reinterpret_cast<DWORD>(destination) + 5);
}

bool Prepare(Hook& hook) {
    hook.allocation = static_cast<BYTE*>(VirtualAlloc(NULL, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!hook.allocation) return false;
    hook.trampoline = reinterpret_cast<DWORD>(hook.allocation);
    memcpy(hook.allocation, hook.expected, hook.length);
    Jump(hook.allocation + hook.length, hook.site + static_cast<DWORD>(hook.length));
    BYTE* stub = hook.allocation + 64;
    stub[0] = 0x68;
    *reinterpret_cast<DWORD*>(stub + 1) = hook.trampoline;
    stub[5] = 0x68;
    *reinterpret_cast<DWORD*>(stub + 6) = reinterpret_cast<DWORD>(hook.handler);
    Jump(stub + 10, reinterpret_cast<DWORD>(CommonDispatch));
    DWORD old;
    return VirtualProtect(hook.allocation, 4096, PAGE_EXECUTE_READ, &old) &&
        FlushInstructionCache(GetCurrentProcess(), hook.allocation, 4096);
}

bool Commit() {
    // Validate all sites before allocating or changing any of them.
    for (unsigned i = 0; i < gCount; ++i) {
        Hook& h = gHooks[i];
        if (!Readable(reinterpret_cast<void*>(h.site), h.length) ||
            memcmp(reinterpret_cast<void*>(h.site), h.expected, h.length)) {
            Log("Signature mismatch at %08lX (%s); no UI hooks installed.", h.site, h.name);
            return false;
        }
        MEMORY_BASIC_INFORMATION memory;
        if (!VirtualQuery(reinterpret_cast<void*>(h.site), &memory, sizeof(memory)) ||
            h.site + h.length > reinterpret_cast<DWORD>(memory.BaseAddress) + memory.RegionSize) return false;
        h.originalProtection = memory.Protect;
    }
    for (unsigned i = 0; i < gCount; ++i) {
        if (!Prepare(gHooks[i])) { ReleaseQueued(); return false; }
    }
    for (unsigned i = 0; i < gCount; ++i) {
        Hook& h = gHooks[i];
        BYTE patch[32];
        memset(patch, 0x90, sizeof(patch));
        patch[0] = 0xE9;
        *reinterpret_cast<DWORD*>(patch + 1) = reinterpret_cast<DWORD>(h.allocation + 64) - (h.site + 5);
        // Set written before the call: even a protection-restore failure rolls back.
        h.written = true;
        if (!WriteCode(h.site, patch, h.length, false, h.originalProtection)) {
            bool restored = true;
            for (unsigned j = 0; j <= i; ++j) {
                Hook& prior = gHooks[j];
                if (WriteCode(prior.site, prior.expected, prior.length, true, prior.originalProtection)) prior.written = false;
                else restored = false;
            }
            Log("UI hook installation failed; rollback %s.", restored ? "completed" : "FAILED");
            if (!restored) {
                // Never continue with an inconsistent instruction stream.
                MessageBoxW(NULL, L"The experimental UI could not initialize safely. The game will close. Your game files and saves have not been changed.", L"Red Alert 2 UI", MB_OK | MB_ICONERROR);
                TerminateProcess(GetCurrentProcess(), ERROR_WRITE_FAULT);
            }
            ReleaseQueued();
            return false;
        }
    }
    for (unsigned i = 0; i < gCount; ++i)
        Log("Installed %s at %08lX.", gHooks[i].name, gHooks[i].site);
    return true;
}
} // namespace

bool Readable(const void* address, size_t length) {
    const uintptr_t begin = reinterpret_cast<uintptr_t>(address);
    if (!begin || !length || begin + length < begin) return false;
    uintptr_t cursor = begin, end = begin + length;
    while (cursor < end) {
        MEMORY_BASIC_INFORMATION memory;
        if (!VirtualQuery(reinterpret_cast<void*>(cursor), &memory, sizeof(memory)) ||
            memory.State != MEM_COMMIT || (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS)) ||
            !(memory.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY |
                PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))) return false;
        const uintptr_t next = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
        if (next <= cursor) return false;
        cursor = next;
    }
    return true;
}

void Log(const char* format, ...) {
    wchar_t path[MAX_PATH];
    if (!FilePath(L"RA2NativeUI.log", path)) return;
    char line[1024];
    SYSTEMTIME now;
    GetLocalTime(&now);
    int start = sprintf_s(line, "%04u-%02u-%02u %02u:%02u:%02u ", now.wYear,
        now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond);
    va_list args;
    va_start(args, format);
    _vsnprintf_s(line + start, sizeof(line) - start - 2, _TRUNCATE, format, args);
    va_end(args);
    strcat_s(line, "\r\n");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(file, line, static_cast<DWORD>(strlen(line)), &written, NULL);
    CloseHandle(file);
}

bool Enabled(const char* key, bool defaultValue) {
    for (unsigned i = 0; i < _countof(gOptions); ++i)
        if (!strcmp(gOptions[i].key, key)) return gOptions[i].value;
    return defaultValue;
}

int Setting(const char* key, int defaultValue) {
    wchar_t path[MAX_PATH], wide[64];
    if (!key || !FilePath(L"RA2NativeUI.ini", path) ||
        !MultiByteToWideChar(CP_ACP, 0, key, -1, wide, _countof(wide))) return defaultValue;
    return static_cast<int>(GetPrivateProfileIntW(L"UI", wide, defaultValue, path));
}

bool AddHook(DWORD site, const BYTE* expected, size_t length, HookHandler handler, const char* name) {
    if (gInitialized || !expected || !handler || !name || length < 5 || length > 32 ||
        gCount >= _countof(gHooks) || !CodeAddress(site, length)) return false;
    for (unsigned i = 0; i < gCount; ++i)
        if (site < gHooks[i].site + gHooks[i].length && gHooks[i].site < site + length) return false;
    Hook& hook = gHooks[gCount++];
    hook.site = site;
    memcpy(hook.expected, expected, length);
    hook.length = length;
    hook.handler = handler;
    hook.name = name;
    return true;
}

DWORD Install() {
    if (gInitialized) return 1;
    wchar_t path[MAX_PATH];
    if (!FilePath(L"RA2NativeUI.ini", path)) return 0;
    for (unsigned i = 0; i < _countof(gOptions); ++i) {
        wchar_t key[64];
        MultiByteToWideChar(CP_ACP, 0, gOptions[i].key, -1, key, _countof(key));
        gOptions[i].value = !strcmp(gOptions[i].key, "KeepGameSpeed") ?
            ReadKeepGameSpeed(path) : GetPrivateProfileIntW(L"UI", key, 1, path) != 0;
    }
    if (!Enabled("Enabled")) { Log("UI disabled in settings; original engine unchanged."); return 1; }
    if (!FilePath(L"game.exe", path)) return 0;
    char hash[65];
    if (!Sha256(path, hash) || strcmp(hash, "73288C03B58D370BE268CA6D156B4E33BFDB2066DC980359467D8852FF3B00DF")) {
        Log("Refused unsupported original RA2 executable; no UI hooks installed."); return 0;
    }
    BYTE* base = reinterpret_cast<BYTE*>(GetModuleHandleW(NULL));
    if (reinterpret_cast<DWORD>(base) != 0x400000) { Log("Unexpected engine base address."); return 0; }
    const IMAGE_DOS_HEADER* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    const IMAGE_NT_HEADERS* pe = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || pe->Signature != IMAGE_NT_SIGNATURE ||
        pe->FileHeader.Machine != IMAGE_FILE_MACHINE_I386) return 0;
    gCodeBegin = 0x400000 + pe->OptionalHeader.BaseOfCode;
    gCodeEnd = gCodeBegin + pe->OptionalHeader.SizeOfCode;
    Log("Native original-engine UI 0.6.0: verified SHA256 %s.", hash);
    Log("Smart Skirmish settings 0.1.1: KeepGameSpeed=%s.", Enabled("KeepGameSpeed") ? "enabled" : "disabled");
    Log("In-game features 0.2.0: NextIdleHarvester=%d QuickSave=%d GameClock=%d.",
        Enabled("NextIdleHarvester"), Enabled("QuickSave"), Enabled("GameClock"));
    if (!InstallTooltips() || !InstallEconomy() ||
        (Enabled("PlacementPreview") && !InstallPlacement()) || !InstallSessionSettings() ||
        !InstallCommands() || !InstallGameClock() || !InstallHealthBars() || !InstallRangeCircles() || !Commit()) {
        Log("UI installation refused; original UI retained."); return 0;
    }
    gInitialized = true;
    Log("Native UI initialization complete (%u hooks).", gCount);
    return 1;
}

#ifdef NATIVE_UI_TEST
void TestRegion(void* begin, DWORD length) {
    gCodeBegin = reinterpret_cast<DWORD>(begin); gCodeEnd = gCodeBegin + length;
}
bool TestCommit() { return Commit(); }
void TestFailWrite(int index) { gFailWrite = index; gWriteCount = 0; }
void TestFailAfterWrite(int index) { gFailAfterWrite = index; gWriteCount = 0; }
bool TestSha(const wchar_t* path, char (&hex)[65]) { return Sha256(path, hex); }
bool TestReadKeepGameSpeed(const wchar_t* path) { return ReadKeepGameSpeed(path); }
bool TestOption(const char* key, bool value) {
    for (unsigned i = 0; i < _countof(gOptions); ++i)
        if (!strcmp(gOptions[i].key, key)) { gOptions[i].value = value; return true; }
    return false;
}
unsigned TestHookCount() { return gCount; }
void TestReset() {
    for (unsigned i = 0; i < gCount; ++i) if (gHooks[i].written) {
        WriteCode(gHooks[i].site, gHooks[i].expected, gHooks[i].length, true, gHooks[i].originalProtection);
        gHooks[i].written = false;
    }
    ReleaseQueued(); memset(gHooks, 0, sizeof(gHooks)); gCount = 0;
    gInitialized = false; TestFailWrite(-1); TestFailAfterWrite(-1);
}
#endif
} // namespace NativeUI

#ifndef NATIVE_UI_TEST
extern "C" __declspec(dllexport) DWORD __cdecl RA2NativeUI_Install() { return NativeUI::Install(); }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
#endif
