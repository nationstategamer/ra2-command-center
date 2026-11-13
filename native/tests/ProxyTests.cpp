#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <wchar.h>
int wmain() {
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(NULL, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - path), L"dsound.dll");
    HMODULE proxy = LoadLibraryW(path);
    if (!proxy) { printf("FAIL: load local sound proxy (%lu)\n", GetLastError()); return 1; }
    const char* names[] = {"DirectSoundCreate","DirectSoundEnumerateA","DirectSoundEnumerateW",
        "DllCanUnloadNow","DllGetClassObject","DirectSoundCaptureCreate","DirectSoundCaptureEnumerateA",
        "DirectSoundCaptureEnumerateW","GetDeviceID","DirectSoundFullDuplexCreate","DirectSoundCreate8","DirectSoundCaptureCreate8"};
    for (int i = 0; i < 12; ++i) if (!GetProcAddress(proxy, names[i]) ||
        GetProcAddress(proxy, names[i]) != GetProcAddress(proxy, MAKEINTRESOURCEA(i + 1))) {
        printf("FAIL: sound export %s\n", names[i]); return 1;
    }
    typedef HRESULT (WINAPI *CanUnload)();
    HRESULT proxied = reinterpret_cast<CanUnload>(GetProcAddress(proxy, "DllCanUnloadNow"))();
    GetSystemDirectoryW(path, MAX_PATH);
    wcscat_s(path, L"\\dsound.dll");
    HMODULE system = LoadLibraryW(path);
    if (!system) return 1;
    HRESULT direct = reinterpret_cast<CanUnload>(GetProcAddress(system, "DllCanUnloadNow"))();
    if (proxied != direct) { printf("FAIL: sound forwarding result differs\n"); return 1; }
    if (GetModuleHandleW(L"RA2NativeUI.dll")) { printf("FAIL: proxy loaded UI into a non-game host\n"); return 1; }
    printf("PASS: all twelve sound exports and ordinals, real system forwarding, and non-game host isolation\n");
    FreeLibrary(system); FreeLibrary(proxy);
    return 0;
}
