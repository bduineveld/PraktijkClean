using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Toont het resultaat van een handmatige simulatie, inclusief bestandsnamen.
/// De namen bestaan alleen in dit venster; ze worden nergens opgeslagen.
/// Via de rechtermuisknop (of de knop "Uitsluiten") kan een bestand direct als uitzondering worden opgeslagen.
/// </summary>
internal sealed class SimulationResultForm : Form
{
    /// <summary>Een bestand dat in aanmerking komt: map en pad binnen die map.</summary>
    private sealed record Candidate(string FolderId, string RelativePath);

    /// <summary>
    /// Voegt uitzonderingen toe en slaat de instellingen op. Geeft null bij succes, anders een foutmelding.
    /// </summary>
    public delegate string? ExcludeHandler(IReadOnlyList<(string FolderId, ExclusionItem Item)> items);

    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
    private readonly ContextMenuStrip _menu = new();
    private readonly Button _btnExclude = new() { Text = "Uitsluiten ▾", AutoSize = true, Enabled = false };
    private readonly Label _lblSaved = new() { AutoSize = true, ForeColor = Color.ForestGreen, Margin = new Padding(8, 8, 3, 3) };
    private readonly AppConfig _config;
    private readonly ExcludeHandler _onExclude;

    public SimulationResultForm(RunReport report, AppConfig config, ExcludeHandler onExclude)
    {
        _config = config;
        _onExclude = onExclude;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiFont.Normal;
        Text = "Simulatie – er is niets verwijderd";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(700, 450);
        MinimizeBox = false;
        ShowInTaskbar = false;

        var summary = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            Padding = new Padding(0, 0, 0, 8),
            Text =
                $"In aanmerking voor verwijdering: {report.Candidates} bestand(en), samen {Format.Bytes(report.CandidateBytes)}.\n" +
                $"Overgeslagen: {report.Skipped}. Nog te recent: {report.TooYoung}. Fouten: {report.Errors}.\n" +
                (report.Candidates > config.MaxFilesPerRun
                    ? $"Let op: dit is meer dan het maximum van {config.MaxFilesPerRun} per keer; bij echt opruimen wordt dan uit voorzorg niets verwijderd.\n"
                    : "") +
                (report.RecycleBin is { } bin
                    ? $"Prullenbak (items die er langer dan {bin.RetentionDays} {(bin.RetentionDays == 1 ? "dag" : "dagen")} in staan): {bin.Candidates} item(s), samen {Format.Bytes(bin.CandidateBytes)}. Nog te recent: {bin.TooYoung}.\n"
                    : "") +
                (report.Messages.Count > 0 ? string.Join("\n", report.Messages) + "\n" : "") +
                "Moet een bestand blijven staan? Klik er met de rechtermuisknop op (of kies 'Uitsluiten') om het voortaan over te slaan.\n" +
                "Bestandsnamen worden alleen in dit venster getoond en niet opgeslagen in logbestanden.",
        };

        _list.Columns.Add("Map", 140);
        _list.Columns.Add("Bestand", 360);
        _list.Columns.Add("Grootte", 90, HorizontalAlignment.Right);
        _list.Columns.Add("Wat gebeurt er", 260);
        // Kolombreedtes staan in pixels bij 100% en schalen niet automatisch mee met de Windows-schaal (DPI).
        Load += (_, _) => { foreach (ColumnHeader col in _list.Columns) col.Width = UiFont.Px(col.Width); };
        FillList(report);

        _list.ContextMenuStrip = _menu;
        _menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };
        _list.SelectedIndexChanged += (_, _) => _btnExclude.Enabled = SelectedCandidates().Count > 0;
        _btnExclude.Click += (_, _) => _menu.Show(_btnExclude, new Point(0, _btnExclude.Height));

        var close = new Button { Text = "Sluiten", AutoSize = true, DialogResult = DialogResult.OK };
        AcceptButton = close;
        CancelButton = close;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_btnExclude, 0, 0);
        bottom.Controls.Add(_lblSaved, 1, 0);
        bottom.Controls.Add(close, 2, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();
    }

    private string NameOf(string id) => _config.Folders.FirstOrDefault(f => f.Id == id)?.DisplayName ?? id;

    private void FillList(RunReport report)
    {
        _list.BeginUpdate();
        foreach (var folder in report.Folders)
        {
            string name = NameOf(folder.Id);
            if (folder.Status is "Geblokkeerd" or "Fout" or "Uitgeschakeld")
            {
                var item = new ListViewItem(new[] { name, "(hele map)", "", folder.Status == "Uitgeschakeld" ? "Map staat uit" : folder.Message })
                {
                    ForeColor = folder.Status == "Uitgeschakeld" ? SystemColors.GrayText : Color.Firebrick,
                };
                _list.Items.Add(item);
                continue;
            }
            var details = report.FileDetails?.FirstOrDefault(d => d.Id == folder.Id);
            if (details is null) continue;
            foreach (var c in details.Candidates)
                _list.Items.Add(new ListViewItem(new[] { name, c.Name, Format.Bytes(c.Size), "Komt in aanmerking voor verwijdering" })
                    { Tag = new Candidate(folder.Id, c.Name) });
            foreach (var s in details.Skipped)
                _list.Items.Add(new ListViewItem(new[] { name, s.Name, "", "Overgeslagen: " + s.Reason }) { ForeColor = SystemColors.GrayText });
            if (folder.TooYoung > 0)
                _list.Items.Add(new ListViewItem(new[] { name, $"({folder.TooYoung} recente bestanden)", "", "Blijven staan: jonger dan de bewaartermijn" })
                    { ForeColor = SystemColors.GrayText });
            if (folder.Message.Length > 0)
                _list.Items.Add(new ListViewItem(new[] { name, "", "", folder.Message }) { ForeColor = Color.DarkOrange });
        }
        if (report.RecycleBin is { } rb)
        {
            foreach (var c in report.RecycleBinDetails ?? new())
                _list.Items.Add(new ListViewItem(new[] { "Prullenbak", c.Name, Format.Bytes(c.Size), "Wordt definitief uit de Prullenbak verwijderd" }));
            if (rb.TooYoung > 0)
                _list.Items.Add(new ListViewItem(new[] { "Prullenbak", $"({rb.TooYoung} recente items)", "", "Blijven staan: korter in de Prullenbak dan de termijn" })
                    { ForeColor = SystemColors.GrayText });
            if (rb.Message.Length > 0)
                _list.Items.Add(new ListViewItem(new[] { "Prullenbak", "", "", rb.Message }) { ForeColor = Color.DarkOrange });
        }
        _list.EndUpdate();
    }

    private List<Candidate> SelectedCandidates() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<Candidate>().ToList();

    /// <summary>Bouwt het menu op basis van de geselecteerde regels.</summary>
    private void BuildMenu()
    {
        _menu.Items.Clear();
        var selected = SelectedCandidates();
        if (selected.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("Kies eerst een of meer bestanden die in aanmerking komen") { Enabled = false });
            return;
        }

        string fileText = selected.Count == 1
            ? $"'{Short(System.IO.Path.GetFileName(selected[0].RelativePath))}' nooit verwijderen"
            : $"Deze {selected.Count} bestanden nooit verwijderen";
        _menu.Items.Add(fileText, null, (_, _) => Exclude(selected.Select(c => (c.FolderId, new ExclusionItem(ExclusionKind.File, c.RelativePath))).ToList(),
            c => selected.Contains(c)));

        // Extensie en submap alleen als de hele selectie uit dezelfde map komt en hetzelfde kenmerk heeft.
        if (selected.Select(c => c.FolderId).Distinct().Count() != 1) return;
        string folderId = selected[0].FolderId;
        string folderName = NameOf(folderId);

        var exts = selected.Select(c => Exclusions.ExtensionOf(c.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (exts.Count == 1 && exts[0] is string ext)
        {
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add($"Alle {ext}-bestanden in '{folderName}' nooit verwijderen", null, (_, _) =>
                Exclude(new[] { (folderId, new ExclusionItem(ExclusionKind.Extension, ext)) },
                    c => c.FolderId == folderId && string.Equals(Exclusions.ExtensionOf(c.RelativePath), ext, StringComparison.OrdinalIgnoreCase)));
        }

        var parents = selected.Select(c => Exclusions.ParentOf(c.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (parents.Count == 1 && parents[0] is string parent)
        {
            _menu.Items.Add($"Submap '{Short(parent)}' in '{folderName}' overslaan", null, (_, _) =>
                Exclude(new[] { (folderId, new ExclusionItem(ExclusionKind.Subfolder, parent)) },
                    c => c.FolderId == folderId && c.RelativePath.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>Slaat de uitzonderingen op en markeert de regels waarvoor ze gelden.</summary>
    private void Exclude(IReadOnlyList<(string FolderId, ExclusionItem Item)> items, Func<Candidate, bool> affects)
    {
        string? error;
        UseWaitCursor = true;
        try { error = _onExclude(items); }
        finally { UseWaitCursor = false; }
        if (error is not null)
        {
            MessageBox.Show(this, "De uitzondering is niet opgeslagen.\n\n" + error, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int count = 0;
        foreach (ListViewItem row in _list.Items)
        {
            if (row.Tag is not Candidate c || !affects(c)) continue;
            row.Tag = null;
            row.ForeColor = SystemColors.GrayText;
            row.SubItems[3].Text = "Wordt voortaan overgeslagen (uitzondering)";
            count++;
        }
        _btnExclude.Enabled = SelectedCandidates().Count > 0;
        _lblSaved.Text = $"Uitzondering opgeslagen: {count} bestand(en) worden voortaan overgeslagen.";
    }

    private static string Short(string s) => s.Length <= 50 ? s : s.Substring(0, 47) + "…";
}
