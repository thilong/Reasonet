using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Reasonet.App.Panels;

/// <summary>
/// Left sidebar: New Chat, session list, search, approvals.
/// </summary>
public class SidebarPanel : StackPanel
{
    public SidebarPanel()
    {
        Width = 220;
        Background = new SolidColorBrush(Color.Parse("#f8f9fa"));
        Children.Add(MakeSection("对话", [
            MakeButton("＋ New Chat", "#e3f2fd", Brushes.Black),
            MakeButton("⎇ History"),
            MakeButton("⏺ Next"),
        ]));
        Children.Add(MakeSection("搜索", [
            new TextBox { Watermark = "搜索会话...", Margin = new Thickness(4) },
        ]));
        Children.Add(MakeSection("RECENT  0", [
            new TextBlock { Text = "  暂无会话", Foreground = Brushes.Gray, Margin = new Thickness(8, 4) },
        ]));
        Children.Add(new Border { Height = 1, Background = Brushes.LightGray, Margin = new Thickness(8, 4) });
        Children.Add(MakeSection("", [
            MakeLink("⊙ Approval Rules"),
            MakeLink("⊙ About"),
            MakeLink("⊙ Settings"),
            MakeButton("⚙ Config"),
        ]));
    }

    static StackPanel MakeSection(string title, List<Control> children)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4) };
        if (!string.IsNullOrEmpty(title))
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(8, 4, 8, 2) });
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    static Button MakeButton(string text, string? bg = null, IBrush? fg = null) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = bg != null ? new SolidColorBrush(Color.Parse(bg)) : Brushes.Transparent,
        Foreground = fg ?? Brushes.Black,
        Margin = new Thickness(4, 1),
        Height = 28,
    };

    static TextBlock MakeLink(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#1565c0")),
        Margin = new Thickness(8, 2),
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
    };
}
