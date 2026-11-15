using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Ultrahand;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Relocate(args)) return 0;
        LauncherUpdate.CleanUp();
        Tr.Use(Settings.Load().UiLang);
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
    }

    /// Opened from outside Platform.InstallDir (the downloaded file): copies itself there and restarts the copy with the same
    /// arguments. Only the published single file does this; a run from bin/ (dotnet run) stays where it is.
    static bool Relocate(string[] args)
    {
        var exe = Environment.ProcessPath;
#pragma warning disable IL3000   // (empty in a single-file program is exactly the test)
        if (exe == null || typeof(Program).Assembly.Location.Length > 0) return false;
#pragma warning restore IL3000
        var target = Platform.InstalledExe;
        if (string.Equals(Path.GetFullPath(exe), target, Platform.PathCompare)) return false;
        try
        {
            Directory.CreateDirectory(Platform.InstallDir);
            var tmp = target + ".tmp";
            File.Copy(exe, tmp, overwrite: true);
            Platform.MakeExecutable(tmp);
            File.Move(tmp, target, overwrite: true);
        }
        catch (Exception)
        {
            try { File.Delete(target + ".tmp"); } catch (Exception) { }
            if (!File.Exists(target)) return false;   // (could not install: run from here)
        }
        var start = new ProcessStartInfo(target) { WorkingDirectory = Platform.InstallDir, UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        try { Process.Start(start); return true; }
        catch (Exception) { return false; }
    }
}

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
