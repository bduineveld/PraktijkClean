using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PraktijkClean.Core;
using PraktijkClean.Core.Windows;

namespace PraktijkClean;

/// <summary>
/// Het hoofdvenster:
/// <list type="bullet">
/// <item>bovenaan een gekleurde kop met de status, één hoofdknop (aanzetten / pauzeren / hervatten / herstellen)
/// en het menu "Meer" met de minder vaak gebruikte functies;</item>
/// <item>in het midden tabbladen: Mappen, Planning en veiligheid, Logboek;</item>
/// <item>onderaan alleen "Simulatie bekijken…" en "Opslaan".</item>
/// </list>
/// De opbouw van het scherm staat in <see cref="BuildLayout"/>, het gedrag in de methoden die met "On" beginnen.
/// Alle echte logica (opslaan, taak, script) zit in PraktijkClean.Core.
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
    private bool _closeAfterBusy;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly List<Label> _wrapLabels = new();
    private readonly System.Windows.Forms.Timer _footerTimer = new() { Interval = 8000 };
    private string _footerFlash = "";

    // ---------------------------------------------------------------- Kop (status)
    private readonly TableLayoutPanel _header = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 12, 12, 12) };
    private readonly Label _lblState = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _lblStateText = new() { AutoSize = true, Margin = new Padding(1, 0, 0, 4) };
    private readonly Label _lblFacts = new() { AutoSize = true, Margin = new Padding(1, 0, 0, 0), ForeColor = Color.FromArgb(70, 70, 70) };
    private readonly Label _lblProblems = new() { AutoSize = true, Margin = new Padding(1, 4, 0, 0), ForeColor = Color.Firebrick };
    private readonly Button _btnPrimary = new() { AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(8, 0, 0, 0) };
    private readonly Button _btnMenu = new() { Text = "☰  Meer", AutoSize = true, Padding = new Padding(4, 2, 4, 2), Margin = new Padding(8, 0, 0, 0) };
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false, ShowCheckMargin = true };
    private readonly ToolStripMenuItem _miCleanNow = new("Nu opruimen…");
    private readonly ToolStripMenuItem _miPause = new("Tijdelijk pauzeren");
    private readonly ToolStripMenuItem _miSimulationMode = new("Terug naar simulatiemodus");
    private readonly ToolStripMenuItem _miTray = new("Icoon rechtsonder in het systeemvak");
    private readonly ToolStripMenuItem _miLog = new("Logboek openen");
    private readonly ToolStripMenuItem _miTrouble = new("Problemen oplossen");
    private readonly ToolStripMenuItem _miRepair = new("Geplande taak en script opnieuw instellen");
    private readonly ToolStripMenuItem _miOpenDir = new("Programmamap openen");
    private readonly ToolStripMenuItem _miUninstall = new("PraktijkClean verwijderen…");
    private readonly ToolStripMenuItem _miAbout = new("Over PraktijkClean");

    // ---------------------------------------------------------------- Tabblad Mappen
    private bool _prewarming;
    private int _prewarmNext;
    private bool _prewarmSubscribed;
    private string _diskStamp = "";

    /// <summary>Wijzigt als een van de bestanden van PraktijkClean is veranderd (of elk uur, voor "morgen 12:30").</summary>
    private string DiskStamp()
    {
        var parts = new[] { _layout.ConfigPath, _layout.StatusPath, _layout.LogPath, _layout.DeletedListPath, _layout.ScriptPath }
            .Select(p => { var f = new FileInfo(p); return f.Exists ? f.LastWriteTimeUtc.Ticks + "/" + f.Length : "-"; });
        return string.Join("|", parts) + "|" + TrayIntegration.IsEnabled() + "|" + DateTime.Now.ToString("yyyyMMddHH");
    }
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
    private readonly ListBox _lstFolders = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly Button _btnAddFolder = new() { Text = "Map toevoegen…", AutoSize = true };
    private readonly Button _btnRemoveFolder = new() { Text = "Verwijderen", AutoSize = true };
    private readonly Label _lblFolderName = new() { AutoSize = true, Margin = new Padding(3, 0, 3, 0) };
    private readonly Label _lblPath = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6) };
    private readonly Label _lblFolderWarning = new() { AutoSize = true, ForeColor = Color.DarkOrange };
    private readonly CheckBox _chkEnabled = new() { Text = "Deze map opruimen", AutoSize = true };
    private readonly ComboBox _cmbRetention = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly NumberBox _numDays = new() { Minimum = 0, Maximum = 3650, Width = 70 };
    private FlowLayoutPanel _daysRow = null!;
    private readonly CheckBox _chkRecurse = new() { Text = "Ook submappen doorzoeken (afgeraden)", AutoSize = true };
    private readonly CheckBox _chkAllowSync = new() { Text = "Opruimen op deze OneDrive-/netwerklocatie toestaan (afgeraden)", AutoSize = true };
    private readonly ListView _lstExclusions = new()
    {
        View = View.Details, FullRowSelect = true, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Height = 110,
    };
    private readonly Button _btnExclAdd = new() { Text = "Toevoegen ▾", AutoSize = true };
    private readonly ContextMenuStrip _exclMenu = new() { ShowImageMargin = false };
    private readonly Button _btnExclRemove = new() { Text = "Verwijderen", AutoSize = true };

    // ---------------------------------------------------------------- Tabblad Planning en veiligheid
    private readonly CheckBox _chkDaily = new() { Text = "Dagelijks om", AutoSize = true };
    private readonly DateTimePicker _dtpTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    private readonly CheckBox _chkLogon = new() { Text = "Bij aanmelden van Windows", AutoSize = true };
    private readonly LinkLabel _lnkLogoff = new() { Text = "Waarom niet bij afmelden?", AutoSize = true };
    private readonly RadioButton _rbRecycle = new() { Text = "Naar de Prullenbak (aanbevolen)", AutoSize = true };
    private readonly RadioButton _rbPermanent = new() { Text = "Direct definitief verwijderen", AutoSize = true };
    private readonly Label _lblDeleteNote = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(22, 0, 3, 6) };
    private readonly CheckBox _chkEmptyBin = new() { Text = "Prullenbak legen: items die er langer dan", AutoSize = true };
    private readonly NumberBox _numBinDays = new() { Minimum = 0, Maximum = 3650, Width = 60 };
    private readonly LinkLabel _lnkBinInfo = new() { Text = "Uitleg", AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
    private readonly NumberBox _numMaxFiles = new() { Minimum = 1, Maximum = 5000, Width = 80 };
    private readonly LinkLabel _lnkDestroy = new() { Text = "Verwijderen is niet hetzelfde als veilig vernietigen – lees meer", AutoSize = true };

    // ---------------------------------------------------------------- Tabblad Logboek
    private readonly Label _lblLastRun = new() { AutoSize = true };
    private readonly Label _lblLastResult = new() { AutoSize = true };
    private readonly Label _lblTask = new() { AutoSize = true };
    private readonly Label _lblNextRun = new() { AutoSize = true };
    private readonly Label _lblWeek = new() { AutoSize = true };
    private readonly Button _btnLog = new() { Text = "Logboek openen", AutoSize = true };
    private readonly Label _lblDeletedInfo = new() { AutoSize = true, Margin = new Padding(3, 0, 3, 6) };
    private readonly TextBox _txtDeletedFilter = new() { Width = 220 };
    private readonly ListView _lstDeleted = new()
    {
        View = View.Details, FullRowSelect = true, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Height = 220,
    };
    private readonly ComboBox _cmbListDays = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Button _btnClearList = new() { Text = "Lijst wissen…", AutoSize = true };
    private List<DeletedFilesList.Entry> _deleted = new();
    private string _deletedStamp = "";

    // ---------------------------------------------------------------- Onderbalk
    private readonly Button _btnSimulate = new() { Text = "Simulatie bekijken…", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly Label _lblFooter = new() { AutoSize = true, Margin = new Padding(10, 11, 3, 3) };
    private readonly Button _btnRevert = new() { Text = "Ongedaan maken", AutoSize = true };
    private readonly Button _btnSave = new() { Text = "Opslaan", AutoSize = true, Padding = new Padding(14, 2, 14, 2) };

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
            FitWrapLabels();
            if (WindowsSystem.IsElevated()) Warn(Texts.ElevatedWarning);
            if (startupNotice is not null) Info(startupNotice);
            // Al geïnstalleerd: de geïnstalleerde kopie, het Startmenu en de Apps-lijst bijwerken (op de achtergrond).
            if (File.Exists(_layout.ConfigPath)) StartRegistration(firstInstallInfo: false);
        };

        // Een tweede start (bijv. via het icoon in het systeemvak of het Startmenu) haalt dit venster naar voren.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            try { BeginInvoke(new Action(BringToFrontNow)); } catch (InvalidOperationException) { }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        FormClosed += (_, _) => { _showWait.Unregister(null); _showEvent.Dispose(); };
    }

    // =====================================================================================
    // Opstarten
    // =====================================================================================

    /// <summary>Het opschoonscript zit in de .exe ingebakken en wordt bij opslaan weggeschreven.</summary>
    internal static string ReadEmbeddedScript()
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
                notice = error + "\n\nDe standaardinstellingen (simulatiemodus) zijn geladen. Controleer ze en klik op 'Opslaan'.";
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
                     "Controleer de waarschuwingen (bijvoorbeeld OneDrive) en klik daarna op 'Opslaan'.";
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
        Text = Texts.AppTitle + " " + Program.Version;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 680);
        MinimumSize = new Size(780, 580);
        if (AppIcons.App is { } appIcon) Icon = appIcon;
        foreach (var o in RetentionOptions.All) _cmbRetention.Items.Add(o);

        // ---- Kop: status links, hoofdknop en menu rechts
        _lblState.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
        _lblFolderName.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
        _btnPrimary.Font = new Font(Font, FontStyle.Bold);
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 4; i++) _header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _header.Controls.Add(Wrap(_lblState), 0, 0);
        _header.Controls.Add(Wrap(_lblStateText), 0, 1);
        _header.Controls.Add(Wrap(_lblFacts), 0, 2);
        _header.Controls.Add(Wrap(_lblProblems), 0, 3);
        var headerButtons = Row(_btnPrimary, _btnMenu);
        headerButtons.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _header.Controls.Add(headerButtons, 1, 0);
        _header.SetRowSpan(headerButtons, 4);

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miCleanNow, _miPause, _miSimulationMode, new ToolStripSeparator(),
            _miTray, _miLog, new ToolStripSeparator(),
            _miTrouble, _miUninstall, new ToolStripSeparator(),
            _miAbout,
        });
        _miTrouble.DropDownItems.AddRange(new ToolStripItem[] { _miRepair, _miOpenDir });
        _miCleanNow.ToolTipText = "Ruimt direct op, na een overzicht en uw bevestiging.";
        _miRepair.ToolTipText = "Zet het opschoonscript en de geplande taak opnieuw neer met uw huidige instellingen. " +
                                "Uw instellingen blijven behouden; er wordt niets verwijderd.";
        _miTray.ToolTipText = "Groen = aan, blauw = simulatie, oranje = gepauzeerd, rood = probleem. Start bij elke aanmelding.";
        _miUninstall.ToolTipText = "Haalt PraktijkClean weg (taak, instellingen, logboek, Startmenu). Uw eigen bestanden blijven staan.";

        // ---- Tabblad Mappen: lijst links, instellingen van de gekozen map rechts
        var folderLeft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 10, 0) };
        folderLeft.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        folderLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        folderLeft.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        folderLeft.Controls.Add(Heading("Mappen", top: 0), 0, 0);
        folderLeft.Controls.Add(_lstFolders, 0, 1);
        folderLeft.Controls.Add(Row(_btnAddFolder, _btnRemoveFolder), 0, 2);

        var details = Grid();
        AddRow(details, _lblFolderName);
        AddRow(details, Wrap(_lblPath));
        AddRow(details, Wrap(_lblFolderWarning));
        AddRow(details, _chkEnabled);
        _daysRow = Row(_numDays, Caption("dagen"));
        AddRow(details, Row(Caption("Opruimen als een bestand:"), _cmbRetention, _daysRow));
        AddRow(details, Hint("Leeftijd = tijd sinds het bestand is aangemaakt of gewijzigd (de nieuwste datum telt). Een net gedownload bestand is dus altijd 'nieuw'."));
        AddRow(details, _chkRecurse);
        AddRow(details, _chkAllowSync);
        AddRow(details, Heading("Nooit verwijderen (uitzonderingen)"));
        AddRow(details, _lstExclusions);
        _lstExclusions.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        AddRow(details, Row(_btnExclAdd, _btnExclRemove));
        AddRow(details, Hint("Snelkoppelingen, verborgen en geopende bestanden worden altijd overgeslagen. Tip: in 'Simulatie bekijken' " +
                             "kunt u met de rechtermuisknop een bestand direct uitsluiten."));
        _exclMenu.Items.Add("Bestand(en) uit deze map kiezen…", null, (_, _) => OnExcludeFiles());
        _exclMenu.Items.Add("Submap kiezen…", null, (_, _) => OnExcludeSubfolder());
        _exclMenu.Items.Add("Extensie, naam of patroon typen…", null, (_, _) => OnExcludeText());

        var folderRight = Scroller(details);
        var folders = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10) };
        folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
        folders.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        folders.Controls.Add(folderLeft, 0, 0);
        folders.Controls.Add(folderRight, 1, 0);

        // ---- Tabblad Planning en veiligheid
        var plan = Grid();
        AddRow(plan, Heading("Wanneer controleren en opruimen", top: 0));
        AddRow(plan, Row(_chkDaily, _dtpTime));
        _chkDaily.Margin = new Padding(3, 6, 3, 3);
        AddRow(plan, _chkLogon);
        AddRow(plan, Row(new Label
        {
            Text = "Een gemiste controle (pc stond uit) wordt later ingehaald.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 6, 8),
        }, _lnkLogoff));
        AddRow(plan, Heading("Wat er met oude bestanden gebeurt"));
        AddRow(plan, _rbRecycle);
        AddRow(plan, _rbPermanent);
        AddRow(plan, Wrap(_lblDeleteNote));
        AddRow(plan, Row(_chkEmptyBin, _numBinDays, Caption("dag(en) in staan"), _lnkBinInfo));
        AddRow(plan, Heading("Veiligheid"));
        AddRow(plan, Row(Caption("Nooit meer dan"), _numMaxFiles, Caption("bestanden per keer")));
        AddRow(plan, Hint("Komen er meer bestanden in aanmerking, dan wordt er uit voorzorg niets verwijderd (bijvoorbeeld bij een verkeerd gekozen map)."));
        AddRow(plan, _lnkDestroy);
        AddRow(plan, Hint("Wijzigingen op dit tabblad en bij Mappen worden pas actief als u op 'Opslaan' klikt.", top: 14));

        // ---- Tabblad Logboek: eerst wat er is verwijderd, daarna de controles
        var log = Grid();
        AddRow(log, Heading("Verwijderde bestanden", top: 0));
        AddRow(log, Wrap(_lblDeletedInfo));
        AddRow(log, Row(Caption("Zoeken:"), _txtDeletedFilter));
        AddRow(log, _lstDeleted);
        _lstDeleted.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        AddRow(log, Row(Caption("Namen bewaren:"), _cmbListDays, _btnClearList));
        _cmbListDays.Margin = new Padding(3, 4, 12, 3);
        AddRow(log, Hint("Na de gekozen termijn worden de namen automatisch uit deze lijst gewist. Dubbelklik op een bestand " +
                         "dat naar de Prullenbak is gegaan om de Prullenbak te openen (daar kunt u het terugzetten)."));
        AddRow(log, Heading("Laatste controle"));
        AddRow(log, Wrap(_lblLastRun));
        AddRow(log, Wrap(_lblLastResult));
        AddRow(log, Heading("Planning"));
        AddRow(log, Wrap(_lblTask));
        AddRow(log, Wrap(_lblNextRun));
        AddRow(log, Heading("Afgelopen 7 dagen"));
        AddRow(log, Wrap(_lblWeek));
        AddRow(log, _btnLog);
        AddRow(log, Hint("Het logboek bevat per controle alleen aantallen en groottes, nooit bestandsnamen. De namen staan alleen " +
                         "in de lijst hierboven, zo lang als u heeft gekozen."));

        _tabs.TabPages.Add(Page("Mappen", folders));
        _tabs.TabPages.Add(Page("Planning en veiligheid", Scroller(plan, padding: 16)));
        _tabs.TabPages.Add(Page("Logboek", Scroller(log, padding: 16)));

        // ---- Onderbalk
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Padding = new Padding(10, 8, 10, 10) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_btnSimulate, 0, 0);
        footer.Controls.Add(Wrap(_lblFooter), 1, 0);
        footer.Controls.Add(_btnRevert, 2, 0);
        footer.Controls.Add(_btnSave, 3, 0);
        _btnSave.Font = new Font(Font, FontStyle.Bold);
        var footerLine = new Label { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 0) };
        middle.Controls.Add(_tabs);

        // Volgorde telt bij Dock: wat het laatst wordt toegevoegd, krijgt als eerste zijn plek.
        Controls.Add(middle);
        Controls.Add(footerLine);
        Controls.Add(footer);
        Controls.Add(_header);

        _lstExclusions.Columns.Add("Soort", 90);
        _lstExclusions.Columns.Add("Wordt overgeslagen", 300);
        _lstDeleted.Columns.Add("Wanneer", 120);
        _lstDeleted.Columns.Add("Bestand", 240);
        _lstDeleted.Columns.Add("Map", 240);
        _lstDeleted.Columns.Add("Wat er gebeurde", 150);
        foreach (var o in DeletedFilesList.Options) _cmbListDays.Items.Add(o.Label);
        Load += (_, _) =>
        {
            // Kolombreedtes en regelhoogte schalen niet vanzelf mee met de Windows-schaal.
            _lstExclusions.Columns[0].Width = UiFont.Px(90);
            FitExclusionColumns();
            _lstDeleted.Height = UiFont.Px(220);
            _lstDeleted.Columns[0].Width = UiFont.Px(120);
            _lstDeleted.Columns[3].Width = UiFont.Px(150);
            FitDeletedColumns();
            _lstFolders.ItemHeight = Math.Min(255, (int)(_lstFolders.Font.Height * 2.7));
            FitWrapLabels();
            BeginInvoke(new Action(PrewarmTabs));
        };
        _lstExclusions.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(new Action(FitExclusionColumns)); };
        _lstDeleted.Resize += (_, _) => { if (IsHandleCreated) BeginInvoke(new Action(FitDeletedColumns)); };
        // Na vergroten/verkleinen kloppen de verborgen tabbladen niet meer: zodra het rustig is opnieuw voorbereiden.
        Resize += (_, _) => { FitWrapLabels(); if (IsHandleCreated && Visible) PrewarmTabs(); };
        // Tabblad wisselen: pas tekenen als het nieuwe tabblad helemaal klaar is (anders bouwt het zichtbaar op).
        _tabs.Selecting += (_, _) => { if (!_prewarming) ScreenUpdate.StopDrawing(_tabs); };
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_prewarming) return;
            FitWrapLabels();
            BeginInvoke(new Action(() => ScreenUpdate.ResumeDrawing(_tabs)));
        };

        var tips = new ToolTip { AutoPopDelay = 15000 };
        tips.SetToolTip(_btnSave, "Slaat de instellingen op en werkt de geplande taak bij. Pas daarna gelden de wijzigingen. Er wordt niets verwijderd.");
        tips.SetToolTip(_btnRevert, "Zet alle velden terug naar de laatst opgeslagen instellingen.");
        tips.SetToolTip(_btnSimulate, "Laat zien welke bestanden nu zouden worden opgeruimd. Er wordt niets verwijderd.");
        tips.SetToolTip(_numBinDays, "Alleen items die langer dan dit aantal dagen in de Prullenbak staan. 0 = alles (behalve de laatste 15 minuten).");
        tips.SetToolTip(_chkEmptyBin, "Leegt ook de Prullenbak van deze gebruiker, inclusief zelf weggegooide bestanden.");
        tips.SetToolTip(_btnExclAdd, "Een bestand, submap, extensie (.docx) of patroon (protocol-*) dat nooit verwijderd mag worden.");
        tips.SetToolTip(_btnLog, "Opent het logboek in Kladblok.");
        tips.SetToolTip(_cmbListDays, "Hoe lang de namen van verwijderde bestanden in deze lijst blijven staan. Geldt na 'Opslaan'.");
        tips.SetToolTip(_btnClearList, "Wist de namen uit deze lijst. De bestanden zelf veranderen hierdoor niet.");
        tips.SetToolTip(_numMaxFiles, "Komen er meer bestanden in aanmerking, dan wordt er uit voorzorg niets verwijderd.");

        // Knoppen passen zich aan hun tekst aan. Standaard mogen ze alleen groeien ("GrowOnly"); dan maakt de
        // schermschaal ze bij 150-250% dubbel zo groot (eerst gemeten met de grote letter, daarna nog eens geschaald).
        foreach (var b in AllControls(this).OfType<Button>().Where(b => b.AutoSize))
        {
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(Math.Max(b.MinimumSize.Width, 80), 28);
            b.Padding = new Padding(Math.Max(b.Padding.Left, 6), b.Padding.Top, Math.Max(b.Padding.Right, 6), b.Padding.Bottom);
        }

        ResumeLayout(false);
        PerformLayout();
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in AllControls(c)) yield return child;
        }
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    /// <summary>Een paneel dat kan scrollen als de inhoud niet past (kleine schermen, hoge schermschaal).</summary>
    private static Panel Scroller(TableLayoutPanel content, int padding = 0)
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(padding) };
        content.Dock = DockStyle.Top;
        p.Controls.Add(content);
        return p;
    }

    /// <summary>
    /// Een kolom met regels onder elkaar. Bewust één kolom: bij twee kolommen geeft TableLayoutPanel de breedte
    /// van regels over de volle breedte aan de eerste kolom, waardoor de tweede kolom onzichtbaar smal wordt.
    /// </summary>
    private static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    /// <summary>Voegt een regel toe over de volle breedte.</summary>
    private static void AddRow(TableLayoutPanel t, Control control)
    {
        int row = t.RowCount;
        t.RowCount = row + 1;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor = AnchorStyles.Left;
        t.Controls.Add(control, 0, row);
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        f.Controls.AddRange(controls);
        return f;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    private Label Heading(string text, int top = 16) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold), Margin = new Padding(0, top, 3, 4),
    };

    private Label Hint(string text, int top = 2, int bottom = 8) => Wrap(new Label
    {
        Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, top, 3, bottom),
    });

    /// <summary>Label dat afbreekt op de beschikbare breedte (zie <see cref="FitWrapLabels"/>).</summary>
    private Label Wrap(Label l)
    {
        _wrapLabels.Add(l);
        return l;
    }

    /// <summary>
    /// Een automatisch groeiend label breekt in WinForms alleen af met een maximale breedte. Die hangt af van de
    /// vensterbreedte en de schermschaal, dus wordt hij bij elke formaatwijziging opnieuw berekend.
    /// </summary>
    private void FitWrapLabels()
    {
        // Eerst alle breedtes bepalen, dan alles in één keer toepassen: elke gewijzigde MaximumSize laat anders
        // het hele tabblad opnieuw indelen (dat kostte per tabblad seconden).
        var changes = new List<(Label Label, int Width)>();
        foreach (var l in _wrapLabels)
        {
            // Alleen wat nu zichtbaar is: op een verborgen tabblad kloppen de breedtes niet, en dan zou elk bezoek aan
            // dat tabblad alles opnieuw laten indelen.
            if (!l.Visible || l.Parent is not Control p || p is FlowLayoutPanel) continue;
            int width;
            if (p is TableLayoutPanel t)
            {
                var pos = t.GetPositionFromControl(l);
                int[] cols = t.GetColumnWidths();
                if (pos.Column < 0 || cols.Length == 0) continue;
                width = 0;
                for (int i = pos.Column; i < Math.Min(cols.Length, pos.Column + t.GetColumnSpan(l)); i++) width += cols[i];
                // Een tabel die zelf meegroeit (AutoSize zonder Dock): neem de breedte van wat eromheen zit.
                if (t.Dock == DockStyle.None && t.Parent is Control tp) width = Math.Max(width, tp.ClientSize.Width - t.Left);
            }
            else
            {
                width = p.ClientSize.Width - l.Left;
            }
            width -= l.Margin.Horizontal + 2;
            if (width > 80 && l.MaximumSize.Width != width) changes.Add((l, width));
        }
        if (changes.Count == 0) return;
        ScreenUpdate.Batch(this, () => { foreach (var (l, w) in changes) l.MaximumSize = new Size(w, 0); });
    }

    // =====================================================================================
    // Gebeurtenissen koppelen
    // =====================================================================================

    private void WireEvents()
    {
        _lstFolders.DrawItem += DrawFolderItem;
        _lstFolders.SelectedIndexChanged += (_, _) => { if (!_loadingUi) ShowSelectedFolder(); };
        _btnAddFolder.Click += (_, _) => OnAddFolder();
        _btnRemoveFolder.Click += (_, _) => OnRemoveFolder();

        _chkEnabled.CheckedChanged += (_, _) => EditFolder(f => f.Enabled = _chkEnabled.Checked);
        _cmbRetention.SelectedIndexChanged += (_, _) => OnRetentionChoice();
        _numDays.ValueChanged += (_, _) => EditFolder(f => f.RetentionDays = (int)_numDays.Value);
        _chkRecurse.CheckedChanged += (_, _) => OnRecurseChanged();
        _chkAllowSync.CheckedChanged += (_, _) => OnAllowSyncChanged();
        _lstExclusions.SelectedIndexChanged += (_, _) => UpdateExclusionButtons();
        _lstExclusions.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) OnRemoveExclusions(); };
        _btnExclAdd.Click += (_, _) => _exclMenu.Show(_btnExclAdd, new Point(0, _btnExclAdd.Height));
        _btnExclRemove.Click += (_, _) => OnRemoveExclusions();

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

        _btnSave.Click += async (_, _) => await RunBusy("Opslaan…", () => { SaveSettings(showConfirmation: true); return Task.CompletedTask; });
        _btnRevert.Click += (_, _) => OnRevert();
        _btnSimulate.Click += async (_, _) => await RunBusy("Simulatie wordt uitgevoerd…", OnSimulate);
        _btnPrimary.Click += async (_, _) => await OnPrimary();
        _btnMenu.Click += (_, _) => _menu.Show(_btnMenu, new Point(_btnMenu.Width - _menu.PreferredSize.Width, _btnMenu.Height));
        _btnLog.Click += (_, _) => OnOpenLog();
        _cmbListDays.SelectedIndexChanged += (_, _) => MarkDirty();
        _btnClearList.Click += (_, _) => OnClearDeletedList();
        _txtDeletedFilter.TextChanged += (_, _) => FillDeletedList();
        _lstDeleted.DoubleClick += (_, _) => OnDeletedDoubleClick();

        _miCleanNow.Click += async (_, _) => await RunBusy("Bezig met opruimen…", OnCleanNow);
        _miPause.Click += async (_, _) => await RunBusy("Bezig…", OnPauseToggle);
        _miSimulationMode.Click += async (_, _) => await RunBusy("Bezig…", OnActivateOrDeactivate);
        _miTray.Click += async (_, _) => await OnTrayToggle();
        _miLog.Click += (_, _) => OnOpenLog();
        _miRepair.Click += async (_, _) => await RunBusy("Taak en script opnieuw instellen…", OnRepair);
        _miOpenDir.Click += (_, _) => OnOpenDirectory();
        _miUninstall.Click += async (_, _) => await RunBusy("Bezig met verwijderen…", OnUninstall);
        _miAbout.Click += (_, _) => Info(string.Format(Texts.About, Program.Version, AppRegistration.WebsiteUrl));

        _footerTimer.Tick += (_, _) => { _footerTimer.Stop(); _footerFlash = ""; UpdateFooter(); };
        FormClosing += OnFormClosing;
        // Terug in het venster: alleen bijwerken als er op schijf iets is veranderd (bijv. de geplande taak heeft
        // gedraaid of het icoon in het systeemvak heeft gepauzeerd). Anders zou elke klik het hele venster hertekenen.
        Activated += (_, _) =>
        {
            if (_busy || DiskStamp() == _diskStamp) return;
            ScreenUpdate.Batch(this, () => { ReloadFromDisk(); RefreshStatus(); });
        };
    }

    /// <summary>Elke map als twee regels: naam (vet) en wat er gebeurt (grijs).</summary>
    private void DrawFolderItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _lstFolders.Items.Count || _lstFolders.Items[e.Index] is not FolderSettings f) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var main = selected ? SystemColors.HighlightText : (f.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
        var sub = selected ? SystemColors.HighlightText : SystemColors.GrayText;
        int pad = UiFont.Px(8);
        using var bold = new Font(_lstFolders.Font, FontStyle.Bold);
        var r = e.Bounds;
        int lineH = _lstFolders.Font.Height;
        int top = r.Top + (r.Height - 2 * lineH - UiFont.Px(2)) / 2;
        var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, f.DisplayName, bold, new Rectangle(r.Left + pad, top, r.Width - 2 * pad, lineH + 2), main, flags);
        TextRenderer.DrawText(e.Graphics, FolderSubtitle(f), _lstFolders.Font, new Rectangle(r.Left + pad, top + lineH + UiFont.Px(2), r.Width - 2 * pad, lineH + 2), sub, flags);
        e.DrawFocusRectangle();
    }

    private static string FolderSubtitle(FolderSettings f)
    {
        if (!f.Enabled) return "Wordt niet opgeruimd";
        string age = f.RetentionDays == 0 ? "Direct opruimen" : f.RetentionDays == 1 ? "Ouder dan 1 dag" : $"Ouder dan {f.RetentionDays} dagen";
        int ex = Exclusions.All(f).Count;
        return age + (f.Recurse ? " · met submappen" : "") + (ex > 0 ? $" · {ex} uitzondering{(ex == 1 ? "" : "en")}" : "");
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
            _cmbListDays.SelectedIndex = DeletedFilesList.OptionIndexFor(_config.DeletedFilesListDays);
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
        if (_cmbListDays.SelectedIndex >= 0) _config.DeletedFilesListDays = DeletedFilesList.Options[_cmbListDays.SelectedIndex].Days;
    }

    private FolderSettings? SelectedFolder => _lstFolders.SelectedItem as FolderSettings;

    private void ShowSelectedFolder()
    {
        var f = SelectedFolder;
        _loadingUi = true;
        try
        {
            foreach (var c in new Control[] { _chkEnabled, _cmbRetention, _numDays, _chkRecurse, _chkAllowSync, _lstExclusions, _btnExclAdd })
                c.Enabled = f is not null;
            FillExclusions(f);
            _btnRemoveFolder.Enabled = f is not null && f.Type == FolderTypes.Custom;
            if (f is null) { _lblFolderName.Text = "Geen map gekozen"; _lblPath.Text = ""; _lblFolderWarning.Text = ""; return; }

            _chkEnabled.Checked = f.Enabled;
            _lblFolderName.Text = f.DisplayName;
            _lblPath.Text = f.ExpectedPath;
            _cmbRetention.SelectedIndex = RetentionOptions.IndexFor(f.RetentionDays);
            _numDays.Value = Math.Min(Math.Max(f.RetentionDays, 0), 3650);
            _daysRow.Visible = RetentionOptions.All[_cmbRetention.SelectedIndex].Days is null;
            _chkRecurse.Checked = f.Recurse;
            _chkAllowSync.Checked = f.AllowNetworkOrSync;

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
            _lblFolderWarning.Visible = _lblFolderWarning.Text.Length > 0;
        }
        finally { _loadingUi = false; }
    }

    private PathCheck CheckPath(string path) =>
        PathRules.Check(path, Environment.GetEnvironmentVariable, _layout.BaseDirectory, _syncRoots, WindowsSystem.IsNonFixedDrive);

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
        _daysRow.Visible = o.Days is null;
        if (o.Days is int d) EditFolder(f => f.RetentionDays = d);
        else EditFolder(f => f.RetentionDays = (int)_numDays.Value);
    }

    private void OnRecurseChanged()
    {
        if (_loadingUi) return;
        if (_chkRecurse.Checked && !Confirm("Ook submappen doorzoeken?", Texts.RecurseWarning, "Submappen doorzoeken", ConfirmIcon.Warning))
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
            if (!Confirm("Opschonen op deze locatie toch toestaan?", warning, "Toch toestaan", ConfirmIcon.Warning))
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
        if (_chkEmptyBin.Checked && !Confirm("Prullenbak automatisch legen?", Texts.EmptyBinConfirm, "Automatisch legen", ConfirmIcon.Warning))
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
            : $"Items die langer dan {_config.RecycleBinRetentionDays} dag(en) in de Prullenbak staan, worden definitief verwijderd (ook wat u zelf heeft weggegooid).")
        : "";

    private void UpdateDeleteNote() =>
        _lblDeleteNote.Text = _rbPermanent.Checked ? Texts.PermanentNote : Texts.RecycleBinNote;

    private void MarkDirty()
    {
        if (_loadingUi) return;
        _dirty = true;
        _footerFlash = "";
        UpdateTitle();
    }

    /// <summary>Werkt de onderbalk bij: niet-opgeslagen wijzigingen, bezig, of een korte bevestiging.</summary>
    private void UpdateTitle() => UpdateFooter();

    private void UpdateFooter(string? busy = null)
    {
        bool installed = File.Exists(_layout.ConfigPath);
        _btnSave.Enabled = !_busy && _dirty;
        _btnRevert.Visible = _dirty && installed;
        _btnRevert.Enabled = !_busy;
        if (busy is not null) { _lblFooter.ForeColor = Color.SteelBlue; _lblFooter.Text = busy; }
        else if (_footerFlash.Length > 0) { _lblFooter.ForeColor = Color.ForestGreen; _lblFooter.Text = _footerFlash; }
        else if (!installed) { _lblFooter.ForeColor = SystemColors.ControlText; _lblFooter.Text = "Klik op 'Opslaan' om te beginnen. Er wordt dan nog niets verwijderd."; }
        else if (_dirty) { _lblFooter.ForeColor = Color.DarkOrange; _lblFooter.Text = "● Niet-opgeslagen wijzigingen: die gelden pas na 'Opslaan'."; }
        else { _lblFooter.Text = ""; }
    }

    private void Flash(string text)
    {
        _footerFlash = text;
        _footerTimer.Stop();
        _footerTimer.Start();
        UpdateFooter();
    }

    // =====================================================================================
    // Opslaan en installeren
    // =====================================================================================

    /// <summary>Slaat op en installeert/werkt bij. Retourneert false als de instellingen ongeldig zijn.</summary>
    private bool SaveSettings(bool showConfirmation)
    {
        ApplyGeneralFieldsToConfig();
        bool firstInstall = !File.Exists(_layout.ConfigPath);
        try
        {
            _installer.SaveAndApply(_config);
        }
        catch (ScheduledTaskException ex)
        {
            // Instellingen en script zijn wel opgeslagen; alleen de taak is niet gelukt.
            _dirty = false;
            UpdateTitle();
            StartRegistration(firstInstall);
            Warn("De instellingen zijn opgeslagen, maar de geplande taak kon niet worden ingesteld.\n\n" + ex.Message +
                 "\n\nHandmatige simulatie en 'Nu opruimen' werken wel. Probeer 'Meer > Problemen oplossen' of neem contact op met uw systeembeheerder.");
            return true;
        }
        _dirty = false;
        PruneDeletedList();
        StartRegistration(firstInstall);
        if (showConfirmation && !firstInstall)
            Flash(TaskXmlBuilder.HasAnyTrigger(_config.Schedule)
                ? "✓ Opgeslagen. De planning is bijgewerkt; er is niets verwijderd."
                : "✓ Opgeslagen. Er is geen automatisch moment gekozen: PraktijkClean werkt nu alleen als u zelf iets start.");
        UpdateTitle();
        return true;
    }

    /// <summary>
    /// Zet de geïnstalleerde kopie, de Startmenu-snelkoppeling en de vermelding bij Geïnstalleerde apps neer.
    /// Op de achtergrond, omdat een virusscanner het kopiëren van een nieuwe .exe flink kan vertragen.
    /// </summary>
    private void StartRegistration(bool firstInstallInfo)
    {
        Task.Run(() =>
        {
            AppRegistration.Install(_layout);
            TrayIntegration.EnsureRunning(_layout);
        }).ContinueWith(t =>
        {
            if (IsDisposed) return;
            if (t.Exception is not null)
            {
                if (firstInstallInfo)
                    Warn("PraktijkClean werkt, maar kon niet in het Startmenu en de lijst met apps worden gezet (" +
                         t.Exception.InnerException?.GetType().Name + "). Probeer het later opnieuw via 'Opslaan'.");
                return;
            }
            if (firstInstallInfo) Info(Texts.Installed);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnRevert()
    {
        if (!File.Exists(_layout.ConfigPath)) return;
        var disk = ConfigStore.TryLoad(_layout.ConfigPath, out string? error);
        if (disk is null) { Warn(error ?? "De opgeslagen instellingen konden niet worden gelezen."); return; }
        _config = disk;
        _dirty = false;
        FillFromConfig();
        RefreshStatus();
    }

    // =====================================================================================
    // Kop en menu
    // =====================================================================================

    /// <summary>De hoofdknop in de kop doet wat bij de huidige status het meest voor de hand ligt.</summary>
    private async Task OnPrimary()
    {
        switch (_btnPrimary.Tag)
        {
            case StatusKind.Simulation: await RunBusy("Bezig…", OnActivateOrDeactivate); break;
            case StatusKind.On:
            case StatusKind.Paused: await RunBusy("Bezig…", OnPauseToggle); break;
            case StatusKind.Problem: await RunBusy("Herstellen…", OnRepair); break;
        }
    }

    private Task OnRepair()
    {
        ApplyGeneralFieldsToConfig();
        _installer.Repair(_config);
        _dirty = false;
        StartRegistration(firstInstallInfo: false);
        UpdateTitle();
        Info("Het opschoonscript en de geplande taak zijn opnieuw ingesteld met uw huidige instellingen. Er is niets verwijderd.");
        return Task.CompletedTask;
    }

    private Task OnUninstall()
    {
        if (!Confirm("PraktijkClean verwijderen?", Texts.UninstallConfirm, "PraktijkClean verwijderen", ConfirmIcon.Warning)) return Task.CompletedTask;
        Info(AppUninstall.Run(_layout));
        // PraktijkClean is weg: het venster sluiten (zonder te vragen om op te slaan, want dan installeert het opnieuw).
        _dirty = false;
        _closeAfterBusy = true;
        return Task.CompletedTask;
    }

    private async Task OnTrayToggle()
    {
        bool enable = !TrayIntegration.IsEnabled();
        if (enable && !File.Exists(_layout.ConfigPath))
        {
            Info("Sla eerst de instellingen op; daarna kunt u het icoon aanzetten.");
            return;
        }
        await RunBusy(enable ? "Icoon wordt aangezet…" : "Icoon wordt uitgezet…", async () =>
        {
            // Kan enkele seconden duren (virusscanner, opstarten van het icoon): niet op de UI-thread.
            await Task.Run(() => { if (enable) TrayIntegration.Enable(_layout); else TrayIntegration.Disable(); });
            if (enable) Info(Texts.TrayEnabled);
        });
    }

    private void OnOpenLog()
    {
        if (!File.Exists(_layout.LogPath)) { Info("Er is nog geen logboek. Het logboek wordt aangemaakt bij de eerste controle."); return; }
        Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _layout.LogPath + "\"") { UseShellExecute = false })?.Dispose();
    }

    // =====================================================================================
    // Lijst verwijderde bestanden
    // =====================================================================================

    /// <summary>Leest de lijst opnieuw als het bestand of de termijn is veranderd (niet bij elke klik in het venster).</summary>
    private void LoadDeletedList(bool installed)
    {
        int days = installed ? _config.DeletedFilesListDays : 0;
        var fi = new FileInfo(_layout.DeletedListPath);
        string stamp = $"{days}|{(fi.Exists ? fi.LastWriteTimeUtc.Ticks + "/" + fi.Length : "-")}|{DateTime.Today:yyyyMMdd}";
        if (stamp != _deletedStamp)
        {
            _deletedStamp = stamp;
            _deleted = DeletedFilesList.Load(_layout.DeletedListPath, days, DateTime.UtcNow);
            FillDeletedList();
        }
        _btnClearList.Enabled = !_busy && (_deleted.Count > 0 || fi.Exists);
        string period = DeletedFilesList.PeriodText(days);
        _lblDeletedInfo.Text = !installed
            ? "Hier ziet u straks welke bestanden PraktijkClean heeft opgeruimd."
            : days <= 0
            ? "De namen van verwijderde bestanden worden niet bijgehouden. Kies hieronder een termijn en klik op 'Opslaan'."
            : _deleted.Count == 0
            ? $"In {period} is er niets verwijderd (of de lijst is gewist)."
            : $"{_deleted.Count} bestand(en) verwijderd in {period} ({Format.Bytes(_deleted.Sum(e => e.Size))}). Nieuwste bovenaan.";
    }

    private void FillDeletedList()
    {
        string filter = _txtDeletedFilter.Text.Trim();
        var shown = filter.Length == 0 ? _deleted
            : _deleted.Where(e => e.FullPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        _lstDeleted.BeginUpdate();
        try
        {
            _lstDeleted.Items.Clear();
            foreach (var e in shown.Take(5000))
            {
                var item = new ListViewItem(e.DeletedUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)) { Tag = e };
                item.SubItems.Add(e.FileName);
                item.SubItems.Add(DeletedFolderText(e));
                item.SubItems.Add(e.KindText);
                item.ToolTipText = e.FullPath + "  (" + Format.Bytes(e.Size) + ")";
                _lstDeleted.Items.Add(item);
            }
        }
        finally { _lstDeleted.EndUpdate(); }
        _lstDeleted.ShowItemToolTips = true;
    }

    /// <summary>De map zoals de gebruiker hem kent ("Bureaublad", "Downloads\Scans"), anders het volledige pad.</summary>
    private string DeletedFolderText(DeletedFilesList.Entry e)
    {
        var f = _config.Folders.FirstOrDefault(x => x.Id == e.FolderId);
        if (f is not null && PathRules.IsInside(e.Directory, f.ExpectedPath))
        {
            string rel = e.Directory.Length > f.ExpectedPath.TrimEnd('\\').Length ? e.Directory.Substring(f.ExpectedPath.TrimEnd('\\').Length) : "";
            return f.DisplayName + rel;
        }
        return e.Directory;
    }

    private void FitDeletedColumns()
    {
        if (_lstDeleted.Columns.Count < 4) return;
        int rest = _lstDeleted.ClientSize.Width - _lstDeleted.Columns[0].Width - _lstDeleted.Columns[3].Width - UiFont.Px(4);
        if (rest < UiFont.Px(160)) return;
        _lstDeleted.Columns[1].Width = rest * 55 / 100;
        _lstDeleted.Columns[2].Width = rest - _lstDeleted.Columns[1].Width;
    }

    private void OnClearDeletedList()
    {
        if (!Confirm("Lijst met verwijderde bestanden wissen?", Texts.ClearDeletedListConfirm, "Lijst wissen")) return;
        try { DeletedFilesList.Clear(_layout.DeletedListPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn("De lijst kon niet worden gewist: " + ex.Message); return; }
        _deletedStamp = "";
        RefreshStatus();
        Flash("✓ De lijst met verwijderde bestanden is gewist.");
    }

    /// <summary>Na opslaan: een kortere termijn (of 'Niet bijhouden') direct toepassen.</summary>
    private void PruneDeletedList()
    {
        try { DeletedFilesList.Prune(_layout.DeletedListPath, _config.DeletedFilesListDays, DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _deletedStamp = "";
    }

    private void OnDeletedDoubleClick()
    {
        if (_lstDeleted.SelectedItems.Count == 0 || _lstDeleted.SelectedItems[0].Tag is not DeletedFilesList.Entry e) return;
        if (e.Kind == DeletedFilesList.KindRecycleBin)
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = false })?.Dispose();
        else if (Directory.Exists(e.Directory))
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + e.Directory + "\"") { UseShellExecute = false })?.Dispose();
    }

    private void OnOpenDirectory()
    {
        if (!Directory.Exists(_layout.BaseDirectory)) { Info("PraktijkClean is nog niet geïnstalleerd; er is nog geen programmamap."); return; }
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _layout.BaseDirectory + "\"") { UseShellExecute = false })?.Dispose();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        if (!_dirty || !File.Exists(_layout.ConfigPath)) return;
        int answer = ConfirmDialog.Show(this, "Wijzigingen opslaan?",
            "U heeft instellingen gewijzigd die nog niet zijn opgeslagen.", ConfirmIcon.Info,
            defaultIndex: 0, cancelIndex: 2, "Opslaan", "Niet opslaan", ConfirmDialog.Cancel);
        if (answer == 2) { e.Cancel = true; return; }
        if (answer == 0)
        {
            try { SaveSettings(showConfirmation: false); }
            catch (InvalidOperationException ex) { Warn(ex.Message); e.Cancel = true; }
        }
    }

    // =====================================================================================
    // Status
    // =====================================================================================

    /// <summary>Werkt kop, tabblad Logboek en menu bij, in één keer (zie <see cref="ScreenUpdate.Batch"/>).</summary>
    private void RefreshStatus()
    {
        ScreenUpdate.Batch(this, RefreshStatusCore);
        FitWrapLabels();
    }

    private void RefreshStatusCore()
    {
        _diskStamp = DiskStamp();
        var state = _installer.GetState();
        var last = RunReport.TryLoadStatus(_layout.StatusPath);
        var problems = new List<string>();
        bool installed = state.ConfigInstalled;

        // ---- Kop
        var info = StatusInfo.Compute(installed ? _config : null, state);
        var (back, fore) = info.BannerColors;
        _header.BackColor = back;
        _lblState.ForeColor = fore;
        _lblState.Text = "●  " + info.Title;
        _lblStateText.Text = info.Detail;

        var week = LogSummary.Load(_layout.LogPath, DateTime.Now.AddDays(-7));
        var facts = new List<string>();
        if (installed)
        {
            facts.Add(StatusInfo.Summary(week, "Afgelopen 7 dagen"));
            facts.Add(last?.LastRunUtc is DateTime l ? "Laatste\u00A0controle: " + When(l.ToLocalTime()) : "Nog niet gecontroleerd");
            if (state.Task is { Enabled: true, NextRunTime: DateTime n }) facts.Add("Volgende: " + When(n));
        }
        _lblFacts.Text = string.Join("   ·   ", facts);
        _lblFacts.Visible = facts.Count > 0;

        (_btnPrimary.Text, _btnPrimary.Tag) = info.Kind switch
        {
            StatusKind.Simulation => ("Automatisch opruimen aanzetten…", (object)StatusKind.Simulation),
            StatusKind.On => ("Pauzeren", StatusKind.On),
            StatusKind.Paused => ("Hervatten", StatusKind.Paused),
            StatusKind.Problem => ("Herstellen", StatusKind.Problem),
            _ => ("", StatusKind.Off),
        };
        _btnPrimary.Visible = info.Kind != StatusKind.Off;

        // ---- Tabblad Logboek
        LoadDeletedList(installed);
        _lblWeek.Text = StatusInfo.Summary(week, "Totaal") + ".";
        if (state.TaskError is not null) problems.Add(state.TaskError);
        if (!installed)
            _lblTask.Text = "Geplande taak: nog niet aangemaakt (dat gebeurt bij 'Opslaan').";
        else if (state.Task is null)
        {
            _lblTask.Text = TaskXmlBuilder.HasAnyTrigger(_config.Schedule)
                ? "Geplande taak: ontbreekt."
                : "Geplande taak: geen (er is geen automatisch moment gekozen; alleen handmatig gebruik).";
            if (TaskXmlBuilder.HasAnyTrigger(_config.Schedule)) problems.Add("De geplande taak ontbreekt.");
        }
        else
            _lblTask.Text = $"Geplande taak '{_installer.TaskName}': {(state.Task.Enabled ? "aan" : "uit")} – {MomentText()}.";

        if (state.ScriptInstalled && !state.ScriptIsCurrent)
            problems.Add("Het geïnstalleerde opschoonscript hoort bij een andere versie; klik op 'Opslaan' of gebruik 'Meer > Problemen oplossen'.");
        if (state.ConfigError is not null) problems.Add(state.ConfigError);

        if (last?.LastRunUtc is DateTime lr)
        {
            _lblLastRun.Text = $"{lr.ToLocalTime():dd-MM-yyyy HH:mm} ({TriggerText(last.Trigger)}, {last.Action}).";
            _lblLastResult.Text = $"{last.Deleted} verwijderd ({Format.Bytes(last.DeletedBytes)}), {last.Candidates} in aanmerking, " +
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
            _lblLastRun.Text = "Nog niet uitgevoerd.";
            _lblLastResult.Text = "";
        }
        _lblLastResult.Visible = _lblLastResult.Text.Length > 0;

        // Taakplanner geeft ook voor een uitgeschakelde (gepauzeerde) taak een volgende tijd terug; die niet tonen.
        _lblNextRun.Text = state.Task is { Enabled: false } ? "Volgende uitvoering: geen (gepauzeerd)."
            : state.Task?.NextRunTime is DateTime next
            ? $"Volgende uitvoering: {next:dd-MM-yyyy HH:mm}" + (_config.Schedule.AtLogon ? " (en bij elke aanmelding)." : ".")
            : _config.Schedule.AtLogon && state.Task is not null ? "Volgende uitvoering: bij de volgende aanmelding." : "Volgende uitvoering: –";

        _lblProblems.Text = problems.Count == 0 ? "" : "⚠ " + string.Join("\n⚠ ", problems.Distinct());
        _lblProblems.Visible = problems.Count > 0;

        // ---- Menu
        bool active = !_config.SimulationMode && _config.AutomaticCleanupConfirmed;
        _miPause.Text = _config.Paused ? "Hervatten" : "Tijdelijk pauzeren";
        _miPause.Enabled = installed;
        _miSimulationMode.Visible = installed && active;
        _miCleanNow.Enabled = installed;
        _miTray.Checked = TrayIntegration.IsEnabled();
        _miLog.Enabled = File.Exists(_layout.LogPath);
        _btnLog.Enabled = !_busy && File.Exists(_layout.LogPath);
        _miUninstall.Enabled = installed || AppRegistration.IsRegistered() || Directory.Exists(_layout.BaseDirectory);
        UpdateFooter();
    }

    /// <summary>
    /// Bouwt de andere tabbladen alvast op terwijl ze onzichtbaar zijn. Windows Forms maakt de onderdelen van een
    /// tabblad pas aan als het voor het eerst wordt geopend, en dat kostte bij de eerste klik seconden.
    /// </summary>
    private void PrewarmTabs()
    {
        _prewarmNext = 0;
        if (_prewarmSubscribed) return;
        _prewarmSubscribed = true;
        Application.Idle += OnIdlePrewarm;
    }

    /// <summary>
    /// Eén tabblad per keer, alleen als er niets anders te doen is (Application.Idle), zodat klikken en typen voorgaan.
    /// </summary>
    private void OnIdlePrewarm(object? sender, EventArgs e)
    {
        if (IsDisposed || _prewarmNext >= _tabs.TabPages.Count)
        {
            Application.Idle -= OnIdlePrewarm;
            _prewarmSubscribed = false;
            return;
        }
        if (_busy) return;
        int index = _prewarmNext++;
        int selected = _tabs.SelectedIndex;
        if (index == selected) return;
        _prewarming = true;
        ScreenUpdate.StopDrawing(_tabs);
        try
        {
            _tabs.SelectedIndex = index;
            FitWrapLabels();
            _tabs.SelectedIndex = selected;
        }
        finally
        {
            _prewarming = false;
            ScreenUpdate.ResumeDrawing(_tabs);
        }
    }

    /// <summary>"vandaag 14:17", "morgen 12:30" of "17-11 12:30".</summary>
    private static string When(DateTime t)
    {
        string day = t.Date == DateTime.Today ? "vandaag" : t.Date == DateTime.Today.AddDays(1) ? "morgen"
            : t.Date == DateTime.Today.AddDays(-1) ? "gisteren" : t.ToString("dd-MM");
        return day + "\u00A0" + t.ToString("HH:mm");   // vaste spatie: datum en tijd niet over twee regels
    }

    // =====================================================================================
    // Uitzonderingen
    // =====================================================================================

    private void FillExclusions(FolderSettings? f)
    {
        _lstExclusions.BeginUpdate();
        _lstExclusions.Items.Clear();
        if (f is not null)
            foreach (var item in Exclusions.All(f))
                _lstExclusions.Items.Add(new ListViewItem(new[] { item.KindLabel, item.Value }) { Tag = item });
        _lstExclusions.EndUpdate();
        UpdateExclusionButtons();
    }

    private void FitExclusionColumns()
    {
        if (_lstExclusions.Columns.Count < 2) return;
        // Iets smaller dan de beschikbare ruimte, anders toont de lijst een onnodige horizontale schuifbalk.
        int rest = _lstExclusions.ClientSize.Width - _lstExclusions.Columns[0].Width - UiFont.Px(4);
        if (rest > 50 && _lstExclusions.Columns[1].Width != rest) _lstExclusions.Columns[1].Width = rest;
    }

    private void UpdateExclusionButtons() =>
        _btnExclRemove.Enabled = SelectedFolder is not null && _lstExclusions.SelectedItems.Count > 0;

    /// <summary>Na een wijziging: markeren als niet-opgeslagen en de lijst opnieuw tonen.</summary>
    private void ExclusionsChanged()
    {
        EditFolder(_ => { });
        FillExclusions(SelectedFolder);
    }

    private void OnExcludeFiles()
    {
        var f = SelectedFolder;
        if (f is null) return;
        using var dialog = new OpenFileDialog
        {
            Title = $"Bestanden in '{f.DisplayName}' die nooit verwijderd mogen worden",
            InitialDirectory = f.ExpectedPath,
            Multiselect = true,
            DereferenceLinks = false,   // een snelkoppeling zelf kiezen, niet het bestand waar hij naar wijst
            CheckFileExists = true,
            RestoreDirectory = true,
            Filter = "Alle bestanden (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AddPathExclusions(f, dialog.FileNames, ExclusionKind.File);
    }

    private void OnExcludeSubfolder()
    {
        var f = SelectedFolder;
        if (f is null) return;
        if (!f.Recurse && !Confirm("Toch een submap uitsluiten?",
                                   "Submappen worden nu niet doorzocht: PraktijkClean kijkt alleen naar bestanden die direct in " +
                                   $"'{f.DisplayName}' staan. Een uitgesloten submap is pas nodig als u 'Ook submappen doorzoeken' aanzet.",
                                   "Submap kiezen…", acceptIsDefault: true))
            return;
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Kies een submap van '{f.DisplayName}' die PraktijkClean moet overslaan",
            SelectedPath = f.ExpectedPath,
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AddPathExclusions(f, new[] { dialog.SelectedPath }, ExclusionKind.Subfolder);
    }

    private void AddPathExclusions(FolderSettings f, IEnumerable<string> fullPaths, ExclusionKind kind)
    {
        var outside = new List<string>();
        var errors = new List<string>();
        int added = 0;
        foreach (string path in fullPaths)
        {
            string? rel = Exclusions.RelativeTo(f.ExpectedPath, path);
            if (rel is null) { outside.Add(path); continue; }
            string? error = Exclusions.TryAdd(f, new ExclusionItem(kind, rel));
            if (error is null) added++; else errors.Add(error);
        }
        if (added > 0) ExclusionsChanged();
        if (outside.Count > 0)
            Warn($"Dit staat niet in de map '{f.DisplayName}' ({f.ExpectedPath}) en is daarom niet toegevoegd:\n\n• " +
                 string.Join("\n• ", outside.Take(10)) +
                 "\n\nKies een bestand of submap die in deze map staat, of selecteer links eerst de juiste map.");
        if (errors.Count > 0) Warn(string.Join("\n", errors.Take(10)));
    }

    private void OnExcludeText()
    {
        var f = SelectedFolder;
        if (f is null) return;
        string? text = InputDialog.Ask(this, "Uitzondering toevoegen",
            $"Wat moet in '{f.DisplayName}' nooit verwijderd worden?\n\n" +
            "• Een extensie, bijvoorbeeld  .docx  (alle Word-bestanden)\n" +
            "• Een bestandsnaam, bijvoorbeeld  Bewaren.pdf\n" +
            "• Een patroon met * (willekeurige tekst) of ? (één teken), bijvoorbeeld  protocol-*  of  *spoed*");
        var item = Exclusions.Classify(text);
        if (item is null) return;
        string? error = Exclusions.TryAdd(f, item);
        if (error is not null) { Warn(error); return; }
        ExclusionsChanged();
    }

    private void OnRemoveExclusions()
    {
        var f = SelectedFolder;
        var items = _lstExclusions.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<ExclusionItem>().ToList();
        if (f is null || items.Count == 0) return;
        string heading = items.Count == 1 ? $"Uitzondering '{items[0].Value}' verwijderen?" : $"Deze {items.Count} uitzonderingen verwijderen?";
        if (!Confirm(heading, "Bestanden die hieronder vallen, kunnen daarna weer worden opgeruimd.",
                     items.Count == 1 ? "Uitzondering verwijderen" : "Uitzonderingen verwijderen")) return;
        foreach (var item in items) Exclusions.Remove(f, item);
        ExclusionsChanged();
    }

    /// <summary>Vanuit het simulatievenster: uitzonderingen toevoegen en direct opslaan.</summary>
    private string? AddExclusionsFromSimulation(IReadOnlyList<(string FolderId, ExclusionItem Item)> items)
    {
        var errors = new List<string>();
        int added = 0;
        foreach (var (folderId, item) in items)
        {
            var f = _config.Folders.FirstOrDefault(x => x.Id == folderId);
            if (f is null) { errors.Add("De map bestaat niet meer in de instellingen."); continue; }
            string? error = Exclusions.TryAdd(f, item);
            if (error is null) added++; else errors.Add(error);
        }
        if (added > 0)
        {
            _dirty = true;
            try { SaveSettings(showConfirmation: false); }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
            ShowSelectedFolder();
            _lstFolders.Refresh();
        }
        return errors.Count == 0 ? null : string.Join("\n", errors.Distinct().Take(10));
    }

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
            if (!Confirm("Map overlapt met een andere map", "Deze map ligt in (of om) een map die al in de lijst staat. Dat kan verwarrend zijn.",
                         "Toch toevoegen", ConfirmIcon.Warning)) return;
        }
        string extra = check.Warnings.Count > 0 ? "\n\nLet op:\n• " + string.Join("\n• ", check.Warnings) : "";
        if (!Confirm("Deze map toevoegen?", string.Format(Texts.AddFolderConfirm, path, extra), "Map toevoegen")) return;

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
        if (!Confirm($"Map '{f.DisplayName}' uit de lijst halen?", "De map en de bestanden erin blijven gewoon bestaan; " +
                     "PraktijkClean ruimt er alleen niet meer in op.", "Uit de lijst halen", acceptIsDefault: true)) return;
        _config.Folders.Remove(f);
        _lstFolders.Items.Remove(f);
        if (_lstFolders.Items.Count > 0) _lstFolders.SelectedIndex = 0;
        MarkDirty();
    }

    // =====================================================================================
    // Simulatie, activeren, pauzeren, nu opruimen
    // =====================================================================================

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
        ShowSimulation(report);
    }

    private void ShowSimulation(RunReport report)
    {
        using var form = new SimulationResultForm(report, _config, AddExclusionsFromSimulation);
        form.ShowDialog(this);
    }

    /// <summary>Knoppen voor een vraag waarbij eerst de simulatie bekeken kan worden; standaard staat die middelste knop.</summary>
    private const int ChoiceGo = 0, ChoiceSimulate = 1, ChoiceCancel = 2;

    private int AskWithSimulation(string heading, string body, string action) =>
        ConfirmDialog.Show(this, heading, body, ConfirmIcon.Warning, defaultIndex: ChoiceSimulate, cancelIndex: ChoiceCancel,
                           action, "Eerst simulatie bekijken…", ConfirmDialog.Cancel);

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

        // Eerst laten zien wat er bij de huidige instellingen zou gebeuren. Na 'Eerst simulatie bekijken' (waarin
        // uitzonderingen kunnen worden toegevoegd) opnieuw rekenen en opnieuw vragen.
        while (true)
        {
            var report = await _runner.SimulateAsync();
            if (!CheckReport(report)) return;
            var sb = new StringBuilder();
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
            sb.AppendLine().Append("U kunt later altijd pauzeren of terug naar simulatiemodus.");
            int choice = AskWithSimulation("Automatisch opruimen activeren?", sb.ToString(), "Activeren");
            if (choice == ChoiceSimulate) { ShowSimulation(report); continue; }
            if (choice != ChoiceGo) return;
            break;
        }

        _installer.ActivateAutomaticCleanup(_config);
        _dirty = false;
        UpdateTitle();
        Info("Automatisch opruimen staat aan. U kunt altijd pauzeren (knop rechtsboven) of via 'Meer' terug naar simulatiemodus.");
    }

    private Task OnPauseToggle()
    {
        bool pause = !_config.Paused;
        ApplyGeneralFieldsToConfig();
        _installer.SetPaused(_config, pause);
        _dirty = false;
        UpdateTitle();
        Flash(pause ? "✓ Gepauzeerd. Klik rechtsboven op 'Hervatten' om weer te starten." : "✓ Hervat.");
        return Task.CompletedTask;
    }

    private async Task OnCleanNow()
    {
        if (!EnsureSaved()) return;
        while (true)
        {
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
                ? $"\n\nDaarnaast worden {pb.Candidates} item(s) ({Format.Bytes(pb.CandidateBytes)}) DEFINITIEF uit de Prullenbak verwijderd."
                : "";
            int choice = AskWithSimulation("Nu opruimen?",
                $"Er worden nu {preview.Candidates} bestand(en) ({Format.Bytes(preview.CandidateBytes)}) {where}" + binLine, "Nu opruimen");
            if (choice == ChoiceSimulate) { ShowSimulation(preview); continue; }
            if (choice != ChoiceGo) return;
            break;
        }

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

    /// <summary>
    /// Het icoon in het systeemvak kan intussen hebben gepauzeerd of hervat. Zonder eigen wijzigingen nemen we de
    /// opgeslagen instellingen opnieuw over; met niet-opgeslagen wijzigingen alleen de pauzestand, zodat
    /// 'Opslaan' die niet ongedaan maakt.
    /// </summary>
    private void ReloadFromDisk()
    {
        if (!File.Exists(_layout.ConfigPath)) return;
        var disk = ConfigStore.TryLoad(_layout.ConfigPath, out _);
        if (disk is null) return;
        if (!_dirty)
        {
            if (ConfigStore.Serialize(disk) == ConfigStore.Serialize(_config)) return;
            _config = disk;
            FillFromConfig();
        }
        else
        {
            _config.Paused = disk.Paused;
        }
    }

    private void BringToFrontNow()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        // Windows staat een ander proces niet altijd toe het venster naar voren te halen; even 'TopMost' helpt.
        TopMost = true;
        Activate();
        TopMost = false;
    }

    private string MomentText() => StatusInfo.Moment(_config.Schedule);

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
        ScreenUpdate.Batch(this, () =>
        {
            SetButtonsEnabled(false);
            UpdateFooter(message);
        });
        UseWaitCursor = true;
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
        catch (System.Security.SecurityException) { Error("Geen toegang tot een instelling van Windows."); }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            if (_closeAfterBusy) { SetButtonsEnabled(true); BeginInvoke(new Action(Close)); }
            else
            {
                ScreenUpdate.Batch(this, () => { SetButtonsEnabled(true); RefreshStatusCore(); });
                FitWrapLabels();
            }
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
        foreach (var b in new[] { _btnSimulate, _btnPrimary, _btnMenu, _btnLog, _btnAddFolder })
            b.Enabled = enabled;
        _btnRemoveFolder.Enabled = enabled && SelectedFolder?.Type == FolderTypes.Custom;
        UpdateFooter(enabled ? null : _lblFooter.Text);
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s.Trim() : s.Substring(0, max).Trim() + "…";

    /// <summary>Vraag met een duidelijke actieknop en "Annuleren" (zie <see cref="ConfirmDialog"/>).</summary>
    private bool Confirm(string heading, string body, string accept, ConfirmIcon icon = ConfirmIcon.Info, bool acceptIsDefault = false) =>
        ConfirmDialog.Ask(this, heading, body, accept, icon, acceptIsDefault: acceptIsDefault);

    private void Info(string text) => MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void Warn(string text) => MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void Error(string text) => MessageBox.Show(this, text, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
