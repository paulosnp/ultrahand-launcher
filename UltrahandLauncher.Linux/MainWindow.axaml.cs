using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;
using IOPath = System.IO.Path;

namespace Ultrahand;

/// The launcher and installer window on Linux: Files > Install > Play for a new install, and the launcher page (Play, plus Update
/// when a new release is out) for an installed folder. It follows UltrahandLauncher/MainWindow.xaml.cs page by page; the work
/// itself is "python -m installer" from a setup package (Installer.cs), which on Linux is the unpacked AppImage (Releases.cs).
///
///     UltrahandLauncher                     the launcher of the last installed folder, else the install page
///     UltrahandLauncher --launch FOLDER     the launcher of that folder (what the shortcuts run)
///     UltrahandLauncher --update FOLDER     update that folder to the latest release
///     UltrahandLauncher --rebuild FOLDER    rebuild for code mods and start the game (totk runs it through build-cache/rebuild.txt)
///
/// The settings pages, the crash page and the release notes are in MainWindow.Settings.cs.
public partial class MainWindow : Window
{
    enum Kind { Install, Resume, Update, Verify, Rebuild }

    static readonly string[] StepOrder = { "check", "fonts", "extract", "lift", "compile", "prepare", "dlss", "cleanup" };
    /// (names used until the setup package's text catalog is loaded)
    static readonly Dictionary<string, string> StepNames = new()
    {
        ["download"] = "Download the setup package",
        ["check"] = "Checking the computer and the game files",
        ["fonts"] = "Extracting the system fonts",
        ["extract"] = "Extracting the game",
        ["lift"] = "Translating the game code to C",
        ["compile"] = "Compiling totk.exe",
        ["prepare"] = "Preparing the shaders",
        ["dlss"] = "Downloading NVIDIA DLSS",
        ["cleanup"] = "Finishing",
    };
    /// Each step's share of the overall progress: compiling and the shaders take most of the time
    static readonly Dictionary<string, double> Weight = new()
    {
        ["download"] = 6, ["check"] = 2, ["fonts"] = 1, ["extract"] = 14, ["lift"] = 14, ["compile"] = 40, ["prepare"] = 20, ["dlss"] = 2, ["cleanup"] = 1,
    };
    const long NeedFirst = 36_000_000_000, NeedUpdate = 20_000_000_000;   // (docs/installer.md: 36 GiB at the peak of a first install)

    readonly Settings settings = Settings.Load();
    Catalog cat = new();
    SetupPackage? localPackage;
    Release? latest;
    bool latestChecked;
    Task<Release?>? latestTask;

    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch elapsed = new();
    readonly Dictionary<string, StageRow> rows = new();
    readonly List<string> order = new();
    readonly HashSet<string> finished = new();
    readonly StringBuilder log = new();
    readonly List<string> warnings = new();
    readonly HashSet<string> touched = new();
    bool programmatic, langTouched, closing, closed, asking, logDirty;

    // the current run
    Kind kind;
    InstallerRun? run;
    CancellationTokenSource? download;
    SetupPackage? runPackage;
    List<string> runArgs = new();
    string runDest = "", launchFolder = "", downloadNote = "", lastFailure = "", lastPhase = "";
    string? current, runRelease, logPath, result;
    double currentFraction;
    (string Id, JsonElement? Params)? error;

    IBrush Br(string key) => (IBrush)this.FindResource(key)!;

    public MainWindow()
    {
        InitializeComponent();
        Tr.Apply(this);
        LauncherVersionText.Text = "v" + LauncherUpdate.Current;
        wasInstalledBefore = settings.LauncherShortcuts;
        LanguageBox.ItemsSource = GameLanguages.All;
        DlssPanel.IsVisible = !Platform.Linux;   // (the DLSS download is nvngx_dlss.dll, for the Windows game)
        clock.Tick += (_, _) => Tick();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, Dropped);
        Closing += OnClosing;
        Closed += (_, _) => closed = true;
        Opened += (_, _) => Start();
        SetStep(0);
    }

    void Post(Action action) => Dispatcher.UIThread.Post(action);

    // ================= splash =================

    async void Start()
    {
        var started = Stopwatch.StartNew();
        var args = Environment.GetCommandLineArgs();
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? FullPath(args[i + 1].Trim().Trim('"').TrimEnd('\\', '/')) : null;
        }
        localPackage = SetupPackage.Newest();
        ApplySettings();
        latestTask = CheckLatest();
        CheckLauncher();
        if (LauncherUpdate.CanApply && !settings.LauncherShortcuts)
        {
            // (the first run of the installed copy: shortcuts to the launcher, or to the game folder it already knows)
            Shortcuts.Make(Installed.Has(settings.Dest) ? settings.Dest : null, settings.DesktopShortcut, settings.StartShortcut);
            settings.LauncherShortcuts = true;
            settings.Save();
        }
        if (Arg("--rebuild") is { } rebuild)
        {
            SplashText.Text = Tr.T("Rebuilding for your mods…");
            await Task.Delay(700);
            ShowMain();
            launchFolder = rebuild;
            BeginFor(rebuild, Kind.Rebuild);
            return;
        }
        if (Arg("--update") is { } update)
        {
            SplashText.Text = Tr.T("Getting the update…");
            await Task.Delay(700);
            ShowMain();
            launchFolder = update;
            BeginFor(update, Kind.Update);
            return;
        }
        var launch = Arg("--launch") ?? (Installed.Has(settings.Dest) ? settings.Dest : null);
        if (launch != null)
        {
            SplashText.Text = Tr.T("Starting TotK Recomp…");
            await Task.Delay(700);
            ShowMain();
            ShowLauncher(launch);
            ShowNotesIfUpdated();
            return;
        }
        SplashText.Text = localPackage != null ? Tr.F("Setup package {0} found", Versions.Short(localPackage.Version)) : Tr.T("Looking for the latest version…");
        var wait = 1200 - (int)started.ElapsedMilliseconds;
        if (wait > 0) await Task.Delay(wait);
        ShowMain();
        ShowFiles();
        ShowNotesIfUpdated();
    }

    void ShowMain()
    {
        Splash.IsVisible = false;
        Main.IsVisible = true;
    }

    /// The latest GitHub release, once per run; the pages refresh when it arrives
    async Task<Release?> CheckLatest()
    {
        try { latest = await Releases.Latest(settings.RepoName); }
        catch (Exception) { latest = null; }
        latestChecked = true;
        RefreshPackageNote();
        RefreshMode();
        RefreshLaunchStatus();
        return latest;
    }

    // ================= launcher update =================

    LauncherUpdate.Update? launcherUpdate;

    /// A newer launcher release; the button shows only in the installed copy, which can replace itself
    async void CheckLauncher()
    {
        if (!LauncherUpdate.CanApply) return;
        launcherUpdate = await LauncherUpdate.Check();
        if (launcherUpdate == null) return;
        LauncherUpdateButton.Content = Tr.F("Update launcher to {0}", launcherUpdate.Version);
        ToolTip.SetTip(LauncherUpdateButton, Tr.F("Ultrahand Launcher {0} is installed. The game is not touched.", LauncherUpdate.Current));
        LauncherUpdateButton.IsVisible = true;
    }

    async void LauncherUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (launcherUpdate == null) return;
        if (run != null || download != null)
        {
            await Dialogs.Ask(this, Tr.T("Wait for the current run to finish, then update the launcher."));
            return;
        }
        LauncherUpdateButton.IsEnabled = false;
        try
        {
            await LauncherUpdate.Apply(launcherUpdate, f => Post(() =>
                LauncherUpdateButton.Content = Tr.F("Updating launcher… {0}%", (int)(f * 100))), CancellationToken.None);
            Close();   // (the new launcher is already starting)
        }
        catch (Exception x)
        {
            await Dialogs.Ask(this, Tr.F("The launcher could not be updated: {0}", x.Message));
            LauncherUpdateButton.Content = Tr.F("Update launcher to {0}", launcherUpdate.Version);
            LauncherUpdateButton.IsEnabled = true;
        }
    }

    /// The version an install or update gets now: the GitHub release when it is newer than the package on this PC
    string? TargetVersion()
    {
        var have = localPackage?.Version;
        return latest != null && (have == null || Versions.Newer(latest.Tag, have)) ? latest.Tag : have;
    }

    void ApplySettings()
    {
        programmatic = true;
        BasePath.Text = settings.Base ?? "";
        UpdatePath.Text = settings.Update ?? "";
        KeysPath.Text = settings.Keys ?? "";
        FirmwarePath.Text = settings.Firmware ?? "";
        OutPath.Text = settings.Dest ?? IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "TotK");
        LanguageBox.SelectedItem = GameLanguages.Find(settings.Lang) ?? GameLanguages.Find("en-US");
        DlssTick.IsChecked = settings.Dlss;
        DesktopTick.IsChecked = settings.DesktopShortcut;
        StartTick.IsChecked = settings.StartShortcut;
        programmatic = false;
    }

    void SaveSettings()
    {
        settings.Base = Value(BasePath);
        settings.Update = Value(UpdatePath);
        settings.Keys = Value(KeysPath);
        settings.Firmware = Value(FirmwarePath);
        settings.Dest = FullPath(Text(OutPath));
        settings.Lang = (LanguageBox.SelectedItem as GameLanguages.Item)?.Code;
        settings.Dlss = DlssTick.IsChecked == true;
        settings.DesktopShortcut = DesktopTick.IsChecked == true;
        settings.StartShortcut = StartTick.IsChecked == true;
        settings.Save();
    }

    static string Text(TextBox box) => (box.Text ?? "").Trim();

    static string? Value(TextBox box) => Text(box) is { Length: > 0 } v ? FullPath(v) : null;

    static string FullPath(string path)
    {
        try { return IOPath.GetFullPath(path); }
        catch (Exception) { return path; }
    }

    // ================= the stepper: 1 Files - 2 Install - 3 Play =================

    void SetStep(int on)
    {
        Stepper.Children.Clear();
        var names = new[] { Tr.T("Files"), Tr.T("Install"), Tr.T("Play") };
        for (var i = 0; i < names.Length; i++)
        {
            if (i > 0)
                Stepper.Children.Add(new Rectangle { Width = 28, Height = 1, Fill = Br(i <= on ? "Accent" : "LineStrong"),
                                                     VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
            var done = i < on;
            var badge = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) };
            badge.Children.Add(new Ellipse
            {
                Fill = Br(i == on ? "Accent" : done ? "AccentSoft" : "Surface"),
                Stroke = i > on ? Br("LineStrong") : null, StrokeThickness = 1,
            });
            if (done)
                badge.Children.Add(new Path { Data = Geometry.Parse("M0,4 L3.5,7.5 L10,0"), Stroke = Br("AccentLight"), StrokeThickness = 1.8,
                                              HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else
                badge.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeight.Bold,
                                                   Foreground = Br(i == on ? "AccentText" : "TextMuted"),
                                                   HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = names[i], FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                                        FontWeight = i == on ? FontWeight.SemiBold : FontWeight.Medium,
                                        Foreground = Br(i == on ? "Text" : done ? "TextDim" : "TextMuted") };
            Stepper.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { badge, label } });
        }
        Stepper.IsVisible = true;
        VersionTag.IsVisible = false;
    }

    /// An update, verify or rebuild shows what it works on instead of the stepper
    void ShowTag(string text)
    {
        Stepper.IsVisible = false;
        VersionTag.IsVisible = true;
        VersionTagText.Text = text;
    }

    void ShowPage(Control page)
    {
        foreach (var p in new Control[] { FilesPage, BuildPage, ReadyPage, ProblemPage, LaunchPage, SettingsPage, CrashPage })
            p.IsVisible = p == page;
    }

    // ================= launcher =================

    void ShowLauncher(string folder)
    {
        launchFolder = folder;
        Stepper.IsVisible = false;
        VersionTag.IsVisible = false;
        var installed = Installed.Release(folder);
        LaunchVersion.Text = installed != null ? $"TotK Recomp {Versions.Short(installed)} · {folder}" : folder;
        LaunchPlay.IsEnabled = File.Exists(Installed.GameExe(folder));
        ShowPage(LaunchPage);
        RefreshLaunchStatus();
    }

    void RefreshLaunchStatus()
    {
        if (!LaunchPage.IsVisible || launchFolder.Length == 0) return;
        LaunchUpdate.IsVisible = false;
        LaunchStatus.Foreground = Br("TextDim");
        LaunchStatus.FontWeight = FontWeight.Normal;
        var installed = Installed.Release(launchFolder);
        if (installed == null)
        {
            LaunchStatus.Text = Tr.T("This folder has no TotK Recomp install. Use \"Install to another folder\".");
            return;
        }
        var target = TargetVersion();
        if (target != null && Versions.Newer(target, installed))
        {
            var fetch = latest != null && target == latest.Tag && (localPackage == null || Versions.Newer(latest.Tag, localPackage.Version));
            LaunchStatus.Text = Tr.F("Update available: {0}", Versions.Short(target)) + (fetch ? Tr.F(" · {0} download", latest!.SizeText) : "");
            LaunchStatus.Foreground = Br("AccentLight");
            LaunchStatus.FontWeight = FontWeight.SemiBold;
            LaunchUpdate.IsVisible = true;
        }
        else if (!latestChecked) LaunchStatus.Text = Tr.T("Looking for updates…");
        else if (latest == null) LaunchStatus.Text = Tr.T("Could not look for updates (GitHub did not answer).");
        else LaunchStatus.Text = Tr.F("Up to date · {0} is the latest version", latest.Tag);
    }

    void LaunchUpdateClick(object? sender, RoutedEventArgs e) => BeginFor(launchFolder, Kind.Update);

    void LaunchVerifyClick(object? sender, RoutedEventArgs e) => BeginFor(launchFolder, Kind.Verify);

    void LaunchPlayClick(object? sender, RoutedEventArgs e) => PlayAndWatch(launchFolder);

    void LaunchFolderClick(object? sender, RoutedEventArgs e) => Platform.OpenFolder(launchFolder);

    void LaunchOtherClick(object? sender, RoutedEventArgs e) => ShowFiles();

    void BackToLauncherClick(object? sender, RoutedEventArgs e)
    {
        if (launchFolder.Length > 0) ShowLauncher(launchFolder);
    }

    // ================= 1: game files =================

    void ShowFiles()
    {
        SetStep(0);
        FormError.IsVisible = false;
        RefreshPackageNote();
        Refresh();
        ShowPage(FilesPage);
    }

    void RefreshPackageNote()
    {
        if (PackageNote == null) return;
        var have = localPackage;
        PackageNote.Foreground = Br("TextDim");
        if (!latestChecked)
            PackageNote.Text = have != null
                ? Tr.F("Setup package {0} on this PC. Looking for newer versions in the GitHub releases…", Versions.Short(have.Version))
                : Tr.F("Looking for the setup package in the GitHub releases ({0})…", settings.RepoName);
        else if (latest != null && (have == null || Versions.Newer(latest.Tag, have.Version)))
            PackageNote.Text = Tr.F("The install downloads package {0} from the GitHub releases ({1}) to {2}.", latest.Tag, latest.SizeText, SetupPackage.Store) +
                               (have != null ? Tr.F(" This PC has {0}.", Versions.Short(have.Version)) : "");
        else if (have != null)
            PackageNote.Text = Tr.F("Setup package {0} on this PC", Versions.Short(have.Version)) +
                               (latest == null ? Tr.T(" (GitHub did not answer: newer versions could not be checked).") : Tr.T(", the latest version.")) +
                               "\n" + Tr.F("Downloads folder: {0}", SetupPackage.Home);
        else
        {
            PackageNote.Text = Tr.F("No setup package on this PC and GitHub did not answer ({0}). Check the connection, or unpack a " +
                               "TotK-Setup-<version>-windows.zip into a \"setup\" folder next to this launcher.", settings.RepoName);
            PackageNote.Foreground = Br("Bad");
        }
    }

    void PathsChanged(object? sender, TextChangedEventArgs e)
    {
        if (!programmatic)
        {
            if (sender == BasePath) touched.Add("base");
            else if (sender == UpdatePath) touched.Add("update");
        }
        Refresh();
    }

    void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!programmatic) langTouched = true;
    }

    void DlssChanged(object? sender, RoutedEventArgs e) => SaveSettings();

    void Refresh()
    {
        if (BuildButton == null || SpaceText == null) return;
        RefreshSpace();
        RefreshMode();
    }

    void RefreshSpace()
    {
        var dest = Text(OutPath);
        var need = Installed.Has(dest) ? NeedUpdate : NeedFirst;
        try
        {
            // (the disk of the nearest folder that exists: on Linux the install folder can be on any mount)
            var at = IOPath.GetFullPath(dest);
            while (!Directory.Exists(at) && IOPath.GetDirectoryName(at) is { } parent) at = parent;
            var disk = Platform.Linux ? at : IOPath.GetPathRoot(at)!;
            var free = new DriveInfo(disk).AvailableFreeSpace;
            var enough = free >= need;
            SpaceBar.Value = Math.Min(1, need / (double)Math.Max(1, free));
            SpaceBar.Foreground = Br(enough ? "Ok" : "Bad");
            SpaceText.Text = Tr.F("Needs {0} GB · {1} has {2} GB free", need / 1_000_000_000, Platform.Linux ? disk : disk.TrimEnd('\\'), free / 1_000_000_000);
            SpaceText.Foreground = Br(enough ? "TextDim" : "Bad");
        }
        catch (Exception)
        {
            SpaceBar.Value = 0;
            SpaceText.Text = Tr.F("Needs {0} GB", need / 1_000_000_000);
            SpaceText.Foreground = Br("TextDim");
        }
    }

    /// The page title and the button: install, continue an install, or update the chosen folder
    void RefreshMode()
    {
        if (BuildButton == null || FilesTitle == null || run != null) return;
        var dest = Text(OutPath);
        var installed = Installed.Has(dest) ? Installed.Release(dest) ?? "?" : null;
        var target = TargetVersion();
        if (installed != null)
        {
            if (!langTouched && GameLanguages.Find(Installed.Lang(dest)) is { } recorded)
            {
                programmatic = true;
                LanguageBox.SelectedItem = recorded;
                programmatic = false;
            }
            if (target != null && Versions.Newer(target, installed))
            {
                FilesTitle.Text = Tr.T("Update available");
                FilesSub.Text = Tr.F("This folder has TotK Recomp {0} and {1} is out. The update keeps the " +
                                "extracted game, your saves and your settings, and redoes only what changed.", Versions.Short(installed), Versions.Short(target));
                BuildButton.Content = Tr.T("Update");
            }
            else
            {
                FilesTitle.Text = Tr.T("This folder already has the game");
                FilesSub.Text = Tr.F("TotK Recomp {0}. Continue resumes an interrupted install and checks what is already done; " +
                                "the game files are only needed if they have to be extracted again.", Versions.Short(installed));
                BuildButton.Content = Tr.T("Continue");
            }
        }
        else
        {
            FilesTitle.Text = Tr.T("Install your game");
            FilesSub.Text = Tr.T("Pick your game files and where the game should go. The installer builds the game on your PC, once.");
            BuildButton.Content = Tr.T("Install the game");
        }
        BuildButton.IsEnabled = dest.Length > 2;
        BackToLauncher.IsVisible = launchFolder.Length > 0;
    }

    TextBox Box(string which) => which switch { "base" => BasePath, "update" => UpdatePath, "keys" => KeysPath, _ => FirmwarePath };

    async void BrowseFile(object? sender, RoutedEventArgs e)
    {
        var which = (string)((Button)sender!).Tag!;
        // (the Windows launcher's filter text, "name|patterns|name|patterns": the first name is the one shown here)
        var keys = which == "keys";
        var filter = keys ? Tr.T("Key files (*.keys)|*.keys|All files|*.*") : Tr.T("Game files (NSP, XCI, NSZ, XCZ)|*.nsp;*.xci;*.nsz;*.xcz|All files|*.*");
        var patterns = keys ? new[] { "*.keys" } : new[] { "*.nsp", "*.xci", "*.nsz", "*.xcz", "*.NSP", "*.XCI", "*.NSZ", "*.XCZ" };
        var options = new FilePickerOpenOptions
        {
            Title = Tr.T(which switch { "base" => "Choose the base game file", "update" => "Choose the 1.1.0, 1.2.1 or 1.4.3 update", _ => "Choose prod.keys" }),
            FileTypeFilter = new[] { new FilePickerFileType(filter.Split('|')[0]) { Patterns = patterns }, FilePickerFileTypes.All },
        };
        var now = Text(Box(which));
        if (now.Length > 0 && IOPath.GetDirectoryName(now) is { } dir && Directory.Exists(dir))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var picked = await StorageProvider.OpenFilePickerAsync(options);
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) Box(which).Text = path;
    }

    async void BrowseFirmware(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder(Tr.T("Choose the Switch firmware folder (Contents/registered)")) is { } f) FirmwarePath.Text = f;
    }

    async void BrowseOut(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder(Tr.T("Choose where the game goes")) is { } f) OutPath.Text = f;
    }

    async Task<string?> PickFolder(string title)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    /// Files dropped on the window: .keys, game files (the update recognised by its name), the firmware folder or the install folder
    void Dropped(object? sender, DragEventArgs e)
    {
        if (run != null || download != null || !FilesPage.IsVisible) return;
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths == null || paths.Count == 0) return;
        e.Handled = true;
        foreach (var p in paths)
        {
            var ext = IOPath.GetExtension(p).ToLowerInvariant();
            if (File.Exists(p) && ext == ".keys") KeysPath.Text = p;
            else if (File.Exists(p) && ext is ".nsp" or ".xci" or ".nsz" or ".xcz")
            {
                if (LooksLikeUpdate(p)) UpdatePath.Text = p;
                else if (Text(BasePath).Length == 0 || ext is ".xci" or ".xcz") BasePath.Text = p;
                else UpdatePath.Text = p;
            }
            else if (Directory.Exists(p))
            {
                if (IOPath.GetFileName(p).Equals("registered", StringComparison.OrdinalIgnoreCase) ||
                    Directory.EnumerateFiles(p, "*.nca").Any() || Directory.EnumerateDirectories(p, "*.nca").Any()) FirmwarePath.Text = p;
                else OutPath.Text = p;
            }
        }
    }

    /// (the TotK update's title id ends in 800, and file names usually carry the version)
    static bool LooksLikeUpdate(string path)
    {
        var name = IOPath.GetFileName(path);
        return new[] { "0100F2C0115B6800", "update", "upd", "1.1.0", "1.2.1", "1.4.3", "v131072", "v262144", "v655360" }
            .Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    List<string> Problems(string dest, bool install)
    {
        var p = new List<string>();
        if (dest.Length < 3) p.Add(Tr.T("Choose the install folder."));
        foreach (var (box, need, missing) in new[]
                 {
                     (BasePath, "Choose the base game file.", "The base game file does not exist."),
                     (UpdatePath, "Choose the 1.1.0, 1.2.1 or 1.4.3 update file.", "The update file does not exist."),
                     (KeysPath, "Choose the prod.keys file.", "The keys file does not exist."),
                 })
        {
            var v = Text(box);
            if (v.Length == 0) { if (install) p.Add(Tr.T(need)); }
            else if (!File.Exists(v)) p.Add(Tr.T(missing));
        }
        if (Text(FirmwarePath) is { Length: > 0 } fw && !Directory.Exists(fw)) p.Add(Tr.T("The firmware folder does not exist."));
        if (LanguageBox.SelectedItem is not GameLanguages.Item) p.Add(Tr.T("Choose the game language."));
        return p;
    }

    void StartInstall(object? sender, RoutedEventArgs e)
    {
        var dest = FullPath(Text(OutPath));
        var installed = Installed.Release(dest);
        var install = !Installed.Has(dest);
        var problems = Problems(dest, install);
        FormError.Text = string.Join(" ", problems);
        FormError.IsVisible = problems.Count > 0;
        if (problems.Count > 0) return;
        SaveSettings();
        var args = new List<string> { "--dest", dest };
        void Add(string flag, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) args.AddRange(new[] { flag, FullPath(value.Trim()) });
        }
        // (for an installed folder the base game and update are passed only when picked now: paths kept from another install would
        // make the extraction run again, as installer/gui.py match_prefill avoids too)
        if (install || touched.Contains("base")) Add("--base", BasePath.Text);
        if (install || touched.Contains("update")) Add("--update", UpdatePath.Text);
        Add("--keys", KeysPath.Text);
        Add("--firmware", FirmwarePath.Text);
        if (LanguageBox.SelectedItem is GameLanguages.Item lang) args.AddRange(new[] { "--lang", lang.Code });
        if (!Platform.Linux && DlssTick.IsChecked == true) args.Add("--dlss");
        var k = install ? Kind.Install : Versions.Newer(TargetVersion(), installed) ? Kind.Update : Kind.Resume;
        _ = Begin(dest, args, k);
    }

    // ================= 2: the run =================

    /// Update, verify or rebuild an installed folder: only --dest (the language and inputs come from its totk-install.json)
    void BeginFor(string folder, Kind k)
    {
        var args = new List<string> { "--dest", folder };
        if (!Platform.Linux && Installed.HasDlss(folder)) args.Add("--dlss");
        if (settings.Keys is { } keys && File.Exists(keys)) args.AddRange(new[] { "--keys", keys });
        if (k == Kind.Verify) args.Add("--verify");
        _ = Begin(folder, args, k);
    }

    async Task Begin(string dest, List<string> args, Kind k)
    {
        kind = k;
        runDest = dest;
        runArgs = args;
        runPackage = null;
        error = null;
        result = runRelease = logPath = current = null;
        currentFraction = 0;
        lastPhase = downloadNote = "";
        warnings.Clear();
        log.Clear();
        Log.Text = "";
        WarnText.IsVisible = false;
        ShowLog.IsChecked = false;
        var installed = Installed.Release(dest);
        switch (k)
        {
            case Kind.Install:
            case Kind.Resume:
                SetStep(1);
                break;
            case Kind.Update:
                ShowTag($"{Versions.Short(installed)} → {Versions.Short(TargetVersion())}");
                break;
            case Kind.Verify:
                ShowTag(Tr.F("Verify · {0}", Versions.Short(installed)));
                break;
            case Kind.Rebuild:
                ShowTag(Tr.F("Mods · {0}", Versions.Short(installed)));
                break;
        }
        BuildTitle.Text = Tr.T(k switch
        {
            Kind.Install => "Installing your game",
            Kind.Resume => "Continuing the install",
            Kind.Update => "Updating your game",
            Kind.Verify => "Verifying the files",
            _ => "Rebuilding for your mods",
        });
        BuildNote.Text = Tr.T("Getting ready…");
        CancelButton.IsEnabled = true;
        CancelButton.Content = Tr.T("Cancel");
        rows.Clear();   // (a new run: nothing of the previous run's rows is kept)
        finished.Clear();
        MakeRows(withDownload: false, StepOrder);
        SetProgress(0);
        ShowPage(BuildPage);
        elapsed.Restart();
        clock.Start();

        var package = await EnsurePackage(k);
        if (package == null || closed) return;
        runPackage = package;
        cat = Catalog.Load(package, Tr.InstallerLang);
        MakeRows(rows.ContainsKey("download"), StepOrder);   // (the step names in the package's own text)
        if (k == Kind.Update) ShowTag($"{Versions.Short(installed)} → {Versions.Short(package.Version)}");
        run = new InstallerRun(package, args);
        AddLog("> " + run.CommandLine);
        run.Event += ev => Post(() => OnEvent(ev));
        run.Line += line => Post(() => AddLog(line));
        run.Exited += code => Post(() => Ended(code));
        run.Start();
    }

    /// The package for this run: the one on this PC, or the latest GitHub release's when that is newer (or there is none).
    /// Verify and rebuild use the package of the installed version: they do not update.
    async Task<SetupPackage?> EnsurePackage(Kind k)
    {
        if (!latestChecked && latestTask != null)
        {
            BuildNote.Text = Tr.T("Looking for the latest version on GitHub…");
            await latestTask;
        }
        var keep = k is Kind.Verify or Kind.Rebuild;
        var have = keep ? SetupPackage.For(Installed.Release(runDest)) : SetupPackage.Newest();
        var fetch = latest != null && (have == null || !keep && Versions.Newer(latest.Tag, have.Version));
        if (!fetch)
        {
            if (have != null) return have;
            clock.Stop();
            ShowProblem(Tr.T("No setup package"),
                        Tr.F("This PC has no TotK Recomp setup package and the releases of {0} did not answer.\n\n" +
                        "Check the connection and try again, or download TotK-Setup-<version>-windows.zip from the releases page and unpack it " +
                        "into a \"setup\" folder next to this launcher ({1}).", settings.RepoName, AppContext.BaseDirectory));
            return null;
        }
        var release = latest!;
        MakeRows(withDownload: true, StepOrder);
        current = "download";
        currentFraction = 0;
        rows["download"].Begin();
        downloadNote = $"{release.Tag} · {release.SizeText}";
        rows["download"].Detail(downloadNote);
        download = new CancellationTokenSource();
        var token = download.Token;
        try
        {
            var package = await Task.Run(() => Releases.Fetch(release,
                f => Post(() => DownloadProgress(f)),
                n => Post(() => { downloadNote = n; AddLog(n); rows.GetValueOrDefault("download")?.Detail(n); }),
                token), token);
            rows["download"].Done();
            rows["download"].Detail(Tr.F("Package {0} ready", release.Tag));
            Finish("download");
            localPackage = SetupPackage.Newest();
            return package;
        }
        catch (OperationCanceledException)
        {
            clock.Stop();
            rows.GetValueOrDefault("download")?.Stopped();
            download = null;
            if (closing) Close();
            else AfterCancel();
            return null;
        }
        catch (Exception x)
        {
            rows.GetValueOrDefault("download")?.Stopped();
            AddLog("download: " + x.Message);
            if (have != null && k is Kind.Install or Kind.Resume)
            {
                // (installing with the package this PC already has beats stopping)
                rows["download"].Detail(Tr.F("Failed ({0}): using package {1} from this PC", x.Message, Versions.Short(have.Version)));
                return have;
            }
            clock.Stop();
            ShowProblem(Tr.T("The setup package could not be downloaded"),
                        x.Message + Tr.T("\n\nNothing was changed in the game folder. Try again in a few minutes, or download the zip from the releases page."));
            return null;
        }
        finally
        {
            download = null;
        }
    }

    void DownloadProgress(double f)
    {
        if (current != "download") return;
        currentFraction = f;
        rows.GetValueOrDefault("download")?.Detail($"{downloadNote} · {(int)(f * 100)}%");
        UpdateOverall();
    }

    string StepName(string id) =>
        id != "download" && Tr.InstallerLang == Tr.Code && cat.Has("step." + id) ? cat.Text("step." + id) : StepNames.TryGetValue(id, out var n) ? Tr.T(n) : id;

    /// The rows of the steps; rows that already exist keep their state
    void MakeRows(bool withDownload, IEnumerable<string> steps)
    {
        var old = new Dictionary<string, StageRow>(rows);
        var ids = new List<string>();
        if (withDownload) ids.Add("download");
        ids.AddRange(steps.Where(s => s != "download"));
        StageList.Children.Clear();
        rows.Clear();
        order.Clear();
        for (var i = 0; i < ids.Count; i++)
        {
            var last = i == ids.Count - 1;
            var row = old.TryGetValue(ids[i], out var had) ? had : new StageRow(StepName(ids[i]), last);
            row.SetLast(last);
            rows[ids[i]] = row;
            order.Add(ids[i]);
            StageList.Children.Add(row.Root);
        }
    }

    StageRow Row(string id)
    {
        if (!rows.ContainsKey(id)) MakeRows(rows.ContainsKey("download"), order.Append(id).ToList());
        return rows[id];
    }

    void Finish(string id)
    {
        finished.Add(id);
        if (current == id) current = null;
    }

    double W(string id) => Weight.TryGetValue(id, out var w) ? w : 2;

    void UpdateOverall()
    {
        var total = order.Sum(W);
        var done = order.Where(id => finished.Contains(id) && rows.ContainsKey(id)).Sum(W);
        if (current != null && !finished.Contains(current)) done += W(current) * Math.Clamp(currentFraction, 0, 1);
        SetProgress(total > 0 ? done / total : 0);
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static JsonElement? Params(JsonElement e) => e.TryGetProperty("params", out var p) ? p : null;

    /// One installer event (installer/events.py): start, plan, step, progress, space, warning, error, end
    void OnEvent(JsonElement e)
    {
        switch (Str(e, "type"))
        {
            case "start":
                runRelease = Str(e, "release") is { Length: > 0 } rel ? rel : null;
                logPath = Str(e, "log");
                if (e.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                {
                    var ids = steps.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => s.Length > 0).ToList();
                    if (!ids.SequenceEqual(order.Where(id => id != "download"))) MakeRows(rows.ContainsKey("download"), ids);
                }
                if (Str(e, "mode") == "update" && kind is Kind.Install or Kind.Resume) BuildTitle.Text = Tr.T("Updating your game");
                break;
            case "plan":
                if (e.TryGetProperty("skip", out var skip) && skip.ValueKind == JsonValueKind.Object)
                    foreach (var s in skip.EnumerateObject())
                        if (rows.TryGetValue(s.Name, out var r) && !r.Started)
                        {
                            r.Skipped(s.Value.ValueKind == JsonValueKind.String ? s.Value.GetString() ?? "" : "");
                            Finish(s.Name);
                        }
                break;
            case "step":
            {
                var id = Str(e, "step");
                if (id.Length == 0) break;
                var row = Row(id);
                switch (Str(e, "state"))
                {
                    case "started":
                        row.Begin();
                        current = id;
                        currentFraction = 0;
                        finished.Remove(id);
                        break;
                    case "done":
                        row.Done(Num(e, "seconds"));
                        Finish(id);
                        break;
                    case "skipped":
                        row.Skipped(Str(e, "reason"));
                        Finish(id);
                        break;
                }
                break;
            }
            case "progress":
            {
                var id = Str(e, "step");
                if (id.Length == 0) break;
                var row = Row(id);
                if (!row.Started) row.Begin();
                current = id;
                var text = cat.Text($"phase.{id}.{Str(e, "phase")}");
                if (Num(e, "total") is double total && total > 0)
                {
                    var done = Num(e, "done") ?? 0;
                    text += "  " + Amount(done, total, Str(e, "unit"));
                    currentFraction = done / total;
                }
                row.Detail(text);
                break;
            }
            case "warning":
                warnings.Add(cat.Text(Str(e, "id"), Params(e)));
                WarnText.Text = string.Join("\n", warnings);
                WarnText.IsVisible = true;
                break;
            case "error":
                error = (Str(e, "id"), Params(e));
                if (rows.TryGetValue(Str(e, "step"), out var failed)) failed.Stopped();
                break;
            case "end":
                result = Str(e, "result");
                break;
        }
        if (Describe(e) is { Length: > 0 } line) AddLog(line);
        UpdateOverall();
    }

    static string Amount(double done, double total, string unit) => unit switch
    {
        "bytes" => string.Format(CultureInfo.InvariantCulture, "{0:0.0} / {1:0.0} GB", done / 1e9, total / 1e9),
        "objects" => Tr.F("{0:0} / {1:0} files", done, total),
        "shaders" => $"{done:0} / {total:0} shaders",
        "chunks" => Tr.F("{0:0} / {1:0} parts", done, total),
        _ => $"{done:0} / {total:0} {unit}".TrimEnd(),
    };

    /// The log line for an event (as installer/guilogic.py describe_event writes it); progress only when the phase changes
    string Describe(JsonElement e)
    {
        switch (Str(e, "type"))
        {
            case "start":
                return $"installer {Str(e, "release")} ({Str(e, "mode")}) in {Str(e, "dest")} · log: {Str(e, "log")}";
            case "step":
                var extra = Num(e, "seconds") is { } sec ? string.Format(CultureInfo.InvariantCulture, " ({0:0.0} s)", sec)
                          : Str(e, "reason") is { Length: > 0 } reason ? $" ({reason})" : "";
                return $"{StepName(Str(e, "step"))}: {Str(e, "state")}{extra}";
            case "progress":
                var key = Str(e, "step") + "." + Str(e, "phase");
                if (key == lastPhase) return "";
                lastPhase = key;
                return "  " + cat.Text($"phase.{key}");
            case "space":
                return string.Format(CultureInfo.InvariantCulture, "Space needed: {0:0.0} GB, free: {1:0.0} GB",
                                     (Num(e, "need") ?? 0) / 1e9, (Num(e, "free") ?? 0) / 1e9);
            case "warning":
                return "Warning: " + cat.Text(Str(e, "id"), Params(e));
            case "error":
                return "Error: " + cat.Text(Str(e, "id"), Params(e)) + $" [{Str(e, "id")}]";
            case "end":
                return $"End: {Str(e, "result")}";
            case "plan":
                return "";
            default:
                return e.GetRawText();
        }
    }

    void AddLog(string line)
    {
        log.AppendLine(line);
        if (log.Length > 600_000) log.Remove(0, log.Length - 400_000);
        logDirty = true;
    }

    /// The details box shows the end of the log; it is refreshed once a second, not per line (a compile writes thousands)
    void ShowLogText()
    {
        logDirty = false;
        var text = log.Length > 60_000 ? log.ToString(log.Length - 60_000, 60_000) : log.ToString();
        Log.Text = text;
        Log.CaretIndex = text.Length;
    }

    void SetProgress(double f)
    {
        f = Math.Clamp(f, 0, 1);
        PercentText.Text = $"{(int)(f * 100)}%";
        OverallBar.Value = f;
    }

    void Tick()
    {
        foreach (var r in rows.Values) r.Tick();
        if (logDirty && Log.IsVisible) ShowLogText();
        if (run == null && download == null) return;
        var hint = Tr.T(current switch
        {
            "download" => "downloading the setup package",
            "compile" => "compiling is the longest step",
            "prepare" => "the shaders are ready before you play",
            _ => "you can keep using your PC",
        });
        BuildNote.Text = Tr.F("Elapsed {0} · {1}", Clock(elapsed.Elapsed), hint);
    }

    static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    static string Span(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min" : $"{t.Seconds} s";

    void Ended(int code)
    {
        clock.Stop();
        elapsed.Stop();
        run = null;
        result = string.IsNullOrEmpty(result) ? code == 3 ? "cancelled" : code == 0 ? "ok" : "failed" : result;
        if (result == "failed" && error == null)
        {
            using var doc = JsonDocument.Parse($"{{\"code\": {code}}}");
            error = ("ui.process_died", doc.RootElement.Clone());
        }
        if (result != "ok" && current != null && rows.TryGetValue(current, out var row)) row.Stopped();
        if (closing)
        {
            Close();
            return;
        }
        switch (result)
        {
            case "ok":
                AfterSuccess();
                break;
            case "cancelled":
                AfterCancel();
                break;
            default:
                var (id, ps) = error ?? ("ui.process_died", (JsonElement?)null);
                var text = cat.Has(id) ? cat.Text(id, ps) : Tr.F("The installer stopped unexpectedly (exit code {0}).", code);
                if (!string.IsNullOrEmpty(logPath)) text += Tr.F("\n\nLog file: {0}", logPath);
                ShowProblem(Tr.T(kind switch { Kind.Update => "The update stopped", Kind.Verify => "The verification stopped", Kind.Rebuild => "The rebuild stopped", _ => "The install stopped" }),
                            text);
                break;
        }
    }

    void AfterSuccess()
    {
        foreach (var id in order)
            if (rows[id] is { Finished: false } r) r.Done();
        SetProgress(1);
        WriteRebuildCommand(runDest);
        if (runPackage != null) SetupPackage.RemoveOlderThan(runPackage, AddLog);
        localPackage = SetupPackage.Newest();
        settings.Dest = runDest;
        settings.Save();
        launchFolder = runDest;
        if (kind == Kind.Rebuild)
        {
            PlayAndWatch(runDest);
            return;
        }
        SetStep(2);
        ReadyTitle.Text = Tr.T(kind switch { Kind.Update => "Your game was updated", Kind.Verify => "The files were verified", _ => "Your game is ready" });
        var verb = Tr.T(kind switch { Kind.Update => "Updated", Kind.Verify => "Verified", _ => "Installed" });
        ReadyNote.Text = Tr.F("{0} in {1} · TotK Recomp {2} · {3}", verb, Span(elapsed.Elapsed), Versions.Short(runRelease ?? runPackage?.Version), runDest);
        Shortcuts.Make(runDest, DesktopTick.IsChecked == true, StartTick.IsChecked == true);
        if (Shortcuts.Error != null) AddLog("shortcut not written: " + Shortcuts.Error);
        ShowPage(ReadyPage);
    }

    void AfterCancel()
    {
        if (kind == Kind.Rebuild)
        {
            ShowProblem(Tr.T("The rebuild was cancelled"), Tr.T("Code mods and ExeFS patches stay out of the game until the next rebuild. " +
                        "The game can start with the code it already has."), head: Tr.T("Cancelled"));
            return;
        }
        if (launchFolder.Length > 0 && kind is Kind.Update or Kind.Verify)
        {
            ShowLauncher(launchFolder);
            return;
        }
        ShowFiles();
    }

    /// build-cache/rebuild.txt points to this launcher, so the game opens it (--rebuild) when a mod needs new code. The installer
    /// writes the command of its own window there; a run from the launcher replaces it.
    void WriteRebuildCommand(string dest)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !IOPath.GetFileName(exe).Equals(Platform.ExeName, StringComparison.OrdinalIgnoreCase)) return;
            var cache = IOPath.Combine(dest, "build-cache");
            Directory.CreateDirectory(cache);
            var tmp = IOPath.Combine(cache, "rebuild.txt.tmp");
            File.WriteAllText(tmp, string.Join("\n", exe, "--rebuild", dest) + "\n", new UTF8Encoding(false));
            File.Move(tmp, IOPath.Combine(cache, "rebuild.txt"), overwrite: true);
            AddLog("build-cache/rebuild.txt: " + exe + " --rebuild");
        }
        catch (Exception x) { AddLog("build-cache/rebuild.txt not written: " + x.Message); }
    }

    void LogToggled(object? sender, RoutedEventArgs e)
    {
        if (Log == null || StepCard == null) return;
        var show = ShowLog.IsChecked == true;
        Log.IsVisible = show;
        StepCard.IsVisible = !show;
        ShowLog.Content = Tr.T(show ? "Hide details" : "Show details");
        if (show) ShowLogText();
    }

    void CancelRun(object? sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        CancelButton.Content = Tr.T("Cancelling…");
        download?.Cancel();
        run?.Cancel();
    }

    // ================= 3: play =================

    void Play(object? sender, RoutedEventArgs e) => PlayAndWatch(runDest);

    void OpenFolder(object? sender, RoutedEventArgs e) => Platform.OpenFolder(runDest);

    void ShortcutsChanged(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (runDest.Length > 0) Shortcuts.Make(runDest, DesktopTick.IsChecked == true, StartTick.IsChecked == true);
    }

    /// Starts the game and waits for it in the background: a normal exit closes the launcher, an error brings it back with the
    /// end of the game's log (logs/totk.log)
    async void PlayAndWatch(string folder)
    {
        Process? game;
        try
        {
            game = Process.Start(new ProcessStartInfo(Installed.GameExe(folder)) { UseShellExecute = !Platform.Linux, WorkingDirectory = folder });
        }
        catch (Exception x)
        {
            await Dialogs.Ask(this, Tr.F("The game could not be started: {0}", x.Message));
            return;
        }
        if (game == null)
        {
            Close();   // (started, but the shell gave no process to wait for)
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
            Close();
            return;
        }
        Show();
        Activate();
        ShowCrash(folder, code, started);
    }

    // ================= when something goes wrong =================

    void ShowProblem(string title, string text, string? head = null)
    {
        lastFailure = text;
        ProblemTitle.Text = title;
        ProblemHead.Text = head ?? Tr.T("What went wrong");
        ProblemText.Text = text;
        ProblemSteps.Children.Clear();
        ProblemSub.Text = kind == Kind.Rebuild
            ? Tr.T("The game keeps the code it already had.")
            : Tr.T("Finished steps are kept: trying again continues where it stopped.");
        PlayAnyway.IsVisible = kind is Kind.Rebuild or Kind.Update or Kind.Verify && File.Exists(Installed.GameExe(runDest));
        ProblemDetails.Content = Tr.T("Show details");
        CopyReport.Content = Tr.T("Copy for a bug report");

        if (kind is Kind.Install or Kind.Resume) SetStep(1);
        ShowPage(ProblemPage);
    }

    void ProblemShowDetails(object? sender, RoutedEventArgs e)
    {
        var lines = log.ToString().Split('\n');
        ProblemSteps.Children.Clear();
        ProblemSteps.Children.Add(new SelectableTextBlock
        {
            Text = string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 80))), FontFamily = (FontFamily)this.FindResource("Mono")!, FontSize = 11,
            Foreground = Br("TextDim"), Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), Padding = new Thickness(8),
        });
    }

    async void CopyForReport(object? sender, RoutedEventArgs e)
    {
        var lines = log.ToString().Split('\n');
        var text = $"Ultrahand Launcher {LauncherUpdate.Current} · package {runPackage?.Version ?? "?"} · {RuntimeInformation.OSDescription}\n" +
                   $"{ProblemTitle.Text}: {lastFailure}\n\n" + string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 200)));
        try
        {
            if (Clipboard == null) return;
            await Clipboard.SetTextAsync(text);
            CopyReport.Content = Tr.T("Copied");
        }
        catch (Exception) { }
    }

    void Back(object? sender, RoutedEventArgs e)
    {
        if (kind is Kind.Install or Kind.Resume || launchFolder.Length == 0) ShowFiles();
        else ShowLauncher(launchFolder);
    }

    void Retry(object? sender, RoutedEventArgs e)
    {
        _ = Begin(runDest, runArgs, kind);
    }

    void PlayAnywayClick(object? sender, RoutedEventArgs e) => PlayAndWatch(runDest);

    // ================= the window =================

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closing || run == null && download == null) return;
        e.Cancel = true;   // (the answer comes later: the window closes from Ended, or from the cancelled download)
        if (asking) return;
        asking = true;
        var yes = await Dialogs.Ask(this, Tr.T("The install is still running. Cancel it and close?"), yesNo: true);
        asking = false;
        if (!yes) return;
        closing = true;
        if (run == null && download == null) { Close(); return; }
        download?.Cancel();
        run?.Cancel();   // (the installer records what finished before it exits)
    }
}
