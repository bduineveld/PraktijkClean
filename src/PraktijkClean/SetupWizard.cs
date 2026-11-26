using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Startwizard bij de allereerste start: welke mappen, hoe lang bewaren en wat met submappen, waarheen en wanneer.
/// Eindigt in simulatiemodus (er wordt niets verwijderd). Altijd over te slaan; dan gelden de standaardinstellingen.
/// </summary>
internal sealed class SetupWizard : Form
{
    private const int ContentWidth = 620;
    private static readonly string[] StepTitles =
    {
        "Welke mappen wilt u laten opruimen?",
        "Hoe lang mogen bestanden blijven staan?",
        "Wat gebeurt er met oude bestanden, en wanneer?",
        "Klaar om te beginnen",
    };
    private static readonly string[] StepIntros =
    {
        "PraktijkClean ruimt oude bestanden op in de mappen die u hier kiest. Andere mappen blijven ongemoeid.",
        "Een bestand komt pas in aanmerking als het langer dan deze tijd niet is aangemaakt of gewijzigd.",
        "U kunt dit later altijd aanpassen op het tabblad 'Planning en veiligheid'.",
        "",
    };

    private readonly FirstRunChoices _choices;
    private readonly Func<IWin32Window, string, bool> _acceptFolder;
    private int _step;

    private readonly Label _lblStep = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 12, 0) };
    private readonly Panel _stepBars = new() { Size = new Size(3 * 28 + 2 * 4, 6), Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _lblTitle = DialogParts.Heading("", 13F);
    private readonly Label _lblIntro = DialogParts.Text("", ContentWidth, gray: true, bottom: 10);
    private readonly Panel _content = new() { BackColor = Color.White, Margin = Padding.Empty };
    private readonly TableLayoutPanel[] _pages = new TableLayoutPanel[4];
    private readonly Button _btnSkip = DialogParts.Button("Overslaan");
    private readonly Button _btnBack = DialogParts.Button("Vorige");
    private readonly Button _btnNext = DialogParts.Button("Volgende", main: true);
    private readonly Label _lblError = new() { AutoSize = true, ForeColor = Color.FromArgb(196, 43, 28), Margin = new Padding(0, 10, 0, 0) };

    // Stap 1
    private readonly CheckBox _chkDesktop = new() { Text = "Bureaublad", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly CheckBox _chkDownloads = new() { Text = "Downloads", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    private readonly ListBox _lstOther = new() { Width = ContentWidth, Height = 70, IntegralHeight = false, Margin = new Padding(0, 2, 0, 4) };
    private readonly Button _btnAddOther = DialogParts.Button("Andere map toevoegen…");
    private readonly Button _btnRemoveOther = DialogParts.Button("Verwijderen");
    // Stap 2
    private readonly RadioButton[] _rbDays;
    private static readonly int[] DayChoices = { 1, 7, 30 };
    private readonly SubfolderCards _cards = new() { Width = ContentWidth };
    private readonly Label _lblLevelHint = DialogParts.Text("", ContentWidth, gray: true, top: 2);
    // Stap 3
    private readonly RadioButton _rbRecycle = new() { Text = "Naar de Prullenbak (aanbevolen): nog even terug te halen", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
    private readonly CheckBox _chkEmptyBin = new() { Text = "Prullenbak daarna legen: wat er langer dan 1 dag in staat", AutoSize = true, Margin = new Padding(20, 4, 0, 0) };
    private readonly RadioButton _rbPermanent = new() { Text = "Direct definitief verwijderen", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    private readonly CheckBox _chkDaily = new() { Text = "Elke dag om", AutoSize = true, Margin = new Padding(0, 6, 6, 0) };
    private readonly DateTimePicker _dtpTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    private readonly CheckBox _chkLogon = new() { Text = "Bij het aanmelden bij Windows", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    // Stap 4
    private readonly TableLayoutPanel _summary = new()
    {
        ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 4),
    };

    public FirstRunChoices Choices => _choices;

    public SetupWizard(FirstRunChoices choices, string desktopPath, string downloadsPath, Func<IWin32Window, string, bool> acceptFolder)
    {
        _choices = choices;
        _acceptFolder = acceptFolder;
        _rbDays = DayChoices.Select((d, i) => new RadioButton
        {
            Text = d == 1 ? "1 dag – aanbevolen voor bureaublad en downloads" : d == 7 ? "1 week" : "1 maand (30 dagen)",
            AutoSize = true, Margin = new Padding(0, i == 0 ? 2 : 6, 0, 0),
        }).ToArray();

        SuspendLayout();
        DialogParts.SetupForm(this, "PraktijkClean instellen");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(ContentWidth + 48, 560);

        // Kop: stap x van 3 met streepjes, titel en korte uitleg.
        var top = DialogParts.Column(new Padding(24, 18, 24, 0));
        top.Dock = DockStyle.Top;
        DialogParts.Add(top, DialogParts.Row(_lblStep, _stepBars));
        DialogParts.Add(top, _lblTitle);
        DialogParts.Add(top, _lblIntro);
        _stepBars.Paint += (_, e) => PaintStepBars(e.Graphics);

        // Pagina's
        _pages[0] = BuildFolders(desktopPath, downloadsPath);
        _pages[1] = BuildRetention();
        _pages[2] = BuildWhereWhen();
        _pages[3] = BuildDone();
        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(24, 0, 24, 0);
        foreach (var p in _pages) { p.Dock = DockStyle.Top; p.Visible = false; _content.Controls.Add(p); }

        _btnSkip.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnBack.Click += (_, _) => GoTo(_step - 1);
        _btnNext.Click += (_, _) => OnNext();
        var bar = DialogParts.ButtonBar(new Control[] { _btnSkip }, new Control[] { _btnBack, _btnNext });
        CancelButton = _btnSkip;
        AcceptButton = _btnNext;

        Controls.Add(_content);
        Controls.Add(top);
        Controls.Add(bar);
        LoadChoices();
        GoTo(0);
        ResumeLayout(false);
        PerformLayout();
    }

    // ------------------------------------------------------------------------------------ pagina's

    private TableLayoutPanel BuildFolders(string desktopPath, string downloadsPath)
    {
        var t = DialogParts.Column(Padding.Empty);
        DialogParts.Add(t, _chkDesktop);
        DialogParts.Add(t, PathLabel(desktopPath));
        DialogParts.Add(t, _chkDownloads);
        DialogParts.Add(t, PathLabel(downloadsPath));
        DialogParts.Add(t, DialogParts.Section("Andere mappen (mag leeg blijven)", top: 16));
        DialogParts.Add(t, _lstOther, stretch: true);
        _btnAddOther.Margin = new Padding(0);
        DialogParts.Add(t, DialogParts.Row(_btnAddOther, _btnRemoveOther));
        DialogParts.Add(t, _lblError);
        _btnAddOther.Click += (_, _) => OnAddOther();
        _btnRemoveOther.Click += (_, _) => { if (_lstOther.SelectedIndex >= 0) _lstOther.Items.RemoveAt(_lstOther.SelectedIndex); UpdateButtons(); };
        _lstOther.SelectedIndexChanged += (_, _) => UpdateButtons();
        _chkDesktop.CheckedChanged += (_, _) => _lblError.Text = "";
        _chkDownloads.CheckedChanged += (_, _) => _lblError.Text = "";
        return t;
    }

    private static Label PathLabel(string path) => new()
    {
        Text = path, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 0), MaximumSize = new Size(ContentWidth - 20, 0),
    };

    private TableLayoutPanel BuildRetention()
    {
        var t = DialogParts.Column(Padding.Empty);
        foreach (var rb in _rbDays) DialogParts.Add(t, rb);
        DialogParts.Add(t, DialogParts.Section("Wat gebeurt er met submappen?", top: 18));
        _cards.Margin = new Padding(0, 2, 0, 4);
        DialogParts.Add(t, _cards, stretch: true);
        DialogParts.Add(t, _lblLevelHint);
        _cards.LevelChanged += (_, _) => _lblLevelHint.Text = FolderDialog.HintFor(_cards.Level, legend: true);
        return t;
    }

    private TableLayoutPanel BuildWhereWhen()
    {
        var t = DialogParts.Column(Padding.Empty);
        DialogParts.Add(t, DialogParts.Section("Waar gaan oude bestanden heen?", top: 0));
        DialogParts.Add(t, _rbRecycle);
        DialogParts.Add(t, _chkEmptyBin);
        DialogParts.Add(t, _rbPermanent);
        _rbRecycle.CheckedChanged += (_, _) => _chkEmptyBin.Enabled = _rbRecycle.Checked;
        DialogParts.Add(t, DialogParts.Section("Wanneer kijkt PraktijkClean?", top: 18));
        DialogParts.Add(t, DialogParts.Row(_chkDaily, _dtpTime));
        DialogParts.Add(t, _chkLogon);
        DialogParts.Add(t, DialogParts.Text("Stond de pc uit op het gekozen tijdstip, dan wordt de controle later ingehaald.", ContentWidth, gray: true, top: 8));
        _chkDaily.CheckedChanged += (_, _) => _dtpTime.Enabled = _chkDaily.Checked;
        return t;
    }

    private TableLayoutPanel BuildDone()
    {
        var t = DialogParts.Column(Padding.Empty);
        _summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        DialogParts.Add(t, _summary);
        var safe = new Label
        {
            Text = "PraktijkClean begint in simulatiemodus: er wordt nog niets verwijderd.\n\n" +
                   "Na 'Opslaan' ziet u meteen welke bestanden in aanmerking zouden komen. Moet een bestand blijven staan, " +
                   "klik er dan met de rechtermuisknop op. Pas als het klopt, zet u zelf 'Automatisch opruimen' aan.",
            AutoSize = true, MaximumSize = new Size(ContentWidth - 28, 0), Padding = new Padding(14, 12, 14, 12),
            BackColor = Color.FromArgb(232, 245, 233), ForeColor = Color.FromArgb(27, 94, 32), Margin = new Padding(0, 14, 0, 0),
        };
        DialogParts.Add(t, safe);
        return t;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Na het schalen: de lijst precies vier regels hoog (een vaste hoogte wordt bij hoge DPI te groot).
        _lstOther.Height = _lstOther.ItemHeight * 4 + UiFont.Px(4);
    }

    // ------------------------------------------------------------------------------------ navigatie

    private void GoTo(int step)
    {
        if (step < 0 || step > 3) return;
        SuspendLayout();
        if (step == 3) FillSummary();
        _step = step;
        for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == step;
        _lblStep.Text = step < 3 ? $"Stap {step + 1} van 3" : "Overzicht";
        _lblTitle.Text = StepTitles[step];
        _lblIntro.Text = StepIntros[step];
        _lblIntro.Visible = StepIntros[step].Length > 0;
        _btnBack.Visible = step > 0;
        _btnNext.Text = step == 3 ? "Opslaan en simulatie bekijken" : "Volgende";
        _stepBars.Invalidate();
        UpdateButtons();
        ResumeLayout(true);
        _btnNext.Focus();
    }

    private void OnNext()
    {
        ReadChoices();
        if (_step == 0 && !_choices.Desktop && !_choices.Downloads && _choices.OtherFolders.Count == 0)
        {
            _lblError.Text = "Kies minstens één map.";
            return;
        }
        if (_step < 3) { GoTo(_step + 1); return; }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnAddOther()
    {
        if (_lstOther.Items.Count >= ConfigValidator.MaxFolders - 2) return;
        using var dialog = new FolderBrowserDialog { Description = "Kies een map die PraktijkClean mag opschonen", ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = dialog.SelectedPath;
        if (_lstOther.Items.Cast<string>().Any(p => PathRules.PathEquals(p, path))) return;
        if (!_acceptFolder(this, path)) return;
        _lstOther.Items.Add(path);
        _lblError.Text = "";
        UpdateButtons();
    }

    private void UpdateButtons() => _btnRemoveOther.Enabled = _lstOther.SelectedIndex >= 0;

    private void PaintStepBars(Graphics g)
    {
        int w = UiFont.Px(28), h = _stepBars.Height, gap = UiFont.Px(4);
        for (int i = 0; i < 3; i++)
        {
            using var b = new SolidBrush(i <= _step ? SystemColors.Highlight : Color.FromArgb(218, 218, 218));
            g.FillRectangle(b, i * (w + gap), 0, w, h);
        }
    }

    // ------------------------------------------------------------------------------------ keuzes

    private void LoadChoices()
    {
        _chkDesktop.Checked = _choices.Desktop;
        _chkDownloads.Checked = _choices.Downloads;
        foreach (string p in _choices.OtherFolders) _lstOther.Items.Add(p);
        int di = Array.IndexOf(DayChoices, _choices.RetentionDays);
        _rbDays[di >= 0 ? di : 0].Checked = true;
        _cards.Level = _choices.SubfolderLevel;
        _lblLevelHint.Text = FolderDialog.HintFor(_cards.Level, legend: true);
        _rbRecycle.Checked = _choices.RecycleBin;
        _rbPermanent.Checked = !_choices.RecycleBin;
        _chkEmptyBin.Checked = _choices.EmptyRecycleBin;
        _chkEmptyBin.Enabled = _choices.RecycleBin;
        _chkDaily.Checked = _choices.Daily;
        _dtpTime.Value = DateTime.Today.Add(TimeSpan.TryParse(_choices.DailyTime, out var t) ? t : new TimeSpan(12, 30, 0));
        _dtpTime.Enabled = _choices.Daily;
        _chkLogon.Checked = _choices.AtLogon;
    }

    private void ReadChoices()
    {
        _choices.Desktop = _chkDesktop.Checked;
        _choices.Downloads = _chkDownloads.Checked;
        _choices.OtherFolders.Clear();
        _choices.OtherFolders.AddRange(_lstOther.Items.Cast<string>());
        for (int i = 0; i < _rbDays.Length; i++) if (_rbDays[i].Checked) _choices.RetentionDays = DayChoices[i];
        _choices.SubfolderLevel = _cards.Level;
        _choices.RecycleBin = _rbRecycle.Checked;
        _choices.EmptyRecycleBin = _chkEmptyBin.Checked;
        _choices.Daily = _chkDaily.Checked;
        _choices.DailyTime = _dtpTime.Value.ToString("HH:mm");
        _choices.AtLogon = _chkLogon.Checked;
    }

    private void FillSummary()
    {
        var folders = new System.Collections.Generic.List<string>();
        if (_choices.Desktop) folders.Add("Bureaublad");
        if (_choices.Downloads) folders.Add("Downloads");
        folders.AddRange(_choices.OtherFolders.Select(p => System.IO.Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p));
        string level = SubfolderLevels.IndexOf(_choices.SubfolderLevel) switch
        {
            0 => "Alleen deze map (Licht): submappen worden overgeslagen",
            1 => "Ook submappen (Midden): de submappen zelf blijven staan",
            _ => "Ook lege submappen (Zwaar): submappen die daarna leeg zijn, gaan ook weg",
        };
        var when = new System.Collections.Generic.List<string>();
        if (_choices.Daily) when.Add("elke dag om " + _choices.DailyTime);
        if (_choices.AtLogon) when.Add("bij het aanmelden bij Windows");

        _summary.SuspendLayout();
        _summary.Controls.Clear();
        _summary.RowStyles.Clear();
        _summary.RowCount = 0;
        AddSummaryRow("Mappen", string.Join(", ", folders));
        AddSummaryRow("Opruimen", "Als een bestand ouder is dan " + (_choices.RetentionDays == 1 ? "1 dag" : $"{_choices.RetentionDays} dagen"));
        AddSummaryRow("Submappen", level);
        AddSummaryRow("Oude bestanden", _choices.RecycleBin
            ? "Naar de Prullenbak" + (_choices.EmptyRecycleBin ? "; die wordt na 1 dag geleegd" : "")
            : "Direct definitief verwijderd");
        AddSummaryRow("Controle", when.Count > 0 ? char.ToUpper(when[0][0]) + string.Join(" en ", when).Substring(1) : "Alleen als u het zelf start");
        _summary.ResumeLayout(true);
    }

    private void AddSummaryRow(string caption, string value)
    {
        int row = _summary.RowCount++;
        _summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _summary.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, UiFont.Px(4), UiFont.Px(18), UiFont.Px(4)) }, 0, row);
        _summary.Controls.Add(new Label
        {
            Text = value, AutoSize = true, MaximumSize = new Size(UiFont.Px(ContentWidth - 150), 0), Margin = new Padding(0, UiFont.Px(4), 0, UiFont.Px(4)),
        }, 1, row);
    }
}
