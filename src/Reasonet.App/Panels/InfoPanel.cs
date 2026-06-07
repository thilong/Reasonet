using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Reasonet.App.Panels;

/// <summary>
/// Right info panel: Files, Tools, Memory, Rules, Context tokens.
/// </summary>
public class InfoPanel : StackPanel
{
    private readonly TextBlock _ctxBar;

    public InfoPanel()
    {
        Width = 200;
        Background = new SolidColorBrush(Color.Parse("#f8f9fa"));

        Children.Add(Section("FILES", Txt("  未添加文件")));
        Children.Add(Section("TOOLS", Txt("  18 tools available")));
        Children.Add(Section("MEMORY", Txt("  无记忆")));
        Children.Add(Section("RULES", Txt("  无自定义规则")));

        _ctxBar = new TextBlock { Text = "0 / 1,000,000", FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(8, 2) };
        Children.Add(Section("CONTEXT TOKENS", _ctxBar));
    }

    public void UpdateContext(int used, int max) =>
        Dispatcher.UIThread.Post(() => _ctxBar.Text = $"{used:N0} / {max:N0}");

    static StackPanel Section(string title, params Control[] items)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = $"\u25bc {title}", FontWeight = FontWeight.Bold, FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#555")),
            Margin = new Thickness(8, 6, 8, 2)
        });
        foreach (var it in items) sp.Children.Add(it);
        return sp;
    }

    static TextBlock Txt(string t) => new() { Text = t, FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(12, 1) };
}
