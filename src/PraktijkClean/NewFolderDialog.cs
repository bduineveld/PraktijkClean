using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Venster na "Map toevoegen…": bewaartermijn en submappen voor de nieuwe map, vooraf ingevuld met de standaard
/// voor nieuwe mappen, met de keuze om dit ook de standaard te maken. Zonder map (<c>path</c> = null) is het
/// venster "Standaard voor nieuwe mappen".
/// </summary>
internal sealed class NewFolderDialog : Form
{
    private const int TextWidth = 600;

    private readonly ComboBox _cmbRetention = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumberBox _numDays = new() { Minimum = 0, Maximum = 3650, Width = 70 };
    private readonly FlowLayoutPanel _daysRow;
    private readonly SubfolderCards _cards = new() { Width = TextWidth };
    private readonly Label _lblHint;
    private readonly CheckBox _chkDefault = new()
    {
        Text = "Deze keuzes ook gebruiken voor mappen die ik hierna toevoeg", AutoSize = true, Margin = new Padding(0, 12, 0, 0),
    };

    public int RetentionDays => RetentionOptions.All[_cmbRetention.SelectedIndex].Days ?? (int)_numDays.Value;
    public string Level => _cards.Level;
    public bool SaveAsDefault => _chkDefault.Checked;

    public NewFolderDialog(string? path, int retentionDays, string level, IReadOnlyList<string> warnings)
    {
        bool defaultsOnly = path is null;
        SuspendLayout();
        DialogParts.SetupForm(this, Texts.AppTitle);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var body = DialogParts.Column(new Padding(24, 20, 24, 18));
        DialogParts.Add(body, DialogParts.Heading(defaultsOnly ? "Standaard voor nieuwe mappen" : "Map toevoegen"));
        DialogParts.Add(body, DialogParts.Text(defaultsOnly
            ? "Zo worden mappen ingesteld die u hierna toevoegt. Mappen die al in de lijst staan, veranderen niet."
            : path!, TextWidth, gray: true));
        if (warnings.Count > 0)
        {
            var warn = DialogParts.Text("Let op:\n• " + string.Join("\n• ", warnings), TextWidth, top: 4);
            warn.ForeColor = Color.DarkOrange;
            DialogParts.Add(body, warn);
        }

        DialogParts.Add(body, DialogParts.Section("Opruimen als een bestand ouder is dan"));
        foreach (var o in RetentionOptions.All) _cmbRetention.Items.Add(o.Days == 0 ? "Direct (geen bewaartermijn)" : o.Label.Replace("Ouder dan ", ""));
        _cmbRetention.SelectedIndex = RetentionOptions.IndexFor(retentionDays);
        _numDays.Value = Math.Max(0, Math.Min(3650, retentionDays));
        _daysRow = DialogParts.Row(_numDays, new Label { Text = "dagen", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
        _daysRow.Visible = RetentionOptions.All[_cmbRetention.SelectedIndex].Days is null;
        _cmbRetention.SelectedIndexChanged += (_, _) => _daysRow.Visible = RetentionOptions.All[_cmbRetention.SelectedIndex].Days is null;
        _cmbRetention.Margin = new Padding(0, 3, 8, 3);
        DialogParts.Add(body, DialogParts.Row(_cmbRetention, _daysRow));

        DialogParts.Add(body, DialogParts.Section("Submappen", top: 16));
        _cards.Level = level;
        if (path is not null) _cards.RootName = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
        _cards.Margin = new Padding(0, 2, 0, 4);
        DialogParts.Add(body, _cards, stretch: true);
        _lblHint = DialogParts.Text(HintFor(level), TextWidth, gray: true, top: 2);
        DialogParts.Add(body, _lblHint);
        _cards.LevelChanged += (_, _) => _lblHint.Text = HintFor(_cards.Level);

        if (!defaultsOnly)
        {
            DialogParts.Add(body, _chkDefault);
            DialogParts.Add(body, DialogParts.Text("Er wordt niets verwijderd tot automatisch opruimen aan staat. Kijk na het toevoegen eerst " +
                                                   "met 'Simulatie bekijken' wat er zou worden opgeruimd.", TextWidth, gray: true, top: 12, bottom: 0));
        }

        var ok = DialogParts.Button(defaultsOnly ? "Standaard instellen" : "Map toevoegen", main: true);
        var cancel = DialogParts.Button(ConfirmDialog.Cancel);
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = ok;
        CancelButton = cancel;

        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 0);
        var bar = DialogParts.ButtonBar(Array.Empty<Control>(), new Control[] { ok, cancel });
        bar.Dock = DockStyle.Fill;
        root.Controls.Add(bar, 0, 1);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>Uitleg bij het gekozen niveau, met bij Zwaar de tip over vaste mappen die leeg mogen zijn.</summary>
    public static string HintFor(string level)
    {
        string text = SubfolderLevels.All[SubfolderLevels.IndexOf(level)].Description;
        if (level == SubfolderLevels.Heavy)
            text += " Mappen die leeg moeten kunnen blijven (zoals een scanmap) zet u na het toevoegen bij 'Nooit verwijderen'.";
        return text + "\nRood doorgestreept = wordt opgeruimd als het ouder is dan de bewaartermijn.";
    }
}
