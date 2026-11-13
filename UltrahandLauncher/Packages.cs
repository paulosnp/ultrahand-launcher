using System.IO;
using System.Text.Json;

namespace Ultrahand;

/// A setup package: the unpacked TotK-Setup-&lt;version&gt;-windows.zip (python\, installer\, tools\, llvm\, runtime\, VERSION...).
/// The launcher builds nothing itself: it runs "python -m installer" from the package (installer/cli.py).
sealed record SetupPackage(string Root, string Version)
{
    public string Python
    {
        get
        {
            var py = Path.Combine(Root, "python", "python.exe");
            return File.Exists(py) ? py : "python";   // (TOTK_SETUP_DIR pointing at a repository checkout: the Python on PATH)
        }
    }

    /// The downloads folder picked in the settings, else %LOCALAPPDATA%\TotK Recomp
    public static string Home => Settings.Load().Downloads is { Length: > 0 } d ? d : Settings.Folder;
    /// What Settings.Downloads keeps for a folder: null for the default one
    public static string? SettingFor(string home) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)) is var full && full.Equals(Settings.Folder, StringComparison.OrdinalIgnoreCase) ? null : full;
    /// Where the launcher keeps the packages it downloads: Home\setups\&lt;tag&gt;
    public static string Store => Path.Combine(Home, "setups");
    /// Where the zip goes while it downloads
    public static string DownloadDir => Path.Combine(Home, "downloads");
    static string DefaultStore => Path.Combine(Settings.Folder, "setups");

    /// Moves the downloaded packages from Home to newHome (a rename on the same drive, else a copy and delete); an unfinished
    /// download is dropped. The caller then saves newHome as Settings.Downloads.
    public static void MoveHome(string newHome, Action<string> note)
    {
        var from = Path.GetFullPath(Store).TrimEnd('\\') + "\\";
        var to = Path.GetFullPath(Path.Combine(newHome, "setups")).TrimEnd('\\') + "\\";
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return;
        if (to.StartsWith(from, StringComparison.OrdinalIgnoreCase) || from.StartsWith(to, StringComparison.OrdinalIgnoreCase))
            throw new IOException(Tr.T("Pick a folder outside the current downloads folder."));
        Directory.CreateDirectory(to);
        if (Directory.Exists(from))
            foreach (var dir in Directory.GetDirectories(from))
            {
                var target = Path.Combine(to, Path.GetFileName(dir));
                if (dir.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || Directory.Exists(target)) { Directory.Delete(dir, true); continue; }
                note(Tr.F("Moving {0}…", Path.GetFileName(dir)));
                if (string.Equals(Path.GetPathRoot(dir), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
                    Directory.Move(dir, target);
                else
                {
                    Copy(dir, target + ".part");
                    Directory.Move(target + ".part", target);
                    Directory.Delete(dir, true);
                }
            }
        try { if (Directory.Exists(DownloadDir)) Directory.Delete(DownloadDir, true); } catch (Exception) { }

        static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var sub in Directory.GetDirectories(source)) Copy(sub, Path.Combine(target, Path.GetFileName(sub)));
        }
    }

    public static bool IsPackage(string dir) =>
        File.Exists(Path.Combine(dir, "installer", "__main__.py")) &&
        (File.Exists(Path.Combine(dir, "python", "python.exe")) || File.Exists(Path.Combine(dir, ".git", "HEAD")) || File.Exists(Path.Combine(dir, "VERSION")));

    public static SetupPackage? At(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) || !IsPackage(dir)) return null;
        string version;
        try { version = File.ReadAllText(Path.Combine(dir, "VERSION")).Trim(); }
        catch (Exception) { version = "unknown"; }
        return new SetupPackage(Path.GetFullPath(dir), version);
    }

    /// The packages on this PC: TOTK_SETUP_DIR, a "setup" folder next to the launcher (or the package itself, when the launcher
    /// was put inside one) and the ones it downloaded
    public static List<SetupPackage> Local()
    {
        var found = new List<SetupPackage>();
        void Add(string? dir)
        {
            if (At(dir) is { } p && !found.Any(f => string.Equals(f.Root, p.Root, StringComparison.OrdinalIgnoreCase))) found.Add(p);
        }
        Add(Environment.GetEnvironmentVariable("TOTK_SETUP_DIR"));
        var here = AppContext.BaseDirectory.TrimEnd('\\', '/');
        Add(here);
        Add(Path.GetDirectoryName(here));
        Add(Path.Combine(here, "setup"));
        foreach (var store in new[] { Store, DefaultStore })   // (the default too: packages left there by a move that failed)
            try
            {
                if (Directory.Exists(store))
                    foreach (var dir in Directory.GetDirectories(store))
                        if (!dir.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) Add(dir);
            }
            catch (Exception) { }
        return found;
    }

    public static SetupPackage? Newest()
    {
        SetupPackage? best = null;
        foreach (var p in Local())
            if (best == null || Versions.Compare(p.Version, best.Version) > 0) best = p;
        return best;
    }

    /// The package that installed this version (to rebuild for mods without updating), else the newest
    public static SetupPackage? For(string? release) =>
        Local().FirstOrDefault(p => release != null && p.Version == release) ?? Newest();

    /// After a successful run: downloaded packages older than keep are removed (each takes several hundred MB)
    public static void RemoveOlderThan(SetupPackage keep, Action<string> log)
    {
        try
        {
            if (!Directory.Exists(Store)) return;
            foreach (var dir in Directory.GetDirectories(Store))
            {
                var p = At(dir);
                var stale = dir.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || p != null && Versions.Compare(p.Version, keep.Version) < 0;
                if (!stale || string.Equals(Path.GetFullPath(dir), keep.Root, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    Directory.Delete(dir, true);
                    log($"old package removed: {dir}");
                }
                catch (Exception e) { log($"old package not removed ({dir}): {e.Message}"); }
            }
        }
        catch (Exception) { }
    }
}

/// What totk-install.json (installer/state.py) says about an installed folder
static class Installed
{
    public const string StateFile = "totk-install.json";

    public static bool Has(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, StateFile));

    static JsonDocument? Read(string folder)
    {
        try { return JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, StateFile))); }
        catch (Exception) { return null; }
    }

    public static string? Release(string? folder) => Value(folder, "release");
    public static string? Lang(string? folder) => Value(folder, "lang");

    static string? Value(string? folder, string name)
    {
        if (!Has(folder)) return null;
        using var doc = Read(folder!);
        return doc != null && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static string GameExe(string folder) => Path.Combine(folder, "totk.exe");

    public static bool HasDlss(string folder) => File.Exists(Path.Combine(folder, "nvngx_dlss.dll"));
}
