ENHANCED RED ALERT 2 (unofficial, experimental)

This folder is a separate copy of your Red Alert 2 installation, made by
Command Center. It runs the original Red Alert 2 engine with its original
units, rules and artwork, plus:

  In-game interface (RA2NativeUI.dll, loaded by the small dsound.dll proxy)
    - Sidebar tooltips with cost, power and build time
    - Translucent building placement preview
    - Harvester (H) and power (P) counters
    - I selects the next idle harvester; Ctrl+S quick-saves
    - Game clock and a Kills / Lost / Army value line
    - Autosave every few minutes (SAVEFFF0-2.SAV)
    - Health bars on damaged units, range circles on selected defenses
    - Your chosen game speed is kept after loading a skirmish save

  Smarter skirmish AI (ai.ini), switchable on the UI & AI tab
    Adds 8 conditional teams for Medium and Brutal computer opponents:
    mixed assaults, anti-air reserves, armour responses and siege groups.
    Easy opponents and all original AI definitions are unchanged. The AI
    gets no free units or bonuses. Turning it off renames ai.ini to
    ai.ini.off, and the game uses its stock AI again.

Settings and saves here are separate from your normal game. Change the
features on Command Center's UI & AI tab, or edit RA2NativeUI.ini with the
game closed.

The extension only runs with the Steam release of Red Alert 2's game.exe
(SHA-256 73288C03B58D370BE268CA6D156B4E33BFDB2066DC980359467D8852FF3B00DF).
It checks every code location before changing anything in memory and never
modifies game.exe on disk.

To remove this edition, delete this folder. Large game archives here may be
hard links to the files in your game folder; deleting this folder removes
only these extra names and leaves your game intact.

Source code, licences and issue tracker:
https://github.com/nationstategamer/ra2-command-center

This is a fan project. It is not made, supported or endorsed by Electronic
Arts. Red Alert 2 and its files belong to their respective owners.
