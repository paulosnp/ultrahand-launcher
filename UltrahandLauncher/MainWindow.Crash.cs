using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Ultrahand;

/// Play: the launcher hides while the game runs and waits for it. A normal exit (code 0, also the game's hand-off to a mods
/// rebuild) closes the launcher; any other code brings the window back with the end of the game's log (logs\totk.log), where a
/// crash leaves a "FATAL:" line (the game's crash handler exits with 71).
public partial class MainWindow
{
    string crashFolder = "", crashReport = "";

    async void PlayAndWatch(string folder)
    {
        Process? game;
        try
        {
            // Through the shell, as a double click in Explorer starts it: CreateProcess from .NET (UseShellExecute = false) hands
            // the game the launcher's inheritable handles and standard handles, and a player reported totk.exe closing at once
            // when the launcher started it while it ran when opened directly (issue #1).
            game = Process.Start(new ProcessStartInfo(Installed.GameExe(folder)) { UseShellExecute = true, WorkingDirectory = folder });
        }
        catch (Exception x)
        {
            MessageBox.Show(this, $"The game could not be started: {x.Message}", "Ultrahand Launcher");
            return;
        }
        if (game == null)
        {
            Application.Current.Shutdown();   // (started, but the shell gave no process to wait for)
            return;
        }
        var started = DateTime.Now;
        Hide();
        int code;
        using (game)
        {
            await game.WaitForExitAsync();
            code = game.ExitCode;
        }
        if (code == 0)
        {
            Application.Current.Shutdown();
            return;
        }
        Show();
        WindowState = WindowState.Normal;
        Activate();
        ShowCrash(folder, code, started);
    }

    void ShowCrash(string folder, int code, DateTime started)
    {
        crashFolder = folder;
        launchFolder = folder;
        var logPath = Path.Combine(folder, "logs", "totk.log");
        var lines = Array.Empty<string>();
        var fresh = false;
        try
        {
            using var file = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            fresh = File.GetLastWriteTime(logPath) >= started.AddSeconds(-5);
        }
        catch (Exception) { }
        var fatal = lines.LastOrDefault(l => l.StartsWith("FATAL"));
        var shown = code is < 0 or > 0xFFFF ? $"0x{(uint)code:X8}" : code.ToString();
        CrashTitle.Text = fatal != null || code == 71 ? "The game crashed" : "The game stopped";
        CrashSub.Text = $"It closed with exit code {shown} after {Span(DateTime.Now - started)}. " +
                        (lines.Length == 0 ? $"No log was found at {logPath}."
                         : fresh ? "The end of its log is below."
                         : "The log below was not written in this run, so it is from an earlier one.");
        CrashSummary.Text = fatal ?? $"No FATAL line in the log; the game exited with code {shown}.";
        var tail = lines.Skip(Math.Max(0, lines.Length - 300)).ToArray();
        CrashLog.Text = string.Join("\n", tail);
        crashReport = $"Ultrahand Launcher {LauncherUpdate.Current} · TotK Recomp {Versions.Short(Installed.Release(folder))} · " +
                      $"Windows {Environment.OSVersion.Version}\nThe game exited with code {shown}\n{fatal}\n\n" +
                      string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 200)));
        CrashCopy.Content = "Copy for a bug report";
        Stepper.Visibility = Visibility.Collapsed;
        VersionTag.Visibility = Visibility.Collapsed;
        ShowPage(CrashPage);
        // (the FATAL line in view, else the end of the log)
        Dispatcher.BeginInvoke(() =>
        {
            var at = fatal != null ? CrashLog.Text.LastIndexOf(fatal, StringComparison.Ordinal) : -1;
            if (at >= 0) CrashLog.ScrollToLine(Math.Max(0, CrashLog.GetLineIndexFromCharacterIndex(at) - 2));
            else CrashLog.ScrollToEnd();
        }, DispatcherPriority.Loaded);
    }

    void CrashCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(crashReport);
            CrashCopy.Content = "Copied";
        }
        catch (Exception) { }
    }

    void CrashLogsClick(object sender, RoutedEventArgs e) => OpenExplorer(Path.Combine(crashFolder, "logs"));

    void CrashBackClick(object sender, RoutedEventArgs e) => ShowLauncher(crashFolder);

    void CrashPlayClick(object sender, RoutedEventArgs e) => PlayAndWatch(crashFolder);
}
