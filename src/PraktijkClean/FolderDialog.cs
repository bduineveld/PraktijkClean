using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Venster met uitleg voor de bewaartermijn en de submappen van één map, met de grote kaarten en hun
/// voorbeeldboompjes. Opent na "Map toevoegen…" (ingevuld met de standaard voor nieuwe mappen) en bij dubbelklikken
/// op een map in de lijst. In het hoofdvenster staan dezelfde keuzes in compacte vorm.
/// </summary>
internal sealed class FolderDialog : Form
{
    private const int TextWidth = 600;

    private readonly ComboBox _cmbRetention = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumberBox _numDays = new() { Minimum = 0, Maximum = 3650, Width = 70 };
    private readonly FlowLayoutPanel _daysRow;
    private readonly SubfolderCards _cards = new() { Width = TextWidth };
    private readonly Label _lblHint;

    public int RetentionDays => RetentionOptions.All[_cmbRetention.SelectedIndex].Days ?? (int)_numDays.Value;
    public string Level => _cards.Level;

    /// <param name="isNew">true na "Map toevoegen…", false bij het aanpassen van een map uit de lijst.</param>
    /// <param name="confirmHeavier">Gevraagd als de gebruiker een grondiger niveau kiest (van, naar); false = niet wijzigen.</param>
    public FolderDialog(string name, string path, int retentionDays, string level, IReadOnlyList<string> warnings, bool isNew,
                        Func<IWin32Window, string, string, bool>? confirmHeavier = null)
    {
        SuspendLayout();
        DialogParts.SetupForm(this, isNew ? "Map toevoegen" : "Map instellen");
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var body = DialogParts.Column(new Padding(24, 20, 24, 18));
        DialogParts.Add(body, DialogParts.Heading(isNew ? "Map toevoegen: " + name : name));
        DialogParts.Add(body, DialogParts.Text(path, TextWidth, gray: true));
        if (warnings.Count > 0)
        {
            var warn = DialogParts.Text("Let op:\n• " + string.Join("\n• ", warnings), TextWidth, top: 4);
            warn.ForeColor = Color.DarkOrange;
            DialogParts.Add(body, warn);
        }

        DialogParts.Add(body, DialogParts.Section("Hoe lang mogen bestanden blijven staan?"));
        foreach (var o in RetentionOptions.All) _cmbRetention.Items.Add(o.Days == 0 ? "Direct (geen bewaartermijn)" : o.Label);
        _cmbRetention.SelectedIndex = RetentionOptions.IndexFor(retentionDays);
        _numDays.Value = Math.Max(0, Math.Min(3650, retentionDays));
        _daysRow = DialogParts.Row(_numDays, new Label { Text = "dagen", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
        _daysRow.Visible = RetentionOptions.All[_cmbRetention.SelectedIndex].Days is null;
        _cmbRetention.SelectedIndexChanged += (_, _) => _daysRow.Visible = RetentionOptions.All[_cmbRetention.SelectedIndex].Days is null;
        _cmbRetention.Margin = new Padding(0, 3, 8, 3);
        DialogParts.Add(body, DialogParts.Row(_cmbRetention, _daysRow));
        DialogParts.Add(body, DialogParts.Text(Texts.AgeExplanation, TextWidth, gray: true, top: 2));

        DialogParts.Add(body, DialogParts.Section("Wat gebeurt er met submappen?", top: 14));
        _cards.Level = level;
        _cards.RootName = name;
        _cards.Margin = new Padding(0, 2, 0, 4);
        DialogParts.Add(body, _cards, stretch: true);
        _lblHint = DialogParts.Text(HintFor(level, legend: true), TextWidth, gray: true, top: 2);
        DialogParts.Add(body, _lblHint);
        if (confirmHeavier is not null)
            _cards.LevelChanging += (_, e) =>
            {
                if (SubfolderLevels.IndexOf(e.To) > SubfolderLevels.IndexOf(e.From)) e.Cancel = !confirmHeavier(this, e.From, e.To);
            };
        _cards.LevelChanged += (_, _) => _lblHint.Text = HintFor(_cards.Level, legend: true);

        DialogParts.Add(body, DialogParts.Text(isNew
            ? "Er wordt niets verwijderd tot automatisch opruimen aan staat. Kijk na het toevoegen eerst met 'Simulatie bekijken' " +
              "wat er zou worden opgeruimd. Uitzonderingen ('Nooit verwijderen') stelt u daarna in het hoofdvenster in."
            : "De wijziging geldt nadat u in het hoofdvenster op 'Opslaan' heeft geklikt.", TextWidth, gray: true, top: 12, bottom: 0));

        var ok = DialogParts.Button(isNew ? "Map toevoegen" : "OK", main: true);
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

    /// <summary>
    /// Uitleg bij het gekozen niveau, met bij Zwaar de tip over vaste mappen die leeg mogen zijn. Met
    /// <paramref name="legend"/> ook de uitleg van de kleuren in de voorbeeldboompjes.
    /// </summary>
    public static string HintFor(string level, bool legend)
    {
        string text = SubfolderLevels.All[SubfolderLevels.IndexOf(level)].Description;
        if (level == SubfolderLevels.Heavy)
            text += " Mappen die leeg moeten kunnen blijven (zoals een scanmap) zet u bij 'Nooit verwijderen'.";
        return legend ? text + "\nRood doorgestreept = wordt opgeruimd als het ouder is dan de bewaartermijn." : text;
    }
}
