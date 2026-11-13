# Changelog

Each release's notes on GitHub come from its section here (`## X.Y.Z`), so add a section before tagging `launcher-vX.Y.Z`.

## 0.1.7

- **Downloads folder** ([#2](https://github.com/paulosnp/ultrahand-launcher/issues/2)): the TotK Recomp setup package (about
  2 GB once unpacked) no longer has to live on drive C. Pick its folder with **Change folder** on the install page or in
  Settings > Launcher > General; the packages already downloaded move with it (a copy then a delete when the new folder is on
  another drive).
- The **Update launcher** button no longer covers the install steps in the title bar.

## 0.1.6

- **Add the game to Steam** (Settings > Launcher > General): adds TotK Recomp to your Steam library as a non-Steam game that
  starts `totk.exe`, with a cover, banner, logo and icon from [SteamGridDB](https://www.steamgriddb.com), downloaded when you
  click (they are not part of the launcher). Steam is closed while the launcher writes its list of non-Steam games and opened
  again after; the previous list is kept as `shortcuts.vdf.bak`. If the game is already there (added by hand or by an earlier
  import), that entry is updated instead of a second one being added, keeping its name and play time.

## 0.1.5

- The launcher speaks **Português (Brasil)** and **Español** as well as English. It follows the Windows display language, or
  the one picked in Settings > Launcher > General (it applies after a restart, with a Restart now button). The game settings
  use the names of the game's own Portuguese menu. The TotK Recomp installer's own messages come in Portuguese or English, as
  it has no Spanish.
- **What's new**: the first start after a launcher update shows what changed since the version you had, with a link to the
  full release notes on GitHub. It can be turned off in the popup or in Settings > Launcher > General.

## 0.1.4

- **Play starts the game the way a double click in Explorer does** (through the Windows shell). Before, the launcher started
  `totk.exe` directly and handed it its own process handles, and the game could close right after its first window while
  opening `totk.exe` by hand worked ([#1](https://github.com/paulosnp/ultrahand-launcher/issues/1)). The crash report of 0.1.3
  still works: an error brings the launcher back with the game's log, a normal exit closes it.

## 0.1.3

- The settings page has two halves: **Game** (the in-game menu's settings, as before) and **Launcher**.
- **Save backups** (Launcher > Saves): **Back up now** zips the save folder (your saves, the game's own backup copy of them, the
  account id and the play time) into `Documents\Ultrahand Launcher\Save backups`. The page lists the backups, opens the save
  folder and the backups folder, and explains how to restore one.
- **Crash report**: the launcher now stays in the background while the game runs. If the game closes with an error, the launcher
  comes back with the end of `logs\totk.log`, the crash line (`FATAL: ...`) highlighted, and buttons to copy a bug report, open
  the logs folder or play again. A normal exit still closes the launcher.
- Launcher > General: the Desktop and Start menu shortcuts can be turned on and off, and **Check now** looks for a launcher update.
- Scroll bars match the launcher's design: thin and rounded, brighter on hover and orange while dragged.
- The window is a little darker behind the text, so descriptions stay readable over a bright desktop.

## 0.1.2

- The launcher's version shows in the title bar, next to its name.
- New **Game settings** page behind the gear button in the title bar, with the Screen, Game, Sound, Controls and Menu settings
  of the game's own menu (Esc / F1): the same choices, ranges and descriptions. Changes are written to the game's `totk.ini`
  the way the game writes it: an existing line gets the new value, a new one goes under its commented example, and every
  other line stays as it is.
- Colour profiles set brightness, contrast, saturation and gamma together, and moving one of them switches the profile to
  Custom, as in the game.
- The page warns when the game is running, since most settings take effect at its next start.
- Not in the launcher yet: the Advanced, Amiibo, Mods and Cheats tabs, the graphics card choice and button remapping. Use the
  game's menu for those.

## 0.1.1

- The first run of the installed launcher writes an **Ultrahand Launcher** shortcut on the Desktop and in the Start menu, with
  the launcher's icon. When a game install is already known, the shortcut opens its launcher page. A shortcut you delete is
  not written again; installing, updating or verifying the game writes it again.

## 0.1.0

First release.

- A single `UltrahandLauncher.exe`: opened from anywhere, it installs itself to
  `%LOCALAPPDATA%\Programs\Ultrahand Launcher` and restarts from there, without admin rights.
- Installs TotK Recomp from your game files by running its own installer from the setup package of the latest
  [TotK Recomp release](https://github.com/valentderah/totk-pc-recomp/releases), downloaded when needed.
- Launcher page with Play, plus Update when a new TotK Recomp release is out; Verify files and Install to another folder.
- Updates itself from this repository's releases: the **Update launcher** button downloads the new `.exe`, checks its size and
  SHA-256, swaps it in and restarts.
- Rebuilds the game for code mods (`--rebuild`), started by `totk.exe` through `build-cache\rebuild.txt`.
