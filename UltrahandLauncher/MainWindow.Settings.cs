using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ultrahand;

/// The settings page behind the gear, in two halves. Game: the totk.ini settings of the game's own menu (GameSettings.cs), read
/// from and written to the totk.ini of the installed game folder as the game itself does. Launcher: save backups (SaveBackup.cs),
/// the shortcuts and the launcher's own version.
public partial class MainWindow
{
    TotkIni? ini;
    FrameworkElement? settingsReturn;   // the page the gear was clicked on
    bool launcherSection;
    int settingsTab, launcherTab;
    string? backupNote;   // the result of the last backup, shown under its button
    bool backupOk;
    readonly Dictionary<string, FrameworkElement> settingControls = new();
    readonly Dictionary<string, (Setting Setting, string Value)> pendingWrites = new();
    DispatcherTimer? writeTimer;
    bool settingsProgrammatic;

    static readonly string[] LauncherTabs = { "Saves", "General" };
    const string ProjectPage = "https://github.com/paulosnp/ultrahand-launcher";

    string? SettingsFolder() =>
        Installed.Has(launchFolder) ? launchFolder : Installed.Has(settings.Dest) ? settings.Dest : null;

    Style St(string key) => (Style)FindResource(key);
    Brush Br(string key) => (Brush)FindResource(key);

    void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (!Main.IsHitTestVisible || SettingsPage.Visibility == Visibility.Visible) return;
        if (run != null || download != null)
        {
            MessageBox.Show(this, Tr.T("Wait for the current run to finish, then change the settings."), "Ultrahand Launcher");
            return;
        }
        ini = SettingsFolder() is { } folder ? new TotkIni(folder) : null;
        if (ini == null) launcherSection = true;   // (no game yet: its settings have no totk.ini to live in)
        settingsReturn = new FrameworkElement[] { FilesPage, ReadyPage, ProblemPage, LaunchPage, CrashPage }
            .FirstOrDefault(p => p.Visibility == Visibility.Visible);
        settingsProgrammatic = true;
        (launcherSection ? LauncherSection : GameSection).IsChecked = true;
        settingsProgrammatic = false;
        ShowPage(SettingsPage);
        ShowSection();
    }

    void SectionChanged(object sender, RoutedEventArgs e)
    {
        if (settingsProgrammatic) return;
        launcherSection = sender == LauncherSection;
        ShowSection();
    }

    void SettingsDone(object sender, RoutedEventArgs e)
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
        SettingsTabs.Children.Clear();
        var names = (launcherSection ? LauncherTabs : GameSettings.Tabs.Select(t => t.Name).ToArray()).Select(Tr.T).ToArray();
        var selected = launcherSection ? launcherTab : settingsTab;
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var item = new RadioButton { Style = St("NavItem"), Content = names[i], GroupName = "settingsTab", IsChecked = i == selected };
            item.Checked += (_, _) => ShowTab(index);
            SettingsTabs.Children.Add(item);
        }
        ShowTab(selected);
    }

    void ShowTab(int index)
    {
        settingControls.Clear();
        SettingsRows.Children.Clear();
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
        SettingsScroll.ScrollToTop();
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
        var folder = Path.GetDirectoryName(ini.Path)!;
        var running = Process.GetProcessesByName("totk").Any(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, Installed.GameExe(folder), StringComparison.OrdinalIgnoreCase); }
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
        SettingsRows.Children.Add(new TextBlock { Text = Tr.T(title).ToUpper(Tr.Culture), Style = St("Caps"), Margin = new Thickness(0, 18, 0, 2) });

    string Current(Setting s) => ini?.Get(s.Key) ?? s.Default;

    FrameworkElement Row(Setting s, bool last)
    {
        var control = s.Toggle ? ToggleControl(s) : s.Choices != null ? ChoiceControl(s) : SliderControl(s);
        settingControls[s.Key] = control;
        return RowOf(Tr.T(s.Label), Tr.T(s.Description), control, last);
    }

    /// One row of the page: the label with its description on the left, the control on the right, a line under it
    FrameworkElement RowOf(string label, string? description, FrameworkElement? control, bool last, FrameworkElement? below = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = control is ComboBox or Grid ? 250 : 0 });
        var texts = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        if (description != null)
            texts.Children.Add(new TextBlock { Text = description, Style = St("Tiny"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        if (below != null) texts.Children.Add(below);
        grid.Children.Add(texts);
        if (control != null)
        {
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        return new Border { Child = grid, BorderBrush = Br("Line"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
    }

    FrameworkElement ToggleControl(Setting s)
    {
        var box = new CheckBox { Style = St("Check"), HorizontalAlignment = HorizontalAlignment.Right,
                                 VerticalAlignment = VerticalAlignment.Center, IsChecked = IsOn(Current(s)) };
        // (Checked and Unchecked, not Click: the keyboard and accessibility tools change the box without a click)
        box.Checked += (_, _) => Write(s, "1");
        box.Unchecked += (_, _) => Write(s, "0");
        return box;
    }

    static bool IsOn(string value) => value.Trim().ToLowerInvariant() is not ("0" or "false" or "off" or "no" or "");

    FrameworkElement ChoiceControl(Setting s)
    {
        var choices = s.Choices!.ToList();
        var now = Current(s);
        var at = choices.FindIndex(c => string.Equals(c.Value, now, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            choices.Add((now, Tr.F("{0} (from totk.ini)", now)));   // a value the menu does not list keeps showing
            at = choices.Count - 1;
        }
        var box = new ComboBox { Style = St("Select"), VerticalAlignment = VerticalAlignment.Center,
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

    FrameworkElement SliderControl(Setting s)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, Width = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var slider = new Slider { Style = St("Range"), Minimum = s.Min, Maximum = s.Max, TickFrequency = s.Step,
                                  SmallChange = s.Step, LargeChange = s.Step * 4,
                                  Value = Math.Clamp(Parse(Current(s), Parse(s.Default, s.Min)), s.Min, s.Max) };
        var label = new TextBlock { Style = St("Muted"), HorizontalAlignment = HorizontalAlignment.Right,
                                    VerticalAlignment = VerticalAlignment.Center, Text = Shown(s, slider.Value) };
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
        writeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => FlushWrites(), Dispatcher);
        writeTimer.Stop();
        writeTimer.Start();
    }

    void FlushWrites()
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
            MessageBox.Show(this, Tr.F("totk.ini could not be written: {0}", x.Message), "Ultrahand Launcher");
        }
    }

    // ================= Launcher: Saves =================

    Button SmallButton(string text, string style = "Btn") =>
        new() { Style = St(style), Content = Tr.T(text), Height = 34, Padding = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };

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
            open.Click += (_, _) => OpenExplorer(profile!);
        }
        SettingsRows.Children.Add(RowOf(Tr.T("Save folder"), hasSaves ? profile : Tr.T("No installed game with a save folder was found."), open, false));

        var backup = SmallButton("Back up now", "BtnAccent");
        backup.IsEnabled = hasSaves;
        var status = new TextBlock { Style = St("Tiny"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                                     Text = backupNote ?? "", Visibility = backupNote == null ? Visibility.Collapsed : Visibility.Visible,
                                     Foreground = Br(backupOk ? "Ok" : "Bad") };
        backup.Click += async (_, _) =>
        {
            backup.IsEnabled = false;
            backup.Content = Tr.T("Backing up…");
            try
            {
                var zip = await Task.Run(() => SaveBackup.Create(profile!));
                backupOk = true;
                backupNote = Tr.F("Saved {0} ({1:0.0} MB).", Path.GetFileName(zip), new FileInfo(zip).Length / 1048576.0);
            }
            catch (Exception x)
            {
                backupOk = false;
                backupNote = Tr.F("The backup failed: {0}", x.Message);
            }
            if (launcherSection && launcherTab == 0 && SettingsPage.Visibility == Visibility.Visible) ShowTab(0);   // (the list below grows)
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Back up the save"),
            Tr.T("Zips the save folder: your saves, the game's own backup copy of them, the account id and the play time. Close the game " +
            "first: a backup made while the game is saving can catch a save half written."), backup, false, status));

        var backups = SaveBackup.List();
        var listed = backups.Length == 0
            ? Tr.T("None yet.")
            : string.Join("\n", backups.Take(5).Select(f => $"{Path.GetFileNameWithoutExtension(f.Name)}  ·  {f.Length / 1048576.0:0.0} MB")) +
              (backups.Length > 5 ? "\n" + Tr.F("and {0} older", backups.Length - 5) : "");
        Button? folderButton = null;
        if (Directory.Exists(SaveBackup.Folder))
        {
            folderButton = SmallButton("Open folder");
            folderButton.Click += (_, _) => OpenExplorer(SaveBackup.Folder);
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
        if (PickFolder(Tr.T("Choose where the downloads go")) is not { } picked) return false;
        BuildButton.IsEnabled = false;   // (no install while the package moves)
        try
        {
            await Task.Run(() => SetupPackage.MoveHome(picked, text => Dispatcher.Invoke(() => note(text))));
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

    async void ChangeDownloadsClick(object sender, RoutedEventArgs e)
    {
        DownloadsButton.IsEnabled = false;
        string? error = null;
        await ChangeDownloads(text => { PackageNote.Text = text; error = text; });   // (progress lines, then "" or the error)
        DownloadsButton.IsEnabled = true;
        if (!string.IsNullOrEmpty(error))
        {
            PackageNote.Text = error;
            PackageNote.Foreground = Br("Bad");
        }
    }

    void ShowGeneralTab()
    {
        Note(Tr.F("Ultrahand Launcher {0}, installed in {1}.", LauncherUpdate.Current, App.InstallDir));
        var folder = SettingsFolder();
        CheckBox Toggle(bool on, Action<bool> set)
        {
            var box = new CheckBox { Style = St("Check"), VerticalAlignment = VerticalAlignment.Center, IsChecked = on };
            box.Checked += (_, _) => set(true);
            box.Unchecked += (_, _) => set(false);
            return box;
        }
        void Shortcut(bool desktop, bool value)
        {
            if (desktop) settings.DesktopShortcut = value;
            else settings.StartShortcut = value;
            settings.Save();
            Shortcuts.Make(folder, settings.DesktopShortcut, settings.StartShortcut);
        }

        Heading("Language");
        var codes = new[] { "" }.Concat(Tr.Languages.Select(l => l.Code)).ToList();
        var names = new[] { Tr.T("Automatic (Windows language)") }.Concat(Tr.Languages.Select(l => l.Name)).ToList();
        var language = new ComboBox { Style = St("Select"), VerticalAlignment = VerticalAlignment.Center, ItemsSource = names,
                                      SelectedIndex = Math.Max(0, codes.IndexOf(settings.UiLang ?? "")) };
        var restart = SmallButton("Restart now", "BtnAccent");
        restart.Margin = new Thickness(0, 8, 0, 0);
        restart.HorizontalAlignment = HorizontalAlignment.Left;
        var restartNote = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        restartNote.Children.Add(new TextBlock { Style = St("Tiny"), Foreground = Br("AccentLight"), TextWrapping = TextWrapping.Wrap,
                                                 Text = Tr.T("The new language applies when the launcher starts again.") });
        restartNote.Children.Add(restart);
        language.SelectionChanged += (_, _) =>
        {
            settings.UiLang = codes[Math.Max(0, language.SelectedIndex)];
            settings.Save();
            restartNote.Visibility = Visibility.Visible;
        };
        restart.Click += (_, _) =>
        {
            if (Environment.ProcessPath is not { } exe) return;
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            if (launchFolder.Length > 0) { start.ArgumentList.Add("--launch"); start.ArgumentList.Add(launchFolder); }
            try { Process.Start(start); Application.Current.Shutdown(); } catch (Exception) { }
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
        var downloadsStatus = new TextBlock { Style = St("Tiny"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                                             Visibility = Visibility.Collapsed };
        downloadsButton.Click += async (_, _) =>
        {
            downloadsButton.IsEnabled = false;
            downloadsStatus.Visibility = Visibility.Visible;
            downloadsStatus.Foreground = Br("TextDim");
            downloadsStatus.Text = "";
            var changed = await ChangeDownloads(text => downloadsStatus.Text = text);
            downloadsButton.IsEnabled = true;
            if (changed && launcherSection && SettingsPage.Visibility == Visibility.Visible) ShowTab(launcherTab);   // (shows the new folder)
            else if (downloadsStatus.Text.Length == 0) downloadsStatus.Visibility = Visibility.Collapsed;
            else downloadsStatus.Foreground = Br("Bad");
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Downloads folder"),
            SetupPackage.Home + "\n" + Tr.T("Where the TotK Recomp setup package is downloaded and kept (about 2 GB once unpacked). Changing it " +
            "moves the packages already there."), downloadsButton, true, downloadsStatus));

        Heading("Steam");
        var steamFound = SteamImport.SteamDir() != null;
        var inSteam = folder != null && steamFound && SteamImport.Has(folder);
        var steamButton = SmallButton("Add to Steam", "BtnAccent");
        if (inSteam) steamButton.Content = Tr.T("Update in Steam");
        steamButton.IsEnabled = folder != null && steamFound;
        var steamStatus = new TextBlock { Style = St("Tiny"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                                          Visibility = Visibility.Collapsed };
        steamButton.Click += async (_, _) =>
        {
            steamButton.IsEnabled = false;
            steamStatus.Visibility = Visibility.Visible;
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
        var status = new TextBlock { Style = St("Tiny"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            var found = await LauncherUpdate.Check();
            check.IsEnabled = true;
            status.Visibility = Visibility.Visible;
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
                LauncherUpdateButton.Visibility = Visibility.Visible;
            }
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Launcher version"),
            Tr.F("You have {0}. The launcher looks for a new version each time it opens.", LauncherUpdate.Current), check, false, status));
        var page = SmallButton("Open");
        page.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(ProjectPage) { UseShellExecute = true }); } catch (Exception) { }
        };
        SettingsRows.Children.Add(RowOf(Tr.T("Project page"), Tr.T("Releases, changelog and bug reports:") + " " + ProjectPage, page, true));
    }
}
