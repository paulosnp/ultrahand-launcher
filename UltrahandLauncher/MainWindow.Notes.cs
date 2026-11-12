using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Ultrahand;

/// After a launcher update, the first start shows what changed since the version the player last saw (ReleaseNotes.cs), with a
/// link to the full notes on GitHub. Launcher > General turns it off.
public partial class MainWindow
{
    bool wasInstalledBefore;   // an earlier launcher already ran installed here (set in the constructor, before this run marks it)

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
        NotesSub.Visibility = NotesSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesText.Inlines.Clear();
        foreach (var section in sections)
        {
            if (sections.Count > 1)
            {
                if (NotesText.Inlines.Count > 0) NotesText.Inlines.Add(new LineBreak());
                NotesText.Inlines.Add(new Run(section.Version) { FontWeight = FontWeights.SemiBold, Foreground = Br("Text") });
                NotesText.Inlines.Add(new LineBreak());
            }
            foreach (var item in section.Items)
            {
                NotesText.Inlines.Add(new Run("•  " + item));
                NotesText.Inlines.Add(new LineBreak());
            }
        }
        NotesOff.IsChecked = false;
        NotesOverlay.Visibility = Visibility.Visible;
    }

    void NotesOkClick(object sender, RoutedEventArgs e)
    {
        if (NotesOff.IsChecked == true)
        {
            settings.ShowReleaseNotes = false;
            settings.Save();
        }
        NotesOverlay.Visibility = Visibility.Collapsed;
    }

    void NotesFullClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"{ProjectPage}/releases/tag/launcher-v{LauncherUpdate.Current}") { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
