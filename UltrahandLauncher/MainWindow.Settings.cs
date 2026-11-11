using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ultrahand;

/// The game settings page behind the gear: the totk.ini settings of the game's own menu (GameSettings.cs), read from and written
/// to the totk.ini of the installed game folder as the game itself does.
public partial class MainWindow
{
    TotkIni? ini;
    FrameworkElement? settingsReturn;   // the page the gear was clicked on
    int settingsTab;
    readonly Dictionary<string, FrameworkElement> settingControls = new();
    readonly Dictionary<string, (Setting Setting, string Value)> pendingWrites = new();
    DispatcherTimer? writeTimer;
    bool settingsProgrammatic;

    string? SettingsFolder() =>
        Installed.Has(launchFolder) ? launchFolder : Installed.Has(settings.Dest) ? settings.Dest : null;

    void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (!Main.IsHitTestVisible || SettingsPage.Visibility == Visibility.Visible) return;
        if (run != null || download != null)
        {
            MessageBox.Show(this, "Wait for the current run to finish, then change the settings.", "Ultrahand Launcher");
            return;
        }
        if (SettingsFolder() is not { } folder)
        {
            MessageBox.Show(this, "Install the game first: its settings live in totk.ini next to totk.exe.", "Ultrahand Launcher");
            return;
        }
        ini = new TotkIni(folder);
        var running = Process.GetProcessesByName("totk").Any(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, Installed.GameExe(folder), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        });
        SettingsNote.Text = running
            ? "The game is running: close it first, or most of these take effect only at its next start."
            : $"The same settings as the game's menu (Esc / F1), saved in {ini.Path}.";
        SettingsNote.Foreground = (Brush)FindResource(running ? "Warn" : "TextDim");
        settingsReturn = new FrameworkElement[] { FilesPage, ReadyPage, ProblemPage, LaunchPage }.FirstOrDefault(p => p.Visibility == Visibility.Visible);
        SettingsTabs.Children.Clear();
        for (var i = 0; i < GameSettings.Tabs.Length; i++)
        {
            var index = i;
            var item = new RadioButton { Style = (Style)FindResource("NavItem"), Content = GameSettings.Tabs[i].Name, GroupName = "settings",
                                         IsChecked = i == settingsTab };
            item.Checked += (_, _) => ShowSettingsTab(index);
            SettingsTabs.Children.Add(item);
        }
        ShowSettingsTab(settingsTab);
        ShowPage(SettingsPage);
    }

    void SettingsDone(object sender, RoutedEventArgs e)
    {
        FlushWrites();
        if (settingsReturn != null) ShowPage(settingsReturn);
        else if (launchFolder.Length > 0) ShowLauncher(launchFolder);
        else ShowFiles();
    }

    void ShowSettingsTab(int index)
    {
        settingsTab = index;
        settingControls.Clear();
        SettingsRows.Children.Clear();
        foreach (var group in GameSettings.Tabs[index].Groups)
        {
            if (group.Title != null)
                SettingsRows.Children.Add(new TextBlock { Text = group.Title.ToUpperInvariant(), Style = (Style)FindResource("Caps"),
                                                          Margin = new Thickness(0, 18, 0, 2) });
            for (var i = 0; i < group.Rows.Length; i++) SettingsRows.Children.Add(Row(group.Rows[i], i == group.Rows.Length - 1));
        }
        SettingsScroll.ScrollToTop();
    }

    string Current(Setting s) => ini?.Get(s.Key) ?? s.Default;

    FrameworkElement Row(Setting s, bool last)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var texts = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = s.Label, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = s.Description, Style = (Style)FindResource("Tiny"), TextWrapping = TextWrapping.Wrap,
                                           Margin = new Thickness(0, 3, 0, 0) });
        grid.Children.Add(texts);
        var control = s.Toggle ? ToggleControl(s) : s.Choices != null ? ChoiceControl(s) : SliderControl(s);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        settingControls[s.Key] = control;
        return new Border { Child = grid, BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
    }

    FrameworkElement ToggleControl(Setting s)
    {
        var box = new CheckBox { Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Right,
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
            choices.Add((now, $"{now} (from totk.ini)"));   // a value the menu does not list keeps showing
            at = choices.Count - 1;
        }
        var box = new ComboBox { Style = (Style)FindResource("Select"), VerticalAlignment = VerticalAlignment.Center,
                                 ItemsSource = choices.Select(c => c.Name).ToList(), SelectedIndex = at, Tag = choices };
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
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var slider = new Slider { Style = (Style)FindResource("Range"), Minimum = s.Min, Maximum = s.Max, TickFrequency = s.Step,
                                  SmallChange = s.Step, LargeChange = s.Step * 4,
                                  Value = Math.Clamp(Parse(Current(s), Parse(s.Default, s.Min)), s.Min, s.Max) };
        var label = new TextBlock { Style = (Style)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Right,
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
            MessageBox.Show(this, $"totk.ini could not be written: {x.Message}", "Ultrahand Launcher");
        }
    }
}
