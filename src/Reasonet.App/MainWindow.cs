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
    private readonly TextBox _inputBox;
    private readonly Button _sendButton;
    private readonly StackPanel _transcriptPanel;
    private readonly ScrollViewer _scrollViewer;
    private readonly ReasonetAppController _controller;
    private bool _isRunning;

    private double _chatWidth = 600;

    public MainWindow()
    {
        Title = "Reasonet Chat";
        Width = 900;
        Height = 600;
        MinWidth = 600;
        MinHeight = 400;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));               // 0: title
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // 1: chat area
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));               // 2: input

        // ── Title bar ──
        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1a1a2e")),
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = "Reasonet Chat",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeight.Bold,
            }
        };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        // ── Chat transcript (scrollable, fills remaining space) ──
        _transcriptPanel = new StackPanel { Margin = new Thickness(8) };
        _scrollViewer = new ScrollViewer
        {
            Content = _transcriptPanel,
            Margin = new Thickness(4),
        };
        Grid.SetRow(_scrollViewer, 1);
        root.Children.Add(_scrollViewer);

        // ── Input area (stretched full width) ──
        _inputBox = new TextBox { Watermark = "输入消息..." };
        _inputBox.KeyDown += OnInputKeyDown;

        _sendButton = new Button
        {
            Content = "发送",
            Width = 70,
            Margin = new Thickness(4, 0, 0, 0),
        };
        _sendButton.Click += OnSendClick;

        var inputRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            }
        };
        Grid.SetColumn(_inputBox, 0);
        Grid.SetColumn(_sendButton, 1);
        inputRow.Children.Add(_inputBox);
        inputRow.Children.Add(_sendButton);

        var inputBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#f0f0f0")),
            Padding = new Thickness(8),
            Child = inputRow,
        };
        Grid.SetRow(inputBar, 2);
        root.Children.Add(inputBar);

        Content = root;

        _controller = new ReasonetAppController(this);
        _ = _controller.InitializeAsync();
        AddSystemMessage("Reasonet Chat 已启动", Brushes.Gray);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _chatWidth = _scrollViewer.Bounds.Width;
        if (_chatWidth < 100) _chatWidth = 600;
    }

    private async void OnSendClick(object? s, RoutedEventArgs e) => await SendAsync();
    private async void OnInputKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SendAsync(); }
    }

    private async Task SendAsync()
    {
        if (_isRunning) return;
        var text = _inputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _inputBox.Text = "";
        AddUserMessage(text);
        _isRunning = true;
        _sendButton.IsEnabled = false;
        try { await _controller.RunAsync(text); }
        catch (Exception ex) { AddErrorMessage($"\u9519\u8bef: {ex.Message}"); }
        finally { _isRunning = false; _sendButton.IsEnabled = true; _inputBox.Focus(); }
    }

    public void AddUserMessage(string t) => Bubble(t, "#e3f2fd", Brushes.Black, BubbleSide.Right);
    public void AddAssistantMessage(string t) => Bubble(t, "#f5f5f5", Brushes.Black, BubbleSide.Left);
    public void AddToolMessage(string t) => Bubble(t, "#fff8e1", Brushes.DimGray, BubbleSide.Left);
    public void AddSystemMessage(string t, IBrush? fg = null) => Bubble(t, "#e8f5e9", fg ?? Brushes.Gray, BubbleSide.Center);
    public void AddErrorMessage(string t) => Bubble(t, "#ffebee", Brushes.Red, BubbleSide.Center);

    private enum BubbleSide { Left, Right, Center }

    private void Bubble(string text, string bg, IBrush fg, BubbleSide side)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var line in text.Split('\n'))
            {
                if (side == BubbleSide.Center)
                {
                    _transcriptPanel.Children.Add(new TextBlock
                    {
                        Text = line,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = fg,
                        Margin = new Thickness(8, 2),
                    });
                    continue;
                }

                var maxWidthRatio = side == BubbleSide.Left ? 0.8 : 0.6;
                var bubble = new Border
                {
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 2),
                    Background = new SolidColorBrush(Color.Parse(bg)),
                    CornerRadius = new CornerRadius(4),
                    MaxWidth = _chatWidth * maxWidthRatio,
                    HorizontalAlignment = side == BubbleSide.Left
                        ? HorizontalAlignment.Left
                        : HorizontalAlignment.Right,
                    Child = new TextBlock
                    {
                        Text = line,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = fg,
                    }
                };

                _transcriptPanel.Children.Add(bubble);
            }
            _scrollViewer.ScrollToEnd();
        });
    }
}
