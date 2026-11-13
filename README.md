# Red Alert 2 Command Center

An unofficial launcher and in-game enhancement pack for the **Steam release of Command & Conquer: Red Alert 2 and Yuri's Revenge**.

It contains no game files. It works with your own installation and never changes the game's own executables or archives.

> **Experimental fan project.** It is not made, supported or endorsed by Electronic Arts. Back up anything you care about. Please report problems on the [issue tracker](../../issues).

## What you get

### Command Center (both games)
A settings window for both games, so you no longer have to edit INI files:

- **Display profiles:** Classic 640×480, Readable 1280×720, Balanced 1600×900 or Wide view 1920×1080. Also borderless or exclusive mode, and crisp or smooth scaling, through the game's bundled DDrawCompat wrapper.
- **Audio and gameplay:** volumes, music on/off, soundtrack shuffle and repeat, scroll speed, intro movie.
- **Backup and restore:** a verified backup of your settings is taken before every change, with one-click undo and restore.
- **Skirmish presets:** save your skirmish setup (map, opponents, credits, options) and switch between setups.
- **Saves:** lists saves with their in-game names, and lets you rename them or move them to a recoverable "deleted" folder.

### Enhanced Red Alert 2 (optional, made by Command Center)
A separate copy of Red Alert 2 on the **original engine**, with its original units, rules and artwork. Your normal game and saves are untouched. It adds:

| Feature | |
| --- | --- |
| Sidebar tooltips | Cost, power and build time |
| Placement preview | Translucent building preview while placing |
| Counters | Working/total harvesters (H) and power balance (P) |
| Next idle harvester | Press **I** |
| Quick save | Press **Ctrl+S** (slot `SAVEFFFF.SAV`) |
| Autosave | Every 5, 10, 15 or 30 minutes of play, 3 rotating slots |
| Game clock and match stats | Elapsed game time plus a "Kills · Lost · Army $" line |
| Health bars | On damaged units, or on everything |
| Range circles | On your selected defenses |
| Game speed fix | Your chosen skirmish speed is kept after loading a save |
| Smarter skirmish AI (switchable) | 8 extra conditional teams for Medium/Brutal opponents: anti-air reserves, armour responses, mixed assaults, siege groups. No cheats or bonuses. |

Every feature can be switched on or off on Command Center's **UI & AI** tab.

## Requirements

- Windows 10 or 11. Command Center uses the .NET Framework 4.x that comes with Windows.
- **The Steam release** of Red Alert 2 / Yuri's Revenge (the EA bundle), launched once from Steam so its first-time setup has run.
  - Command Center needs the DDrawCompat graphics wrapper that this release ships with.
  - Enhanced Red Alert 2 also checks `game.exe`'s SHA-256 and only installs on that exact version.
  - Other releases (CD, Origin, The First Decade) are not supported.

## Install

1. Download `RA2CommandCenter-<version>.zip` from [Releases](../../releases).
2. In Steam, right-click **Command & Conquer: Red Alert 2** → **Manage** → **Browse local files**.
3. Extract the zip there. You should then have an `RA2CommandCenter` folder **next to `game.exe` and `gamemd.exe`**.
4. Run `RA2CommandCenter\RA2CommandCenter.exe`. On first start it backs up your current settings.
5. For the enhanced edition, click **Enhanced Red Alert 2** in the sidebar. Command Center offers to set it up:
   - It makes `RA2CommandCenter\SmartRA2`, a separate copy of the game.
   - On the same drive, the large archives are shared with your game through hard links, so it needs only a few MB. Otherwise it needs about 3 GB.
   - It builds the smarter AI file from your own game data and checks it against a fixed SHA-256.
   - It takes a few seconds.

**Uninstall:** delete the `RA2CommandCenter` folder. Use **Restore original settings** first if you want your old settings back. Your game files are never changed, so nothing else needs undoing.

**Windows SmartScreen** may warn about an unknown publisher, because the files are not code-signed. The full source is here, and the release notes list each file's SHA-256 so you can check what you downloaded.

## Status

| Area | Tested |
| --- | --- |
| Command Center settings, backup/restore | Automated tests, plus play-tested at all four display profiles in both games |
| Tooltips, placement preview, counters, speed fix | Play-tested in skirmish |
| Next idle harvester, quick save, clock, health bars (default mode) | Play-tested in skirmish |
| Autosave, range circles, match stats, renaming saves, music options | Automated tests only, **not yet play-tested** |
| Smarter AI | Loads in-game (all 8 new team types registered, Easy control match played). Not yet seen forming teams against Medium/Brutal opponents, and whether it plays better needs real matches. |
| Campaign, multiplayer, every faction/unit | Not tested |

## How it works

- **Command Center** is a WPF app (C#, .NET Framework 4). It edits only `RA2.INI`, `RA2MD.INI` and `DDrawCompat.ini`, and keeps unknown entries, comments and encodings intact.
- **The in-game extension** (`RA2NativeUI.dll`) is loaded by a small `dsound.dll` proxy, which forwards every DirectSound call to Windows.
  - The extension patches the running game in memory only.
  - It first checks the exact `game.exe` SHA-256, then the bytes at every hook site, and rolls back fully if any check fails.
  - A handler that faults is disabled for the session and logged to `RA2NativeUI.log`.
- **The smarter AI** is shipped only as additions ([`ai/ai-additions.json`](ai/ai-additions.json)): 4 text insertions into the stock `ai.ini`.
  - Command Center reads the stock file from your `ra2.mix` (its own MIX/Blowfish reader is in [`EditionSetup.cs`](launcher/src/EditionSetup.cs)).
  - It checks the input and the result against fixed hashes, then writes the result as the edition's `ai.ini`.
  - Turning the AI off renames it to `ai.ini.off`.
- Every engine address used is documented with its evidence in [`research/evidence.md`](research/evidence.md).

## Repository layout

| Folder | Contents |
| --- | --- |
| `launcher/` | Command Center source, tests, `build.ps1` / `test.ps1` |
| `native/` | In-game extension and loader source, native tests, `build.ps1` |
| `ai/` | Smarter AI additions, design notes and generator scripts |
| `package/` | Default settings and README copied into the enhanced edition |
| `research/` | Address evidence and the analysis scripts used to find it |
| `release.ps1` | Builds everything and assembles the release zip |

## Building

- **Launcher:** `launcher\build.ps1` uses the C# compiler included with Windows. `launcher\test.ps1` runs the tests.
  - Most tests use fake installations.
  - Set `RA2_GAME_ROOT` to your game folder to also run the read-only checks against your real game.
- **Native:** `native\build.ps1` needs Visual Studio 2019 or later (or Build Tools) with the C++ workload. It builds 32-bit x86 and runs 97 native checks.
- **Release:** `release.ps1 -Version 1.0.0` builds both and writes `dist\RA2CommandCenter-1.0.0.zip`.

## Licence

- This project is licensed under the **GNU LGPL v3.0** ([`LICENSE`](LICENSE), with the GPL v3.0 text it builds on in [`COPYING`](COPYING)).
- Parts of the in-game extension follow the behaviour of [Phobos](https://github.com/Phobos-developers/Phobos) (LGPL-3.0).
- The DirectSound loader adapts [gitTerebi/ra2-enhancements](https://github.com/gitTerebi/ra2-enhancements) (MIT).
- See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

Command & Conquer and Red Alert are trademarks of Electronic Arts Inc. This repository contains no game files, art, audio or game executables.
