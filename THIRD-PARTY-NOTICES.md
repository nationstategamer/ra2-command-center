# Third-party notices

## Phobos (LGPL-3.0)
The in-game extension's tooltip, placement-preview and harvester/power counter behaviour follows [Phobos](https://github.com/Phobos-developers/Phobos) v0.4.0.2, copyright its contributors. Phobos is licensed under the GNU Lesser General Public License v3.0. The adaptation in `native/src` is provided under the same licence. See `LICENSE` and `COPYING`.

## gitTerebi/ra2-enhancements (MIT)
`native/src/Loader.cpp` and `native/src/DSoundForward.h` adapt the deferred DirectSound proxy and export stubs from [gitTerebi/ra2-enhancements](https://github.com/gitTerebi/ra2-enhancements) (`hwcursor/src/dsoundproxy.cpp`, `hwcursor/src/dsoundforward.h`). Copyright (c) 2026 gitTerebi. The MIT licence text is in `native/LICENSE-loader.txt`.

## Blowfish and MIX format
The MIX archive reader in `launcher/src/EditionSetup.cs` and `research/mixread.py` follows the publicly documented XCC/Westwood MIX format. Blowfish (Bruce Schneier, public domain) tables are computed from the digits of pi rather than copied. The Westwood public key is the well-known constant used by every MIX tool.

## Red Alert 2
Command & Conquer, Red Alert and Yuri's Revenge are trademarks of Electronic Arts Inc. This project is not affiliated with or endorsed by Electronic Arts.

This repository and its releases contain no game executables, archives, art, audio or text from the game.
- The smarter AI ships only its own additions. The full `ai.ini` is assembled on the player's PC from their own installation.
- The address documentation in `research/` describes locations in the player's own `game.exe` and contains no game code.
