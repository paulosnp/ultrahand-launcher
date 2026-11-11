# Changelog

Each release's notes on GitHub come from its section here (`## X.Y.Z`), so add a section before tagging `launcher-vX.Y.Z`.

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
