using System.Windows;
using System.Windows.Controls;

namespace Ultrahand;

static partial class Tr
{
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
