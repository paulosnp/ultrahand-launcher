using System.IO;
using System.IO.Compression;

namespace Ultrahand;

/// Save backups: the game's profile folder (the saves, their backup copy, the account id and the play time) zipped into
/// Documents\Ultrahand Launcher\Save backups. To restore one, close the game and unzip its profile folder over the game's.
static class SaveBackup
{
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ultrahand Launcher", "Save backups");

    /// The profile folder: profile_dir from totk.ini (relative to the game folder), else profile next to totk.exe
    public static string ProfileDir(string gameFolder, TotkIni ini)
    {
        var set = ini.Get("profile_dir");
        if (set == null) return Path.Combine(gameFolder, "profile");
        return Path.IsPathRooted(set) ? set : Path.GetFullPath(Path.Combine(gameFolder, set));
    }

    /// Folders the game re-creates by itself: the item icons and the scratch space of a save in progress
    static readonly string[] Skip = { "cache", "temp" };

    /// Zips the profile into a new backup and returns its path. Files the running game holds open are read anyway.
    public static string Create(string profile)
    {
        if (!Directory.Exists(profile)) throw new DirectoryNotFoundException($"There is no save folder at {profile}.");
        Directory.CreateDirectory(Folder);
        var zip = Path.Combine(Folder, $"TotK save {DateTime.Now:yyyy-MM-dd HH-mm-ss}.zip");
        var part = zip + ".part";
        try
        {
            using (var file = new FileStream(part, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                foreach (var path in Directory.EnumerateFiles(profile, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(profile, path);
                    if (Skip.Contains(relative.Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase)) continue;
                    var entry = archive.CreateEntry("profile/" + relative.Replace('\\', '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(path);
                    using var from = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var to = entry.Open();
                    from.CopyTo(to);
                }
            File.Move(part, zip);
            return zip;
        }
        catch (Exception)
        {
            try { File.Delete(part); } catch (Exception) { }
            throw;
        }
    }

    /// The backups made so far, newest first
    public static FileInfo[] List() =>
        Directory.Exists(Folder) ? new DirectoryInfo(Folder).GetFiles("*.zip").OrderByDescending(f => f.LastWriteTime).ToArray() : Array.Empty<FileInfo>();
}
