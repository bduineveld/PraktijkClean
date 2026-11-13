using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean;

/// <summary>
/// Het icoon in het systeemvak (rechtsonder): groen = aan, blauw = simulatie, oranje = gepauzeerd,
/// rood = probleem, grijs = niet ingesteld. Leest elke 20 seconden config.json, status.json, het logboek en de
/// geplande taak. Verwijdert zelf nooit iets; het kan alleen pauzeren/hervatten en de app openen.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly InstallLayout _layout = InstallLayout.ForCurrentUser();
    private readonly Installer _installer;
    private readonly NotifyIcon _icon = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 20_000 };
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _exitWait;
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<StatusKind, Icon> _icons = new();
    private StatusKind? _lastKind;
    private AppConfig? _config;

    public TrayContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _installer = new Installer(_layout, new WindowsTaskSchedulerService(), WindowsSystem.CreateTaskEnvironment(), PraktijkClean.MainForm.ReadEmbeddedScript());

        // De beheerapp kan het icoon laten afsluiten (bij uitzetten, bijwerken of verwijderen).
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, TrayIntegration.ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => _ui.Post(_ => ExitThread(), null), null, Timeout.Infinite, executeOnlyOnce: true);

        _icon.ContextMenuStrip = _menu;
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenApp(); };
        _icon.BalloonTipClicked += (_, _) => OpenApp();
        _menu.Opening += (_, _) => { Refresh(); BuildMenu(); };
        _timer.Tick += (_, _) => Refresh();

        Refresh();
        BuildMenu();
        _icon.Visible = true;
        _timer.Start();
    }

    private void Refresh()
    {
        try
        {
            var state = _installer.GetState();
            _config = state.ConfigInstalled ? ConfigStore.TryLoad(_layout.ConfigPath, out _) : null;
            var info = StatusInfo.Compute(_config, state);
            var today = LogSummary.Load(_layout.LogPath, DateTime.Today);

            _icon.Icon = IconFor(info.Kind);
            string todayText = today.Runs == 0 ? "Vandaag nog niet gecontroleerd"
                : today.DeletedFiles + today.BinItemsDeleted == 0 ? "Vandaag niets opgeruimd"
                : $"Vandaag {today.DeletedFiles + today.BinItemsDeleted} opgeruimd";
            // NotifyIcon.Text mag in .NET Framework maximaal 63 tekens zijn.
            _icon.Text = Limit($"PraktijkClean: {info.Short}\n{todayText}", 63);

            if (info.Kind == StatusKind.Problem && _lastKind != StatusKind.Problem)
                _icon.ShowBalloonTip(10_000, "PraktijkClean", "PraktijkClean staat aan, maar de geplande taak ontbreekt of staat uit. Klik om de app te openen.", ToolTipIcon.Warning);
            _lastKind = info.Kind;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ScheduledTaskException or InvalidOperationException)
        {
            _icon.Icon = IconFor(StatusKind.Off);
            _icon.Text = "PraktijkClean: status onbekend";
        }
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        var open = new ToolStripMenuItem("PraktijkClean openen", null, (_, _) => OpenApp()) { Font = new Font(_menu.Font, FontStyle.Bold) };
        _menu.Items.Add(open);
        _menu.Items.Add(new ToolStripSeparator());

        StatusInfo? info = null;
        try { info = StatusInfo.Compute(_config, _installer.GetState()); }
        catch (ScheduledTaskException) { }
        _menu.Items.Add(new ToolStripMenuItem("Status: " + (info?.Short ?? "onbekend")) { Enabled = false });
        _menu.Items.Add(new ToolStripMenuItem(StatusInfo.Summary(LogSummary.Load(_layout.LogPath, DateTime.Today), "Vandaag")) { Enabled = false });
        _menu.Items.Add(new ToolStripMenuItem(StatusInfo.Summary(LogSummary.Load(_layout.LogPath, DateTime.Now.AddDays(-7)), "Afgelopen 7 dagen")) { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        if (_config is not null)
        {
            bool paused = _config.Paused;
            _menu.Items.Add(paused ? "Hervatten" : "Tijdelijk pauzeren", null, (_, _) => TogglePause(!paused));
        }
        _menu.Items.Add("Icoon verbergen", null, (_, _) => HideIcon());
    }

    private void TogglePause(bool pause)
    {
        var config = ConfigStore.TryLoad(_layout.ConfigPath, out string? error);
        if (config is null) { MessageBox.Show(error ?? "De instellingen konden niet worden gelezen.", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try { _installer.SetPaused(config, pause); }
        catch (Exception ex) when (ex is ScheduledTaskException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Dat is niet gelukt.\n\n" + ex.Message, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Refresh();
    }

    private void HideIcon()
    {
        if (MessageBox.Show("Het icoon verbergen? PraktijkClean blijft gewoon werken.\n\nU kunt het icoon later weer aanzetten in de app " +
                            "('Icoon rechtsonder in het systeemvak tonen').", Texts.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try { TrayIntegration.Disable(_layout); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        ExitThread();
    }

    private void OpenApp()
    {
        // Is de app al open, dan haalt Program die naar voren.
        try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false, WorkingDirectory = _layout.BaseDirectory })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private Icon IconFor(StatusKind kind)
    {
        if (_icons.TryGetValue(kind, out var cached)) return cached;
        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float pad = size / 16f;
            var circle = new RectangleF(pad, pad, size - 2 * pad - 1, size - 2 * pad - 1);
            using (var fill = new SolidBrush(StatusInfo.IconColor(kind))) g.FillEllipse(fill, circle);
            using (var edge = new Pen(Color.White, Math.Max(1f, size / 16f))) g.DrawEllipse(edge, circle);

            // Een eenvoudig wit teken in de cirkel, zodat de status ook zonder kleurenzien herkenbaar is.
            using var mark = new Pen(Color.White, Math.Max(1.5f, size / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float s = size;
            switch (kind)
            {
                case StatusKind.On:          // vinkje
                    g.DrawLines(mark, new[] { new PointF(s * .30f, s * .52f), new PointF(s * .45f, s * .67f), new PointF(s * .72f, s * .36f) });
                    break;
                case StatusKind.Paused:      // twee streepjes
                    g.DrawLine(mark, s * .40f, s * .33f, s * .40f, s * .67f);
                    g.DrawLine(mark, s * .60f, s * .33f, s * .60f, s * .67f);
                    break;
                case StatusKind.Problem:     // uitroepteken
                    g.DrawLine(mark, s * .5f, s * .28f, s * .5f, s * .56f);
                    g.DrawLine(mark, s * .5f, s * .70f, s * .5f, s * .72f);
                    break;
                case StatusKind.Simulation:  // open rondje
                    g.DrawEllipse(mark, s * .36f, s * .36f, s * .28f, s * .28f);
                    break;
                default:                     // streepje
                    g.DrawLine(mark, s * .33f, s * .5f, s * .67f, s * .5f);
                    break;
            }
        }
        IntPtr handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        _icons[kind] = icon;
        return icon;
    }

    private static string Limit(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _exitWait.Unregister(null);
            _exitEvent.Dispose();
            foreach (var i in _icons.Values) i.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        base.ExitThreadCore();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
