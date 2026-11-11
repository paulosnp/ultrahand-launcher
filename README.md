# Ultrahand Launcher

A Windows launcher and installer for [TotK Recomp](https://github.com/valentderah/totk-pc-recomp), in a single `UltrahandLauncher.exe`.
Download it, open it, pick your game files: it installs the game, keeps it up to date and keeps itself up to date.

- **Installs itself.** Opened from anywhere (Downloads, for example), it copies itself to
  `%LOCALAPPDATA%\Programs\Ultrahand Launcher\UltrahandLauncher.exe` and restarts from there, without admin rights.
- **Installs the game** from your files (base game, update, `prod.keys`, optional firmware) by running TotK Recomp's own installer
  (`python -m installer`) from its setup package, downloaded from the TotK Recomp releases.
- **Launches the game** from the Desktop and Start menu shortcuts it creates.
- **Updates the game** when TotK Recomp publishes a release: it keeps `romfs/`, `exefs/`, `profile/` (saves) and `totk.ini` and
  redoes only what changed.
- **Updates itself** when this repository publishes a launcher release.
- **Rebuilds for code mods**: it writes `build-cache\rebuild.txt` pointing to itself, so `totk.exe` opens the launcher when a mod
  needs new code.

The launcher builds nothing by itself: every step is TotK Recomp's installer, and its messages come from the installer's own text.

## Two kinds of update

| What | Comes from | Shown as |
|---|---|---|
| The game | the latest release of [valentderah/totk-pc-recomp](https://github.com/valentderah/totk-pc-recomp/releases) (`TotK-Setup-*-windows.zip`) | **Update** on the launcher page |
| The launcher | the newest `launcher-vX.Y.Z` release of this repository (`UltrahandLauncher.exe`) | **Update launcher to X.Y.Z** in the title bar |

A launcher update downloads the new `.exe`, checks its size and the SHA-256 GitHub lists for it, swaps it for the running one and
restarts. The game is not touched.

## Use

1. Download `UltrahandLauncher.exe` from the [latest release](https://github.com/paulosnp/ultrahand-launcher/releases/latest) and
   open it. It installs itself and reopens; the downloaded file can then be deleted.
2. Pick the game files, the install folder and the language, and click **Install the game**. The setup package (several hundred MB)
   is downloaded to `%LOCALAPPDATA%\TotK Recomp\setups\<tag>`.
3. Afterwards, open the game from the **TotK Recomp** shortcut.

Command line:

```
UltrahandLauncher.exe                     launcher for the last installed folder, else the install page
UltrahandLauncher.exe --launch FOLDER     launcher for that folder
UltrahandLauncher.exe --update FOLDER     update that folder to the latest TotK Recomp release
UltrahandLauncher.exe --rebuild FOLDER    rebuild for mods, then start the game
```

## Configuration

`%LOCALAPPDATA%\TotK Recomp\launcher.json` keeps the chosen paths. Game releases come from `valentderah/totk-pc-recomp`; to use
another repository, set `"Repo": "owner/repo"` there or the `TOTK_LAUNCHER_REPO` environment variable. Local setup package (no
download): unpack `TotK-Setup-<version>-windows.zip` into a `setup` folder next to `UltrahandLauncher.exe`, or point `TOTK_SETUP_DIR`
at it.

## Build

Needs the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1    # writes dist\UltrahandLauncher\UltrahandLauncher.exe (one self-contained file, no .NET install needed)
```

Every push also builds it in GitHub Actions (artifact `UltrahandLauncher-win-x64`).

## Releasing a launcher version

The version comes from the tag; there is nothing to edit by hand.

```bash
git tag launcher-v1.1.0
git push origin launcher-v1.1.0
```

The workflow builds `UltrahandLauncher.exe` as 1.1.0 and publishes the `launcher-v1.1.0` release. Installed launchers older than
that offer the update the next time they open. Drafts and pre-releases are ignored.

## License

MIT, see [LICENSE](LICENSE). Third-party material and credits: [NOTICE.md](NOTICE.md). Not affiliated with Nintendo.
