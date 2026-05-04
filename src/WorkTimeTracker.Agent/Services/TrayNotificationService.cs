using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

// Tray notification service.
//
// On agent start it creates a NotifyIcon in the user's system tray and
// shows a balloon with the compliance text "Этот компьютер находится под
// наблюдением". The balloon is re-shown on session unlock and on remote
// connect — whenever the user resumes interactive work.
//
// The notify icon also acts as a visible indicator: there is no stealth
// mode in this product, by design. Right-click → "About" shows hostname,
// agent version, and where to ask questions.

[SupportedOSPlatform("windows")]
public sealed class TrayNotificationService : IHostedService, IDisposable
{
    private readonly ILogger<TrayNotificationService> _logger;
    private readonly IRdpSessionMonitor _rdpMonitor;
    private Thread? _uiThread;
    private NotifyIcon? _icon;
    private ApplicationContext? _ctx;
    private System.Threading.SynchronizationContext? _sync;

    private const string ComplianceTitle = "WorkTimeTracker";
    private const string ComplianceText = "Этот компьютер находится под наблюдением. Ведётся учёт рабочего времени, скриншотов и активности.";

    public TrayNotificationService(
        ILogger<TrayNotificationService> logger,
        IRdpSessionMonitor rdpMonitor)
    {
        _logger = logger;
        _rdpMonitor = rdpMonitor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("Tray notification skipped: not Windows");
            return Task.CompletedTask;
        }

        _uiThread = new Thread(RunUiThread)
        {
            IsBackground = true,
            Name = "TrayNotifyIcon"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        _rdpMonitor.SessionEvent += OnSessionEvent;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _rdpMonitor.SessionEvent -= OnSessionEvent;
        try
        {
            _ctx?.ExitThread();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing tray UI thread");
        }
        _uiThread?.Join(TimeSpan.FromSeconds(3));
        return Task.CompletedTask;
    }

    private void RunUiThread()
    {
        try
        {
            _sync = new WindowsFormsSynchronizationContext();
            System.Threading.SynchronizationContext.SetSynchronizationContext(_sync);

            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "WorkTimeTracker — учёт рабочего времени"
            };

            _icon.ContextMenuStrip = BuildContextMenu();

            // Initial compliance balloon.
            _icon.ShowBalloonTip(8000, ComplianceTitle, ComplianceText, ToolTipIcon.Info);

            _ctx = new ApplicationContext();
            Application.Run(_ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray UI thread crashed");
        }
        finally
        {
            if (_icon is not null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add($"WorkTimeTracker {GetVersion()}").Enabled = false;
        menu.Items.Add($"Хост: {Environment.MachineName}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("О мониторинге", null, (_, _) =>
        {
            MessageBox.Show(
                ComplianceText + "\n\nЕсли у вас есть вопросы по обработке данных, обратитесь в IT-отдел или к руководителю.",
                ComplianceTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        return menu;
    }

    private void OnSessionEvent(object? sender, RdpSessionEvent evt)
    {
        if (evt.Type is not (ActivityEventType.SessionUnlock or ActivityEventType.RemoteConnect or ActivityEventType.ConsoleConnect))
        {
            return;
        }

        try
        {
            _sync?.Post(_ =>
            {
                _icon?.ShowBalloonTip(6000, ComplianceTitle, ComplianceText, ToolTipIcon.Info);
            }, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-show compliance balloon");
        }
    }

    private static string GetVersion() =>
        typeof(TrayNotificationService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public void Dispose()
    {
        _icon?.Dispose();
    }
}
