#include "NativeUI.h"
#include <string.h>

namespace NativeUI {
namespace {
struct Point { int x, y; };
struct Coord { int x, y, z; };
struct Rect { int x, y, width, height; };
struct Cell { short x, y; };

// These are original RA2 routines. The shape renderer has no YR TintColor
// argument: two fastcall registers plus thirteen stack arguments, ret 0x34.
typedef void* (__thiscall *LoadShapeFn)(void*);
typedef int (__thiscall *FloorHeightFn)(void*, const Point*);
typedef Point* (__thiscall *ToScreenFn)(void*, Point*, const Coord*);
typedef void* (__thiscall *DrawerFn)(void*);
typedef void (__fastcall *DrawShapeFn)(void*, void*, void*, int,
    const Point*, const Rect*, unsigned int, int, int, int, int,
    void*, int, int, int);

bool previewFaulted = false;
bool previewLogged = false;

template<class T> T Read(DWORD address) {
    return *reinterpret_cast<const T*>(address);
}

bool Match(DWORD address, const BYTE* bytes, size_t count) {
    return Readable(reinterpret_cast<void*>(address), count)
        && memcmp(reinterpret_cast<void*>(address), bytes, count) == 0;
}

bool ValidShape(void* shape, int frame) {
    if (!Readable(shape, 8)) return false;
    const DWORD address = reinterpret_cast<DWORD>(shape);
    const unsigned short type = Read<unsigned short>(address);
    const short width = Read<short>(address + 2);
    const short height = Read<short>(address + 4);
    const short frames = Read<short>(address + 6);
    // Native RA2 expects a real SHP allocation, not a YR SHPReference.
    if (type == 0xFFFF || width <= 0 || width > 4096 || height <= 0
        || height > 4096 || frames <= 0 || frames > 4096
        || frame < 0 || frame >= frames) return false;
    const DWORD header = address + 8 + frame * 24;
    if (!Readable(reinterpret_cast<void*>(header), 24)) return false;
    const DWORD dataOffset = Read<DWORD>(header + 20);
    if (dataOffset == 0 || dataOffset > 64 * 1024 * 1024) return false;
    return Readable(reinterpret_cast<void*>(address + dataOffset), 1);
}

void DrawPreview() {
    const DWORD building = Read<DWORD>(0x833680);
    const DWORD type = Read<DWORD>(0x833684);
    if (!Readable(reinterpret_cast<void*>(building), 0x41C)
        || !Readable(reinterpret_cast<void*>(type), 0x1418)) return;
    // Reject non-buildings and unexpected layouts before invoking a game method.
    if (Read<DWORD>(building) != 0x79CBBC || Read<DWORD>(type) != 0x79D1F8
        || Read<DWORD>(building + 0x418) != type) return;
    const DWORD owner = Read<DWORD>(building + 0x1B4);
    if (!Readable(reinterpret_cast<void*>(owner), 0x15F44)) return;
    const int schemeIndex = Read<int>(owner + 0x15F40);
    const DWORD schemes = Read<DWORD>(0xAB6EFC);
    const int schemeCount = Read<int>(0xAB6F08);
    if (schemeIndex < 0 || schemeIndex >= schemeCount || schemeCount > 256
        || !Readable(reinterpret_cast<void*>(schemes + schemeIndex * 4), 4)) return;
    const DWORD scheme = Read<DWORD>(schemes + schemeIndex * 4);
    if (!Readable(reinterpret_cast<void*>(scheme), 0x310)) return;

    const Cell center = Read<Cell>(0x833650);
    const Cell offset = Read<Cell>(0x833654);
    const int cellX = static_cast<int>(center.x) + offset.x;
    const int cellY = static_cast<int>(center.y) + offset.y;
    if (cellX < 0 || cellX >= 512 || cellY < 0 || cellY >= 512) return;
    const DWORD cells = Read<DWORD>(0x832618); // Map singleton + 0x138.
    const DWORD cellSlot = cells + (cellY * 512 + cellX) * 4;
    if (!Readable(reinterpret_cast<void*>(cellSlot), 4)) return;
    const DWORD cell = Read<DWORD>(cellSlot);
    if (!Readable(reinterpret_cast<void*>(cell), 0x130)) return;
    const Cell storedCell = Read<Cell>(cell + 0x24);
    if (storedCell.x != cellX || storedCell.y != cellY) return;

    const DWORD tactical = Read<DWORD>(0x83996C);
    const DWORD surface = Read<DWORD>(0x83995C);
    if (!Readable(reinterpret_cast<void*>(tactical), 0xB8)
        || !Readable(reinterpret_cast<void*>(surface), 12)) return;
    const int surfaceWidth = Read<int>(surface + 4);
    const int surfaceHeight = Read<int>(surface + 8);
    if (surfaceWidth < 320 || surfaceWidth > 16384
        || surfaceHeight < 200 || surfaceHeight > 16384) return;
    Rect bounds = Read<Rect>(0x839630);
    if (bounds.x < 0 || bounds.y < 0 || bounds.x >= surfaceWidth
        || bounds.y >= surfaceHeight - 32 || bounds.width <= 0
        || bounds.height <= 0 || bounds.width > 16384 || bounds.height > 16384) return;
    if (bounds.width > surfaceWidth - bounds.x) bounds.width = surfaceWidth - bounds.x;
    if (bounds.height > surfaceHeight - 32 - bounds.y)
        bounds.height = surfaceHeight - 32 - bounds.y;

    void* image = reinterpret_cast<LoadShapeFn>(0x45E790)(reinterpret_cast<void*>(type));
    int frame = 0;
    if (image && Readable(image, 8)) {
        frame = Read<short>(reinterpret_cast<DWORD>(image) + 6) / 2 - 1;
    } else {
        image = reinterpret_cast<LoadShapeFn>(0x458CE0)(reinterpret_cast<void*>(type));
    }
    if (!ValidShape(image, frame)) return;

    const Point subcoords = {0, 0};
    const int floor = reinterpret_cast<FloorHeightFn>(0x4706C0)(
        reinterpret_cast<void*>(cell), &subcoords);
    if (floor < -32768 || floor > 32767) return;
    const Coord position = {cellX * 256 + 128, cellY * 256 + 128, floor + 1};
    Point screen = {0, 0};
    reinterpret_cast<ToScreenFn>(0x6A27A0)(reinterpret_cast<void*>(tactical), &screen, &position);
    screen.x -= Read<int>(tactical + 0xB0);
    screen.y -= Read<int>(tactical + 0xB4) + 15;
    void* palette = reinterpret_cast<DrawerFn>(0x6D1480)(reinterpret_cast<void*>(building));
    if (!Readable(palette, 0x180)) return;
    // Centered | Nonzero | MultiPass | TransLucent50. Reuse original art and
    // the building's own remap palette; leave placement legality/grid untouched.
    reinterpret_cast<DrawShapeFn>(0x49EEA0)(reinterpret_cast<void*>(surface),
        palette, image, frame, &screen, &bounds, 0x20304,
        0, 0, 0, 1000, nullptr, 0, 0, 0);
    if (!previewLogged) {
        previewLogged = true;
        Log("Placement preview rendered with original RA2 buildup art.");
    }
}

DWORD __cdecl PlacementHook(Registers*) {
    if (!Enabled("PlacementPreview") || previewFaulted) return 0;
    __try { DrawPreview(); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        previewFaulted = true;
        Log("Placement preview disabled for this session after exception 0x%08lX.",
            GetExceptionCode());
    }
    return 0;
}
}

bool InstallPlacement() {
    const BYTE buildup[] = {0x81,0xEC,0xD0,0x01,0x00,0x00,0x56,0x8B,0xF1};
    const BYTE image[] = {0x81,0xEC,0xCC,0x01,0x00,0x00,0x56,0x8B,0xF1};
    const BYTE floor[] = {0x55,0x8B,0xEC,0x83,0xE4,0xF8,0x83,0xEC,0x0C};
    const BYTE screen[] = {0x83,0xEC,0x08,0x53,0x8B,0x5C,0x24,0x14,0x56,0x57};
    const BYTE drawer[] = {0x56,0x8B,0xF1,0x57,0x8B,0x06,0xFF,0x90,0x80,0x00,0x00,0x00};
    const BYTE shape[] = {0x83,0xEC,0x6C,0x53,0x8B,0x5C,0x24,0x74,0x85,0xDB};
    if (!Match(0x45E790,buildup,sizeof(buildup)) || !Match(0x458CE0,image,sizeof(image))
        || !Match(0x4706C0,floor,sizeof(floor)) || !Match(0x6A27A0,screen,sizeof(screen))
        || !Match(0x6D1480,drawer,sizeof(drawer)) || !Match(0x49EEA0,shape,sizeof(shape))) {
        Log("Placement preview refused: original RA2 rendering code did not match.");
        return false;
    }
    const BYTE expected[] = {0x8B,0x0D,0x84,0x36,0x83,0x00};
    return AddHook(0x6A58CA, expected, sizeof(expected), PlacementHook, "placement-preview");
}
}
