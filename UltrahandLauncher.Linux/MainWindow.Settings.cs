using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IOPath = System.IO.Path;

namespace Ultrahand;

/// The rest of the window, as in the Windows launcher (MainWindow.Settings.cs, MainWindow.Crash.cs and MainWindow.Notes.cs there):
/// the settings page in two halves (Game: the totk.ini settings of the game's own menu; Launcher: save backups, language,
/// shortcuts, downloads folder, Steam, updates), the page a game crash brings up, and what's new after a launcher update.
public partial class MainWindow
{
    TotkIni? ini;
    Control? settingsReturn;   // the page Settings was opened from
    bool launcherSection;
    int settingsTab, launcherTab;
    string? backupNote;   // the result of the last backup, shown under its button
    bool backupOk;
    readonly Dictionary<string, Control> settingControls = new();
    readonly Dictionary<string, (Setting Setting, string Value)> pendingWrites = new();
    DispatcherTimer? writeTimer;
    bool settingsProgrammatic;

    static readonly string[] LauncherTabs = { "Saves", "General" };
    const string ProjectPage = "https://github.com/paulosnp/ultrahand-launcher";

    string? SettingsFolder() =>
        Installed.Has(launchFolder) ? launchFolder : Installed.Has(settings.Dest) ? settings.Dest : null;

    TextBlock Small(string text, string cls = "tiny") => new() { Text = text, Classes = { cls }, TextWrapping = TextWrapping.Wrap };

    async void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (!Main.IsVisible || SettingsPage.IsVisible) return;
        if (run != null || download != null)
        {
            await Dialogs.Ask(this, Tr.T("Wait for the current run to finish, then change the settings."));
            return;
        }
        ini = SettingsFolder() is { } folder ? new TotkIni(folder) : null;
        if (ini == null) launcherSection = true;   // (no game yet: its settings have no totk.ini to live in)
        settingsReturn = new Control[] { FilesPage, ReadyPage, ProblemPage, LaunchPage, CrashPage }.FirstOrDefault(p => p.IsVisible);
        ShowPage(SettingsPage);
        ShowSection();
    }

    void SectionChanged(object? sender, RoutedEventArgs e)
    {
        launcherSection = sender == LauncherSection;
        ShowSection();
    }

    void SettingsDone(object? sender, RoutedEventArgs e)
    {
        FlushWrites();
        if (settingsReturn != null) ShowPage(settingsReturn);
        else if (launchFolder.Length > 0) ShowLauncher(launchFolder);
        else ShowFiles();
    }

    /// The tab list of the half shown, and its selected tab
    void ShowSection()
    {
        FlushWrites();
        GameSection.Classes.Set("on", !launcherSection);
        LauncherSection.Classes.Set("on", launcherSection);
        SettingsTabs.Children.Clear();
        var names = (launcherSection ? LauncherTabs : GameSettings.Tabs.Select(t => t.Name).ToArray()).Select(Tr.T).ToArray();
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var item = new Button { Content = names[i], Classes = { "nav" } };
            item.Click += (_, _) => ShowTab(index);
            SettingsTabs.Children.Add(item);
        }
        ShowTab(launcherSection ? launcherTab : settingsTab);
    }

    void ShowTab(int index)
    {
        settingControls.Clear();
        SettingsRows.Children.Clear();
        for (var i = 0; i < SettingsTabs.Children.Count; i++) SettingsTabs.Children[i].Classes.Set("on", i == index);
        if (launcherSection)
        {
            launcherTab = index;
            if (index == 0) ShowSavesTab();
            else ShowGeneralTab();
        }
        else
        {
            settingsTab = index;
            ShowGameTab(index);
        }
        SettingsScroll.ScrollToHome();
    }

    void Note(string text, string brush = "TextDim")
    {
        SettingsNote.Text = text;
        SettingsNote.Foreground = Br(brush);
    }

    // ================= Game =================

    void ShowGameTab(int index)
    {
        if (ini == null)
        {
            Note(Tr.T("Install the game first: its settings live in totk.ini next to totk.exe."), "Warn");
            return;
        }
        var folder = IOPath.GetDirectoryName(ini.Path)!;
        var running = Process.GetProcessesByName("totk").Any(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, Installed.GameExe(folder), Platform.PathCompare); }
            catch (Exception) { return false; }
        });
        if (running) Note(Tr.T("The game is running: close it first, or most of these take effect only at its next start."), "Warn");
        else Note(Tr.F("The same settings as the game's menu (Esc / F1), saved in {0}.", ini.Path));
        foreach (var group in GameSettings.Tabs[index].Groups)
        {
            if (group.Title != null) Heading(group.Title);
            for (var i = 0; i < group.Rows.Length; i++) SettingsRows.Children.Add(Row(group.Rows[i], i == group.Rows.Length - 1));
        }
    }

    void Heading(string title) =>
        SettingsRows.Children.Add(new TextBlock { Text = Tr.T(title).ToUpper(Tr.Culture), Classes = { "caps" }, Margin = new Thickness(0, 18, 0, 2) });

    string Current(Setting s) => ini?.Get(s.Key) ?? s.Default;

    Control Row(Setting s, bool last)
    {
        var control = s.Toggle ? ToggleControl(s) : s.Choices != null ? ChoiceControl(s) : SliderControl(s);
        settingControls[s.Key] = control;
        return RowOf(Tr.T(s.Label), Tr.T(s.Description), control, last);
    }

    /// One row of the page: the label with its description on the left, the control on the right, a line under it
    Control RowOf(string label, string? description, Control? control, bool last, Control? below = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 12), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        if (control is ComboBox or Grid) grid.ColumnDefinitions[1].MinWidth = 250;
        var texts = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
        if (description != null)
        {
            var d = Small(description);
            d.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(d);
        }
        if (below != null) texts.Children.Add(below);
        grid.Children.Add(texts);
        if (control != null)
        {
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        return new Border { Child = grid, BorderBrush = Br("Line"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
    }

    CheckBox Toggle(bool on, Action<bool> set)
    {
        var box = new CheckBox { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsChecked = on, MinWidth = 0 };
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        return box;
    }

    Control ToggleControl(Setting s) => Toggle(IsOn(Current(s)), on => Write(s, on ? "1" : "0"));

    static bool IsOn(string value) => value.Trim().ToLowerInvariant() is not ("0" or "false" or "off" or "no" or "");

    Control ChoiceControl(Setting s)
    {
        var choices = s.Choices!.ToList();
        var now = Current(s);
        var at = choices.FindIndex(c => string.Equals(c.Value, now, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            choices.Add((now, Tr.F("{0} (from totk.ini)", now)));   // a value the menu does not list keeps showing
            at = choices.Count - 1;
        }
        var box = new ComboBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                                 ItemsSource = choices.Select(c => Tr.T(c.Name)).ToList(), SelectedIndex = at, Tag = choices };
        box.SelectionChanged += (_, _) =>
        {
            if (settingsProgrammatic || box.SelectedIndex < 0) return;
            var value = choices[box.SelectedIndex].Value;
            Write(s, value);
            if (s.Key == "color_preset" && GameSettings.ColorPresets.TryGetValue(value, out var p))
            {
                // (a profile writes its four values, as the game's menu does)
                foreach (var (key, v) in new[] { ("color_brightness", p.B), ("color_contrast", p.C), ("color_saturation", p.S), ("color_gamma", p.G) })
                    if (Find(key) is { } row)
                    {
                        Write(row, v);
                        if (settingControls.TryGetValue(key, out var c) && c is Grid g && g.Children[0] is Slider slider)
                        {
                            settingsProgrammatic = true;
                            slider.Value = Parse(v, row.Min);
                            settingsProgrammatic = false;
                        }
                    }
            }
        };
        return box;
    }

    Control SliderControl(Setting s)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, Width = 250, ColumnDefinitions = new ColumnDefinitions("*,70") };
        var slider = new Slider { Minimum = s.Min, Maximum = s.Max, TickFrequency = s.Step, IsSnapToTickEnabled = true,
                                  SmallChange = s.Step, LargeChange = s.Step * 4,
                                  Value = Math.Clamp(Parse(Current(s), Parse(s.Default, s.Min)), s.Min, s.Max) };
        var label = Small(Shown(s, slider.Value), "muted");
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        grid.Children.Add(slider);
        grid.Children.Add(label);
        slider.ValueChanged += (_, _) =>
        {
            label.Text = Shown(s, slider.Value);
            if (settingsProgrammatic) return;
            Write(s, Text(s, slider.Value));
            if (s.Key.StartsWith("color_") && Find("color_preset") is { } preset &&
                settingControls.TryGetValue("color_preset", out var c) && c is ComboBox box &&
                box.Tag is List<(string Value, string Name)> choices && box.SelectedIndex >= 0 && choices[box.SelectedIndex].Value != "custom")
            {
                // (moving a colour value makes the profile Custom, as in the game's menu)
                Write(preset, "custom");
                settingsProgrammatic = true;
                box.SelectedIndex = choices.FindIndex(x => x.Value == "custom");
                settingsProgrammatic = false;
            }
        };
        return grid;
    }

    static Setting? Find(string key) =>
        GameSettings.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Rows).FirstOrDefault(r => r.Key == key);

    static double Parse(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    static string Shown(Setting s, double value) => string.Format(CultureInfo.InvariantCulture, s.Format, value);

    /// The value as totk.ini gets it: whole numbers for whole steps, else the step's decimals
    static string Text(Setting s, double value)
    {
        var rounded = Math.Round(value / s.Step) * s.Step;
        return s.Step >= 1 ? Math.Round(rounded).ToString(CultureInfo.InvariantCulture)
                           : Math.Round(rounded, 3).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// Writes go out a moment after the last change, so dragging a slider writes the file once
    void Write(Setting s, string value)
    {
        pendingWrites[s.Key] = (s, value);
        if (writeTimer == null)
        {
            writeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            writeTimer.Tick += (_, _) => FlushWrites();
        }
        writeTimer.Stop();
        writeTimer.Start();
    }

    async void FlushWrites()
    {
        writeTimer?.Stop();
        if (ini == null || pendingWrites.Count == 0) return;
        try
        {
            foreach (var (s, value) in pendingWrites.Values) ini.Set(s.Key, s.Section, value);
            pendingWrites.Clear();
        }
        catch (Exception x)
        {
            pendingWrites.Clear();
            await Dialogs.Ask(this, Tr.F("totk.ini could not be written: {0}", x.Message));
        }
    }

    // ================= Launcher: Saves =================

    Button SmallButton(string text, bool accent = false)
    {
        var button = new Button { Content = Tr.T(text), Height = 34, Padding = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        if (accent) button.Classes.Add("accent");
        return button;
    }

    TextBlock Status(string? text = null, bool ok = true)
    {
        var status = Small(text ?? "");
        status.Margin = new Thickness(0, 6, 0, 0);
        status.IsVisible = text != null;
        status.Foreground = Br(ok ? "Ok" : "Bad");
        return status;
    }

    void ShowSavesTab()
    {
        Note(Tr.F("Save backups go to {0}.", SaveBackup.Folder));
        var folder = SettingsFolder();
        var profile = folder != null && ini != null ? SaveBackup.ProfileDir(folder, ini) : null;
        var hasSaves = profile != null && Directory.Exists(profile);

        Button? open = null;
        if (hasSaves)
        {
            open = SmallButton("Open");
            open.Click += (_, _) => Platform.OpenFolder(profile!);
        }
        SettingsRows.Children.Add(RowOf(Tr.T("Save folder"), hasSaves ? profile : Tr.T("No installed game with a save folder was found."), open, false));

        var backup = SmallButton("Back up now", accent: true);
        backup.IsEnabled = hasSaves;
        backup.Click += async (_, _) =>
        {
            backup.IsEnabled = false;
            backup.Content = Tr.T("Backing up…");
            try
            {
                var zip = await Task.Run(() => SaveBackup.Create(profile!));
                backupOk = true;
                backupNote = Tr.F("Saved {0} ({1:0.0} MB).", IOPath.GetFileName(zip), new FileInfo(zip).Length / 1048576.0);
            }
            catch (Exception x)
            {
                backupOk = false;
                backupNote = Tr.F("The backup failed: {0}", x.Message);
            }
            if (launcherSection && launcherTab == 0 && SettingsPage.IsVisible) ShowTab(0);   // (the list below grows)
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Back up the save"),
            Tr.T("Zips the save folder: your saves, the game's own backup copy of them, the account id and the play time. Close the game " +
            "first: a backup made while the game is saving can catch a save half written."), backup, false, Status(backupNote, backupOk)));

        var backups = SaveBackup.List();
        var listed = backups.Length == 0
            ? Tr.T("None yet.")
            : string.Join("\n", backups.Take(5).Select(f => $"{IOPath.GetFileNameWithoutExtension(f.Name)}  ·  {f.Length / 1048576.0:0.0} MB")) +
              (backups.Length > 5 ? "\n" + Tr.F("and {0} older", backups.Length - 5) : "");
        Button? folderButton = null;
        if (Directory.Exists(SaveBackup.Folder))
        {
            folderButton = SmallButton("Open folder");
            folderButton.Click += (_, _) => Platform.OpenFolder(SaveBackup.Folder);
        }
        SettingsRows.Children.Add(RowOf(Tr.T("Backups"), listed, folderButton, false));
        SettingsRows.Children.Add(RowOf(Tr.T("Restoring a backup"),
            Tr.T("Close the game, open the backup (it is a zip file) and copy its profile folder into the game folder, replacing the files " +
            "there."), null, true));
    }

    // ================= Launcher: General =================

    /// Picks a new downloads folder and moves the setup packages there (SetupPackage.MoveHome); note gets the progress and any
    /// error. True when the folder changed.
    async Task<bool> ChangeDownloads(Action<string> note)
    {
        if (run != null || download != null)
        {
            note(Tr.T("Wait for the install or update to finish first."));
            return false;
        }
        if (await PickFolder(Tr.T("Choose where the downloads go")) is not { } picked) return false;
        BuildButton.IsEnabled = false;   // (no install while the package moves)
        try
        {
            await Task.Run(() => SetupPackage.MoveHome(picked, text => Post(() => note(text))));
            settings.Downloads = SetupPackage.SettingFor(picked);
            settings.Save();
            note("");
            return true;
        }
        catch (Exception x)
        {
            note(Tr.F("The downloads folder could not be changed: {0}", x.Message));
            return false;
        }
        finally
        {
            localPackage = SetupPackage.Newest();
            RefreshPackageNote();
            Refresh();
        }
    }

    async void ChangeDownloadsClick(object? sender, RoutedEventArgs e)
    {
        DownloadsButton.IsEnabled = false;
        string? error = null;
        await ChangeDownloads(text => { PackageNote.Text = text; error = text; });   // (progress lines, then "" or the error)
        DownloadsButton.IsEnabled = true;
        if (string.IsNullOrEmpty(error)) return;
        PackageNote.Text = error;
        PackageNote.Foreground = Br("Bad");
    }

    void ShowGeneralTab()
    {
        Note(Tr.F("Ultrahand Launcher {0}, installed in {1}.", LauncherUpdate.Current, Platform.InstallDir));
        var folder = SettingsFolder();
        void Shortcut(bool desktop, bool value)
        {
            if (desktop) settings.DesktopShortcut = value;
            else settings.StartShortcut = value;
            settings.Save();
            Shortcuts.Make(folder, settings.DesktopShortcut, settings.StartShortcut);
        }

        Heading("Language");
        var codes = new[] { "" }.Concat(Tr.Languages.Select(l => l.Code)).ToList();
        var names = new[] { Tr.T("Automatic (system language)") }.Concat(Tr.Languages.Select(l => l.Name)).ToList();
        var language = new ComboBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                                      ItemsSource = names, SelectedIndex = Math.Max(0, codes.IndexOf(settings.UiLang ?? "")) };
        var restart = SmallButton("Restart now", accent: true);
        restart.Margin = new Thickness(0, 8, 0, 0);
        restart.HorizontalAlignment = HorizontalAlignment.Left;
        var restartText = Small(Tr.T("The new language applies when the launcher starts again."));
        restartText.Foreground = Br("AccentLight");
        var restartNote = new StackPanel { IsVisible = false, Margin = new Thickness(0, 6, 0, 0), Children = { restartText, restart } };
        language.SelectionChanged += (_, _) =>
        {
            settings.UiLang = codes[Math.Max(0, language.SelectedIndex)];
            settings.Save();
            restartNote.IsVisible = true;
        };
        restart.Click += (_, _) =>
        {
            if (Environment.ProcessPath is not { } exe) return;
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            if (launchFolder.Length > 0) { start.ArgumentList.Add("--launch"); start.ArgumentList.Add(launchFolder); }
            try { Process.Start(start); Close(); } catch (Exception) { }
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Launcher language"),
            Tr.T("The language of this launcher. The game's own language is under Game > Game."), language, true, restartNote));

        Heading("Shortcuts");
        SettingsRows.Children.Add(RowOf(Tr.T("Desktop shortcut"), Tr.T("Ultrahand Launcher on the Desktop."),
            Toggle(settings.DesktopShortcut, v => Shortcut(true, v)), false));
        SettingsRows.Children.Add(RowOf(Tr.T("Start menu shortcut"), Tr.T("Ultrahand Launcher in the Start menu."),
            Toggle(settings.StartShortcut, v => Shortcut(false, v)), true));

        Heading("Downloads");
        var downloadsButton = SmallButton("Change");
        var downloadsStatus = Status();
        downloadsButton.Click += async (_, _) =>
        {
            downloadsButton.IsEnabled = false;
            downloadsStatus.IsVisible = true;
            downloadsStatus.Foreground = Br("TextDim");
            downloadsStatus.Text = "";
            var changed = await ChangeDownloads(text => downloadsStatus.Text = text);
            downloadsButton.IsEnabled = true;
            if (changed && launcherSection && SettingsPage.IsVisible) ShowTab(launcherTab);   // (shows the new folder)
            else if (string.IsNullOrEmpty(downloadsStatus.Text)) downloadsStatus.IsVisible = false;
            else downloadsStatus.Foreground = Br("Bad");
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Downloads folder"),
            SetupPackage.Home + "\n" + Tr.T("Where the TotK Recomp setup package is downloaded and kept (about 2 GB once unpacked). Changing it " +
            "moves the packages already there."), downloadsButton, true, downloadsStatus));

        Heading("Steam");
        var steamFound = SteamImport.SteamDir() != null;
        var inSteam = folder != null && steamFound && SteamImport.Has(folder);
        var steamButton = SmallButton("Add to Steam", accent: true);
        if (inSteam) steamButton.Content = Tr.T("Update in Steam");
        steamButton.IsEnabled = folder != null && steamFound;
        var steamStatus = Status();
        steamButton.Click += async (_, _) =>
        {
            steamButton.IsEnabled = false;
            steamStatus.IsVisible = true;
            steamStatus.Foreground = Br("TextDim");
            try
            {
                var failed = await SteamImport.Add(folder!, text => steamStatus.Text = text);
                steamStatus.Text = failed == 0
                    ? Tr.T("Done: the game is in your Steam library, with its pictures.")
                    : Tr.F("The game is in your Steam library, but {0} of its pictures could not be downloaded. Try again later.", failed);
                steamStatus.Foreground = Br(failed == 0 ? "Ok" : "AccentLight");
                steamButton.Content = Tr.T("Update in Steam");
            }
            catch (Exception x)
            {
                steamStatus.Text = Tr.F("Could not add the game to Steam: {0}", x.Message);
                steamStatus.Foreground = Br("Bad");
            }
            steamButton.IsEnabled = true;
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Add the game to Steam"),
            !steamFound ? Tr.T("Steam was not found on this PC.")
            : folder == null ? Tr.T("Install the game first.")
            : Tr.T("Adds the game to your Steam library as a non-Steam game, with a cover, banner, logo and icon from SteamGridDB. Play " +
                   "in Steam starts totk.exe. Steam is closed while the launcher writes its list, and opened again after."),
            steamButton, true, steamStatus));

        Heading("Updates");
        SettingsRows.Children.Add(RowOf(Tr.T("Show what's new after an update"),
            Tr.T("The first start after a launcher update shows its release notes, with a link to the full notes on GitHub."),
            Toggle(settings.ShowReleaseNotes, v => { settings.ShowReleaseNotes = v; settings.Save(); }), false));
        var check = SmallButton("Check now");
        var status = Status();
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            var found = await LauncherUpdate.Check();
            check.IsEnabled = true;
            status.IsVisible = true;
            if (found == null)
            {
                status.Text = Tr.F("{0} is the latest version (or GitHub could not be reached).", LauncherUpdate.Current);
                status.Foreground = Br("TextDim");
                return;
            }
            status.Text = LauncherUpdate.CanApply
                ? Tr.F("Version {0} is out: use \"Update launcher\" at the top of the window.", found.Version)
                : Tr.F("Version {0} is out. This copy is not the installed one, so it cannot update itself.", found.Version);
            status.Foreground = Br("AccentLight");
            launcherUpdate = found;
            if (LauncherUpdate.CanApply)
            {
                LauncherUpdateButton.Content = Tr.F("Update launcher to {0}", found.Version);
                LauncherUpdateButton.IsVisible = true;
            }
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Launcher version"),
            Tr.F("You have {0}. The launcher looks for a new version each time it opens.", LauncherUpdate.Current), check, false, status));
        var page = SmallButton("Open");
        page.Click += (_, _) => Platform.OpenLink(ProjectPage);
        SettingsRows.Children.Add(RowOf(Tr.T("Project page"), Tr.T("Releases, changelog and bug reports:") + " " + ProjectPage, page, true));
    }

    // ================= the game stopped with an error =================

    string crashFolder = "", crashReport = "";

    /// The end of the game's log (logs/totk.log), where a crash leaves a "FATAL:" line (the game's crash handler exits with 71)
    void ShowCrash(string folder, int code, DateTime started)
    {
        crashFolder = folder;
        launchFolder = folder;
        var file = IOPath.Combine(folder, "logs", "totk.log");
        var lines = Array.Empty<string>();
        var fresh = false;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            fresh = File.GetLastWriteTime(file) >= started.AddSeconds(-5);
        }
        catch (Exception) { }
        var fatal = lines.LastOrDefault(l => l.StartsWith("FATAL"));
        var shown = code is < 0 or > 0xFFFF ? $"0x{(uint)code:X8}" : code.ToString();
        CrashTitle.Text = Tr.T(fatal != null || code == 71 ? "The game crashed" : "The game stopped");
        CrashSub.Text = Tr.F("It closed with exit code {0} after {1}. ", shown, Span(DateTime.Now - started)) +
                        (lines.Length == 0 ? Tr.F("No log was found at {0}.", file)
                         : fresh ? Tr.T("The end of its log is below.")
                         : Tr.T("The log below was not written in this run, so it is from an earlier one."));
        CrashSummary.Text = fatal ?? Tr.F("No FATAL line in the log; the game exited with code {0}.", shown);
        var text = string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 300)));
        CrashLog.Text = text;
        crashReport = $"Ultrahand Launcher {LauncherUpdate.Current} · TotK Recomp {Versions.Short(Installed.Release(folder))} · " +
                      $"{RuntimeInformation.OSDescription}\nThe game exited with code {shown}\n{fatal}\n\n" +
                      string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 200)));
        CrashCopy.Content = Tr.T("Copy for a bug report");
        Stepper.IsVisible = false;
        VersionTag.IsVisible = false;
        ShowPage(CrashPage);
        // (the FATAL line in view, else the end of the log)
        var at = fatal != null ? text.LastIndexOf(fatal, StringComparison.Ordinal) : -1;
        Post(() => CrashLog.CaretIndex = at >= 0 ? at : text.Length);
    }

    async void CrashCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard == null) return;
            await Clipboard.SetTextAsync(crashReport);
            CrashCopy.Content = Tr.T("Copied");
        }
        catch (Exception) { }
    }

    void CrashLogsClick(object? sender, RoutedEventArgs e) => Platform.OpenFolder(IOPath.Combine(crashFolder, "logs"));

    void CrashBackClick(object? sender, RoutedEventArgs e) => ShowLauncher(crashFolder);

    void CrashPlayClick(object? sender, RoutedEventArgs e) => PlayAndWatch(crashFolder);

    // ================= what's new =================

    bool wasInstalledBefore;   // an earlier launcher already ran installed here (set in the constructor, before this run marks it)

    /// After a launcher update, the first start shows what changed since the version the player last saw (ReleaseNotes.cs)
    void ShowNotesIfUpdated()
    {
        if (!LauncherUpdate.CanApply) return;
        var current = LauncherUpdate.Current;
        var last = settings.LastSeenVersion;
        if (last != null && !Versions.Newer(current, last)) return;
        settings.LastSeenVersion = current;
        settings.Save();
        if (!settings.ShowReleaseNotes || last == null && !wasInstalledBefore) return;   // (a fresh install has nothing to tell)
        var sections = ReleaseNotes.Between(last, current);
        if (sections.Count == 0) return;
        NotesTitle.Text = Tr.F("What's new in Ultrahand Launcher {0}", current);
        NotesSub.Text = last != null && sections.Count > 1 ? Tr.F("The changes since {0}, newest first.", Versions.Short(last)) : "";
        NotesSub.IsVisible = NotesSub.Text.Length > 0;
        var inlines = new InlineCollection();
        foreach (var section in sections)
        {
            if (sections.Count > 1)
            {
                if (inlines.Count > 0) inlines.Add(new LineBreak());
                inlines.Add(new Run(section.Version) { FontWeight = FontWeight.SemiBold, Foreground = Br("Text") });
                inlines.Add(new LineBreak());
            }
            foreach (var item in section.Items)
            {
                inlines.Add(new Run("•  " + item));
                inlines.Add(new LineBreak());
            }
        }
        NotesText.Inlines = inlines;
        NotesOff.IsChecked = false;
        NotesOverlay.IsVisible = true;
    }

    void NotesOkClick(object? sender, RoutedEventArgs e)
    {
        if (NotesOff.IsChecked == true)
        {
            settings.ShowReleaseNotes = false;
            settings.Save();
        }
        NotesOverlay.IsVisible = false;
    }

    void NotesFullClick(object? sender, RoutedEventArgs e) => Platform.OpenLink($"{ProjectPage}/releases/tag/launcher-v{LauncherUpdate.Current}");
}
