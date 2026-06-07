using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Reasonet.App.Panels;

/// <summary>
/// Bottom status bar: connection, cache, tokens, cost, jobs, workspace, model, balance, theme.
/// </summary>
public class StatusBarPanel : Grid
{
    private readonly TextBlock _left;
    private readonly TextBlock _right;

    public StatusBarPanel()
    {
        Height = 22;
        Background = new SolidColorBrush(Color.Parse("#e9ecef"));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        _left = new TextBlock { FontSize = 12, Foreground = Brushes.DimGray, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        _right = new TextBlock { FontSize = 12, Foreground = Brushes.DimGray, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(8, 0) };

        SetColumn(_left, 0); Children.Add(_left);
        SetColumn(_right, 1); Children.Add(_right);

        Update(null, 0, 0, 0, 0.0);
    }

    public void Update(string? ws = null, int hit = 0, int miss = 0, int tokens = 0, double cost = 0.0)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var total = hit + miss;
            var cachePct = total > 0 ? hit * 100 / total : 0;
            _left.Text = $"\u25cf Online    \u25c8 Cache {cachePct}%    \u2666 Tokens {tokens}    \u25c6 This turn \u00a5{cost:F4}";
            _right.Text = $"\u25c6 Jobs 0    \u25c7 {ws ?? "."}    \u25c7 deepseek-v4-flash max    \u25c6 Balance \u00a50.00    \u2740 Sandstone";
        });
    }
}
