// Deferred sound proxy loader adapted from gitTerebi/ra2-enhancements (MIT).
// Copyright (c) 2026 gitTerebi. See ../LICENSE.txt.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <intrin.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include "DSoundForward.h"

namespace {
typedef DWORD (WINAPI *ThreadIdProc)();
typedef DWORD (__cdecl *InstallProc)();

static ThreadIdProc realThreadId = nullptr;
static void** threadIdSlot = nullptr;
static volatile LONG installStarted = 0;
static BYTE* exeBase = nullptr;
static DWORD exeSize = 0;
static wchar_t payloadPath[32768] = {};
static wchar_t settingsPath[32768] = {};
static wchar_t logPath[32768] = {};

bool InImage(DWORD rva, size_t length) {
    return rva < exeSize && length <= exeSize - rva;
}

bool CopySibling(wchar_t* destination, size_t capacity, const wchar_t* original,
    size_t prefix, const wchar_t* filename) {
    const size_t suffix = wcslen(filename);
    if (prefix + suffix >= capacity) return false;
    memcpy(destination, original, prefix * sizeof(wchar_t));
    memcpy(destination + prefix, filename, (suffix + 1) * sizeof(wchar_t));
    return true;
}

bool PrepareHost() {
    exeBase = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (exeBase != reinterpret_cast<BYTE*>(0x400000)) return false;
    wchar_t filename[32768];
    const DWORD length = GetModuleFileNameW(nullptr, filename, _countof(filename));
    if (!length || length >= _countof(filename)) return false;
    const wchar_t* slash = wcsrchr(filename, L'\\');
    if (!slash || _wcsicmp(slash + 1, L"game.exe") != 0) return false;
    const IMAGE_DOS_HEADER* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(exeBase);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 64
        || dos->e_lfanew > 4096) return false;
    const IMAGE_NT_HEADERS32* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(exeBase + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC
        || nt->OptionalHeader.ImageBase != 0x400000
        || nt->OptionalHeader.SizeOfImage < 0x3A2000
        || nt->OptionalHeader.SizeOfImage > 0x1000000) return false;
    exeSize = nt->OptionalHeader.SizeOfImage;
    // Verified WinMain trigger, separate from the earlier CRT thread-ID calls:
    // 0x68E2B2 FF 15 C4 A1 79 00 = call [GetCurrentThreadId IAT], return 0x68E2B8.
    // CRT entry reaches WinMain0x68E280 through its call at 0x785B7B.
    const BYTE call[] = {0xFF,0x15,0xC4,0xA1,0x79,0x00};
    if (memcmp(exeBase + 0x28E2B2, call, sizeof(call)) != 0) return false;
    const size_t prefix = slash + 1 - filename;
    return CopySibling(payloadPath, _countof(payloadPath), filename, prefix, L"RA2NativeUI.dll")
        && CopySibling(settingsPath, _countof(settingsPath), filename, prefix, L"RA2NativeUI.ini")
        && CopySibling(logPath, _countof(logPath), filename, prefix, L"RA2NativeUI-loader.log");
}

bool NameAt(DWORD rva, const char* expected, bool ignoreCase) {
    const size_t length = strlen(expected) + 1;
    if (!InImage(rva, length)) return false;
    const char* value = reinterpret_cast<const char*>(exeBase + rva);
    return ignoreCase ? _strnicmp(value, expected, length) == 0
        : memcmp(value, expected, length) == 0;
}

void** FindThreadIdSlot() {
    const IMAGE_DOS_HEADER* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(exeBase);
    const IMAGE_NT_HEADERS32* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(exeBase + dos->e_lfanew);
    const IMAGE_DATA_DIRECTORY imports = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!imports.VirtualAddress || !InImage(imports.VirtualAddress, imports.Size)) return nullptr;
    const unsigned int count = imports.Size / sizeof(IMAGE_IMPORT_DESCRIPTOR);
    for (unsigned int i = 0; i < count; ++i) {
        const IMAGE_IMPORT_DESCRIPTOR* descriptor = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(
            exeBase + imports.VirtualAddress + i * sizeof(IMAGE_IMPORT_DESCRIPTOR));
        if (!descriptor->Name) break;
        if (!NameAt(descriptor->Name, "kernel32.dll", true)
            || !descriptor->OriginalFirstThunk || !descriptor->FirstThunk) continue;
        for (unsigned int j = 0; j < 4096; ++j) {
            const DWORD nameRva = descriptor->OriginalFirstThunk + j * 4;
            const DWORD slotRva = descriptor->FirstThunk + j * 4;
            if (!InImage(nameRva, 4) || !InImage(slotRva, 4)) break;
            const DWORD name = *reinterpret_cast<const DWORD*>(exeBase + nameRva);
            if (!name) break;
            if (IMAGE_SNAP_BY_ORDINAL32(name) || name > exeSize - 2) continue;
            if (NameAt(name + 2, "GetCurrentThreadId", false))
                return reinterpret_cast<void**>(exeBase + slotRva);
        }
    }
    return nullptr;
}

bool ReplaceSlot(void* expected, void* replacement) {
    if (!threadIdSlot || (reinterpret_cast<ULONG_PTR>(threadIdSlot) & 3)) return false;
    DWORD previousProtection = 0;
    if (!VirtualProtect(threadIdSlot, sizeof(void*), PAGE_READWRITE, &previousProtection)) return false;
    void* previous = InterlockedCompareExchangePointer(
        reinterpret_cast<void* volatile*>(threadIdSlot), replacement, expected);
    DWORD ignored = 0;
    const BOOL protectedAgain = VirtualProtect(threadIdSlot, sizeof(void*), previousProtection, &ignored);
    return previous == expected && protectedAgain;
}

void LoadPayload() {
    DSoundEnsureReal();
    if (!DSoundForward::ready) {
        LoaderLog("Native UI skipped because the system sound proxy did not initialize", 0);
        return;
    }
    if (GetPrivateProfileIntW(L"UI", L"Enabled", 1, settingsPath) == 0) {
        LoaderLog("Native UI disabled in settings; original game continues", 0);
        return;
    }
    HMODULE payload = LoadLibraryExW(payloadPath, nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!payload) {
        LoaderLog("Native UI payload unavailable; original game continues", GetLastError());
        return;
    }
    InstallProc install = reinterpret_cast<InstallProc>(GetProcAddress(payload, "RA2NativeUI_Install"));
    if (!install) {
        LoaderLog("Native UI installer export missing; original game continues", GetLastError());
        FreeLibrary(payload);
        return;
    }
    // Do not unload after an installer call: this also avoids dangling callback
    // pointers if a future installer returns failure after doing partial work.
    DWORD result = 0;
    __try { result = install(); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        LoaderLog("Native UI installer raised an exception", GetExceptionCode());
        return;
    }
    LoaderLog(result ? "Native UI installed" : "Native UI refused installation; original game continues", result);
}

DWORD WINAPI FirstThreadIdCall() {
    // Earlier CRT and non-executable callers only forward. WinMain is reached
    // from the EXE's CRT entry point, after Windows finishes loader callbacks.
    const void* caller = _ReturnAddress();
    if (caller == reinterpret_cast<void*>(0x68E2B8)
        && InterlockedCompareExchange(&installStarted, 1, 0) == 0) {
        // Restore before any logging, loading or sidecar code can recurse.
        if (ReplaceSlot(reinterpret_cast<void*>(FirstThreadIdCall),
            reinterpret_cast<void*>(realThreadId))) {
            LoaderLog("Verified original RA2 WinMain reached; temporary IAT hook restored", 0);
            LoadPayload();
        } else {
            LoaderLog("Temporary IAT hook could not be restored; native UI skipped", GetLastError());
        }
    }
    return realThreadId();
}

void ArmDeferredLoad() {
    threadIdSlot = FindThreadIdSlot();
    if (threadIdSlot != reinterpret_cast<void**>(0x79A1C4) || !*threadIdSlot) {
        threadIdSlot = nullptr;
        return;
    }
    realThreadId = reinterpret_cast<ThreadIdProc>(*threadIdSlot);
    ReplaceSlot(reinterpret_cast<void*>(realThreadId), reinterpret_cast<void*>(FirstThreadIdCall));
}
}

static void LoaderLog(const char* message, DWORD detail) {
    if (!logPath[0]) return;
    // Only one writer may inspect/append at once; competing log writes skip.
    HANDLE file = CreateFileW(logPath, FILE_APPEND_DATA | GENERIC_READ,
        FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER size = {};
    if (GetFileSizeEx(file, &size) && size.QuadPart < 65536 - 512) {
        char line[512];
        const int length = _snprintf_s(line, sizeof(line), _TRUNCATE,
            "%s (0x%08lX)\r\n", message, detail);
        if (length > 0) {
            DWORD written = 0;
            WriteFile(file, line, static_cast<DWORD>(length), &written, nullptr);
        }
    }
    CloseHandle(file);
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        // No LoadLibrary, logging, INI reads, payload calls or worker threads
        // under the loader lock. Real sound is resolved lazily by its stubs.
        if (PrepareHost()) ArmDeferredLoad();
    } else if (reason == DLL_PROCESS_DETACH && !reserved && threadIdSlot && realThreadId) {
        // Imported dsound normally stays loaded for the process lifetime. If
        // explicitly unloaded early, do not leave an IAT pointer into it.
        ReplaceSlot(reinterpret_cast<void*>(FirstThreadIdCall), reinterpret_cast<void*>(realThreadId));
    }
    return TRUE;
}
