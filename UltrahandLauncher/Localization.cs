using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Ultrahand;

/// The launcher's language: English, Português (Brasil) or Español. The English text is the key: Tr.T looks it up in
/// Translations.Table and gives it back unchanged when there is no entry, so a missing translation shows English. The language is
/// picked once at start (Settings.UiLang, else the Windows display language); a change in the settings applies at the next start.
static class Tr
{
    public static readonly (string Code, string Name)[] Languages = { ("en", "English"), ("pt-BR", "Português (Brasil)"), ("es", "Español") };

    public static string Code { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// The language of the TotK Recomp installer's messages: it has pt-BR, not Spanish
    public static string InstallerLang => Code == "pt-BR" ? "pt-BR" : "en";

    /// setting: a code from Languages, or empty for the Windows display language
    public static void Use(string? setting)
    {
        var ui = CultureInfo.CurrentUICulture.Name;
        Code = Languages.Any(l => l.Code == setting) ? setting!
             : ui.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR"
             : ui.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es"
             : "en";
        Culture = CultureInfo.GetCultureInfo(Code == "en" ? "en-US" : Code);
    }

    public static string T(string english)
    {
        if (Code == "en" || !Translations.Table.TryGetValue(english, out var t)) return english;
        return Code == "pt-BR" ? t.Pt : t.Es;
    }

    public static string F(string english, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(english), args);

    /// Translates the texts the window's XAML sets: TextBlock.Text, the string Content of buttons and boxes, and string tooltips
    public static void Apply(DependencyObject root)
    {
        if (Code == "en") return;
        switch (root)
        {
            case TextBlock { Text.Length: > 0 } text:
                text.Text = T(text.Text);
                break;
            case ContentControl { Content: string content }:
                ((ContentControl)root).Content = T(content);
                break;
        }
        if (root is FrameworkElement { ToolTip: string tip } element) element.ToolTip = T(tip);
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d) Apply(d);
    }
}
