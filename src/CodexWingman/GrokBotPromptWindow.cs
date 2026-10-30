using System.Runtime.InteropServices;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// Wingman's reconnect prompt: a small native window on the screen with the notification area (the main taskbar,
/// Shell_TrayWnd), in the corner by it wherever the taskbar is (GrokBotPromptPlacement). Its sizes are scaled for the
/// display's DPI. Topmost through its window styles
/// (WS_EX_TOPMOST | WS_EX_NOACTIVATE, shown with SW_SHOWNOACTIVATE), not Form.TopMost, which activates the window; never
/// in the taskbar, and never takes focus, so typing elsewhere goes on.
/// It only shows a <see cref="GrokBotReconnectPrompt"/> and reports its buttons; the tray decides, so a click always belongs
/// to this one prompt (unlike a tray balloon click, which Windows doesn't tie to a balloon). It has no timeout.
/// </summary>
internal sealed class GrokBotPromptWindow : Form
{
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int ScreenGap = 12;
    private const int MessageWidth = 360;
    private const int ButtonWidth = 88;

    private readonly Label message = new() { AutoSize = true };
    private readonly Button primary = new() { AutoSize = true };
    private readonly Button secondary = new() { AutoSize = true };
    private bool closingFromCode;
    private bool openMode;

    public event Action? ReconnectClicked;
    public event Action? OpenClicked;
    public event Action? NotNowClicked;

    public GrokBotPromptWindow(Icon? icon)
    {
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Sizes are in 96-DPI units, scaled for the display (Wingman is system DPI aware).
        int Px(int logical) => GrokBotPromptPlacement.Scaled(logical, DeviceDpi);
        message.MaximumSize = new Size(Px(MessageWidth), 0);
        message.Margin = new Padding(0, 0, 0, Px(12));
        primary.MinimumSize = new Size(Px(ButtonWidth), 0);
        secondary.MinimumSize = new Size(Px(ButtonWidth), 0);
        Padding = new Padding(Px(14));
        if (icon is not null) Icon = icon;
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty,
        };
        buttons.Controls.Add(secondary);
        buttons.Controls.Add(primary);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(message);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        primary.Click += (_, _) => (openMode ? OpenClicked : ReconnectClicked)?.Invoke();
        secondary.Click += (_, _) => NotNowClicked?.Invoke();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExTopmost | WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    /// <summary>
    /// Shows <paramref name="prompt"/>'s state: [Reconnect] [Not now]; a disabled "Working…"; [Cancel] while waiting for the
    /// quit or while Grok Bot updates; or why Wingman couldn't reopen Grok Bot with [Open Grok Bot] [Not now].
    /// </summary>
    public void Render(GrokBotReconnectPrompt prompt)
    {
        Text = prompt.Offer.Title;
        // The primary button is Open Grok Bot only where it shows it, with or without the quit's proof.
        openMode = prompt.State == GrokBotPromptState.CouldNotStart;
        message.Text = prompt.Message;
        switch (prompt.State)
        {
            case GrokBotPromptState.WaitingForQuit:
                primary.Visible = false;
                secondary.Text = "Cancel";
                break;
            case GrokBotPromptState.Updating:
                primary.Visible = false;
                secondary.Text = "Cancel";
                break;
            case GrokBotPromptState.CouldNotStart:
                primary.Visible = true;
                primary.Enabled = true;
                primary.Text = "Open Grok Bot";
                secondary.Text = "Not now";
                break;
            default:
                primary.Visible = true;
                primary.Enabled = prompt.State == GrokBotPromptState.Offered;
                primary.Text = prompt.State == GrokBotPromptState.Offered ? "Reconnect" : "Working…";
                secondary.Text = "Not now";
                break;
        }
        PerformLayout();
        if (IsHandleCreated) Place();
    }

    /// <summary>Shows the window topmost without activating it (SW_SHOWNOACTIVATE, then HWND_TOPMOST with SWP_NOACTIVATE).</summary>
    public void ShowWithoutFocus()
    {
        var handle = Handle; // creates the window with CreateParams' styles
        PerformLayout();
        Place();
        ShowWindow(handle, SwShowNoActivate);
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    // The caption's close button means Not now; the tray closes the window itself.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!closingFromCode && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            NotNowClicked?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        closingFromCode = true;
        base.Dispose(disposing);
    }

    private void Place()
    {
        if (TrayScreen() is not { } screen) return;
        var corner = GrokBotPromptPlacement.Corner(screen.Bounds, screen.WorkingArea, Size, GrokBotPromptPlacement.Scaled(ScreenGap, DeviceDpi));
        SetWindowPos(Handle, HwndTopmost, corner.X, corner.Y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    /// <summary>The screen holding the notification area (the main taskbar); the primary screen if it can't be found.</summary>
    private static Screen? TrayScreen()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        return taskbar != IntPtr.Zero ? Screen.FromHandle(taskbar) : Screen.PrimaryScreen;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
