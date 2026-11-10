using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean;

/// <summary>
/// Het hoofdvenster. De opbouw van het scherm staat in <see cref="BuildLayout"/>, het gedrag van de
/// knoppen in de methoden die met "On" beginnen. Alle echte logica (opslaan, taak, script) zit in
/// PraktijkClean.Core; dit venster roept die alleen aan.
/// </summary>
internal sealed class MainForm : Form
{
    // ---------------------------------------------------------------- Kernobjecten
    private readonly InstallLayout _layout = InstallLayout.ForCurrentUser();
    private readonly Installer _installer;
    private readonly ScriptRunner _runner;
    private readonly List<string> _syncRoots;
    private AppConfig _config;
    private bool _dirty;
    private bool _loadingUi;   // true terwijl velden worden gevuld: dan telt een wijziging niet als bewerking
    private bool _busy;

    // ---------------------------------------------------------------- Status
    private readonly Label _lblMode = new() { AutoSize = true };
    private readonly Label _lblTask = new() { AutoSize = true };
    private readonly Label _lblLastRun = new() { AutoSize = true };
    private readonly Label _lblLastResult = new() { AutoSize = true };
    private readonly Label _lblNextRun = new() { AutoSize = true };
    private readonly Label _lblProblems = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly Label _lblBusy = new() { AutoSize = true, ForeColor = Color.SteelBlue };

    // ---------------------------------------------------------------- Mappen
    private readonly ListBox _lstFolders = new() { Dock = DockStyle.Fill, IntegralHeight = false, FormattingEnabled = true };
    private readonly Button _btnAddFolder = new() { Text = "Map toevoegen…", AutoSize = true };
    private readonly Button _btnRemoveFolder = new() { Text = "Map verwijderen", AutoSize = true };

    // ---------------------------------------------------------------- Instellingen per map
    private readonly CheckBox _chkEnabled = new() { Text = "Deze map opschonen", AutoSize = true };
    private readonly Label _lblPath = new() { AutoSize = true };
    private readonly Label _lblFolderWarning = new() { AutoSize = true, ForeColor = Color.DarkOrange };
    private readonly ComboBox _cmbRetention = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumberBox _numDays = new() { Minimum = 0, Maximum = 3650, Width = 70 };
    private readonly CheckBox _chkRecurse = new() { Text = "Ook submappen doorzoeken (afgeraden)", AutoSize = true };
    private readonly CheckBox _chkAllowSync = new() { Text = "Opschonen op deze OneDrive-/netwerklocatie toestaan (afgeraden)", AutoSize = true };
    private readonly TextBox _txtExtensions = new() { Width = 320 };
    private readonly TextBox _txtFiles = new() { Width = 320 };
    private readonly TextBox _txtSubfolders = new() { Width = 320 };

    // ---------------------------------------------------------------- Planning en veiligheid
    private readonly CheckBox _chkDaily = new() { Text = "Dagelijks om", AutoSize = true };
    private readonly DateTimePicker _dtpTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    private readonly CheckBox _chkLogon = new() { Text = "Bij aanmelden van Windows", AutoSize = true };
    private readonly CheckBox _chkLogoff = new() { Text = "Bij afmelden van Windows (niet beschikbaar)", AutoSize = true, Enabled = false };
    private readonly LinkLabel _lnkLogoff = new() { Text = "Waarom niet?", AutoSize = true };
    private readonly NumberBox _numMaxFiles = new() { Minimum = 1, Maximum = 5000, Width = 80 };
    private readonly RadioButton _rbRecycle = new() { Text = "Naar de Prullenbak", AutoSize = true };
    private readonly RadioButton _rbPermanent = new() { Text = "Definitief verwijderen", AutoSize = true };
    private readonly Label _lblDeleteNote = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly CheckBox _chkEmptyBin = new() { Text = "Prullenbak automatisch legen", AutoSize = true };
    private readonly NumberBox _numBinDays = new() { Minimum = 0, Maximum = 3650, Width = 70 };
    private readonly LinkLabel _lnkBinInfo = new() { Text = "Uitleg", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    private readonly LinkLabel _lnkDestroy = new() { Text = "Verwijderen is niet hetzelfde als veilig vernietigen – lees meer", AutoSize = true };

    // ---------------------------------------------------------------- Knoppen
    private readonly Button _btnSave = new() { Text = "Instellingen opslaan", AutoSize = true };
    private readonly Button _btnSimulate = new() { Text = "Simulatie uitvoeren", AutoSize = true };
    private readonly Button _btnActivate = new() { Text = "Automatische opschoning activeren…", AutoSize = true };
    private readonly Button _btnPause = new() { Text = "Tijdelijk pauzeren", AutoSize = true };
    private readonly Button _btnCleanNow = new() { Text = "Nu opruimen…", AutoSize = true };
    private readonly Button _btnRepair = new() { Text = "Installatie herstellen", AutoSize = true };
    private readonly Button _btnUninstall = new() { Text = "PraktijkClean volledig verwijderen…", AutoSize = true };
    private readonly Button _btnLog = new() { Text = "Logboek openen", AutoSize = true };

    public MainForm()
    {
        var env = WindowsSystem.CreateTaskEnvironment();
        _installer = new Installer(_layout, new WindowsTaskSchedulerService(), env, ReadEmbeddedScript());
        _runner = new ScriptRunner(WindowsSystem.PowerShellPath, _layout);
        _syncRoots = WindowsSystem.GetSyncRoots();
        _config = LoadOrCreateConfig(out string? startupNotice);

        BuildLayout();
        WireEvents();
        FillFromConfig();
        RefreshStatus();

        Shown += (_, _) =>
        {
            if (WindowsSystem.IsElevated()) Warn(Texts.ElevatedWarning);
            if (startupNotice is not null) Info(startupNotice);
        };
    }

    // =====================================================================================
    // Opstarten
    // =====================================================================================

    /// <summary>Het opschoonscript zit in de .exe ingebakken en wordt bij opslaan weggeschreven.</summary>
    private static string ReadEmbeddedScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PraktijkClean.cleanup.ps1")
            ?? throw new InvalidOperationException("Het ingebouwde opschoonscript ontbreekt.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private AppConfig LoadOrCreateConfig(out string? notice)
    {
        notice = null;
        AppConfig? config = null;
        if (File.Exists(_layout.ConfigPath))
        {
            config = ConfigStore.TryLoad(_layout.ConfigPath, out string? error);
            if (config is null)
                notice = error + "\n\nDe standaardinstellingen (simulatiemodus) zijn geladen. Controleer ze en klik op 'Instellingen opslaan'.";
        }
        if (config is null)
        {
            config = ConfigStore.CreateDefault(KnownFolders.GetPath(KnownFolderNames.Desktop), KnownFolders.GetPath(KnownFolderNames.Downloads));
            _dirty = true;
            return config;
        }

        // Is de werkelijke locatie van Bureaublad/Downloads veranderd (bijv. door OneDrive)?
        var moved = new List<string>();
        foreach (var f in config.Folders.Where(f => f.Type == FolderTypes.KnownFolder))
        {
            string now = KnownFolders.GetPath(f.KnownFolder!);
            if (!PathRules.PathEquals(now, f.ExpectedPath))
            {
                moved.Add($"• {f.DisplayName}: {now}");
                f.ExpectedPath = now;
            }
        }
        if (moved.Count > 0)
        {
            _dirty = true;
            notice = "De locatie van de volgende map(pen) is gewijzigd:\n\n" + string.Join("\n", moved) +
                     "\n\nTot u de instellingen opnieuw opslaat, slaat PraktijkClean deze map(pen) uit voorzorg over. " +
                     "Controleer de waarschuwingen (bijvoorbeeld OneDrive) en klik daarna op 'Instellingen opslaan'.";
        }
        return config;
    }

    // =====================================================================================
    // Schermopbouw
    // =====================================================================================

    private void BuildLayout()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiFont.Normal;
        Text = Texts.AppTitle;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 780);
        MinimumSize = new Size(960, 700);
        _lblMode.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        foreach (var l in new[] { _lblMode, _lblTask, _lblLastRun, _lblLastResult, _lblNextRun, _lblProblems })
            l.MaximumSize = new Size(1040, 0);
        foreach (var l in new[] { _lblPath, _lblFolderWarning })
            l.MaximumSize = new Size(480, 0);
        _lblDeleteNote.MaximumSize = new Size(470, 0);
        foreach (var o in RetentionOptions.All) _cmbRetention.Items.Add(o);

        // Status bovenaan
        var status = Column(_lblMode, _lblTask, _lblLastRun, _lblLastResult, _lblNextRun, _lblProblems, _lblBusy);
        var statusGroup = Group("Status", status);

        // Links: mappen, planning, veiligheid
        var folderButtons = Row(_btnAddFolder, _btnRemoveFolder);
        var folderTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        folderTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        folderTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        folderTable.Controls.Add(_lstFolders, 0, 0);
        folderTable.Controls.Add(folderButtons, 0, 1);
        var foldersGroup = Group("Mappen", folderTable, autoSize: false);

        var schedule = Grid();
        AddRow(schedule, _chkDaily, _dtpTime);
        AddRow(schedule, _chkLogon);
        AddRow(schedule, _chkLogoff, _lnkLogoff);
        AddRow(schedule, Hint("Een gemiste controle (pc stond uit) wordt later ingehaald."));
        var scheduleGroup = Group("Moment van opschonen", schedule);

        var safety = Grid();
        AddRow(safety, Caption("Maximaal aantal bestanden per keer:"), _numMaxFiles);
        AddRow(safety, Caption("Verwijderde bestanden:"), Row(_rbPermanent, _rbRecycle));
        AddRow(safety, _lblDeleteNote);
        AddRow(safety, _chkEmptyBin, Row(Caption("ouder dan"), _numBinDays, Caption("dagen"), _lnkBinInfo));
        AddRow(safety, _lnkDestroy);
        var safetyGroup = Group("Veiligheid", safety);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(foldersGroup, 0, 0);
        left.Controls.Add(scheduleGroup, 0, 1);
        left.Controls.Add(safetyGroup, 0, 2);

        // Rechts: instellingen van de geselecteerde map
        var details = Grid();
        AddRow(details, _chkEnabled);
        AddRow(details, Caption("Locatie:"), _lblPath);
        AddRow(details, _lblFolderWarning);
        AddRow(details, Caption("Bewaartermijn:"), Row(_cmbRetention, _numDays, Caption("dagen")));
        AddRow(details, Hint("Leeftijd = tijd sinds de laatste aanmaak- óf wijzigingsdatum (de nieuwste telt). " +
                             "Een net gedownload bestand is dus altijd 'nieuw'."));
        AddRow(details, _chkRecurse);
        AddRow(details, _chkAllowSync);
        AddRow(details, Caption("Uitgesloten extensies:"), _txtExtensions);
        AddRow(details, Caption("Uitgesloten bestanden of patronen:"), _txtFiles);
        AddRow(details, Caption("Uitgesloten submappen:"), _txtSubfolders);
        AddRow(details, Hint("Scheid meerdere items met een puntkomma (;). Voorbeelden: extensies \".lnk; .url\", " +
                             "bestanden \"Bewaren.pdf\", patronen \"protocol-*\" (* = willekeurige tekst, ? = één teken). " +
                             "Snelkoppelingen (.lnk), verborgen bestanden en geopende bestanden worden altijd overgeslagen."));
        var detailsGroup = Group("Instellingen voor de geselecteerde map", details, autoSize: false);

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        middle.Controls.Add(left, 0, 0);
        middle.Controls.Add(detailsGroup, 1, 0);

        // Knoppen onderaan
        var buttons = Row(_btnSave, _btnSimulate, _btnActivate, _btnPause, _btnCleanNow, _btnRepair, _btnUninstall, _btnLog);
        buttons.WrapContents = true;
        buttons.Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(statusGroup, 0, 0);
        root.Controls.Add(middle, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        var tips = new ToolTip();
        tips.SetToolTip(_btnSave, "Slaat de instellingen op en installeert of werkt het script en de geplande taak bij. Er wordt niets verwijderd.");
        tips.SetToolTip(_btnSimulate, "Laat zien welke bestanden in aanmerking komen. Er wordt niets verwijderd.");
        tips.SetToolTip(_btnCleanNow, "Ruimt direct op, na een overzicht en uw bevestiging.");
        tips.SetToolTip(_btnRepair, "Schrijft het script en de instellingen opnieuw weg en maakt de geplande taak opnieuw aan.");
        tips.SetToolTip(_numBinDays, "Alleen items die langer dan dit aantal dagen in de Prullenbak staan. 0 = alles (behalve de laatste 15 minuten).");
        tips.SetToolTip(_chkEmptyBin, "Leegt ook de Prullenbak van deze gebruiker, inclusief zelf weggegooide bestanden.");
        tips.SetToolTip(_numMaxFiles, "Komen er meer bestanden in aanmerking, dan wordt er uit voorzorg niets verwijderd.");

        ResumeLayout(false);
        PerformLayout();
    }

    private static GroupBox Group(string title, Control content, bool autoSize = true)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8) };
        if (autoSize) { g.AutoSize = true; g.AutoSizeMode = AutoSizeMode.GrowAndShrink; }
        content.Dock = DockStyle.Fill;
        g.Controls.Add(content);
        return g;
    }

    private static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    /// <summary>Voegt een regel toe: één control over de volle breedte, of label + control.</summary>
    private static void AddRow(TableLayoutPanel t, Control first, Control? second = null)
    {
        int row = t.RowCount;
        t.RowCount = row + 1;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        first.Anchor = AnchorStyles.Left;
        t.Controls.Add(first, 0, row);
        if (second is null)
        {
            t.SetColumnSpan(first, 2);
        }
        else
        {
            // Tekstvakken meerekken met de kolom. Met alleen Anchor.Left krimpt TableLayoutPanel ze bij de
            // eerste (nog smalle) opmaak tot 0 pixels breed en groeien ze daarna niet meer terug.
            second.Anchor = second is TextBoxBase ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
            t.Controls.Add(second, 1, row);
        }
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        f.Controls.AddRange(controls);
        return f;
    }

    private static FlowLayoutPanel Column(params Control[] controls)
    {
        var f = Row(controls);
        f.FlowDirection = FlowDirection.TopDown;
        return f;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    private static Label Hint(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 8),
    };

    // =====================================================================================
    // Gebeurtenissen koppelen
    // =====================================================================================

    private void WireEvents()
    {
        _lstFolders.Format += (_, e) => e.Value = DescribeFolder((FolderSettings)e.ListItem!);
        _lstFolders.SelectedIndexChanged += (_, _) => { if (!_loadingUi) ShowSelectedFolder(); };
        _btnAddFolder.Click += (_, _) => OnAddFolder();
        _btnRemoveFolder.Click += (_, _) => OnRemoveFolder();

        _chkEnabled.CheckedChanged += (_, _) => EditFolder(f => f.Enabled = _chkEnabled.Checked);
        _cmbRetention.SelectedIndexChanged += (_, _) => OnRetentionChoice();
        _numDays.ValueChanged += (_, _) => EditFolder(f => f.RetentionDays = (int)_numDays.Value);
        _chkRecurse.CheckedChanged += (_, _) => OnRecurseChanged();
        _chkAllowSync.CheckedChanged += (_, _) => OnAllowSyncChanged();
        _txtExtensions.TextChanged += (_, _) => EditFolder(f => f.Exclusions.Extensions = ConfigStore.NormalizeExtensions(ConfigStore.SplitList(_txtExtensions.Text)));
        _txtFiles.TextChanged += (_, _) => EditFolder(f =>
        {
            var (files, patterns) = ConfigStore.SplitFilesAndPatterns(ConfigStore.SplitList(_txtFiles.Text));
            f.Exclusions.Files = files;
            f.Exclusions.Patterns = patterns;
        });
        _txtSubfolders.TextChanged += (_, _) => EditFolder(f => f.Exclusions.Subfolders = ConfigStore.SplitList(_txtSubfolders.Text));

        _chkDaily.CheckedChanged += (_, _) => MarkDirty();
        _chkLogon.CheckedChanged += (_, _) => MarkDirty();
        _rbRecycle.CheckedChanged += (_, _) => { MarkDirty(); UpdateDeleteNote(); };
        _rbPermanent.CheckedChanged += (_, _) => { MarkDirty(); UpdateDeleteNote(); };
        _chkEmptyBin.CheckedChanged += (_, _) => OnEmptyBinChanged();
        _numBinDays.ValueChanged += (_, _) => MarkDirty();
        _lnkBinInfo.LinkClicked += (_, _) => Info(Texts.EmptyBinInfo);
        _dtpTime.ValueChanged += (_, _) => MarkDirty();
        _numMaxFiles.ValueChanged += (_, _) => MarkDirty();
        _lnkLogoff.LinkClicked += (_, _) => Info(Texts.LogoffExplanation);
        _lnkDestroy.LinkClicked += (_, _) => Info(Texts.DeleteVersusDestroy);

        _btnSave.Click += async (_, _) => await RunBusy("Instellingen opslaan…", () => { SaveSettings(showConfirmation: true); return Task.CompletedTask; });
        _btnSimulate.Click += async (_, _) => await RunBusy("Simulatie wordt uitgevoerd…", OnSimulate);
        _btnActivate.Click += async (_, _) => await RunBusy("Bezig…", OnActivateOrDeactivate);
        _btnPause.Click += async (_, _) => await RunBusy("Bezig…", OnPauseToggle);
        _btnCleanNow.Click += async (_, _) => await RunBusy("Bezig met opruimen…", OnCleanNow);
        _btnRepair.Click += async (_, _) => await RunBusy("Installatie herstellen…", OnRepair);
        _btnUninstall.Click += async (_, _) => await RunBusy("Bezig met verwijderen…", OnUninstall);
        _btnLog.Click += (_, _) => OnOpenLog();

        FormClosing += OnFormClosing;
        Activated += (_, _) => { if (!_busy) RefreshStatus(); };
    }

    // =====================================================================================
    // Configuratie <-> scherm
    // =====================================================================================

    private void FillFromConfig()
    {
        _loadingUi = true;
        try
        {
            int selected = Math.Max(0, _lstFolders.SelectedIndex);
            _lstFolders.Items.Clear();
            foreach (var f in _config.Folders) _lstFolders.Items.Add(f);
            if (_lstFolders.Items.Count > 0) _lstFolders.SelectedIndex = Math.Min(selected, _lstFolders.Items.Count - 1);

            _chkDaily.Checked = _config.Schedule.Daily;
            _dtpTime.Value = DateTime.Today.Add(ConfigValidator.TryParseTime(_config.Schedule.DailyTime, out var t) ? t : new TimeSpan(12, 30, 0));
            _chkLogon.Checked = _config.Schedule.AtLogon;
            _numMaxFiles.Value = Math.Min(Math.Max(_config.MaxFilesPerRun, 1), 5000);
            _rbRecycle.Checked = _config.DeleteMode == DeleteModes.RecycleBin;
            _rbPermanent.Checked = _config.DeleteMode == DeleteModes.Permanent;
            _chkEmptyBin.Checked = _config.EmptyRecycleBin;
            _numBinDays.Value = Math.Min(Math.Max(_config.RecycleBinRetentionDays, 0), 3650);
            _numBinDays.Enabled = _chkEmptyBin.Checked;
            UpdateDeleteNote();
        }
        finally { _loadingUi = false; }
        ShowSelectedFolder();
        UpdateTitle();
    }

    /// <summary>Neemt de algemene velden (planning, veiligheid) over in de configuratie.</summary>
    private void ApplyGeneralFieldsToConfig()
    {
        _config.Schedule.Daily = _chkDaily.Checked;
        _config.Schedule.DailyTime = _dtpTime.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        _config.Schedule.AtLogon = _chkLogon.Checked;
        _config.MaxFilesPerRun = (int)_numMaxFiles.Value;
        _config.DeleteMode = _rbPermanent.Checked ? DeleteModes.Permanent : DeleteModes.RecycleBin;
        _config.EmptyRecycleBin = _chkEmptyBin.Checked;
        _config.RecycleBinRetentionDays = (int)_numBinDays.Value;
    }

    private FolderSettings? SelectedFolder => _lstFolders.SelectedItem as FolderSettings;

    private void ShowSelectedFolder()
    {
        var f = SelectedFolder;
        _loadingUi = true;
        try
        {
            foreach (var c in new Control[] { _chkEnabled, _cmbRetention, _numDays, _chkRecurse, _chkAllowSync, _txtExtensions, _txtFiles, _txtSubfolders })
                c.Enabled = f is not null;
            _btnRemoveFolder.Enabled = f is not null && f.Type == FolderTypes.Custom;
            if (f is null) { _lblPath.Text = "–"; _lblFolderWarning.Text = ""; return; }

            _chkEnabled.Checked = f.Enabled;
            _lblPath.Text = f.ExpectedPath;
            _cmbRetention.SelectedIndex = RetentionOptions.IndexFor(f.RetentionDays);
            _numDays.Value = Math.Min(Math.Max(f.RetentionDays, 0), 3650);
            _numDays.Visible = RetentionOptions.All[_cmbRetention.SelectedIndex].Days is null;
            _chkRecurse.Checked = f.Recurse;
            _chkAllowSync.Checked = f.AllowNetworkOrSync;
            _txtExtensions.Text = ConfigStore.JoinList(f.Exclusions.Extensions);
            _txtFiles.Text = ConfigStore.JoinList(f.Exclusions.Files.Concat(f.Exclusions.Patterns));
            _txtSubfolders.Text = ConfigStore.JoinList(f.Exclusions.Subfolders);

            var check = CheckPath(f.ExpectedPath);
            _chkAllowSync.Visible = check.IsSync || check.IsNetwork || f.AllowNetworkOrSync;
            if (check.Blocked)
            {
                _lblFolderWarning.ForeColor = Color.Firebrick;
                _lblFolderWarning.Text = "Geblokkeerd: " + check.BlockReason;
            }
            else if ((check.IsSync || check.IsNetwork) && !f.AllowNetworkOrSync)
            {
                _lblFolderWarning.ForeColor = Color.Firebrick;
                _lblFolderWarning.Text = (check.IsSync ? "OneDrive/cloud" : "Netwerklocatie") +
                    ": opschonen is hier geblokkeerd. Klik hieronder alleen op 'toestaan' als u de gevolgen begrijpt.";
            }
            else
            {
                _lblFolderWarning.ForeColor = Color.DarkOrange;
                _lblFolderWarning.Text = string.Join("\n", check.Warnings);
            }
        }
        finally { _loadingUi = false; }
    }

    private PathCheck CheckPath(string path) =>
        PathRules.Check(path, Environment.GetEnvironmentVariable, _layout.BaseDirectory, _syncRoots, WindowsSystem.IsNonFixedDrive);

    private static string DescribeFolder(FolderSettings f)
    {
        string days = f.RetentionDays == 0 ? "direct" : f.RetentionDays == 1 ? "na 1 dag" : $"na {f.RetentionDays} dagen";
        return (f.Enabled ? "" : "(uit) ") + f.DisplayName + " – " + days;
    }

    private void EditFolder(Action<FolderSettings> change)
    {
        if (_loadingUi || SelectedFolder is null) return;
        change(SelectedFolder);
        MarkDirty();
        // Lijstweergave bijwerken zonder selectie te verliezen.
        int i = _lstFolders.SelectedIndex;
        _loadingUi = true;
        try { _lstFolders.Items[i] = _lstFolders.Items[i]; }
        finally { _loadingUi = false; }
    }

    private void OnRetentionChoice()
    {
        if (_loadingUi || _cmbRetention.SelectedItem is not RetentionOptions.Option o) return;
        _numDays.Visible = o.Days is null;
        if (o.Days is int d) EditFolder(f => f.RetentionDays = d);
        else EditFolder(f => f.RetentionDays = (int)_numDays.Value);
    }

    private void OnRecurseChanged()
    {
        if (_loadingUi) return;
        if (_chkRecurse.Checked && !Confirm(Texts.RecurseWarning))
        {
            _loadingUi = true; _chkRecurse.Checked = false; _loadingUi = false;
            return;
        }
        EditFolder(f => f.Recurse = _chkRecurse.Checked);
    }

    private void OnAllowSyncChanged()
    {
        if (_loadingUi || SelectedFolder is null) return;
        if (_chkAllowSync.Checked)
        {
            var check = CheckPath(SelectedFolder.ExpectedPath);
            string warning = (check.IsSync ? Texts.SyncWarning : Texts.NetworkWarning) + Texts.AllowSyncConfirm;
            if (!Confirm(warning))
            {
                _loadingUi = true; _chkAllowSync.Checked = false; _loadingUi = false;
                return;
            }
        }
        EditFolder(f => f.AllowNetworkOrSync = _chkAllowSync.Checked);
        ShowSelectedFolder();
    }

    private void OnEmptyBinChanged()
    {
        _numBinDays.Enabled = _chkEmptyBin.Checked;
        if (_loadingUi) return;
        if (_chkEmptyBin.Checked && !Confirm(Texts.EmptyBinConfirm, MessageBoxIcon.Warning))
        {
            _loadingUi = true; _chkEmptyBin.Checked = false; _loadingUi = false;
            _numBinDays.Enabled = false;
            return;
        }
        MarkDirty();
    }

    /// <summary>Korte beschrijving van wat er met de Prullenbak gebeurt, of "" als de optie uit staat.</summary>
    private string BinText() => _config.EmptyRecycleBin
        ? (_config.RecycleBinRetentionDays == 0
            ? "De Prullenbak wordt geleegd."
            : $"Items die langer dan {_config.RecycleBinRetentionDays} dagen in de Prullenbak staan, worden definitief verwijderd.")
        : "";

    private void UpdateDeleteNote() =>
        _lblDeleteNote.Text = _rbPermanent.Checked ? Texts.PermanentNote : Texts.RecycleBinNote;

    private void MarkDirty()
    {
        if (_loadingUi) return;
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle() => Text = Texts.AppTitle + (_dirty ? " – niet-opgeslagen wijzigingen" : "");

    // =====================================================================================
    // Mappen toevoegen en verwijderen
    // =====================================================================================

    private void OnAddFolder()
    {
        if (_config.Folders.Count >= ConfigValidator.MaxFolders) { Warn("Het maximale aantal mappen is bereikt."); return; }
        using var dialog = new FolderBrowserDialog { Description = "Kies een map die PraktijkClean mag opschonen", ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = dialog.SelectedPath;

        var check = CheckPath(path);
        if (check.Blocked) { Warn("Deze map kan niet worden gekozen.\n\n" + check.BlockReason); return; }
        if (_config.Folders.Any(f => PathRules.PathEquals(f.ExpectedPath, path))) { Warn("Deze map staat al in de lijst."); return; }
        if (_config.Folders.Any(f => PathRules.IsInside(path, f.ExpectedPath) || PathRules.IsInside(f.ExpectedPath, path)))
        {
            if (!Confirm("Deze map overlapt met een map die al in de lijst staat. Dat kan verwarrend zijn. Toch toevoegen?")) return;
        }
        string extra = check.Warnings.Count > 0 ? "\n\nLet op:\n• " + string.Join("\n• ", check.Warnings) : "";
        if (!Confirm(string.Format(Texts.AddFolderConfirm, path, extra))) return;

        var folder = ConfigStore.CreateCustomFolder(ConfigStore.NextCustomId(_config), path);
        _config.Folders.Add(folder);
        _lstFolders.Items.Add(folder);
        _lstFolders.SelectedItem = folder;
        MarkDirty();
    }

    private void OnRemoveFolder()
    {
        var f = SelectedFolder;
        if (f is null || f.Type != FolderTypes.Custom) return;
        if (!Confirm($"Map '{f.DisplayName}' uit PraktijkClean verwijderen?\n\nDe map en de bestanden erin blijven gewoon bestaan; " +
                     "PraktijkClean ruimt er alleen niet meer in op.")) return;
        _config.Folders.Remove(f);
        _lstFolders.Items.Remove(f);
        if (_lstFolders.Items.Count > 0) _lstFolders.SelectedIndex = 0;
        MarkDirty();
    }

    // =====================================================================================
    // Knoppen
    // =====================================================================================

    /// <summary>Slaat op en installeert/werkt bij. Retourneert false als de instellingen ongeldig zijn.</summary>
    private bool SaveSettings(bool showConfirmation)
    {
        ApplyGeneralFieldsToConfig();
        try
        {
            _installer.SaveAndApply(_config);
        }
        catch (ScheduledTaskException ex)
        {
            // Instellingen en script zijn wel opgeslagen; alleen de taak is niet gelukt.
            _dirty = false;
            UpdateTitle();
            Warn("De instellingen zijn opgeslagen, maar de geplande taak kon niet worden ingesteld.\n\n" + ex.Message +
                 "\n\nHandmatige simulatie en 'Nu opruimen' werken wel. Probeer 'Installatie herstellen' of neem contact op met uw systeembeheerder.");
            return true;
        }
        _dirty = false;
        UpdateTitle();
        if (showConfirmation)
        {
            Info(TaskXmlBuilder.HasAnyTrigger(_config.Schedule)
                ? "De instellingen zijn opgeslagen en de geplande taak is bijgewerkt. Er is niets verwijderd."
                : "De instellingen zijn opgeslagen. Er is geen automatisch moment gekozen, dus PraktijkClean werkt nu alleen handmatig.");
        }
        return true;
    }

    /// <summary>Zorgt dat het script en de actuele instellingen op schijf staan voordat het script draait.</summary>
    private bool EnsureSaved()
    {
        var state = _installer.GetState();
        if (_dirty || !state.ConfigInstalled || !state.ScriptIsCurrent || state.ConfigError is not null)
            return SaveSettings(showConfirmation: false);
        return true;
    }

    private async Task OnSimulate()
    {
        if (!EnsureSaved()) return;
        var report = await _runner.SimulateAsync();
        if (!CheckReport(report)) return;
        using var form = new SimulationResultForm(report, _config);
        form.ShowDialog(this);
    }

    private async Task OnActivateOrDeactivate()
    {
        bool active = !_config.SimulationMode && _config.AutomaticCleanupConfirmed;
        if (active)
        {
            ApplyGeneralFieldsToConfig();
            _installer.ReturnToSimulation(_config);
            _dirty = false;
            UpdateTitle();
            Info("PraktijkClean staat weer in simulatiemodus. Er wordt niets meer automatisch verwijderd.");
            return;
        }

        ApplyGeneralFieldsToConfig();
        if (!TaskXmlBuilder.HasAnyTrigger(_config.Schedule))
        {
            Warn("Kies eerst een moment van opschonen ('Dagelijks' en/of 'Bij aanmelden').");
            return;
        }
        if (!EnsureSaved()) return;

        // Eerst laten zien wat er bij de huidige instellingen zou gebeuren.
        var report = await _runner.SimulateAsync();
        if (!CheckReport(report)) return;
        var sb = new StringBuilder();
        sb.AppendLine("Automatische opschoning activeren?").AppendLine();
        sb.AppendLine($"Bij de huidige instellingen komen nu {report.Candidates} bestand(en) ({Format.Bytes(report.CandidateBytes)}) in aanmerking voor verwijdering.");
        foreach (var f in report.Folders.Where(f => f.Status is "Geblokkeerd" or "Fout"))
            sb.AppendLine($"• {NameOf(f.Id)}: {f.Message}");
        sb.AppendLine();
        sb.AppendLine("Vanaf nu worden oude bestanden automatisch " +
                      (_config.DeleteMode == DeleteModes.Permanent ? "DEFINITIEF verwijderd" : "naar de Prullenbak verplaatst") +
                      ": " + MomentText() + ".");
        if (report.RecycleBin is { } bin)
            sb.AppendLine(BinText() + $" Nu komen daarvoor {bin.Candidates} item(s) ({Format.Bytes(bin.CandidateBytes)}) in aanmerking.");
        if (report.Candidates > _config.MaxFilesPerRun)
            sb.AppendLine().AppendLine($"Let op: dat is meer dan het maximum van {_config.MaxFilesPerRun} per keer. Zolang dat zo is, wordt er uit voorzorg niets verwijderd.");
        sb.AppendLine().Append("Tip: bekijk eerst de details met 'Simulatie uitvoeren'. U kunt later altijd pauzeren of terug naar simulatiemodus.");
        if (!Confirm(sb.ToString(), MessageBoxIcon.Warning)) return;

        _installer.ActivateAutomaticCleanup(_config);
        _dirty = false;
        UpdateTitle();
        Info("Automatische opschoning is actief.");
    }

    private Task OnPauseToggle()
    {
        bool pause = !_config.Paused;
        ApplyGeneralFieldsToConfig();
        _installer.SetPaused(_config, pause);
        _dirty = false;
        UpdateTitle();
        Info(pause ? "Automatische opschoning is gepauzeerd. Klik op 'Hervatten' om weer te starten."
                   : "Automatische opschoning is hervat.");
        return Task.CompletedTask;
    }

    private async Task OnCleanNow()
    {
        if (!EnsureSaved()) return;
        var preview = await _runner.SimulateAsync();
        if (!CheckReport(preview)) return;
        int binCount = preview.RecycleBin?.Candidates ?? 0;
        if (preview.Candidates == 0 && binCount == 0)
        {
            Info("Er zijn op dit moment geen bestanden die in aanmerking komen. Er is niets verwijderd.");
            return;
        }
        if (preview.Candidates > _config.MaxFilesPerRun)
        {
            Warn($"Er komen {preview.Candidates} bestanden in aanmerking; dat is meer dan het maximum van {_config.MaxFilesPerRun} per keer. " +
                 "Uit voorzorg wordt er niets verwijderd. Controleer de simulatie en verhoog zo nodig het maximum.");
            return;
        }
        string where = _config.DeleteMode == DeleteModes.Permanent
            ? "DEFINITIEF verwijderd. Dit kan niet via Windows ongedaan worden gemaakt."
            : "naar de Prullenbak verplaatst.";
        string binLine = preview.RecycleBin is { } pb && pb.Candidates > 0
            ? $"Daarnaast worden {pb.Candidates} item(s) ({Format.Bytes(pb.CandidateBytes)}) DEFINITIEF uit de Prullenbak verwijderd.\n\n"
            : "";
        if (!Confirm($"Er worden nu {preview.Candidates} bestand(en) ({Format.Bytes(preview.CandidateBytes)}) {where}\n\n" + binLine +
                     "Wilt u eerst de lijst zien? Kies dan 'Nee' en gebruik 'Simulatie uitvoeren'.\n\nNu opruimen?", MessageBoxIcon.Warning))
            return;

        var result = await _runner.CleanNowAsync();
        if (!CheckReport(result)) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{result.Deleted} bestand(en) verwijderd ({Format.Bytes(result.DeletedBytes)}).");
        if (result.RecycleBin is { } rb)
            sb.AppendLine($"Prullenbak: {rb.Deleted} item(s) verwijderd ({Format.Bytes(rb.DeletedBytes)}).");
        sb.AppendLine($"{result.Skipped} overgeslagen, {result.Errors} fout(en).");
        foreach (var m in result.Messages) sb.AppendLine(m);
        foreach (var f in result.Folders.Where(f => f.Message.Length > 0)) sb.AppendLine($"• {NameOf(f.Id)}: {f.Message}");
        if (result.RecycleBin is { Message.Length: > 0 } rbm) sb.AppendLine($"• Prullenbak: {rbm.Message}");
        if (result.Errors > 0) Warn(sb.ToString()); else Info(sb.ToString());
    }

    private Task OnRepair()
    {
        ApplyGeneralFieldsToConfig();
        _installer.Repair(_config);
        _dirty = false;
        UpdateTitle();
        Info("De installatie is hersteld: script, instellingen en geplande taak zijn opnieuw ingesteld. Er is niets verwijderd.");
        return Task.CompletedTask;
    }

    private Task OnUninstall()
    {
        if (!Confirm(Texts.UninstallConfirm, MessageBoxIcon.Warning)) return Task.CompletedTask;
        var r = _installer.Uninstall();
        var sb = new StringBuilder("PraktijkClean is verwijderd voor deze gebruiker. Uw eigen bestanden zijn niet aangeraakt.");
        if (!r.TaskRemoved) sb.Append("\n\nDe geplande taak kon niet worden verwijderd: ").Append(r.TaskError)
                              .Append("\nVerwijder de taak '").Append(_installer.TaskName).Append("' eventueel handmatig in Taakplanner.");
        if (r.RemainingItems.Count > 0)
            sb.Append("\n\nIn ").Append(_layout.BaseDirectory).Append(" staan nog bestanden die niet van PraktijkClean zijn; die zijn blijven staan.");
        Info(sb.ToString());

        // Terug naar de beginsituatie (niet opgeslagen, simulatiemodus).
        _config = ConfigStore.CreateDefault(KnownFolders.GetPath(KnownFolderNames.Desktop), KnownFolders.GetPath(KnownFolderNames.Downloads));
        _dirty = true;
        FillFromConfig();
        return Task.CompletedTask;
    }

    private void OnOpenLog()
    {
        if (!File.Exists(_layout.LogPath)) { Info("Er is nog geen logboek. Het logboek wordt aangemaakt bij de eerste controle."); return; }
        Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _layout.LogPath + "\"") { UseShellExecute = false });
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        if (!_dirty) return;
        var answer = MessageBox.Show(this, "Er zijn niet-opgeslagen wijzigingen. Nu opslaan?", Texts.AppTitle,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
        if (answer == DialogResult.Yes)
        {
            try { SaveSettings(showConfirmation: false); }
            catch (InvalidOperationException ex) { Warn(ex.Message); e.Cancel = true; }
        }
    }

    // =====================================================================================
    // Status
    // =====================================================================================

    private void RefreshStatus()
    {
        var state = _installer.GetState();
        var last = RunReport.TryLoadStatus(_layout.StatusPath);
        var problems = new List<string>();

        if (!state.ConfigInstalled)
        {
            _lblMode.ForeColor = Color.DimGray;
            _lblMode.Text = "Nog niet ingesteld – controleer de instellingen en klik op 'Instellingen opslaan'.";
        }
        else if (_config.Paused)
        {
            _lblMode.ForeColor = Color.DarkOrange;
            _lblMode.Text = "Gepauzeerd – er wordt niets automatisch gecontroleerd of verwijderd.";
        }
        else if (_config.SimulationMode || !_config.AutomaticCleanupConfirmed)
        {
            _lblMode.ForeColor = Color.SteelBlue;
            _lblMode.Text = "Simulatiemodus – er wordt alleen gecontroleerd, niets verwijderd.";
        }
        else
        {
            _lblMode.ForeColor = Color.ForestGreen;
            _lblMode.Text = "Actief – oude bestanden worden automatisch " +
                            (_config.DeleteMode == DeleteModes.Permanent ? "definitief verwijderd." : "naar de Prullenbak verplaatst.") +
                            (_config.EmptyRecycleBin ? " De Prullenbak wordt automatisch geleegd." : "");
        }

        if (state.TaskError is not null) problems.Add(state.TaskError);
        if (!state.ConfigInstalled)
            _lblTask.Text = "Geplande taak: nog niet aangemaakt.";
        else if (state.Task is null)
        {
            _lblTask.Text = TaskXmlBuilder.HasAnyTrigger(_config.Schedule)
                ? "Geplande taak: ontbreekt."
                : "Geplande taak: geen (er is geen automatisch moment gekozen; alleen handmatig gebruik).";
            if (TaskXmlBuilder.HasAnyTrigger(_config.Schedule)) problems.Add("De geplande taak ontbreekt. Gebruik 'Installatie herstellen'.");
        }
        else
            _lblTask.Text = $"Geplande taak: '{_installer.TaskName}' ({(state.Task.Enabled ? "aan" : "uit")}) – {MomentText()}.";

        if (state.ScriptInstalled && !state.ScriptIsCurrent)
            problems.Add("Het geïnstalleerde opschoonscript wijkt af van deze versie. Gebruik 'Installatie herstellen'.");
        if (state.ConfigError is not null) problems.Add(state.ConfigError);

        if (last?.LastRunUtc is DateTime lr)
        {
            _lblLastRun.Text = $"Laatste controle: {lr.ToLocalTime():dd-MM-yyyy HH:mm} ({TriggerText(last.Trigger)}, {last.Action}).";
            _lblLastResult.Text = $"Resultaat: {last.Deleted} verwijderd ({Format.Bytes(last.DeletedBytes)}), {last.Candidates} in aanmerking, " +
                                  $"{last.Skipped} overgeslagen, {last.TooYoung} te recent, {last.Errors} fout(en).";
            problems.AddRange(last.Messages.Where(m => last.Errors > 0 || last.ExitCode != 0));
            problems.AddRange(last.Folders.Where(f => f.Status is "Geblokkeerd" or "Fout").Select(f => $"{NameOf(f.Id)}: {f.Message}"));
            if (last.RecycleBin is { } lb)
            {
                _lblLastResult.Text += $"\nPrullenbak: {lb.Deleted} item(s) geleegd ({Format.Bytes(lb.DeletedBytes)}), {lb.Candidates} in aanmerking, {lb.TooYoung} te recent.";
                if (lb.Message.Length > 0) problems.Add("Prullenbak: " + lb.Message);
            }
        }
        else
        {
            _lblLastRun.Text = "Laatste controle: nog niet uitgevoerd.";
            _lblLastResult.Text = "";
        }

        // Taakplanner geeft ook voor een uitgeschakelde (gepauzeerde) taak een volgende tijd terug; die niet tonen.
        _lblNextRun.Text = state.Task is { Enabled: false } ? "Volgende geplande uitvoering: geen (gepauzeerd)."
            : state.Task?.NextRunTime is DateTime next
            ? $"Volgende geplande uitvoering: {next:dd-MM-yyyy HH:mm}" + (_config.Schedule.AtLogon ? " (en bij elke aanmelding)." : ".")
            : _config.Schedule.AtLogon && state.Task is not null ? "Volgende uitvoering: bij de volgende aanmelding." : "Volgende geplande uitvoering: –";

        _lblProblems.Text = problems.Count == 0 ? "" : "Aandacht: " + string.Join("\n", problems.Distinct());

        bool active = !_config.SimulationMode && _config.AutomaticCleanupConfirmed;
        _btnActivate.Text = active ? "Terug naar simulatiemodus" : "Automatische opschoning activeren…";
        _btnPause.Text = _config.Paused ? "Hervatten" : "Tijdelijk pauzeren";
        _btnPause.Enabled = !_busy && state.ConfigInstalled;
        _btnLog.Enabled = !_busy && File.Exists(_layout.LogPath);
    }

    private string MomentText()
    {
        var parts = new List<string>();
        if (_config.Schedule.Daily) parts.Add("dagelijks om " + _config.Schedule.DailyTime);
        if (_config.Schedule.AtLogon) parts.Add("bij aanmelden");
        return parts.Count == 0 ? "alleen handmatig" : string.Join(" en ", parts);
    }

    private static string TriggerText(string? trigger) => trigger switch
    {
        "Taakplanner" => "automatisch",
        "Handmatig" => "handmatig",
        null or "" => "onbekend",
        _ => trigger,
    };

    private string NameOf(string id) => _config.Folders.FirstOrDefault(f => f.Id == id)?.DisplayName ?? id;

    // =====================================================================================
    // Hulpfuncties
    // =====================================================================================

    private async Task RunBusy(string message, Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        SetButtonsEnabled(false);
        UseWaitCursor = true;
        _lblBusy.Text = message;
        try
        {
            await action();
        }
        catch (ScriptRunException ex)
        {
            string details = string.IsNullOrWhiteSpace(ex.Details) ? "" : "\n\nTechnische details:\n" + Shorten(ex.Details!, 800);
            Error(ex.Message + details);
        }
        catch (ScheduledTaskException ex) { Error(ex.Message); }
        catch (InvalidOperationException ex) { Error(ex.Message); }
        catch (IOException ex) { Error("Een bestand kon niet worden gelezen of geschreven (" + ex.GetType().Name + ")."); }
        catch (UnauthorizedAccessException) { Error("Geen toegang tot een bestand of map van PraktijkClean."); }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            _lblBusy.Text = "";
            SetButtonsEnabled(true);
            RefreshStatus();
        }
    }

    /// <summary>Controleert of het script een bruikbaar resultaat gaf; toont anders de reden.</summary>
    private bool CheckReport(RunReport report)
    {
        if (report.ExitCode == 4)
        {
            Info("Er draait op dit moment al een opschoning (bijvoorbeeld de geplande taak). Probeer het over een minuut opnieuw.");
            return false;
        }
        if (report.ExitCode == 1)
        {
            Error("Het opschoonscript heeft niets gedaan:\n\n" + string.Join("\n", report.Messages));
            return false;
        }
        return true;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (var b in new[] { _btnSave, _btnSimulate, _btnActivate, _btnPause, _btnCleanNow, _btnRepair, _btnUninstall, _btnLog, _btnAddFolder })
            b.Enabled = enabled;
        _btnRemoveFolder.Enabled = enabled && SelectedFolder?.Type == FolderTypes.Custom;
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s.Trim() : s.Substring(0, max).Trim() + "…";

    private bool Confirm(string text, MessageBoxIcon icon = MessageBoxIcon.Question) =>
        MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void Info(string text) => MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void Warn(string text) => MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void Error(string text) => MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
