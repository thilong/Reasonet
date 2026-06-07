using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonet.App.Panels;

namespace Reasonet.App;

public class MainWindow : Window
{
    public ChatPanel Chat { get; }
    public InfoPanel Info { get; }
    public StatusBarPanel StatusBar { get; }

    private readonly ReasonetAppController _controller;
    private bool _isRunning;

    public MainWindow()
    {
        Title = "Reasonet"; Width = 1200; Height = 760;
        MinWidth = 900; MinHeight = 520;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // 0: main
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // 1: status

        // ── Row 0 — Main 3-column layout ──
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));  // sidebar
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star))); // chat
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); // info

        var sidebar = new Panels.SidebarPanel();
        Chat = new ChatPanel();
        Info = new InfoPanel();

        Grid.SetColumn(sidebar, 0); body.Children.Add(sidebar);
        Grid.SetColumn(Chat, 1); body.Children.Add(Chat);
        Grid.SetColumn(Info, 2); body.Children.Add(Info);

        Grid.SetRow(body, 0); root.Children.Add(body);

        // ── Row 1 — Status bar ──
        StatusBar = new StatusBarPanel();
        Grid.SetRow(StatusBar, 1); root.Children.Add(StatusBar);

        Content = root;

        // ── Wire events ──
        Chat.OnSend = OnSend;

        _controller = new ReasonetAppController(this);
        _ = _controller.InitializeAsync();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var w = Chat.Bounds.Width;
        if (w > 50) Chat.SetBubbleWidth(w);
    }

    async void OnSend()
    {
        if (_isRunning) return;
        var t = Chat.InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(t)) return;
        Chat.InputBox.Text = "";
        Chat.AddUserMessage(t);

        _isRunning = true; Chat.SendButton.IsEnabled = false;
        StatusBar.Update(System.IO.Directory.GetCurrentDirectory());
        try { await _controller.RunAsync(t); }
        catch (System.Exception ex) { AddErrorMessage($"\u9519\u8bef: {ex.Message}"); }
        finally { _isRunning = false; Chat.SendButton.IsEnabled = true; Chat.InputBox.Focus(); }
    }

    public void AddAssistantMessage(string t) => Chat.AddAssistantMessage(t);
    public void AddUserMessage(string t) => Chat.AddUserMessage(t);
    public void AddToolBubble(string t) => Chat.AddToolBubble(t);
    public void AddInline(string t, IBrush? fg = null) => Chat.AddInline(t, fg ?? Brushes.Gray);
    public void AddStatusLine(string t, IBrush? fg = null) => Chat.AddStatusLine(t, fg ?? Brushes.Gray);
    public void AddErrorMessage(string t) => Chat.AddInline(t, Brushes.Red);
}
