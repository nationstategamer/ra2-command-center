#include "NativeUI.h"
#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include <new>

// Keyboard commands for the original RA2 engine: Next Idle Harvester and Quick Save.
// They are ordinary engine CommandClass entries, so they appear in Options > Keyboard,
// can be rebound there, and are saved to KEYBOARD.INI by the game itself.
//
// Every address below is for game.exe SHA256 73288C03...FF3B00DF only (the framework
// refuses any other executable) and was traced to the stock code that uses it; see
// NativeRA2/analysis/features/evidence.md.
namespace NativeUI {
namespace {
// Command registration (stock "Init Commands", 0x5173C0) and KEYBOARD.INI loading.
const DWORD kRegisterSite = 0x519150;   // before the last stock command is added, then 0x519240 loads KEYBOARD.INI
const BYTE  kRegisterBytes[] = { 0x8D,0x44,0x24,0x08, 0xB9,0x58,0x23,0x83,0x00 };
const DWORD kDefaultsSite = 0x519164;   // directly after KEYBOARD.INI has been applied
const BYTE  kDefaultsBytes[] = { 0x8D,0x4C,0x24,0x14, 0xBE,0x2E,0x00,0x00,0x00 };
const DWORD kCommandArray = 0x832358;   // DynamicVectorClass<CommandClass*>
const DWORD kCommandAdd = 0x51E360;     // DynamicVectorClass::AddItem(const T&), thiscall, ret 4
const DWORD kHotkeys = 0x832380;        // IndexClass<WWKey, CommandClass*>: items, count, capacity, sorted
const DWORD kHotkeyAdd = 0x51E3B0;      // IndexClass::AddIndex(const key&, const value&), thiscall
// Main loop, at the stock campaign-autosave check (Phobos YR 0x55DBCD equivalent).
const DWORD kMainLoopSite = 0x540433;
const BYTE  kMainLoopBytes[] = { 0x39,0x3D,0x98,0xD2,0xA3,0x00 };
// Calls copied from the stock NextObject command's Execute (0x51B8B0).
const DWORD kMap = 0x8324E0;
const DWORD kSetSellMode = 0x49C980, kSetWaypointMode = 0x49C860, kSetRepairMode = 0x49CA20, kSetPowerMode = 0x49C7C0;
const DWORD kCurrentObjects = 0xA40C60; // DynamicVectorClass<ObjectClass*>: items +4, count +0x10
const DWORD kUnselectAll = 0x482BC0, kCenterMap = 0x49E3B0, kMarkRedraw = 0x4E09A0;
const DWORD kSelectSlot = 0x140;        // ObjectClass::Select vtable offset used by the stock command
// Messages, strings and saving.
const DWORD kMessageList = 0xA3DC70, kAddMessage = 0x5B4DA0;
const DWORD kLoadString = 0x6FA180;     // label -> wide string (stock CSF)
const DWORD kSaveGame = 0x6554A0;       // ScenarioClass::SaveGame(file, description, bargraph)
const DWORD kScenario = 0xA3D290, kSessionMode = 0xA3D298;
const DWORD kScenarioName = 0x940;      // wide scenario name, as used by the stock autosave
const DWORD kCurrentPlayer = 0xA35DB4, kHouseColorScheme = 0x15F40;
const int kModeCampaign = 0, kModeSkirmish = 5;
const int kKeyCtrl = 0x200, kKeyRelease = 0x800;
const char kQuickSaveFile[] = "SAVEFFFF.SAV";   // SAVE%04lX.SAV, the stock save-name pattern

template<class T> T Read(DWORD address) { return *reinterpret_cast<const T*>(address); }
bool Memory(DWORD address, size_t length) { return address && Readable(reinterpret_cast<const void*>(address), length); }

const wchar_t* Text(const char* label, const wchar_t* fallback) {
    typedef const wchar_t* (__fastcall *LoadFn)(const char*, void*);
    const wchar_t* value = reinterpret_cast<LoadFn>(kLoadString)(label, NULL);
    return value && Memory(reinterpret_cast<DWORD>(value), sizeof(wchar_t)) && *value ? value : fallback;
}

void Message(const wchar_t* text) {
    if (!text || !Memory(kMessageList, 4)) return;
    int scheme = 3;   // the stock debug-toggle command's scheme, if the player's is unavailable
    const DWORD house = Memory(kCurrentPlayer, 4) ? Read<DWORD>(kCurrentPlayer) : 0;
    if (Memory(house + kHouseColorScheme, 4)) scheme = Read<int>(house + kHouseColorScheme);
    typedef void* (__thiscall *AddFn)(void*, const wchar_t*, int, const wchar_t*, int, int, int, int);
    reinterpret_cast<AddFn>(kAddMessage)(reinterpret_cast<void*>(kMessageList), NULL, 0, text, scheme, 0x4046, 0x96, 0);
}

bool SinglePlayer() {
    if (!Memory(kSessionMode, 4)) return false;
    const int mode = Read<int>(kSessionMode);
    return mode == kModeCampaign || mode == kModeSkirmish;
}

// Same nine-slot layout as the engine's CommandClass (verified against stock vtables
// 0x7A43C4 and 0x7A3DFC): destructor, name, UI name, category, description, three key
// filters, Execute. The filter defaults reproduce the stock 0x51AE70/0x51AE80/0x51AE90.
class Command {
public:
    virtual ~Command() {}
    virtual const char* GetName() const = 0;
    virtual const wchar_t* GetUIName() const = 0;
    virtual const wchar_t* GetUICategory() const = 0;
    virtual const wchar_t* GetUIDescription() const = 0;
    virtual bool PreventCombinationOverride(int) const { return false; }
    virtual bool ExtraTriggerCondition(int key) const { return !(key & kKeyRelease); }
    virtual bool CheckLoop(int) const { return false; }
    virtual void Execute(int key) const = 0;
    virtual int DefaultKey() const = 0;   // extension slot, after the engine's nine
};

void RunNextIdleHarvester();
void RunQuickSave();

// The engine calls Execute directly. Faults are contained so a bad state can never
// take the game down; the command then stays disabled for the session.
volatile LONG gCommandFault = 0;
void Guarded(void (*body)(), const char* name) {
    if (gCommandFault) return;
    DWORD code = 0;
    __try { body(); }
    __except (code = GetExceptionCode(), EXCEPTION_EXECUTE_HANDLER) {
        InterlockedExchange(&gCommandFault, 1);
        Log("Disabled keyboard commands for this session after a fault in %s (exception=%08lX).", name, code);
    }
}

class NextIdleHarvesterCommand : public Command {
public:
    const char* GetName() const { return "NextIdleHarvester"; }
    const wchar_t* GetUIName() const { return L"Next Idle Harvester"; }
    const wchar_t* GetUICategory() const { return Text("TXT_SELECTION", L"Selection"); }
    const wchar_t* GetUIDescription() const { return L"Select the next harvester that is not mining or unloading, and center the view on it."; }
    void Execute(int) const { Guarded(RunNextIdleHarvester, "Next Idle Harvester"); }
    int DefaultKey() const { return 'I'; }
};

class QuickSaveCommand : public Command {
public:
    const char* GetName() const { return "QuickSave"; }
    const wchar_t* GetUIName() const { return L"Quick Save"; }
    const wchar_t* GetUICategory() const { return Text("TXT_INTERFACE", L"Interface"); }
    const wchar_t* GetUIDescription() const { return L"Save a campaign or skirmish game to the quick-save slot. Load it from the normal Load menu."; }
    void Execute(int) const { Guarded(RunQuickSave, "Quick Save"); }
    int DefaultKey() const { return kKeyCtrl | 'S'; }
};

Command* gCommands[2] = {};
unsigned gCommandCount = 0;
volatile LONG gQuickSavePending = 0;

void RunNextIdleHarvester() {
    DWORD units[256];
    bool idle[256];
    const int count = CollectHarvesters(units, idle, 256);
    DWORD current = 0;
    if (Memory(kCurrentObjects, 0x14) && Read<int>(kCurrentObjects + 0x10) > 0) {
        const DWORD items = Read<DWORD>(kCurrentObjects + 4);
        if (Memory(items, 4)) current = Read<DWORD>(items);
    }
    const int pick = NextIdleIndex(units, idle, count, current);
    if (pick < 0) { Message(L"No idle harvesters."); return; }
    const DWORD unit = units[pick];
    const DWORD vtable = Read<DWORD>(unit);
    if (!Memory(vtable + kSelectSlot, 4)) return;
    const DWORD select = Read<DWORD>(vtable + kSelectSlot);
    if (select < 0x401000 || select >= 0x780000) return;
    // Exactly the stock NextObject sequence, with an idle harvester as the target.
    void* map = reinterpret_cast<void*>(kMap);
    typedef void (__thiscall *Mode1)(void*, int);
    typedef void (__thiscall *Mode2)(void*, int, int);
    reinterpret_cast<Mode1>(kSetSellMode)(map, 0);
    reinterpret_cast<Mode2>(kSetWaypointMode)(map, 0, 0);
    reinterpret_cast<Mode1>(kSetRepairMode)(map, 0);
    reinterpret_cast<Mode1>(kSetPowerMode)(map, 0);
    reinterpret_cast<void (*)()>(kUnselectAll)();
    reinterpret_cast<bool (__thiscall *)(void*)>(select)(reinterpret_cast<void*>(unit));
    reinterpret_cast<void (__thiscall *)(void*)>(kCenterMap)(map);
    reinterpret_cast<void (__thiscall *)(void*, int)>(kMarkRedraw)(map, 1);
}

void RunQuickSave() {
    if (!SinglePlayer()) {
        Message(L"Quick Save is available in campaign and skirmish games.");
        return;
    }
    if (InterlockedExchange(&gQuickSavePending, 1) == 0)
        Message(Text("TXT_SAVING_GAME", L"Saving game..."));
}

DWORD __cdecl RegisterCommands(Registers*) {
    static bool done = false;
    if (done) return 0;
    done = true;
    typedef bool (__thiscall *AddFn)(void*, Command* const*);
    for (unsigned i = 0; i < gCommandCount; ++i) {
        if (!reinterpret_cast<AddFn>(kCommandAdd)(reinterpret_cast<void*>(kCommandArray), &gCommands[i]))
            Log("Could not register keyboard command %s.", gCommands[i]->GetName());
        else
            Log("Registered keyboard command %s.", gCommands[i]->GetName());
    }
    return 0;
}

// Give each new command its default key only when the player has not bound the
// command and the key is not already used by anything else.
DWORD __cdecl ApplyDefaultKeys(Registers*) {
    if (!Memory(kHotkeys, 0x10)) return 0;
    typedef bool (__thiscall *AddIndexFn)(void*, const int*, Command* const*);
    for (unsigned c = 0; c < gCommandCount; ++c) {
        const DWORD items = Read<DWORD>(kHotkeys);
        const int count = Read<int>(kHotkeys + 4);
        if (count < 0 || count > 4096 || (count && !Memory(items, static_cast<size_t>(count) * 8))) return 0;
        const int key = gCommands[c]->DefaultKey();
        bool bound = false, used = false;
        for (int i = 0; i < count; ++i) {
            if (Read<DWORD>(items + i * 8 + 4) == reinterpret_cast<DWORD>(gCommands[c])) bound = true;
            if ((Read<DWORD>(items + i * 8) & 0xFFFF) == static_cast<DWORD>(key)) used = true;
        }
        if (bound) { Log("%s keeps the key saved in KEYBOARD.INI.", gCommands[c]->GetName()); continue; }
        if (used) { Log("%s left unbound: its default key %#x is already in use.", gCommands[c]->GetName(), key); continue; }
        if (reinterpret_cast<AddIndexFn>(kHotkeyAdd)(reinterpret_cast<void*>(kHotkeys), &key, &gCommands[c]))
            Log("%s bound to default key %#x.", gCommands[c]->GetName(), key);
    }
    return 0;
}

const wchar_t* ScenarioName() {
    const DWORD scenario = Memory(kScenario, 4) ? Read<DWORD>(kScenario) : 0;
    if (!Memory(scenario + kScenarioName, 2 * 32)) return L"";
    const wchar_t* name = reinterpret_cast<const wchar_t*>(scenario + kScenarioName);
    return name[0] ? name : L"";
}

bool SaveTo(const char* file, const wchar_t* description) {
    typedef bool (__fastcall *SaveFn)(const char*, const wchar_t*, bool);
    return reinterpret_cast<SaveFn>(kSaveGame)(file, description, false);
}

void RunQuickSaveNow() {
    wchar_t description[40] = L"Quick Save";
    if (ScenarioName()[0]) swprintf_s(description, L"Quick Save - %.24ls", ScenarioName());
    const bool saved = SaveTo(kQuickSaveFile, description);
    Log("Quick Save to %s %s.", kQuickSaveFile, saved ? "succeeded" : "FAILED");
    Message(saved ? Text("TXT_GAME_WAS_SAVED", L"Game saved.") : Text("TXT_ERROR_SAVING_GAME", L"Error saving game!"));
}

// Autosave: three rotating slots, SAVEFFF0..2.SAV (the stock SAVE%04lX.SAV pattern), so
// they appear in the normal Load menu. The oldest slot is overwritten.
const DWORD kGameActive = 0xA40958;     // 1 while a game is in progress (see SessionSettings.cpp)
const DWORD kCurrentFrame = 0xA40D2C;
const DWORD kHouseDefeated = 0x13D;     // HouseClass defeated flag (see economy-evidence.txt)
bool gAutosave = false;
DWORD gAutosaveIntervalMs = 10 * 60 * 1000;
AutosaveClock gAutosaveClock = {};

void RunAutosave(int frame) {
    char files[3][16];
    FILETIME times[3] = {};
    bool exists[3] = {};
    wchar_t directory[MAX_PATH];
    const DWORD n = GetModuleFileNameW(NULL, directory, MAX_PATH);
    wchar_t* slash = n && n < MAX_PATH ? wcsrchr(directory, L'\\') : NULL;
    for (int i = 0; i < 3; ++i) {
        sprintf_s(files[i], "SAVEFFF%d.SAV", i);
        if (!slash) continue;
        wchar_t path[MAX_PATH];
        swprintf_s(path, L"%.*ls\\SAVEFFF%d.SAV", static_cast<int>(slash - directory), directory, i);
        WIN32_FILE_ATTRIBUTE_DATA data;
        if (GetFileAttributesExW(path, GetFileExInfoStandard, &data)) { exists[i] = true; times[i] = data.ftLastWriteTime; }
    }
    const int slot = PickAutosaveSlot(exists, times, 3);
    const int seconds = GameSeconds(frame);
    wchar_t description[40];
    swprintf_s(description, L"Autosave %d:%02d - %.20ls", seconds / 60, seconds % 60, ScenarioName()[0] ? ScenarioName() : L"Game");
    const bool saved = SaveTo(files[slot], description);
    Log("Autosave to %s %s.", files[slot], saved ? "succeeded" : "FAILED");
    Message(saved ? L"Game autosaved." : Text("TXT_ERROR_SAVING_GAME", L"Error saving game!"));
}

bool AutosaveAllowed() {
    if (!SinglePlayer() || !Memory(kGameActive, 1) || Read<BYTE>(kGameActive) != 1) return false;
    const DWORD house = Memory(kCurrentPlayer, 4) ? Read<DWORD>(kCurrentPlayer) : 0;
    return Memory(house + kHouseDefeated, 1) && !Read<BYTE>(house + kHouseDefeated);
}

// Runs where the stock game performs its own campaign autosave, between frames.
DWORD __cdecl MainLoopSavePoint(Registers*) {
    if (gQuickSavePending) {
        InterlockedExchange(&gQuickSavePending, 0);
        if (SinglePlayer()) RunQuickSaveNow();
    }
    if (gAutosave && Memory(kCurrentFrame, 4)) {
        const int frame = Read<int>(kCurrentFrame);
        if (AdvanceAutosave(gAutosaveClock, frame, GetTickCount(), gAutosaveIntervalMs) && AutosaveAllowed())
            RunAutosave(frame);
    }
    return 0;
}
} // namespace

// Counts real playing time: only the time between consecutive advancing game frames,
// ignoring gaps over five seconds (pauses, menus, the Load screen). A frame counter
// that goes backwards means a new game or a loaded save, which restarts the count.
bool AdvanceAutosave(AutosaveClock& clock, int frame, DWORD now, DWORD intervalMs) {
    if (!clock.started) {
        clock.started = true; clock.lastFrame = frame; clock.lastTick = now; clock.activeMs = 0;
        return false;
    }
    if (frame < clock.lastFrame) {
        clock.activeMs = 0; clock.lastTick = now;
    } else if (frame > clock.lastFrame) {
        const DWORD gap = now - clock.lastTick;
        if (gap < 5000) clock.activeMs += gap;
        clock.lastTick = now;
    }
    clock.lastFrame = frame;
    if (intervalMs && clock.activeMs >= intervalMs) { clock.activeMs = 0; return true; }
    return false;
}

int PickAutosaveSlot(const bool* exists, const FILETIME* times, int count) {
    int oldest = 0;
    for (int i = 0; i < count; ++i) {
        if (!exists[i]) return i;
        if (CompareFileTime(&times[i], &times[oldest]) < 0) oldest = i;
    }
    return oldest;
}

bool InstallCommands() {
    const bool harvester = Enabled("NextIdleHarvester"), quick = Enabled("QuickSave");
    gAutosave = Enabled("Autosave");
    int minutes = Setting("AutosaveMinutes", 10);
    if (minutes < 1 || minutes > 120) minutes = 10;
    gAutosaveIntervalMs = static_cast<DWORD>(minutes) * 60 * 1000;
    Log("Autosave %s (every %d minutes of play, slots SAVEFFF0-2.SAV).", gAutosave ? "enabled" : "disabled", minutes);
    if (harvester) gCommands[gCommandCount++] = new (std::nothrow) NextIdleHarvesterCommand();
    if (quick) gCommands[gCommandCount++] = new (std::nothrow) QuickSaveCommand();
    for (unsigned i = 0; i < gCommandCount; ++i) if (!gCommands[i]) return false;
    if (gCommandCount && !(AddHook(kRegisterSite, kRegisterBytes, sizeof kRegisterBytes, RegisterCommands, "Commands: register keyboard commands")
        && AddHook(kDefaultsSite, kDefaultsBytes, sizeof kDefaultsBytes, ApplyDefaultKeys, "Commands: default keys"))) return false;
    return !(quick || gAutosave) ||
        AddHook(kMainLoopSite, kMainLoopBytes, sizeof kMainLoopBytes, MainLoopSavePoint, "Main loop: quick save and autosave point");
}

#ifdef NATIVE_UI_TEST
// Exposes the command objects to FeatureTests so the vtable layout can be checked
// exactly as the engine calls it.
void* TestCommand(unsigned index) {
    static NextIdleHarvesterCommand harvester;
    static QuickSaveCommand quick;
    return index == 0 ? static_cast<void*>(static_cast<Command*>(&harvester))
         : index == 1 ? static_cast<void*>(static_cast<Command*>(&quick)) : NULL;
}
#endif
} // namespace NativeUI
