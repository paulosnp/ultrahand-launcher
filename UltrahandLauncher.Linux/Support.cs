using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;

namespace Ultrahand;

static partial class Tr
{
    /// Translates the texts the window's XAML sets: TextBlock.Text, the string Content of buttons and boxes, and string tooltips
    public static void Apply(ILogical root)
    {
        if (Code == "en") return;
        switch (root)
        {
            case TextBlock { Text: { Length: > 0 } text } block:
                block.Text = T(text);
                break;
            case ContentControl { Content: string content } control:
                control.Content = T(content);
                break;
        }
        if (root is Control c && ToolTip.GetTip(c) is string tip) ToolTip.SetTip(c, T(tip));
        foreach (var child in root.LogicalChildren) Apply(child);
    }
}

/// A message or a yes/no question in a small window over the launcher (Avalonia has no MessageBox)
static class Dialogs
{
    public static async Task<bool> Ask(Window owner, string text, bool yesNo = false)
    {
        var result = false;
        var window = new Window
        {
            Title = "Ultrahand Launcher", Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = owner.Background, Foreground = owner.Foreground,
        };
        var yes = new Button { Content = Tr.T(yesNo ? "Yes" : "OK") };
        yes.Classes.Add("accent");
        var no = new Button { Content = Tr.T("No"), IsVisible = yesNo };
        yes.Click += (_, _) => { result = true; window.Close(); };
        no.Click += (_, _) => window.Close();
        window.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 22,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { no, yes } },
            },
        };
        await window.ShowDialog(owner);
        return result;
    }
}

/// The launcher's entries in the applications menu and on the Desktop: .desktop files that start the installed launcher, with
/// --launch and the game folder once a game is installed (the Linux twin of UltrahandLauncher/Shortcuts.cs)
static class Shortcuts
{
    const string FileName = "ultrahand-launcher.desktop";

    /// The last shortcut that could not be written, for the log
    public static string? Error;

    /// gameFolder null: a shortcut to the launcher itself, before any game install
    public static void Make(string? gameFolder, bool desktop, bool menu)
    {
        if (!Platform.Linux || !LauncherUpdate.CanApply) return;   // (only the installed copy: a shortcut must not point into bin/)
        var icon = Path.Combine(Platform.DataFolder, "ultrahand.png");
        try
        {
            Directory.CreateDirectory(Platform.DataFolder);
            using var source = AssetLoader.Open(new Uri("avares://UltrahandLauncher/Images/ultrahand.png"));
            using var file = File.Create(icon);
            source.CopyTo(file);
        }
        catch (Exception e) { Error = $"icon: {e.Message}"; }

        var folder = gameFolder?.TrimEnd('/');
        var exec = Quote(Platform.InstalledExe) + (folder != null ? " --launch " + Quote(folder) : "");
        var text = "[Desktop Entry]\nType=Application\nName=Ultrahand Launcher\n" +
                   "Comment=Install, update and play TotK Recomp\n" +
                   $"Exec={exec}\nIcon={icon}\nCategories=Game;\nTerminal=false\n";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Write(Path.Combine(local, "applications"), menu);
        if (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) is { Length: > 0 } desk && Directory.Exists(desk)) Write(desk, desktop);

        void Write(string where, bool wanted)
        {
            var path = Path.Combine(where, FileName);
            try
            {
                if (!wanted)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                Directory.CreateDirectory(where);
                File.WriteAllText(path, text, new UTF8Encoding(false));
                Platform.MakeExecutable(path);   // (desktops ask for it before they start a .desktop file from the Desktop)
            }
            catch (Exception e) { Error = $"{path}: {e.Message}"; }
        }
    }

    /// An argument of a .desktop Exec line (Desktop Entry Specification: double quotes, with " ` $ \ escaped)
    static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
}
