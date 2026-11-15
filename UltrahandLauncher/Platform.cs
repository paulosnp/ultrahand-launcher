using System.Diagnostics;
using System.IO;

namespace Ultrahand;

/// What differs between the Windows launcher (WPF, this folder) and the Linux one (Avalonia, UltrahandLauncher.Linux, which compiles
/// the shared files of this folder): folders, file names and how a folder is opened.
static class Platform
{
    public static readonly bool Linux = OperatingSystem.IsLinux();

    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);   // (Linux: ~/.local/share)

    /// launcher.json and the downloaded setup packages
    public static string DataFolder => Path.Combine(Local, Linux ? "ultrahand-launcher" : "TotK Recomp");

    /// Where the launcher lives: the shortcuts and build-cache/rebuild.txt point here, so the downloaded file can be deleted
    public static string InstallDir => Linux ? Path.Combine(DataFolder, "bin") : Path.Combine(Local, "Programs", "Ultrahand Launcher");
    public static string ExeName => Linux ? "UltrahandLauncher" : "UltrahandLauncher.exe";
    public static string InstalledExe => Path.Combine(InstallDir, ExeName);

    /// The launcher's file in a GitHub release of this repository
    public static string ReleaseAsset => Linux ? "UltrahandLauncher-linux-x64" : "UltrahandLauncher.exe";

    public static string GameExeName => Linux ? "totk" : "totk.exe";

    /// The Documents folder (on Linux .NET gives the home folder for it: ~/Documents when there is one)
    public static string Documents
    {
        get
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Linux && Directory.Exists(Path.Combine(folder, "Documents")) ? Path.Combine(folder, "Documents") : folder;
        }
    }

    /// The setup package in a TotK Recomp release (tools/make_release.py and make_release_linux.py)
    public static string PackageAsset(string version) => Linux ? $"TotK-Setup-{version}-x86_64.AppImage" : $"TotK-Setup-{version}-windows.zip";

    public static StringComparison PathCompare => Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            if (Linux) Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { folder }, UseShellExecute = false });
            else Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    public static void OpenLink(string url)
    {
        try
        {
            if (Linux) Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
