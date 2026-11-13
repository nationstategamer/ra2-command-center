# Research notes

[`evidence.md`](evidence.md) lists every Red Alert 2 engine address the extension uses, and how each one was confirmed.

The scripts are the read-only tools used to find those addresses. They expect to sit three folders below the game folder, as they did during development; adjust the paths at the top of each script for other layouts.

- **`match.py`** matches Yuri's Revenge functions (whose addresses are documented by the YRpp/Phobos projects) to their Red Alert 2 equivalents. It compares normalised instruction sequences and needs the `capstone` and `pefile` Python packages.
- **`mixread.py`** is a dependency-free MIX archive reader. Use it to inspect stock INI files from your own installation.
- **`hooksites.py`** checks that every hook site has its expected bytes, and that no branch in the game lands inside a patched range.

Nothing these scripts extract from the game belongs in this repository; `.gitignore` excludes their output folders.
