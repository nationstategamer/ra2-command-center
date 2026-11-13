#pragma once
// Sound forwarding adapted from gitTerebi/ra2-enhancements (MIT).
// Copyright (c) 2026 gitTerebi. See ../LICENSE.txt.
#include <windows.h>
#include <stdlib.h>
#include <string.h>

#if !defined(_M_IX86)
#error This proxy must be compiled for 32-bit x86.
#endif

#pragma comment(linker, "/export:DirectSoundCreate=_DsFn0,@1")
#pragma comment(linker, "/export:DirectSoundEnumerateA=_DsFn1,@2")
#pragma comment(linker, "/export:DirectSoundEnumerateW=_DsFn2,@3")
#pragma comment(linker, "/export:DllCanUnloadNow=_DsFn3,@4,PRIVATE")
#pragma comment(linker, "/export:DllGetClassObject=_DsFn4,@5,PRIVATE")
#pragma comment(linker, "/export:DirectSoundCaptureCreate=_DsFn5,@6")
#pragma comment(linker, "/export:DirectSoundCaptureEnumerateA=_DsFn6,@7")
#pragma comment(linker, "/export:DirectSoundCaptureEnumerateW=_DsFn7,@8")
#pragma comment(linker, "/export:GetDeviceID=_DsFn8,@9")
#pragma comment(linker, "/export:DirectSoundFullDuplexCreate=_DsFn9,@10")
#pragma comment(linker, "/export:DirectSoundCreate8=_DsFn10,@11")
#pragma comment(linker, "/export:DirectSoundCaptureCreate8=_DsFn11,@12")

static void LoaderLog(const char* message, DWORD detail);

namespace DSoundForward {
static const char* const names[12] = {
    "DirectSoundCreate", "DirectSoundEnumerateA", "DirectSoundEnumerateW",
    "DllCanUnloadNow", "DllGetClassObject", "DirectSoundCaptureCreate",
    "DirectSoundCaptureEnumerateA", "DirectSoundCaptureEnumerateW", "GetDeviceID",
    "DirectSoundFullDuplexCreate", "DirectSoundCreate8", "DirectSoundCaptureCreate8"
};
// Correct stdcall stack cleanup even if the Windows library cannot be loaded.
// No export can ever jump through a null address.
static HRESULT WINAPI Fail0() { return S_FALSE; }
static HRESULT WINAPI Fail2(void*, void*) { return E_NOTIMPL; }
static HRESULT WINAPI Fail3(void*, void*, void*) { return E_NOTIMPL; }
static HRESULT WINAPI Fail10(void*, void*, void*, void*, void*, DWORD,
    void*, void*, void*, void*) { return E_NOTIMPL; }
static INIT_ONCE once = INIT_ONCE_STATIC_INIT;
static HMODULE module = nullptr;
static bool ready = false;
}

static FARPROC g_dsFn[12] = {
    reinterpret_cast<FARPROC>(DSoundForward::Fail3),
    reinterpret_cast<FARPROC>(DSoundForward::Fail2),
    reinterpret_cast<FARPROC>(DSoundForward::Fail2),
    reinterpret_cast<FARPROC>(DSoundForward::Fail0),
    reinterpret_cast<FARPROC>(DSoundForward::Fail3),
    reinterpret_cast<FARPROC>(DSoundForward::Fail3),
    reinterpret_cast<FARPROC>(DSoundForward::Fail2),
    reinterpret_cast<FARPROC>(DSoundForward::Fail2),
    reinterpret_cast<FARPROC>(DSoundForward::Fail2),
    reinterpret_cast<FARPROC>(DSoundForward::Fail10),
    reinterpret_cast<FARPROC>(DSoundForward::Fail3),
    reinterpret_cast<FARPROC>(DSoundForward::Fail3)
};

static BOOL CALLBACK DSoundInitialize(PINIT_ONCE, PVOID, PVOID*) {
    wchar_t path[MAX_PATH];
    const UINT length = GetSystemDirectoryW(path, MAX_PATH);
    const wchar_t suffix[] = L"\\dsound.dll";
    if (!length || length >= MAX_PATH - _countof(suffix)) {
        LoaderLog("System sound directory unavailable", GetLastError());
        return TRUE; // A failed attempt keeps the safe failure targets.
    }
    memcpy(path + length, suffix, sizeof(suffix));
    HMODULE real = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!real) {
        LoaderLog("System sound library failed to load", GetLastError());
        return TRUE;
    }
    FARPROC resolved[12] = {};
    for (unsigned int i = 0; i != _countof(resolved); ++i) {
        resolved[i] = GetProcAddress(real, DSoundForward::names[i]);
        if (!resolved[i]) {
            LoaderLog("System sound export missing; proxy uses safe failure targets", i + 1);
            FreeLibrary(real);
            return TRUE;
        }
    }
    // InitOnce publishes all resolved entries before any stub proceeds.
    memcpy(g_dsFn, resolved, sizeof(g_dsFn));
    DSoundForward::module = real; // Retained for the entire process lifetime.
    DSoundForward::ready = true;
    return TRUE;
}

extern "C" void __cdecl DSoundEnsureReal() {
    InitOnceExecuteOnce(&DSoundForward::once, DSoundInitialize, nullptr, nullptr);
}

// Preserve the caller's registers/flags and original arguments, then tail-call
// the genuine export. InitOnce runs only on first use, never from our DllMain.
#define DSOUND_STUB(slot) \
    extern "C" __declspec(naked) void DsFn##slot() { \
        __asm { pushfd } \
        __asm { pushad } \
        __asm { call DSoundEnsureReal } \
        __asm { popad } \
        __asm { popfd } \
        __asm { jmp dword ptr [g_dsFn + slot * 4] } \
    }
DSOUND_STUB(0)
DSOUND_STUB(1)
DSOUND_STUB(2)
DSOUND_STUB(3)
DSOUND_STUB(4)
DSOUND_STUB(5)
DSOUND_STUB(6)
DSOUND_STUB(7)
DSOUND_STUB(8)
DSOUND_STUB(9)
DSOUND_STUB(10)
DSOUND_STUB(11)
#undef DSOUND_STUB
