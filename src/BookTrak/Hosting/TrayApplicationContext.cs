using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BookTrak.Hosting;

/// <summary>
/// Owns the WinForms message loop on the main STA thread. The Blazor/Kestrel host runs on a
/// background thread; this context just shows a tray icon and bridges Quit/crash to host
/// shutdown.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    // Invisible message-only-ish form gives us a UI-thread handle to marshal calls
    // (Invoke/BeginInvoke) from the background host thread — NotifyIcon/ContextMenuStrip
    // alone don't reliably expose one.
    private readonly Form _hiddenForm;
    private readonly NotifyIcon _notifyIcon;
    private readonly int _port;
    private readonly Action _requestShutdown;

    public TrayApplicationContext(int port, Action requestShutdown)
    {
        _port = port;
        _requestShutdown = requestShutdown;

        _hiddenForm = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            Size = new Size(0, 0),
        };
        _hiddenForm.Load += (_, _) => _hiddenForm.Hide();
        _ = _hiddenForm.Handle; // force handle creation now, on this thread

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open BookTrak", null, (_, _) => OpenBrowser());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "BookTrak — running",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenBrowser();

        MainForm = _hiddenForm;
    }

    public void UpdateTooltip(string text)
    {
        var trimmed = text.Length > 63 ? text[..63] : text;
        if (_hiddenForm.InvokeRequired)
        {
            _hiddenForm.BeginInvoke(() => _notifyIcon.Text = trimmed);
        }
        else
        {
            _notifyIcon.Text = trimmed;
        }
    }

    /// <summary>Uses the exe's own embedded icon (set via &lt;ApplicationIcon&gt; in the csproj)
    /// so the tray icon matches the taskbar/Explorer icon — one source of truth instead of a
    /// separately-embedded resource. Falls back to a system icon if extraction ever fails.</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }

    private void OpenBrowser() => BrowserLauncher.Open(_port);

    private void Quit()
    {
        _notifyIcon.Visible = false;
        _requestShutdown();
    }

    /// <summary>Called by the host watcher when the background host thread dies unexpectedly.</summary>
    public void OnHostCrashed()
    {
        if (_hiddenForm.InvokeRequired)
        {
            _hiddenForm.BeginInvoke(Shutdown);
        }
        else
        {
            Shutdown();
        }
    }

    /// <summary>Called once the background host has finished stopping — safe to exit the tray loop.</summary>
    public void Shutdown()
    {
        if (_hiddenForm.InvokeRequired)
        {
            _hiddenForm.BeginInvoke(Shutdown);
            return;
        }

        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
            _hiddenForm.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class BrowserLauncher
{
    public static void Open(int port)
    {
        var url = $"http://127.0.0.1:{port}/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
