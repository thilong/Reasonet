using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Reasonet.App.Panels;

/// <summary>
/// Central chat area: session header → transcript → input bar.
/// </summary>
public class ChatPanel : Grid
{
    public TextBox InputBox { get; }
    public Button SendButton { get; }
    public StackPanel TranscriptList { get; }
    private readonly ScrollViewer _scroller;
    private readonly List<Border> _bubbles = [];
    private double _chatW = 600;
    private double _bubbleRatio = 0.9;

    public ChatPanel()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 0: header
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // 1: transcript
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 2: input

        // Row 0 — session header
        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#f1f3f4")),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = "New Session  ·  Reasonet Chat",
                FontSize = 12, Foreground = Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Center,
            }
        });

        // Row 1 — transcript
        TranscriptList = new StackPanel { Margin = new Thickness(12, 8) };
        _scroller = new ScrollViewer { Content = TranscriptList };
        SetRow(_scroller, 1); Children.Add(_scroller);

        // Row 2 — input bar (card style)
        InputBox = new TextBox
        {
            Watermark = "输入消息...（/ 命令，@ 提及）",
            MinHeight = 36,
            MaxHeight = 120,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
        };
        InputBox.KeyDown += OnInputKeyDown;

        SendButton = new Button
        {
            Content = "\u25b6", FontSize = 14,
            Width = 36, Height = 32,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        SendButton.Click += OnSendClick;

        var inputRow = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 6) };
        DockPanel.SetDock(SendButton, Dock.Right);
        inputRow.Children.Add(SendButton);
        inputRow.Children.Add(InputBox);

        var inputCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#fff")),
            BorderBrush = new SolidColorBrush(Color.Parse("#ddd")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 4),
            Padding = new Thickness(4),
            Child = inputRow,
        };
        SetRow(inputCard, 2); Children.Add(inputCard);
    }

    public void SetBubbleWidth(double w) { _chatW = w; UpdateBubbles(); }
    public void SetBubbleRatio(double r) { _bubbleRatio = r; UpdateBubbles(); }

    void UpdateBubbles()
    {
        foreach (var b in _bubbles)
            b.MaxWidth = _chatW * ((b.Tag as string) == "user" ? 0.6 : _bubbleRatio);
    }

    async void OnSendClick(object? s, RoutedEventArgs e) => OnSend?.Invoke();
    async void OnInputKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { e.Handled = true; OnSend?.Invoke(); }
    }

    public Action? OnSend { get; set; }

    // ── Streaming support ──

    private StreamBubble? _currentStream;

    /// <summary>Start a new streaming assistant bubble and return a handle to append to it.</summary>
    public StreamBubble BeginAssistantStream()
    {
        CloseCurrentStream();
        var sb = new StreamBubble(this, _chatW * 0.9);
        Dispatcher.UIThread.Post(() =>
        {
            var b = sb.Border;
            _bubbles.Add(b);
            TranscriptList.Children.Add(b);
            ScrollDown();
        });
        _currentStream = sb;
        return sb;
    }

    /// <summary>Append text to the current stream bubble (or start one if none exists).</summary>
    public void AppendToStream(string text)
    {
        if (_currentStream == null)
            _currentStream = BeginAssistantStream();
        _currentStream.Append(text);
    }

    /// <summary>Finalize the current streaming bubble (closes it).</summary>
    public void CloseCurrentStream()
    {
        _currentStream?.Close();
        _currentStream = null;
    }

    /// <summary>Handle a final Message event — close stream if active, else create a regular bubble.</summary>
    public void FinalizeMessage(string text)
    {
        if (_currentStream != null)
        {
            // Stream was already active — set the final text and close
            _currentStream.SetText(text);
            CloseCurrentStream();
        }
        else
        {
            // No streaming happened — create a regular bubble
            AddBubble(text, "#f2d4ec", null, "asst", HorizontalAlignment.Left);
        }
    }

    // ── Public API ──

    public void AddUserMessage(string t) => AddBubble(t, "#e3f2fd", Brushes.Black, "user", HorizontalAlignment.Right);
    public void AddAssistantMessage(string t) => FinalizeMessage(t);
    public void AddToolBubble(string t) => AddBubble(t, "#1e1e1e", new SolidColorBrush(Color.Parse("#d4d4d4")), "tool", HorizontalAlignment.Left);

    public void AddInline(string text, IBrush fg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TranscriptList.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = fg, Margin = new Thickness(8, 2) });
            ScrollDown();
        });
    }

    // ── StreamBubble helper ──

    public sealed class StreamBubble
    {
        private readonly ChatPanel _panel;
        private readonly Markdown.Avalonia.MarkdownScrollViewer _viewer;
        private readonly Border _border;
        private readonly System.Text.StringBuilder _sb = new();
        private bool _closed;

        public Border Border => _border;

        internal StreamBubble(ChatPanel panel, double maxW)
        {
            _panel = panel;
            _viewer = new Markdown.Avalonia.MarkdownScrollViewer
            {
                Markdown = "",
                MaxWidth = maxW,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _border = new Border
            {
                Tag = "asst",
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 3),
                Background = new SolidColorBrush(Color.Parse("#f2d4ec")),
                CornerRadius = new CornerRadius(6),
                MaxWidth = maxW,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _viewer,
            };
        }

        public void Append(string text)
        {
            lock (_sb) { _sb.Append(text); }
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed) return;
                lock (_sb) { _viewer.Markdown = _sb.ToString(); }
                _panel.ScrollDown();
            });
        }

        public void SetText(string text)
        {
            lock (_sb) { _sb.Clear(); _sb.Append(text); }
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed) return;
                lock (_sb) { _viewer.Markdown = _sb.ToString(); }
                _panel.ScrollDown();
            });
        }

        public void Close()
        {
            _closed = true;
        }
    }

    void AddBubble(string text, string bg, IBrush fg, string tag, HorizontalAlignment align)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var ratio = tag == "user" ? 0.6 : _bubbleRatio;
            Control content;

            if (tag == "asst")
            {
                // Markdown.Avalonia renders the full markdown content
                var md = new Markdown.Avalonia.MarkdownScrollViewer
                {
                    Markdown = text,
                    MaxWidth = _chatW * ratio,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                content = md;
            }
            else
            {
                content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = fg };
            }

            var b = new Border
            {
                Tag = tag, Padding = new Thickness(10, 8), Margin = new Thickness(0, 3),
                Background = new SolidColorBrush(Color.Parse(bg)),
                CornerRadius = new CornerRadius(6),
                MaxWidth = _chatW * ratio,
                HorizontalAlignment = align,
                Child = content
            };
            _bubbles.Add(b);
            TranscriptList.Children.Add(b);
            ScrollDown();
        });
    }

    public void ScrollDown() { try { _scroller.ScrollToEnd(); } catch { } }
}
