using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Reasonet.App;

public class MainWindow : Window
{
    public TextBox InputBox { get; }
    public Button SendButton { get; }
    private readonly StackPanel _transcriptPanel;
    private readonly ScrollViewer _scrollViewer;
    private readonly ReasonetAppController _controller;
    private bool _isRunning;
    private double _chatW = 600;
    private readonly List<Border> _bubbles = [];

    public MainWindow()
    {
        Title = "Reasonet Chat"; Width = 900; Height = 600; MinWidth = 600; MinHeight = 400;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // Row 0 — title
        var title = new Border { Background = new SolidColorBrush(Color.Parse("#1a1a2e")), Padding = new Thickness(8, 6),
            Child = new TextBlock { Text = "Reasonet Chat", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeight.Bold } };
        Grid.SetRow(title, 0); grid.Children.Add(title);

        // Row 1 — transcript
        _transcriptPanel = new StackPanel { Margin = new Thickness(8) };
        _scrollViewer = new ScrollViewer { Content = _transcriptPanel, Margin = new Thickness(4) };
        Grid.SetRow(_scrollViewer, 1); grid.Children.Add(_scrollViewer);

        // Row 2 — input
        InputBox = new TextBox { Watermark = "输入消息..." };
        InputBox.KeyDown += OnInputKeyDown;
        SendButton = new Button { Content = "发送", Width = 70, Margin = new Thickness(4, 0, 0, 0) };
        SendButton.Click += OnSendClick;

        var inputRow = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(GridLength.Auto) } };
        Grid.SetColumn(InputBox, 0); inputRow.Children.Add(InputBox);
        Grid.SetColumn(SendButton, 1); inputRow.Children.Add(SendButton);
        var inputBar = new Border { Background = new SolidColorBrush(Color.Parse("#f0f0f0")), Padding = new Thickness(8), Child = inputRow };
        Grid.SetRow(inputBar, 2); grid.Children.Add(inputBar);

        Content = grid;

        _controller = new ReasonetAppController(this);
        _ = _controller.InitializeAsync();
        AddSystemMessage("Reasonet Chat 已启动");
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _chatW = _scrollViewer.Bounds.Width;
        if (_chatW < 100) _chatW = 600;
        foreach (var b in _bubbles)
            b.MaxWidth = _chatW * ((b.Tag as string) == "user" ? 0.6 : 0.9);
    }

    async void OnSendClick(object? s, RoutedEventArgs e) => await Send();
    async void OnInputKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await Send(); }
    }

    async Task Send()
    {
        if (_isRunning) return;
        var t = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(t)) return;
        InputBox.Text = "";
        AddUserMessage(t);
        _isRunning = true; SendButton.IsEnabled = false;
        try { await _controller.RunAsync(t); }
        catch (Exception ex) { AddErrorMessage($"错误: {ex.Message}"); }
        finally { _isRunning = false; SendButton.IsEnabled = true; InputBox.Focus(); }
    }

    // ── Public helpers for UiSink ──

    public void AddUserMessage(string t) => AddChatBubble(t, "#e3f2fd", Brushes.Black, "user", HorizontalAlignment.Right);
    public void AddAssistantMessage(string t) => AddChatBubble(t, "#f2d4ec", Brushes.Black, "asst", HorizontalAlignment.Left);

    public void AddToolCall(string name, string? args)
    {
        var t = $"  ▶ {name}" + (string.IsNullOrEmpty(args) || args == "{}" ? "" : $" {Compact(args)}");
        AddInline(t, Brushes.DimGray);
    }

    public void AddToolResult(string name, string? output, string? error)
    {
        if (!string.IsNullOrEmpty(error)) { AddErrorMessage($"  ✘ {name}: {error}"); return; }
        if (!string.IsNullOrEmpty(output))
        {
            var lines = output.Split('\n');
            var preview = string.Join('\n', lines.Take(10));
            if (lines.Length > 10) preview += $"\n  ... ({lines.Length} lines total)";
            AddChatBubble(preview, "#1e1e1e", new SolidColorBrush(Color.Parse("#d4d4d4")), "tool", HorizontalAlignment.Left);
        }
    }

    public void AddSystemMessage(string t) => AddInline(t, Brushes.Gray);
    public void AddErrorMessage(string t) => AddInline(t, Brushes.Red);

    /// <summary>Inline message (non-bubble, single line) dispatched on UI thread.</summary>
    void AddInline(string text, IBrush fg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _transcriptPanel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = fg, Margin = new Thickness(8, 2) });
            ScrollDown();
        });
    }

    // ── Core chat bubble: all text in ONE Border with a single TextBlock ──

    void AddChatBubble(string text, string bg, IBrush fg, string tag, HorizontalAlignment align)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var ratio = tag == "user" ? 0.6 : 0.9;
            var b = new Border
            {
                Tag = tag, Padding = new Thickness(8, 6), Margin = new Thickness(0, 2),
                Background = new SolidColorBrush(Color.Parse(bg)), CornerRadius = new CornerRadius(4),
                MaxWidth = _chatW * ratio, HorizontalAlignment = align,
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = fg }
            };
            _bubbles.Add(b);
            _transcriptPanel.Children.Add(b);
            ScrollDown();
        });
    }

    void ScrollDown() { try { _scrollViewer.ScrollToEnd(); } catch { } }

    static string Compact(string s) => s.Length > 60 ? s[..57] + "…" : s;
}
