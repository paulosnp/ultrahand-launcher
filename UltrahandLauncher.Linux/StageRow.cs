using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Ultrahand;

/// One installer step in the progress list: a status mark, the step name with a detail line under it, and the elapsed time.
/// The states follow the installer's step events (installer/events.py): pending, running, done, skipped, stopped.
/// (The Avalonia twin of UltrahandLauncher/StageRow.cs.)
sealed class StageRow
{
    public readonly Border Root;
    readonly Grid mark = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
    readonly TextBlock title = new() { FontWeight = FontWeight.Medium };
    readonly TextBlock detail = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
    readonly TextBlock timer = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 1, 0, 0) };
    readonly Stopwatch clock = new();
    bool active;

    public bool Started { get; private set; }
    public bool Finished { get; private set; }

    static IBrush Res(string key) => (IBrush)Application.Current!.FindResource(key)!;

    public StageRow(string name, bool last)
    {
        title.Text = name;
        timer.FontFamily = (FontFamily)Application.Current!.FindResource("Mono")!;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto") };
        var texts = new StackPanel { Children = { title, detail } };
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(timer, 2);
        grid.Children.Add(mark);
        grid.Children.Add(texts);
        grid.Children.Add(timer);
        Root = new Border { Child = grid, Padding = new Thickness(0, 11, 0, 11), BorderBrush = Res("Line") };
        SetLast(last);
        ShowPending();
    }

    /// The last row has no separator line under it
    public void SetLast(bool last) => Root.BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1);

    public void Detail(string text)
    {
        detail.Text = text;
        detail.IsVisible = text.Length > 0;
    }

    void Mark(Control glyph)
    {
        mark.Children.Clear();
        mark.Children.Add(glyph);
    }

    static Ellipse Circle(double size, IBrush? fill, IBrush? stroke = null) =>
        new() { Width = size, Height = size, Fill = fill, Stroke = stroke, StrokeThickness = 1.5 };

    static string Format(double seconds) => $"{(int)(seconds / 60)}:{(int)seconds % 60:00}";

    void ShowPending()
    {
        Mark(Circle(10, null, Res("LineStrong")));
        title.Foreground = Res("TextMuted");
        detail.Foreground = Res("TextMuted");
        timer.Foreground = Res("TextMuted");
    }

    public void Begin()
    {
        Started = active = true;
        Finished = false;
        clock.Restart();
        Mark(new Grid { Children = { Circle(18, Res("AccentSoft")), Circle(8, Res("Accent")) } });
        title.Foreground = Res("Text");
        title.FontWeight = FontWeight.SemiBold;
        detail.Foreground = Res("TextDim");
        timer.Foreground = Res("AccentLight");
        Tick();
    }

    public void Tick()
    {
        if (active) timer.Text = Format(clock.Elapsed.TotalSeconds);
    }

    /// seconds: the time the installer measured for the step, when its done event carries one
    public void Done(double? seconds = null)
    {
        if (active) Tick();
        if (seconds is { } s) timer.Text = Format(s);
        active = false;
        Started = Finished = true;
        clock.Stop();
        Mark(new Path
        {
            Data = Geometry.Parse("M1,9 L6,14 L16,3"), Stroke = Res("Ok"), StrokeThickness = 2.2,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        });
        title.Foreground = Res("Text");
        title.FontWeight = FontWeight.Medium;
        detail.Foreground = Res("TextDim");
        timer.Foreground = Res("TextMuted");
    }

    /// reason "current": the step's output is already there and checks out; anything else: the step is not needed
    public void Skipped(string reason)
    {
        active = false;
        Started = Finished = true;
        clock.Stop();
        Mark(new Rectangle { Width = 10, Height = 2, Fill = Res("TextMuted") });
        title.Foreground = Res("TextDim");
        title.FontWeight = FontWeight.Medium;
        timer.Text = "";
        Detail(Tr.T(reason == "current" ? "Already done: nothing changed" : "Not needed"));
    }

    public void Stopped()
    {
        active = false;
        clock.Stop();
        Mark(new Grid
        {
            Children =
            {
                Circle(18, Res("BadSoft")),
                new TextBlock { Text = "!", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Res("Bad"),
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        timer.Foreground = Res("Bad");
    }
}
